"""T06 Validation & ReleaseGate + T07 CPU latency & parity checks + artifact deployment to Unity StreamingAssets.

Run directly:
    python -m training.release_gate
"""
from __future__ import annotations

import json
import os
import shutil
import time
from typing import Dict, List

import numpy as np
import onnxruntime as ort

from .calibrate_m02 import calibrate_m02_tables
from .train_m01_pace import train_and_export_m01_ensemble
from .train_tactical_response import train_and_export_tactical_response


def benchmark_onnx_latency_ms(onnx_path: str, input_dim: int, runs: int = 300) -> Dict[str, float]:
    sess = ort.InferenceSession(onnx_path, providers=["CPUExecutionProvider"])
    in_name = sess.get_inputs()[0].name
    x = np.zeros((1, input_dim), dtype=np.float32)
    # Warm up
    for _ in range(25):
        sess.run(None, {in_name: x})
    times = []
    for _ in range(runs):
        t0 = time.perf_counter()
        sess.run(None, {in_name: x})
        times.append((time.perf_counter() - t0) * 1000.0)
    arr = np.asarray(times, dtype=np.float64)
    return {
        "p50_ms": round(float(np.percentile(arr, 50)), 4),
        "p95_ms": round(float(np.percentile(arr, 95)), 4),
        "p99_ms": round(float(np.percentile(arr, 99)), 4),
    }


