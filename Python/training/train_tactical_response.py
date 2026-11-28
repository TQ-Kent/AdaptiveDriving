"""Phase 4: Train, calibrate, validate, and export the Neural Tactical Response Model (T05 + T07).

Trains a compact PyTorch MLP predicting P(OpponentResponse | DuelSnapshot, OpponentStyleEstimate, CandidateManeuver)
from AI-vs-AI & PlayerBot encounters, fits post-hoc temperature scaling for calibrated probabilities, compares against
the rule-based TemplateTacticalResponseModel on held-out bot families (NLL, Brier score, ECE, accuracy), exports ONNX
(verified with onnxruntime), and serializes the exact ModelManifest JSON for Unity's M04 runtime.
"""
from __future__ import annotations

import hashlib
import json
import os
from typing import Dict, List, Tuple

import numpy as np
import onnx
import onnxruntime as ort
import torch
import torch.nn as nn
import torch.nn.functional as F

from .dataset_builder import (
    TACTICAL_FEATURE_NAMES,
    TacticalDataset,
    apply_standardizer,
    build_tactical_dataset,
    fit_standardizer,
    group_train_val_split,
)
from .track_kinematics import DRIVER_VERSION, RESPONSE_NAMES, template_response_probs


class TacticalResponseMLP(nn.Module):
    def __init__(self, in_dim: int = 22, hidden1: int = 48, hidden2: int = 32, out_dim: int = 5):
        super().__init__()
        self.fc1 = nn.Linear(in_dim, hidden1)
        self.fc2 = nn.Linear(hidden1, hidden2)
        self.fc3 = nn.Linear(hidden2, out_dim)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        h = F.relu(self.fc1(x))
        h = F.relu(self.fc2(h))
        return self.fc3(h)


def multiclass_brier(probs: np.ndarray, y: np.ndarray) -> float:
    one_hot = np.eye(probs.shape[1], dtype=np.float64)[y]
    return float(np.mean(np.sum((probs - one_hot) ** 2, axis=1)))


def multiclass_nll(probs: np.ndarray, y: np.ndarray) -> float:
    p = np.clip(probs[np.arange(len(y)), y], 1e-7, 1.0)
    return float(-np.mean(np.log(p)))


def expected_calibration_error(probs: np.ndarray, y: np.ndarray, n_bins: int = 10) -> float:
    conf = probs.max(axis=1)
    pred = probs.argmax(axis=1)
    acc = (pred == y).astype(np.float64)
    ece = 0.0
    for b in range(n_bins):
        lo, hi = b / n_bins, (b + 1) / n_bins
        mask = (conf > lo) & (conf <= hi) if b > 0 else (conf >= lo) & (conf <= hi)
        if np.any(mask):
            ece += (np.sum(mask) / len(y)) * abs(float(acc[mask].mean()) - float(conf[mask].mean()))
    return float(ece)


def _fit_temperature(logits_val: torch.Tensor, y_val: torch.Tensor) -> float:
    log_temp = torch.zeros(1, requires_grad=True)
    opt = torch.optim.LBFGS([log_temp], lr=0.1, max_iter=50)

    def closure():
        opt.zero_grad()
        temp = torch.exp(log_temp).clamp(0.2, 5.0)
        loss = F.cross_entropy(logits_val / temp, y_val)
        loss.backward()
        return loss

    opt.step(closure)
    return float(torch.exp(log_temp).clamp(0.2, 5.0).item())


def _extract_layers(model: TacticalResponseMLP) -> List[Dict]:
    specs = []
    for layer, act in ((model.fc1, "relu"), (model.fc2, "relu"), (model.fc3, "linear")):
        w = layer.weight.detach().cpu().numpy().astype(np.float32)
        b = layer.bias.detach().cpu().numpy().astype(np.float32)
        specs.append({
            "in_dim": int(w.shape[1]),
            "out_dim": int(w.shape[0]),
            "activation": act,
            "weights": [round(float(v), 7) for v in w.reshape(-1)],
            "bias": [round(float(v), 7) for v in b.reshape(-1)],
        })
    return specs


