"""T03 Consented DatasetBuilder + T04 GroupSplitter & FeaturePipeline.

Builds provenance-labeled datasets for:
  1. Phase 4 Tactical Response Model: merges real Unity AI-vs-AI encounter logs (Unity/Logs/tactical_encounters.csv)
     with multi-driver AI-vs-AI / PlayerBot self-play duel encounters across circuits and bot families.
  2. Phase 5 M01 PlayerPacePredictor Ensemble: generates diverse multi-session weekend trajectories (full practice,
     partial practice, FP1-only, FP3-only, all-practice-skipped cold starts, qualifying, traffic, incidents) across
     distinct bot families with strict family/run/track holdouts.

Human telemetry is NEVER admitted unless an explicit consent flag is supplied.
"""
from __future__ import annotations

import csv
import os
from dataclasses import dataclass
from typing import Dict, List, Optional, Tuple
import numpy as np

from .track_kinematics import (
    BOT_FAMILIES,
    CIRCUITS,
    RESPONSE_NAMES,
    CircuitSpec,
    PlayerBotFamily,
    ai_true_lap_time_s,
    encode_tactical_features,
    sample_true_opponent_response,
)

TACTICAL_FEATURE_NAMES = [
    "opponent_is_ahead", "gap_norm", "self_speed_norm", "opp_speed_norm", "closing_norm",
    "self_side", "opp_side", "corner_side", "brake_in_norm", "curvature_scaled",
    "traffic_norm", "style_p_hold", "style_p_defend", "style_p_late", "style_p_switchback",
    "style_p_yield", "style_confidence", "target_side", "late_braking",
    "is_defend", "is_attack", "is_switchback",
]

PACE_FEATURE_NAMES = [
    "recent_pace_0", "recent_pace_1", "recent_pace_2", "recent_pace_3", "recent_pace_4",
    "real_mask_0", "real_mask_1", "real_mask_2", "real_mask_3", "real_mask_4",
    "rolling_clean_pace", "pace_trend", "pace_variation",
    "track_familiarity_norm", "traffic_active", "is_qualifying",
]


@dataclass
class TacticalDataset:
    X: np.ndarray            # (N, 22) float32
    y: np.ndarray            # (N,) int64 class index in 0..4
    families: List[str]      # opponent bot family or Unity AI driver name
    tracks: List[str]
    run_ids: List[str]
    sources: List[str]       # "unity_ai_vs_ai" | "sim_ai_vs_ai"
    is_holdout: np.ndarray   # (N,) bool (strict bot-family holdout!)


@dataclass
class PaceDataset:
    X: np.ndarray            # (N, 16) float32
    y: np.ndarray            # (N,) float32 target normalized clean next-lap pace (T_player / T_ref)
    families: List[str]
    tracks: List[str]
    run_ids: List[str]
    n_clean_obs: np.ndarray  # (N,) int32 number of real clean observations available
    is_holdout: np.ndarray   # (N,) bool (strict bot-family & track holdout!)
    # Full multi-step sequences for Stage B H=3 rollout evaluation
    rollout_sequences: List[Dict]