def run_full_training_and_release_gate(
    artifacts_dir: str,
    unity_streaming_dir: str,
    unity_logs_dir: str | None = None,
    unity_sweep_csv: str | None = None,
    seed: int = 42,
) -> Dict:
    os.makedirs(artifacts_dir, exist_ok=True)
    os.makedirs(unity_streaming_dir, exist_ok=True)

    # Phase 4: Tactical response MLP
    tac_metrics = train_and_export_tactical_response(
        output_dir=artifacts_dir, unity_logs_dir=unity_logs_dir, seed=seed
    )
    tac_latency = benchmark_onnx_latency_ms(tac_metrics["onnx_path"], input_dim=22)

    # Phase 5a: M02 monotonic calibration tables
    m02_metrics = calibrate_m02_tables(
        output_dir=artifacts_dir, unity_sweep_csv=unity_sweep_csv, seed=seed + 160
    )

    # Phase 5b: M01 3-MLP ensemble + M03 rollout validation
    m01_metrics = train_and_export_m01_ensemble(
        output_dir=artifacts_dir, m02_table_path=m02_metrics["table_path"], seed=seed + 59
    )
    m01_latency = benchmark_onnx_latency_ms(os.path.join(artifacts_dir, "m01_member_0.onnx"), input_dim=16)

    # T06 Release Gate checks (strictly enforced before deploying to Unity!)
    gates: List[Dict] = [
        {
            "gate": "tactical_neural_beats_or_matches_template_nll",
            "passed": tac_metrics["neural_holdout"]["nll"] <= tac_metrics["template_holdout"]["nll"],
            "detail": f"neural_nll={tac_metrics['neural_holdout']['nll']} vs template_nll={tac_metrics['template_holdout']['nll']}",
        },
        {
            "gate": "tactical_calibration_ece_under_0_08",
            "passed": tac_metrics["neural_holdout"]["ece"] <= 0.08,
            "detail": f"ece={tac_metrics['neural_holdout']['ece']}",
        },
        {
            "gate": "tactical_onnx_parity_under_1e-5",
            "passed": tac_metrics["onnx_parity_max_diff"] < 1e-5,
            "detail": f"max_diff={tac_metrics['onnx_parity_max_diff']:.2e}",
        },
        {
            "gate": "tactical_cpu_p99_under_2ms",
            "passed": tac_latency["p99_ms"] < 2.0,
            "detail": f"p99={tac_latency['p99_ms']}ms",
        },
        {
            "gate": "m02_monotonic_calibration",
            "passed": bool(m02_metrics["monotonic_verified"]),
            "detail": f"tables={m02_metrics['num_tables']}, driver={m02_metrics['driver_version']}",
        },
        {
            "gate": "m01_1step_beats_rolling_baseline",
            "passed": m01_metrics["holdout_1step"]["m01_mae_norm"] <= m01_metrics["holdout_1step"]["rolling_mae_norm"],
            "detail": f"m01={m01_metrics['holdout_1step']['m01_mae_norm']} vs rolling={m01_metrics['holdout_1step']['rolling_mae_norm']}",
        },
        {
            "gate": "m01_3step_beats_rolling_baseline",
            "passed": m01_metrics["multistep_rollout_H3"]["m01_mae_s_by_step"][-1] <= m01_metrics["multistep_rollout_H3"]["rolling_baseline_mae_s_by_step"][-1],
            "detail": f"step3 m01={m01_metrics['multistep_rollout_H3']['m01_mae_s_by_step'][-1]}s vs rolling={m01_metrics['multistep_rollout_H3']['rolling_baseline_mae_s_by_step'][-1]}s",
        },
        {
            "gate": "m01_uncertainty_coverage_sane",
            "passed": 0.75 <= m01_metrics["holdout_1step"]["coverage_90"] <= 0.99,
            "detail": f"coverage_90={m01_metrics['holdout_1step']['coverage_90']}",
        },
        {
            "gate": "m01_ood_disagreement_rises",
            "passed": m01_metrics["holdout_1step"]["ood_mean_disagreement"] > m01_metrics["holdout_1step"]["mean_disagreement"],
            "detail": f"ood={m01_metrics['holdout_1step']['ood_mean_disagreement']} vs id={m01_metrics['holdout_1step']['mean_disagreement']}",
        },
        {
            "gate": "m01_onnx_parity_under_1e-5",
            "passed": m01_metrics["onnx_parity_max_diff"] < 1e-5,
            "detail": f"max_diff={m01_metrics['onnx_parity_max_diff']:.2e}",
        },
        {
            "gate": "m01_cpu_p99_under_5ms",
            "passed": m01_latency["p99_ms"] * 3.0 < 5.0,
            "detail": f"3x_member_p99={m01_latency['p99_ms'] * 3.0:.3f}ms",
        },
    ]

    all_passed = all(g["passed"] for g in gates)
    deployed_files = []
    if all_passed:
        files_to_deploy = [
            "tactical_response_manifest.json",
            "tactical_response.onnx",
            "m02_pace_table.json",
            "m01_pace_ensemble_manifest.json",
            "m01_member_0.onnx",
            "m01_member_1.onnx",
            "m01_member_2.onnx",
        ]
        for fname in files_to_deploy:
            src = os.path.join(artifacts_dir, fname)
            dst = os.path.join(unity_streaming_dir, fname)
            if os.path.exists(src):
                shutil.copy2(src, dst)
                deployed_files.append(dst)

    report = {
        "release_approved": all_passed,
        "gates": gates,
        "tactical_response": {**tac_metrics, "cpu_latency": tac_latency},
        "m02_calibration": m02_metrics,
        "m01_pace_ensemble": {**m01_metrics, "cpu_latency_per_member": m01_latency},
        "deployed_to_unity": deployed_files,
    }
    report_path = os.path.join(artifacts_dir, "release_gate_report.json")
    with open(report_path, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2)
    return report


if __name__ == "__main__":
    py_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    repo_root = os.path.dirname(py_root)
    artifacts = os.path.join(py_root, "artifacts")
    unity_streaming = os.path.join(repo_root, "Unity", "Assets", "StreamingAssets", "AIModels")
    unity_logs = os.path.join(repo_root, "Unity", "Logs")
    sweep_csv = os.path.join(py_root, "calibration", "ai_pace_sweep.csv")

    rep = run_full_training_and_release_gate(
        artifacts_dir=artifacts,
        unity_streaming_dir=unity_streaming,
        unity_logs_dir=unity_logs,
        unity_sweep_csv=sweep_csv,
    )
    print(f"Release approved: {rep['release_approved']}")
    for g in rep["gates"]:
        status = "PASS" if g["passed"] else "FAIL"
        print(f"  [{status}] {g['gate']}: {g['detail']}")
    print(f"Deployed {len(rep['deployed_to_unity'])} artifacts to {unity_streaming}")