def _template_probs_batch(X: np.ndarray) -> np.ndarray:
    out = np.zeros((len(X), 5), dtype=np.float64)
    for i, row in enumerate(X):
        opp_ahead = row[0] > 0.5
        closing = float(row[4] * 25.0)
        style_p = row[11:16]
        style_unc = float(1.0 - row[16])
        if row[19] > 0.5:
            m = "DefendInside"
        elif row[21] > 0.5:
            m = "Switchback"
        elif row[20] > 0.5:
            m = "AttackInside" if row[18] > 0.5 else "AttackOutside"
        else:
            m = "HoldLine"
        out[i] = template_response_probs(opp_ahead, closing, m, style_p, style_unc)
    return out


def train_and_export_tactical_response(
    output_dir: str,
    unity_logs_dir: str | None = None,
    seed: int = 42,
    epochs: int = 45,
) -> Dict:
    torch.manual_seed(seed)
    np.random.seed(seed)
    os.makedirs(output_dir, exist_ok=True)

    ds: TacticalDataset = build_tactical_dataset(seed=seed, encounters_per_family=1600, unity_logs_dir=unity_logs_dir)
    tr_idx, va_idx, ho_idx = group_train_val_split(ds.run_ids, ds.is_holdout, val_frac=0.2, seed=seed)

    mean, std, fmin, fmax = fit_standardizer(ds.X[tr_idx])
    X_tr = torch.from_numpy(apply_standardizer(ds.X[tr_idx], mean, std))
    y_tr = torch.from_numpy(ds.y[tr_idx])
    # Upweight real Unity AI-vs-AI encounters (3x) so accumulated Unity races meaningfully shape the neural policy!
    w_tr = torch.tensor([3.0 if ds.sources[i] == "unity_ai_vs_ai" else 1.0 for i in tr_idx], dtype=torch.float32)
    X_va = torch.from_numpy(apply_standardizer(ds.X[va_idx], mean, std))
    y_va = torch.from_numpy(ds.y[va_idx])
    X_ho = torch.from_numpy(apply_standardizer(ds.X[ho_idx], mean, std))
    y_ho = ds.y[ho_idx]

    model = TacticalResponseMLP(in_dim=ds.X.shape[1], hidden1=48, hidden2=32, out_dim=5)
    opt = torch.optim.AdamW(model.parameters(), lr=3e-3, weight_decay=1e-3)
    batch_size = 256
    best_val_loss = float("inf")
    best_state = None

    for _ in range(epochs):
        model.train()
        perm = torch.randperm(len(X_tr))
        for start in range(0, len(X_tr), batch_size):
            idx = perm[start:start + batch_size]
            opt.zero_grad()
            per_sample = F.cross_entropy(model(X_tr[idx]), y_tr[idx], reduction="none")
            loss = torch.mean(per_sample * w_tr[idx])
            loss.backward()
            opt.step()

        model.eval()
        with torch.no_grad():
            val_loss = float(F.cross_entropy(model(X_va), y_va).item())
            if val_loss < best_val_loss:
                best_val_loss = val_loss
                best_state = {k: v.clone() for k, v in model.state_dict().items()}

    model.load_state_dict(best_state)
    model.eval()

    with torch.no_grad():
        val_logits = model(X_va)
        temperature = _fit_temperature(val_logits, y_va)
        ho_logits = model(X_ho)
        ho_probs = F.softmax(ho_logits / temperature, dim=-1).numpy()

    templ_ho_probs = _template_probs_batch(ds.X[ho_idx])

    metrics = {
        "n_train": int(len(tr_idx)),
        "n_val": int(len(va_idx)),
        "n_holdout": int(len(ho_idx)),
        "unity_rows_ingested": int(sum(1 for s in ds.sources if s == "unity_ai_vs_ai")),
        "temperature": round(temperature, 5),
        "neural_holdout": {
            "nll": round(multiclass_nll(ho_probs, y_ho), 4),
            "brier": round(multiclass_brier(ho_probs, y_ho), 4),
            "ece": round(expected_calibration_error(ho_probs, y_ho), 4),
            "accuracy": round(float(np.mean(ho_probs.argmax(axis=1) == y_ho)), 4),
        },
        "template_holdout": {
            "nll": round(multiclass_nll(templ_ho_probs, y_ho), 4),
            "brier": round(multiclass_brier(templ_ho_probs, y_ho), 4),
            "ece": round(expected_calibration_error(templ_ho_probs, y_ho), 4),
            "accuracy": round(float(np.mean(templ_ho_probs.argmax(axis=1) == y_ho)), 4),
        },
    }

    # Export ONNX & verify with onnxruntime (T07)
    onnx_name = "tactical_response.onnx"
    onnx_path = os.path.join(output_dir, onnx_name)
    dummy = torch.zeros((1, ds.X.shape[1]), dtype=torch.float32)
    torch.onnx.export(
        model, dummy, onnx_path,
        input_names=["features"], output_names=["logits"],
        dynamic_axes={"features": {0: "batch"}, "logits": {0: "batch"}},
        opset_version=17,
        dynamo=False,
    )
    onnx.checker.check_model(onnx.load(onnx_path))

    sess = ort.InferenceSession(onnx_path, providers=["CPUExecutionProvider"])
    sample_raw = ds.X[ho_idx[:16]]
    sample_norm = apply_standardizer(sample_raw, mean, std)
    ort_logits = sess.run(["logits"], {"features": sample_norm})[0]
    with torch.no_grad():
        pt_logits = model(torch.from_numpy(sample_norm)).numpy()
    max_onnx_diff = float(np.max(np.abs(ort_logits - pt_logits)))
    metrics["onnx_parity_max_diff"] = max_onnx_diff

    with open(onnx_path, "rb") as f:
        onnx_sha = hashlib.sha256(f.read()).hexdigest()

    # Compute exact softmax parity vectors from raw inputs for Unity C# T07 verification!
    parity_vectors = []
    for i in range(min(12, len(sample_raw))):
        logits = ort_logits[i] / temperature
        logits = logits - np.max(logits)
        probs = np.exp(logits)
        probs = probs / probs.sum()
        parity_vectors.append({
            "input": [round(float(v), 6) for v in sample_raw[i]],
            "expected_output": [round(float(v), 6) for v in probs],
        })

    manifest = {
        "schema_version": 1,
        "model_name": "tactical_response_mlp",
        "model_version": "1.2.0",
        "driver_version": DRIVER_VERSION,
        "track_scope": "MonzaScaled,SilverstoneStyle,TightHairpinCircuit",
        "task": "tactical_response",
        "input_dim": int(ds.X.shape[1]),
        "output_dim": len(RESPONSE_NAMES),
        "feature_names": TACTICAL_FEATURE_NAMES,
        "feature_mean": [round(float(v), 7) for v in mean],
        "feature_std": [round(float(v), 7) for v in std],
        "feature_min": [round(float(v), 7) for v in fmin],
        "feature_max": [round(float(v), 7) for v in fmax],
        "temperature": round(temperature, 6),
        "target_mean": 0.0,
        "target_std": 1.0,
        "max_latency_ms": 2.0,
        "members": [{
            "member_id": "tactical_mlp_0",
            "onnx_file": onnx_name,
            "onnx_sha256": onnx_sha,
            "layers": _extract_layers(model),
        }],
        "parity_vectors": parity_vectors,
    }

    manifest_path = os.path.join(output_dir, "tactical_response_manifest.json")
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)

    metrics["manifest_path"] = manifest_path
    metrics["onnx_path"] = onnx_path
    return metrics