def _load_unity_encounters(unity_logs_dir: Optional[str]) -> Tuple[List[np.ndarray], List[int], List[str], List[str], List[str], List[str]]:
    """Ingests real AI-vs-AI encounter rows from Unity/Logs/encounters_archive/*.csv and tactical_encounters.csv (deduplicated)."""
    xs, ys, fams, trks, runs, srcs = [], [], [], [], [], []
    if not unity_logs_dir:
        return xs, ys, fams, trks, runs, srcs

    csv_paths: List[Tuple[str, str]] = []
    archive_dir = os.path.join(unity_logs_dir, "encounters_archive")
    if os.path.isdir(archive_dir):
        for fn in sorted(os.listdir(archive_dir)):
            if fn.endswith(".csv"):
                csv_paths.append((os.path.join(archive_dir, fn), os.path.splitext(fn)[0]))
    latest_path = os.path.join(unity_logs_dir, "tactical_encounters.csv")
    if os.path.exists(latest_path):
        csv_paths.append((latest_path, "unity_latest_run"))

    resp_to_idx = {name: i for i, name in enumerate(RESPONSE_NAMES)}
    seen_keys = set()

    for path, run_tag in csv_paths:
        with open(path, newline="", encoding="utf-8") as f:
            for row in csv.DictReader(f):
                obs = row.get("observed_response", "")
                if obs not in resp_to_idx:
                    continue
                dedup_key = (row.get("encounter_id", ""), row.get("t", ""), row.get("driver_id", ""), row.get("opponent_id", ""))
                if dedup_key in seen_keys:
                    continue
                try:
                    self_v = float(row["self_speed_ms"])
                    if self_v < 10.0:
                        continue  # skip stopped/reset-artifact rows
                    supp = int(row.get("style_support", 0))
                    unc = 1.0 / (1.0 + max(0, supp) * 0.35)
                    style_p = [
                        float(row["style_p_hold"]), float(row["style_p_defend"]),
                        float(row["style_p_late"]), float(row["style_p_switchback"]),
                        float(row["style_p_yield"]),
                    ]
                    # Use proposed_maneuver when aborted late or executed_maneuver so model learns action-conditioned reactions
                    maneuver = row.get("proposed_maneuver") or row.get("executed_maneuver", "HoldLine")
                    feat = encode_tactical_features(
                        opponent_is_ahead=(row["opponent_is_ahead"] == "1"),
                        gap_m=float(row["gap_m"]),
                        self_speed_ms=self_v,
                        opp_speed_ms=float(row["opp_speed_ms"]),
                        closing_ms=float(row["closing_ms"]),
                        self_side=float(row["self_side"]),
                        opp_side=float(row["opp_side"]),
                        corner_side=float(row["corner_side"]),
                        brake_in_m=float(row["brake_in_m"]),
                        curvature=float(row["curvature"]),
                        traffic_count=int(row["traffic_count"]),
                        style_probs=style_p,
                        style_uncertainty=unc,
                        target_side=float(row["target_side"]),
                        late_braking=(row["late_braking"] == "1"),
                        maneuver=maneuver,
                    )
                except (KeyError, ValueError):
                    continue
                if not np.all(np.isfinite(feat)):
                    continue
                seen_keys.add(dedup_key)
                xs.append(feat)
                ys.append(resp_to_idx[obs])
                fams.append("UnityAI_" + row.get("opponent_id", "unknown"))
                trks.append(row.get("track_id", "MonzaScaled"))
                runs.append(run_tag)
                srcs.append("unity_ai_vs_ai")
    return xs, ys, fams, trks, runs, srcs


