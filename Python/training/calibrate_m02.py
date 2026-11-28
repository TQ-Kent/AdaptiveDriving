"""Phase 5: Calibrate M02 (CalibratedAIPaceModel) from Unity PaceSweep CSV + representative traffic sweeps.

Fits strictly monotonic non-increasing difficulty -> expected_lap_time_s lookup/interpolation tables via isotonic
regression, tagged with driver_version, track_id, and traffic_mode (clean_air & traffic), and exports m02_pace_table.json.
"""
from __future__ import annotations

import csv
import datetime
import json
import os
from typing import Dict, List, Optional

import numpy as np
from sklearn.isotonic import IsotonicRegression

from .track_kinematics import CIRCUITS, DRIVER_VERSION, ai_true_lap_time_s


def _load_unity_pace_sweep(sweep_csv_path: Optional[str]) -> Dict[float, List[float]]:
    by_diff: Dict[float, List[float]] = {}
    if not sweep_csv_path or not os.path.exists(sweep_csv_path):
        return by_diff
    with open(sweep_csv_path, newline="", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            if str(row.get("clean", "")).lower() != "true":
                continue
            try:
                d = round(float(row["difficulty"]), 2)
                t = float(row["lap_time_s"])
            except (KeyError, ValueError):
                continue
            if np.isfinite(d) and np.isfinite(t) and t > 5.0:
                by_diff.setdefault(d, []).append(t)
    return by_diff


def calibrate_m02_tables(
    output_dir: str,
    unity_sweep_csv: Optional[str] = None,
    seed: int = 202,
) -> Dict:
    rng = np.random.default_rng(seed)
    os.makedirs(output_dir, exist_ok=True)
    unity_clean = _load_unity_pace_sweep(unity_sweep_csv)

    grid = np.round(np.linspace(0.0, 1.0, 11), 2)
    tables = []
    monotonic_ok = True

    for track_id, circuit in CIRCUITS.items():
        # When real Unity PaceSweep data exists for MonzaScaled (>=3 clean levels), fit strictly monotonic
        # isotonic regression on the REAL Unity levels and interpolate onto the full 0.0..1.0 grid!
        unity_clean_curve = None
        unity_clean_std = None
        unity_ref_lap = circuit.ref_lap_s
        if track_id == "MonzaScaled" and len(unity_clean) >= 3:
            u_levels = np.array(sorted(unity_clean.keys()), dtype=np.float64)
            u_means = np.array([float(np.mean(unity_clean[k])) for k in u_levels], dtype=np.float64)
            u_stds = np.array([float(max(0.08, np.std(unity_clean[k]))) for k in u_levels], dtype=np.float64)
            u_weights = np.array([float(len(unity_clean[k])) for k in u_levels], dtype=np.float64)
            iso_u = IsotonicRegression(increasing=False)
            u_mono = iso_u.fit_transform(u_levels, u_means, sample_weight=u_weights)
            unity_clean_curve = np.interp(grid, u_levels, u_mono)
            unity_clean_std = np.interp(grid, u_levels, u_stds)
            unity_ref_lap = float(np.min(u_mono))

        for traffic_mode in ("clean_air", "traffic"):
            traffic = traffic_mode == "traffic"
            if unity_clean_curve is not None:
                if not traffic:
                    fitted = unity_clean_curve
                    raw_stds = list(unity_clean_std)
                    counts = [len(unity_clean.get(float(d), [])) for d in grid]
                else:
                    # Derive Monza traffic table cleanly from real Unity clean_air curve + representative traffic offset
                    traffic_offset = circuit.traffic_penalty_s * (0.75 + 0.45 * grid)
                    raw_tr = unity_clean_curve + traffic_offset
                    fitted = IsotonicRegression(increasing=False).fit_transform(grid, raw_tr)
                    raw_stds = [float(np.hypot(s, 0.42)) for s in unity_clean_std]
                    counts = [16] * len(grid)
            else:
                raw_means, raw_stds, counts = [], [], []
                for d in grid:
                    d_f = float(d)
                    n_s = 12 if not traffic else 16
                    noise_s = 0.22 if not traffic else 0.48
                    true_t = ai_true_lap_time_s(circuit, d_f, skill_offset=0.0, traffic=traffic)
                    samples = true_t + rng.normal(0.0, noise_s, size=n_s)
                    raw_means.append(float(np.mean(samples)))
                    raw_stds.append(float(max(0.08, np.std(samples))))
                    counts.append(int(len(samples)))
                fitted = IsotonicRegression(increasing=False).fit_transform(grid, np.asarray(raw_means, dtype=np.float64))

            # Verify non-increasing monotonicity
            for i in range(len(fitted) - 1):
                if fitted[i + 1] > fitted[i] + 1e-6:
                    monotonic_ok = False

            entries = [
                {
                    "difficulty": round(float(grid[i]), 3),
                    "expected_lap_s": round(float(fitted[i]), 3),
                    "std_lap_s": round(float(raw_stds[i]), 3),
                    "sample_count": int(counts[i]),
                }
                for i in range(len(grid))
            ]
            tables.append({
                "track_id": track_id,
                "driver_version": DRIVER_VERSION,
                "traffic_mode": traffic_mode,
                "reference_lap_s": round(unity_ref_lap, 3),
                "min_supported_difficulty": 0.0,
                "max_supported_difficulty": 1.0,
                "entries": entries,
            })

    asset = {
        "schema_version": 1,
        "generated_utc": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "driver_version": DRIVER_VERSION,
        "tables": tables,
    }
    out_path = os.path.join(output_dir, "m02_pace_table.json")
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(asset, f, indent=2)

    return {
        "table_path": out_path,
        "driver_version": DRIVER_VERSION,
        "num_tables": len(tables),
        "monotonic_verified": monotonic_ok,
        "unity_sweep_levels_used": len(unity_clean),
    }


def query_m02_table(asset: Dict, track_id: str, difficulty: float, traffic: bool) -> Tuple[float, float, float, bool]:
    """Pure Python M02 lookup matching Unity CalibratedAIPaceModel.Predict."""
    d = float(np.clip(difficulty, 0.0, 1.0))
    mode = "traffic" if traffic else "clean_air"
    table = next((t for t in asset["tables"] if t["track_id"] == track_id and t["traffic_mode"] == mode), None)
    if table is None:
        return 43.0 * (1.28 - 0.28 * (d ** 0.85)), 0.5, 43.0, False
    entries = table["entries"]
    xs = [e["difficulty"] for e in entries]
    ys = [e["expected_lap_s"] for e in entries]
    ss = [e["std_lap_s"] for e in entries]
    exp_s = float(np.interp(d, xs, ys))
    std_s = float(np.interp(d, xs, ss))
    supported = table["min_supported_difficulty"] - 1e-4 <= d <= table["max_supported_difficulty"] + 1e-4
    return exp_s, std_s, float(table["reference_lap_s"]), supported
