"""Phase 5: Train M01 PlayerPacePredictor (3-MLP bootstrap ensemble), validate 1-step & H=3 M01+M02+M03 rollouts,
benchmark against the non-neural rolling clean-pace estimator (A02/A08), and export ONNX + Unity ModelManifest (T05-T07).
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

from .calibrate_m02 import query_m02_table
from .dataset_builder import (
    PACE_FEATURE_NAMES,
    PaceDataset,
    apply_standardizer,
    build_pace_dataset,
    fit_standardizer,
    group_train_val_split,
)
from .track_kinematics import DRIVER_VERSION


class PlayerPaceMLP(nn.Module):
    """Architecture B8: compact 2x64 MLP outputting (standardized_mean, log_var)."""
    def __init__(self, in_dim: int = 16, hidden: int = 64):
        super().__init__()
        self.fc1 = nn.Linear(in_dim, hidden)
        self.fc2 = nn.Linear(hidden, hidden)
        self.fc3 = nn.Linear(hidden, 2)
        with torch.no_grad():
            # Initialize log_var bias near physical normalized lap-pace variance (~0.035^2 -> ln(0.0012) ~= -6.7)
            self.fc3.bias[1].fill_(-6.5)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        h = F.relu(self.fc1(x))
        h = F.relu(self.fc2(h))
        return self.fc3(h)


def gaussian_nll_loss(out: torch.Tensor, target_std: torch.Tensor) -> torch.Tensor:
    mean = out[:, 0]
    log_var = out[:, 1].clamp(-9.0, -1.0)
    # Work in unstandardized residual variance scale: scale mean err by 1, regularize log_var smoothly
    inv_var = torch.exp(-log_var)
    return torch.mean(0.5 * (log_var + (target_std - mean) ** 2 * inv_var))


def _extract_layers(model: PlayerPaceMLP) -> List[Dict]:
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


def predict_ensemble_numpy(
    models: List[PlayerPaceMLP],
    X_raw: np.ndarray,
    mean: np.ndarray,
    std: np.ndarray,
    t_mean: float,
    t_std: float,
) -> Tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Exact ensemble decoding matching Unity `PlayerPaceEnsembleModel.Predict` (T07 parity)."""
    X_norm = torch.from_numpy(apply_standardizer(X_raw, mean, std))
    m_means, m_vars = [], []
    with torch.no_grad():
        for m in models:
            out = m(X_norm).numpy()
            pred_mean = out[:, 0] * t_std + t_mean
            pred_var = np.exp(np.clip(out[:, 1], -9.0, -1.0))
            m_means.append(pred_mean)
            m_vars.append(pred_var)
    m_means_arr = np.stack(m_means, axis=0)
    m_vars_arr = np.stack(m_vars, axis=0)
    ens_mean = np.clip(m_means_arr.mean(axis=0), 0.70, 1.60)
    aleatoric = m_vars_arr.mean(axis=0)
    epistemic = m_means_arr.var(axis=0)
    total_std = np.sqrt(np.maximum(1e-6, aleatoric + epistemic))
    disagree = np.sqrt(epistemic)
    return ens_mean, total_std, disagree