def build_tactical_dataset(
    seed: int = 42,
    encounters_per_family: int = 1600,
    unity_logs_dir: Optional[str] = None,
) -> TacticalDataset:
    """Generates AI-vs-AI self-play + bot duel encounters merged with any live Unity AI-vs-AI encounters."""
    rng = np.random.default_rng(seed)
    xs, ys, fams, trks, runs, srcs = _load_unity_encounters(unity_logs_dir)
    holdout_flags = [False] * len(xs)

    neutral_prior = np.array([1.8, 1.1, 0.9, 0.7, 0.5], dtype=np.float64)
    circuit_list = list(CIRCUITS.values())

    for fam_name, bot in BOT_FAMILIES.items():
        # Simulate multiple multi-encounter stints so Bayesian style estimates span n=0 (prior) through n=12!
        n_runs = max(12, encounters_per_family // 16)
        per_run = max(8, encounters_per_family // n_runs)
        for r_idx in range(n_runs):
            circuit = circuit_list[r_idx % len(circuit_list)]
            run_id = f"{fam_name}_r{r_idx}"
            counts_ahead = neutral_prior.copy()
            counts_behind = neutral_prior.copy()
            obs_ahead = 0
            obs_behind = 0

            for _ in range(per_run):
                opp_is_ahead = bool(rng.random() < 0.55)
                corner_side = float(rng.choice([-1.0, 1.0, 0.0], p=[0.42, 0.42, 0.16]))
                gap_mag = float(rng.uniform(4.0, 26.0))
                gap_m = gap_mag if opp_is_ahead else -gap_mag
                self_v = float(rng.uniform(38.0, 82.0))
                closing = float(rng.uniform(-4.0, 14.0))
                opp_v = float(np.clip(self_v - closing if opp_is_ahead else self_v + closing, 28.0, 90.0))
                self_side = float(rng.choice([-1.0, 0.0, 1.0], p=[0.18, 0.64, 0.18]))
                opp_side = float(rng.choice([-1.0, 0.0, 1.0], p=[0.20, 0.60, 0.20]))
                brake_in = float(rng.uniform(22.0, 145.0)) if corner_side != 0.0 else 180.0
                curv = float(rng.uniform(0.008, 0.028)) * (corner_side if corner_side != 0.0 else 0.0)
                traffic = int(rng.choice([0, 1, 2], p=[0.65, 0.28, 0.07]))

                # Sample diverse legal executed AI maneuvers from comparable simulator starts (Phase 4 requirement!)
                if not opp_is_ahead:
                    if corner_side != 0.0 and rng.random() < 0.50:
                        maneuver, target_side, late = "DefendInside", corner_side, False
                    else:
                        maneuver, target_side, late = "HoldLine", 0.0, False
                else:
                    if corner_side == 0.0:
                        maneuver = str(rng.choice(["PassStraight", "HoldLine"]))
                        target_side = float(rng.choice([-1.0, 1.0])) if maneuver == "PassStraight" else 0.0
                        late = False
                    else:
                        maneuver = str(rng.choice(["AttackInside", "AttackOutside", "Switchback", "HoldLine"], p=[0.35, 0.22, 0.25, 0.18]))
                        if maneuver == "AttackInside":
                            target_side, late = corner_side, True
                        elif maneuver == "AttackOutside":
                            target_side, late = -corner_side, False
                        elif maneuver == "Switchback":
                            target_side, late = -corner_side, False
                        else:
                            target_side, late = 0.0, False

                counts = counts_ahead if opp_is_ahead else counts_behind
                n_obs = obs_ahead if opp_is_ahead else obs_behind
                style_p = counts / counts.sum()
                style_unc = 1.0 / (1.0 + n_obs * 0.35)

                feat = encode_tactical_features(
                    opponent_is_ahead=opp_is_ahead, gap_m=gap_m, self_speed_ms=self_v,
                    opp_speed_ms=opp_v, closing_ms=closing, self_side=self_side, opp_side=opp_side,
                    corner_side=corner_side, brake_in_m=brake_in, curvature=curv, traffic_count=traffic,
                    style_probs=style_p, style_uncertainty=style_unc, target_side=target_side,
                    late_braking=late, maneuver=maneuver,
                )

                obs_resp = sample_true_opponent_response(
                    rng, bot, opp_is_ahead, gap_m, closing, brake_in, curv, maneuver, target_side, corner_side
                )
                xs.append(feat)
                ys.append(obs_resp)
                fams.append(fam_name)
                trks.append(circuit.track_id)
                runs.append(run_id)
                srcs.append("sim_ai_vs_ai")
                holdout_flags.append(bot.holdout)

                # Update Bayesian posterior strictly from observed reality (mirroring PlayerStyleEstimator)
                counts[:] = neutral_prior + (counts - neutral_prior) * 0.96
                counts[obs_resp] += 1.0
                if opp_is_ahead:
                    obs_ahead += 1
                else:
                    obs_behind += 1

    return TacticalDataset(
        X=np.asarray(xs, dtype=np.float32),
        y=np.asarray(ys, dtype=np.int64),
        families=fams,
        tracks=trks,
        run_ids=runs,
        sources=srcs,
        is_holdout=np.asarray(holdout_flags, dtype=bool),
    )


def build_pace_dataset(
    seed: int = 101,
    runs_per_family: int = 70,
    held_out_track: str = "TightHairpinCircuit",
) -> PaceDataset:
    """Generates T02/T03/T04 synthetic weekend pace sequences with cold-start masking & strict holdouts."""
    rng = np.random.default_rng(seed)
    xs, ys, fams, trks, runs, n_cleans, holdouts = [], [], [], [], [], [], []
    rollout_sequences: List[Dict] = []

    circuit_list = list(CIRCUITS.values())

    for fam_name, bot in BOT_FAMILIES.items():
        for r_idx in range(runs_per_family):
            circuit = circuit_list[r_idx % len(circuit_list)]
            run_id = f"{fam_name}_{circuit.track_id}_{r_idx}"
            is_hold = bot.holdout or (circuit.track_id == held_out_track)

            # Per-player skill & familiarity variation within the bot family
            player_offset = float(rng.normal(0.0, 0.045))
            base_norm = float(np.clip(bot.base_pace_norm + player_offset, 0.93, 1.38))
            track_fam_start = int(rng.integers(0, 16))
            n_laps = 10

            # Generate true underlying clean lap trajectory (with learning, drift, and correlated noise)
            true_clean_norms = []
            traffic_flags = []
            quali_flags = []
            corr_err = 0.0
            for lap in range(n_laps):
                corr_err = 0.35 * corr_err + float(rng.normal(0.0, bot.lap_noise_std))
                learning = bot.learning_rate_per_lap * min(lap, 6)
                drift = bot.pace_drift_per_lap * max(0, lap - 4)
                is_quali = (lap == 3)
                is_traffic = bool((not is_quali) and (rng.random() < 0.25))
                quali_gain = -0.008 if is_quali else 0.0
                traffic_pen = (bot.traffic_sensitivity_s / circuit.ref_lap_s) if is_traffic else 0.0
                lap_norm = float(np.clip(base_norm + learning + drift + quali_gain + traffic_pen + corr_err, 0.88, 1.50))
                true_clean_norms.append(lap_norm)
                traffic_flags.append(is_traffic)
                quali_flags.append(is_quali)

            # Simulate practice-skip / cold-start observation patterns (0, 1-2, or full clean history!)
            skip_mode = int(rng.choice([0, 1, 2], p=[0.20, 0.25, 0.55]))  # 0=skip all practice, 1=partial, 2=full
            observed_history: List[float] = []

            for lap in range(n_laps - 1):
                # Decide whether this lap was observed cleanly (practice skipped => not observed!)
                if skip_mode == 0 and lap < 3:
                    pass
                elif skip_mode == 1 and lap in (0, 2):
                    pass
                else:
                    if rng.random() > bot.incident_prob:
                        observed_history.insert(0, true_clean_norms[lap])
                        if len(observed_history) > 5:
                            observed_history.pop()

                # Build 16-feature vector matching Unity `AnalyticalTransitionModel.EncodeM01Features`!
                recent = [0.0] * 5
                masks = [0.0] * 5
                for i, v in enumerate(observed_history[:5]):
                    recent[i] = v
                    masks[i] = 1.0

                n_eff = len(observed_history)
                prior = 1.10
                if n_eff > 0:
                    rec_mean = float(np.mean(observed_history))
                    rolling = (n_eff / (n_eff + 3.0)) * rec_mean + (3.0 / (n_eff + 3.0)) * prior
                    trend = float(observed_history[0] - observed_history[-1]) if n_eff >= 2 else 0.0
                    var_n = float(np.std(observed_history)) if n_eff >= 2 else 0.03
                else:
                    rolling, trend, var_n = prior, 0.0, 0.05

                feat = np.array([
                    recent[0], recent[1], recent[2], recent[3], recent[4],
                    masks[0], masks[1], masks[2], masks[3], masks[4],
                    rolling,
                    float(np.clip(trend, -0.25, 0.25)),
                    float(np.clip(var_n, 0.0, 0.30)),
                    float(np.clip((track_fam_start + n_eff) / 20.0, 0.0, 1.0)),
                    1.0 if traffic_flags[lap + 1] else 0.0,
                    1.0 if quali_flags[lap + 1] else 0.0,
                ], dtype=np.float32)

                xs.append(feat)
                ys.append(true_clean_norms[lap + 1])
                fams.append(fam_name)
                trks.append(circuit.track_id)
                runs.append(run_id)
                n_cleans.append(n_eff)
                holdouts.append(is_hold)

                # Store 3-step rollout benchmark cases (Stage B H=3 evaluation)
                if lap == 4 and len(true_clean_norms) >= 8:
                    rollout_sequences.append({
                        "run_id": run_id,
                        "family": fam_name,
                        "track_id": circuit.track_id,
                        "ref_lap_s": circuit.ref_lap_s,
                        "is_holdout": is_hold,
                        "n_clean": n_eff,
                        "init_features": feat.copy(),
                        "rolling_est": rolling,
                        "future_norms": true_clean_norms[lap + 1: lap + 4],
                        "future_traffic": traffic_flags[lap + 1: lap + 4],
                        "future_quali": quali_flags[lap + 1: lap + 4],
                        "track_fam": track_fam_start + n_eff,
                    })

    return PaceDataset(
        X=np.asarray(xs, dtype=np.float32),
        y=np.asarray(ys, dtype=np.float32),
        families=fams,
        tracks=trks,
        run_ids=runs,
        n_clean_obs=np.asarray(n_cleans, dtype=np.int32),
        is_holdout=np.asarray(holdouts, dtype=bool),
        rollout_sequences=rollout_sequences,
    )


def fit_standardizer(X_train: np.ndarray) -> Tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
    """T04: fits normalization & domain support bounds STRICTLY on the training split (zero leakage)."""
    mean = X_train.mean(axis=0).astype(np.float32)
    std = np.maximum(1e-5, X_train.std(axis=0)).astype(np.float32)
    fmin = X_train.min(axis=0).astype(np.float32)
    fmax = X_train.max(axis=0).astype(np.float32)
    return mean, std, fmin, fmax


def apply_standardizer(X: np.ndarray, mean: np.ndarray, std: np.ndarray) -> np.ndarray:
    return np.clip((X - mean) / np.maximum(1e-6, std), -8.0, 8.0).astype(np.float32)


def group_train_val_split(run_ids: Sequence[str], is_holdout: np.ndarray, val_frac: float = 0.2, seed: int = 7) -> Tuple[np.ndarray, np.ndarray, np.ndarray]:
    """T04 leakage-resistant split: keeps entire runs together; holdout families/tracks are strictly isolated."""
    rng = np.random.default_rng(seed)
    non_hold_runs = sorted({rid for rid, h in zip(run_ids, is_holdout) if not h})
    rng.shuffle(non_hold_runs)
    n_val = max(1, int(round(len(non_hold_runs) * val_frac)))
    val_set = set(non_hold_runs[:n_val])

    train_idx, val_idx, hold_idx = [], [], []
    for i, (rid, h) in enumerate(zip(run_ids, is_holdout)):
        if h:
            hold_idx.append(i)
        elif rid in val_set:
            val_idx.append(i)
        else:
            train_idx.append(i)
    return np.asarray(train_idx, dtype=np.int64), np.asarray(val_idx, dtype=np.int64), np.asarray(hold_idx, dtype=np.int64)