def evaluate_multistep_rollouts(
    models: List[PlayerPaceMLP],
    sequences: List[Dict],
    m02_asset: Dict,
    mean: np.ndarray,
    std: np.ndarray,
    t_mean: float,
    t_std: float,
) -> Dict:
    """Stage B: Evaluate H=3 M01+M02+M03 rollouts on held-out sequences vs rolling baseline."""
    hold_seqs = [s for s in sequences if s["is_holdout"]]
    m01_step_errs = [[], [], []]
    roll_step_errs = [[], [], []]
    covered_90 = []
    gap_errs_3 = []

    for seq in hold_seqs:
        feat = seq["init_features"].copy()
        ref_s = seq["ref_lap_s"]
        rolling_est = seq["rolling_est"]
        accum_var_norm = 0.0
        pred_gap_s = 0.0
        true_gap_s = 0.0
        ai_lap_s, _, _, _ = query_m02_table(m02_asset, seq["track_id"], 0.65, False)

        for h in range(3):
            feat[14] = 1.0 if seq["future_traffic"][h] else 0.0
            feat[15] = 1.0 if seq["future_quali"][h] else 0.0
            p_mean, p_std, _ = predict_ensemble_numpy(models, feat[None, :], mean, std, t_mean, t_std)
            pred_norm = float(p_mean[0])
            step_std = float(p_std[0])
            true_norm = float(seq["future_norms"][h])

            m01_step_errs[h].append(abs(pred_norm - true_norm) * ref_s)
            roll_step_errs[h].append(abs(rolling_est - true_norm) * ref_s)

            accum_var_norm = step_std ** 2
            covered_90.append(abs(pred_norm - true_norm) <= 1.645 * math.sqrt(accum_var_norm))

            # M03 Analytical gap progression: g_{t+1} = g_t + T_player - T_AI
            pred_gap_s += pred_norm * ref_s - ai_lap_s
            true_gap_s += true_norm * ref_s - ai_lap_s

            # Append prediction into window with real_mask=0 (NEVER increment real evidence count!)
            feat[1:5] = feat[0:4]
            feat[0] = pred_norm
            feat[6:10] = feat[5:9]
            feat[5] = 0.0  # predicted, NOT real observation

        gap_errs_3.append(abs(pred_gap_s - true_gap_s))

    return {
        "n_holdout_sequences": len(hold_seqs),
        "m01_mae_s_by_step": [round(float(np.mean(e)), 3) for e in m01_step_errs],
        "rolling_baseline_mae_s_by_step": [round(float(np.mean(e)), 3) for e in roll_step_errs],
        "m01_3step_gap_mae_s": round(float(np.mean(gap_errs_3)), 3),
        "interval_90_coverage": round(float(np.mean(covered_90)), 4),
    }


import math


def train_and_export_m01_ensemble(
    output_dir: str,
    m02_table_path: str,
    seed: int = 101,
    epochs: int = 55,
    n_members: int = 3,
) -> Dict:
    torch.manual_seed(seed)
    np.random.seed(seed)
    os.makedirs(output_dir, exist_ok=True)

    with open(m02_table_path, encoding="utf-8") as f:
        m02_asset = json.load(f)

    ds: PaceDataset = build_pace_dataset(seed=seed, runs_per_family=70)
    tr_idx, va_idx, ho_idx = group_train_val_split(ds.run_ids, ds.is_holdout, val_frac=0.2, seed=seed)

    mean, std, fmin, fmax = fit_standardizer(ds.X[tr_idx])
    t_mean = float(ds.y[tr_idx].mean())
    t_std = float(max(1e-4, ds.y[tr_idx].std()))

    X_va = torch.from_numpy(apply_standardizer(ds.X[va_idx], mean, std))
    y_va_raw = ds.y[va_idx]
    y_va_std = torch.from_numpy(((y_va_raw - t_mean) / t_std).astype(np.float32))

    # Group training indices by run_id for proper run-level bootstrap sampling (architecture B8!)
    tr_run_to_rows: Dict[str, List[int]] = {}
    for i in tr_idx:
        tr_run_to_rows.setdefault(ds.run_ids[i], []).append(int(i))
    tr_run_keys = sorted(tr_run_to_rows.keys())

    models: List[PlayerPaceMLP] = []
    member_specs = []
    onnx_diffs = []

    for m_i in range(n_members):
        m_rng = np.random.default_rng(seed + 1000 * (m_i + 1))
        torch.manual_seed(seed + 1000 * (m_i + 1))

        # Run-level bootstrap resample (with replacement)
        boot_runs = m_rng.choice(tr_run_keys, size=len(tr_run_keys), replace=True)
        boot_rows = [row for r in boot_runs for row in tr_run_to_rows[r]]
        X_b = torch.from_numpy(apply_standardizer(ds.X[boot_rows], mean, std))
        y_b_std = torch.from_numpy(((ds.y[boot_rows] - t_mean) / t_std).astype(np.float32))

        model = PlayerPaceMLP(in_dim=ds.X.shape[1], hidden=64)
        opt = torch.optim.AdamW(model.parameters(), lr=2.5e-3, weight_decay=1e-3)
        best_val = float("inf")
        best_state = None
        batch_size = 128

        for _ in range(epochs):
            model.train()
            perm = torch.randperm(len(X_b))
            for start in range(0, len(X_b), batch_size):
                idx = perm[start:start + batch_size]
                opt.zero_grad()
                out = model(X_b[idx])
                # Train mean in standardized units and log_var in raw normalized pace units
                mean_pred = out[:, 0] * t_std + t_mean
                target_raw = y_b_std[idx] * t_std + t_mean
                log_var = out[:, 1].clamp(-9.0, -1.0)
                loss = torch.mean(0.5 * (log_var + (target_raw - mean_pred) ** 2 * torch.exp(-log_var)))
                loss.backward()
                opt.step()

            model.eval()
            with torch.no_grad():
                vout = model(X_va)
                vmean = vout[:, 0] * t_std + t_mean
                vlogv = vout[:, 1].clamp(-9.0, -1.0)
                vloss = float(torch.mean(0.5 * (vlogv + (torch.from_numpy(y_va_raw) - vmean) ** 2 * torch.exp(-vlogv))).item())
                if vloss < best_val:
                    best_val = vloss
                    best_state = {k: v.clone() for k, v in model.state_dict().items()}

        model.load_state_dict(best_state)
        model.eval()
        models.append(model)

        # Export member ONNX & verify parity
        onnx_name = f"m01_member_{m_i}.onnx"
        onnx_path = os.path.join(output_dir, onnx_name)
        dummy = torch.zeros((1, ds.X.shape[1]), dtype=torch.float32)
        torch.onnx.export(
            model, dummy, onnx_path,
            input_names=["features"], output_names=["mean_and_logvar"],
            dynamic_axes={"features": {0: "batch"}, "mean_and_logvar": {0: "batch"}},
            opset_version=17,
            dynamo=False,
        )
        onnx.checker.check_model(onnx.load(onnx_path))

        sess = ort.InferenceSession(onnx_path, providers=["CPUExecutionProvider"])
        sample_norm = apply_standardizer(ds.X[ho_idx[:16]], mean, std)
        ort_out = sess.run(["mean_and_logvar"], {"features": sample_norm})[0]
        with torch.no_grad():
            pt_out = model(torch.from_numpy(sample_norm)).numpy()
        onnx_diffs.append(float(np.max(np.abs(ort_out - pt_out))))

        with open(onnx_path, "rb") as f:
            sha = hashlib.sha256(f.read()).hexdigest()

        member_specs.append({
            "member_id": f"m01_member_{m_i}",
            "onnx_file": onnx_name,
            "onnx_sha256": sha,
            "layers": _extract_layers(model),
        })

    # Stage A (1-step) & Stage C (vs Rolling Baseline) on strictly held-out bot families & track
    ho_mean, ho_std, ho_dis = predict_ensemble_numpy(models, ds.X[ho_idx], mean, std, t_mean, t_std)
    ho_y = ds.y[ho_idx]
    rolling_ho = ds.X[ho_idx, 10]

    # Cold-start breakdown: 0, 1-3, >3 clean observations
    cold_breakdown = {}
    for label, cond in (
        ("0_clean", ds.n_clean_obs[ho_idx] == 0),
        ("1_to_3_clean", (ds.n_clean_obs[ho_idx] >= 1) & (ds.n_clean_obs[ho_idx] <= 3)),
        ("gt_3_clean", ds.n_clean_obs[ho_idx] > 3),
    ):
        if np.any(cond):
            cold_breakdown[label] = {
                "n": int(np.sum(cond)),
                "m01_mae_norm": round(float(np.mean(np.abs(ho_mean[cond] - ho_y[cond]))), 4),
                "rolling_mae_norm": round(float(np.mean(np.abs(rolling_ho[cond] - ho_y[cond]))), 4),
                "mean_pred_std": round(float(np.mean(ho_std[cond])), 4),
                "coverage_90": round(float(np.mean(np.abs(ho_mean[cond] - ho_y[cond]) <= 1.645 * ho_std[cond])), 4),
            }

    # OOD check: ensemble disagreement increases on out-of-distribution inputs
    ood_x = ds.X[ho_idx[:32]].copy()
    ood_x[:, 10] = 2.40  # extreme OOD pace
    ood_x[:, 12] = 0.65
    _, _, ood_dis = predict_ensemble_numpy(models, ood_x, mean, std, t_mean, t_std)

    # Stage B: Multi-step H=3 rollouts
    rollout_metrics = evaluate_multistep_rollouts(models, ds.rollout_sequences, m02_asset, mean, std, t_mean, t_std)

    # Parity test vectors (raw inputs -> [ensemble_mean, ensemble_total_std]) for Unity C# T07 verification!
    sample_raw = ds.X[ho_idx[:12]]
    s_mean, s_std, _ = predict_ensemble_numpy(models, sample_raw, mean, std, t_mean, t_std)
    parity_vectors = [
        {
            "input": [round(float(v), 6) for v in sample_raw[i]],
            "expected_output": [round(float(s_mean[i]), 6), round(float(s_std[i]), 6)],
        }
        for i in range(len(sample_raw))
    ]

    manifest = {
        "schema_version": 1,
        "model_name": "m01_player_pace_ensemble",
        "model_version": "1.2.0",
        "driver_version": DRIVER_VERSION,
        "track_scope": "MonzaScaled,SilverstoneStyle,TightHairpinCircuit",
        "task": "player_pace_ensemble",
        "input_dim": int(ds.X.shape[1]),
        "output_dim": 2,
        "feature_names": PACE_FEATURE_NAMES,
        "feature_mean": [round(float(v), 7) for v in mean],
        "feature_std": [round(float(v), 7) for v in std],
        "feature_min": [round(float(v), 7) for v in fmin],
        "feature_max": [round(float(v), 7) for v in fmax],
        "temperature": 1.0,
        "target_mean": round(t_mean, 7),
        "target_std": round(t_std, 7),
        "max_latency_ms": 5.0,
        "members": member_specs,
        "parity_vectors": parity_vectors,
    }
    manifest_path = os.path.join(output_dir, "m01_pace_ensemble_manifest.json")
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)

    return {
        "n_train": int(len(tr_idx)),
        "n_val": int(len(va_idx)),
        "n_holdout": int(len(ho_idx)),
        "holdout_1step": {
            "m01_mae_norm": round(float(np.mean(np.abs(ho_mean - ho_y))), 4),
            "rolling_mae_norm": round(float(np.mean(np.abs(rolling_ho - ho_y))), 4),
            "m01_rmse_norm": round(float(np.sqrt(np.mean((ho_mean - ho_y) ** 2))), 4),
            "coverage_90": round(float(np.mean(np.abs(ho_mean - ho_y) <= 1.645 * ho_std)), 4),
            "mean_disagreement": round(float(np.mean(ho_dis)), 4),
            "ood_mean_disagreement": round(float(np.mean(ood_dis)), 4),
        },
        "cold_start_breakdown": cold_breakdown,
        "multistep_rollout_H3": rollout_metrics,
        "onnx_parity_max_diff": float(max(onnx_diffs)),
        "manifest_path": manifest_path,
    }
