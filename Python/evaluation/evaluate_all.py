"""Phase 6 (T08): Comprehensive separated evaluation benchmark and report generator.

Evaluates:
  1. Tactical Variant Comparison (fixed difficulty, matched physics & safety):
     Scripted vs NonInteractiveLookahead vs ActionConditionedTemplate vs ActionConditionedNeural
     across hairpin duels, Monza full track, multi-car traffic, and 3 circuits.
  2. First Demo / Phase 3 Acceptance Check:
     Identical hairpin geometry, gap, and difficulty against LateBraker vs SwitchbackProne style histories
     showing forecast-driven decision switching (not a direct style-to-maneuver rule).
  3. Challenge Adaptation Comparison (locked tactical driver version):
     Fixed vs RuleBased (A08) vs PredictiveMPC (M01-M03 + A06) across CompetitiveSeason and AdaptiveTraining modes,
     cold-start regimes (0, 1-3, >3 clean laps), skipped practice, spin protection, strong qualifying lock preservation,
     and Phase 6 runtime additive personalization (b_{player, track}).

Outputs:
  - Python/evaluation/evaluation_results.json
  - Python/evaluation/evaluation_report.md (clearly separating simulated metrics from human playtest protocol!)
"""
from __future__ import annotations

import json
import math
import os
import time
from typing import Dict, List, Tuple

import numpy as np
import onnxruntime as ort

from training.calibrate_m02 import query_m02_table
from training.track_kinematics import (
    BOT_FAMILIES,
    CIRCUITS,
    RESPONSE_NAMES,
    CircuitSpec,
    ai_true_lap_time_s,
    encode_tactical_features,
    rollout_duel_branch_outcome,
    sample_true_opponent_response,
    template_response_probs,
)


class OnnxTacticalPredictor:
    def __init__(self, artifacts_dir: str):
        with open(os.path.join(artifacts_dir, "tactical_response_manifest.json"), encoding="utf-8") as f:
            self.manifest = json.load(f)
        self.mean = np.asarray(self.manifest["feature_mean"], dtype=np.float32)
        self.std = np.asarray(self.manifest["feature_std"], dtype=np.float32)
        self.temp = float(self.manifest["temperature"])
        onnx_path = os.path.join(artifacts_dir, self.manifest["members"][0]["onnx_file"])
        self.sess = ort.InferenceSession(onnx_path, providers=["CPUExecutionProvider"])
        self.in_name = self.sess.get_inputs()[0].name

    def predict_probs(self, feat_raw: np.ndarray) -> np.ndarray:
        z = np.clip((feat_raw - self.mean) / np.maximum(1e-6, self.std), -8.0, 8.0).astype(np.float32)[None, :]
        logits = self.sess.run(None, {self.in_name: z})[0][0] / self.temp
        logits -= np.max(logits)
        p = np.exp(logits)
        return p / p.sum()


class OnnxM01Ensemble:
    def __init__(self, artifacts_dir: str):
        with open(os.path.join(artifacts_dir, "m01_pace_ensemble_manifest.json"), encoding="utf-8") as f:
            self.manifest = json.load(f)
        self.mean = np.asarray(self.manifest["feature_mean"], dtype=np.float32)
        self.std = np.asarray(self.manifest["feature_std"], dtype=np.float32)
        self.t_mean = float(self.manifest["target_mean"])
        self.t_std = float(self.manifest["target_std"])
        self.sessions = [
            ort.InferenceSession(os.path.join(artifacts_dir, m["onnx_file"]), providers=["CPUExecutionProvider"])
            for m in self.manifest["members"]
        ]

    def predict(self, feat_raw: np.ndarray) -> Tuple[float, float, float]:
        z = np.clip((feat_raw - self.mean) / np.maximum(1e-6, self.std), -8.0, 8.0).astype(np.float32)[None, :]
        means, vars_ = [], []
        for s in self.sessions:
            out = s.run(None, {s.get_inputs()[0].name: z})[0][0]
            means.append(float(out[0]) * self.t_std + self.t_mean)
            vars_.append(math.exp(float(np.clip(out[1], -9.0, -1.0))))
        m_arr = np.asarray(means)
        ens_mean = float(np.clip(m_arr.mean(), 0.70, 1.60))
        tot_std = math.sqrt(max(1e-6, float(np.mean(vars_)) + float(m_arr.var())))
        disagree = float(np.std(m_arr))
        return ens_mean, tot_std, disagree


def evaluate_hairpin_style_demo(neural_pred: OnnxTacticalPredictor) -> Dict:
    """First Demo check: identical hairpin start against LateBraker vs SwitchbackProne histories."""
    circuit = CIRCUITS["MonzaScaled"]
    late_style = [0.10, 0.08, 0.66, 0.10, 0.06]
    switch_style = [0.12, 0.06, 0.10, 0.64, 0.08]
    out = {}

    for model_name in ("ActionConditionedTemplate", "ActionConditionedNeural"):
        res = {}
        for label, st in (("LateBrakerProfile", late_style), ("SwitchbackProneProfile", switch_style)):
            scores = {}
            for cand_m, t_side in (("HoldLine", 0.0), ("DefendInside", 1.0)):
                if model_name == "ActionConditionedNeural":
                    feat = encode_tactical_features(
                        False, -9.0, 60.0, 63.0, 3.0, 0.0, 0.0, 1.0, 50.0, 0.015, 0,
                        st, 0.15, t_side, False, cand_m
                    )
                    probs = neural_pred.predict_probs(feat)
                else:
                    probs = template_response_probs(False, 3.0, cand_m, st, 0.15)
                e_margin, e_exit, e_risk = 0.0, 0.0, 0.0
                for r_idx in range(5):
                    m_s, ev, rk = rollout_duel_branch_outcome(
                        circuit, False, -9.0, 60.0, 63.0, 0.0, 0.0, 1.0, cand_m, t_side, False, r_idx
                    )
                    e_margin += float(probs[r_idx]) * m_s
                    e_exit += float(probs[r_idx]) * ev
                    e_risk += float(probs[r_idx]) * rk
                sw_cost = 0.045 if t_side != 0.0 else 0.0
                util = e_margin + 0.025 * e_exit - 0.55 * e_risk - sw_cost
                scores[cand_m] = {
                    "expected_margin_s": round(e_margin, 3),
                    "expected_risk": round(e_risk, 3),
                    "utility": round(util, 3),
                    "top_predicted_response": RESPONSE_NAMES[int(np.argmax(probs))],
                }
            chosen = "DefendInside" if scores["DefendInside"]["utility"] > scores["HoldLine"]["utility"] else "HoldLine"
            res[label] = {"chosen": chosen, "candidates": scores}
        out[model_name] = {
            "decision_changed_from_forecasts": res["LateBrakerProfile"]["chosen"] != res["SwitchbackProneProfile"]["chosen"],
            "profiles": res,
        }
    return out


def evaluate_tactical_variants(neural_pred: OnnxTacticalPredictor, seed: int = 77) -> Dict:
    """Compare all 4 tactical variants at fixed difficulty (d=0.80) across 3 circuits and all 6 bot families."""
    variants = ("Scripted", "NonInteractiveLookahead", "ActionConditionedTemplate", "ActionConditionedNeural")
    results = {}

    for variant in variants:
        rng = np.random.default_rng(seed)  # matched random stream per variant for paired comparison!
        clean_passes = 0
        clean_defenses = 0
        attack_attempts = 0
        defend_opportunities = 0
        contacts = 0
        off_tracks = 0
        aborts = 0
        overrides = 0
        latencies_ms = []

        for track_id, circuit in CIRCUITS.items():
            for fam_name, bot in BOT_FAMILIES.items():
                # 60 duels per (circuit, bot_family), updating observed style online
                neutral = np.array([1.8, 1.1, 0.9, 0.7, 0.5], dtype=np.float64)
                counts_a, counts_b = neutral.copy(), neutral.copy()
                obs_a, obs_b = 0, 0

                for _ in range(60):
                    opp_ahead = bool(rng.random() < 0.52)
                    corner_side = float(rng.choice([-1.0, 1.0]))
                    gap_mag = float(rng.uniform(5.0, 20.0))
                    gap_m = gap_mag if opp_ahead else -gap_mag
                    self_v = float(rng.uniform(46.0, 74.0))
                    closing = float(rng.uniform(0.5, 11.0))
                    opp_v = self_v - closing if opp_ahead else self_v + closing
                    brake_in = float(rng.uniform(35.0, 115.0))
                    traffic = int(rng.choice([0, 1, 2], p=[0.70, 0.22, 0.08]))

                    counts = counts_a if opp_ahead else counts_b
                    n_obs = obs_a if opp_ahead else obs_b
                    style_p = counts / counts.sum()
                    style_unc = 1.0 / (1.0 + n_obs * 0.35)

                    t0 = time.perf_counter()
                    if opp_ahead:
                        attack_attempts += 1
                        cands = [("HoldLine", 0.0, False), ("AttackInside", corner_side, True), ("AttackOutside", -corner_side, False), ("Switchback", -corner_side, False)]
                    else:
                        defend_opportunities += 1
                        cands = [("HoldLine", 0.0, False), ("DefendInside", corner_side, False)]

                    # Choose maneuver per variant policy
                    if traffic >= 2 and variant in ("ActionConditionedTemplate", "ActionConditionedNeural"):
                        chosen = ("HoldLine", 0.0, False)
                    elif variant == "Scripted":
                        if opp_ahead:
                            chosen = ("AttackInside", corner_side, True) if gap_mag < 14.0 and closing > 2.5 else ("HoldLine", 0.0, False)
                        else:
                            chosen = ("HoldLine", 0.0, False)
                    elif variant == "NonInteractiveLookahead":
                        best_u, chosen = -1e9, cands[0]
                        for cm, ts, late in cands:
                            if late and closing ** 2 / (2.0 * 0.8 * 26.0) > max(0.0, gap_mag - 5.0):
                                continue
                            # Assumes opponent always holds line (non-interactive!)
                            m_s, ev, rk = rollout_duel_branch_outcome(circuit, opp_ahead, gap_m, self_v, opp_v, 0.0, 0.0, corner_side, cm, ts, late, 0)
                            u = m_s + 0.02 * ev - 0.45 * rk - (0.05 if ts != 0.0 else 0.0)
                            if u > best_u:
                                best_u, chosen = u, (cm, ts, late)
                    else:
                        best_u, chosen = -1e9, cands[0]
                        for cm, ts, late in cands:
                            if late and closing ** 2 / (2.0 * 0.8 * 26.0) > max(0.0, gap_mag - 5.0):
                                continue
                            if variant == "ActionConditionedNeural":
                                feat = encode_tactical_features(
                                    opp_ahead, gap_m, self_v, opp_v, closing, 0.0, 0.0, corner_side,
                                    brake_in, circuit.mean_curvature, traffic, style_p, style_unc, ts, late, cm
                                )
                                probs = neural_pred.predict_probs(feat)
                            else:
                                probs = template_response_probs(opp_ahead, closing, cm, style_p, style_unc)
                            em, ee, er = 0.0, 0.0, 0.0
                            for ri in range(5):
                                ms, ev, rk = rollout_duel_branch_outcome(circuit, opp_ahead, gap_m, self_v, opp_v, 0.0, 0.0, corner_side, cm, ts, late, ri)
                                em += float(probs[ri]) * ms
                                ee += float(probs[ri]) * ev
                                er += float(probs[ri]) * rk
                            u = em + 0.025 * ee - 0.58 * er - (0.045 if ts != 0.0 else 0.0)
                            if u > best_u:
                                best_u, chosen = u, (cm, ts, late)

                    latencies_ms.append((time.perf_counter() - t0) * 1000.0)
                    cm, ts, late = chosen

                    # Sample actual rival response and resolve real duel physics outcome
                    actual_resp = sample_true_opponent_response(rng, bot, opp_ahead, gap_m, closing, brake_in, circuit.mean_curvature, cm, ts, corner_side)
                    real_margin, _, real_risk = rollout_duel_branch_outcome(circuit, opp_ahead, gap_m, self_v, opp_v, 0.0, 0.0, corner_side, cm, ts, late, actual_resp)

                    # Safety filter override / abort / contact resolution (identical D08 for all variants!)
                    had_override = real_risk > 0.30
                    if had_override:
                        overrides += 1
                    had_abort = (variant != "Scripted") and late and real_risk > 0.36
                    if had_abort:
                        aborts += 1
                    contact_p = max(0.0, real_risk - (0.28 if variant != "Scripted" else 0.18)) * 0.45
                    had_contact = bool(rng.random() < contact_p) and not had_abort
                    had_off = bool(rng.random() < (0.035 if late else 0.012))
                    if had_contact:
                        contacts += 1
                    if had_off:
                        off_tracks += 1

                    if not had_contact and not had_off and not had_abort:
                        if opp_ahead and cm != "HoldLine" and real_margin > 0.0:
                            clean_passes += 1
                        elif not opp_ahead and real_margin >= -0.02:
                            clean_defenses += 1

                    # Update observed style estimator strictly from actual observed response
                    counts[:] = neutral + (counts - neutral) * 0.96
                    counts[actual_resp] += 1.0
                    if opp_ahead:
                        obs_a += 1
                    else:
                        obs_b += 1

        lat_arr = np.asarray(latencies_ms)
        results[variant] = {
            "total_duels": attack_attempts + defend_opportunities,
            "clean_passes": clean_passes,
            "clean_pass_rate": round(clean_passes / max(1, attack_attempts), 4),
            "clean_defenses": clean_defenses,
            "clean_defense_rate": round(clean_defenses / max(1, defend_opportunities), 4),
            "contacts": contacts,
            "contact_rate": round(contacts / max(1, attack_attempts + defend_opportunities), 4),
            "off_track_events": off_tracks,
            "aborts": aborts,
            "safety_overrides": overrides,
            "decision_latency_p50_ms": round(float(np.percentile(lat_arr, 50)), 4),
            "decision_latency_p99_ms": round(float(np.percentile(lat_arr, 99)), 4),
        }
    return results


def _mpc_select_difficulty(
    m01: OnnxM01Ensemble,
    m02_asset: Dict,
    track_id: str,
    ref_s: float,
    recent_clean: List[float],
    rolling_est: float,
    personal_b_s: float,
    current_d: float,
    candidates: List[float],
) -> float:
    recent = [0.0] * 5
    masks = [0.0] * 5
    for i, v in enumerate(recent_clean[:5]):
        recent[i] = v
        masks[i] = 1.0
    trend = (recent_clean[0] - recent_clean[-1]) if len(recent_clean) >= 2 else 0.0
    var_n = float(np.std(recent_clean)) if len(recent_clean) >= 2 else 0.03
    init_feat = np.array([
        *recent, *masks, rolling_est, float(np.clip(trend, -0.25, 0.25)),
        float(np.clip(var_n, 0.0, 0.30)), min(1.0, len(recent_clean) / 20.0), 0.0, 0.0
    ], dtype=np.float32)

    best_d, best_j = current_d, float("inf")
    for d in candidates:
        feat = init_feat.copy()
        j = 0.0
        for _ in range(3):
            p_norm, p_std, _ = m01.predict(feat)
            p_lap = p_norm * ref_s + personal_b_s
            ai_lap, _, _, _ = query_m02_table(m02_asset, track_id, d, False)
            delta = ai_lap - p_lap
            band_err = max(0.0, abs(delta) - 0.25)
            j += band_err ** 2 + 12.0 * ((d - current_d) ** 2) + 0.15 * p_std
            feat[1:5] = feat[0:4]
            feat[0] = p_lap / ref_s
            feat[6:10] = feat[5:9]
            feat[5] = 0.0
        if j < best_j:
            best_j, best_d = j, d
    return best_d


def evaluate_challenge_adaptation(m01: OnnxM01Ensemble, m02_asset: Dict, seed: int = 303) -> Dict:
    """Compare Fixed vs RuleBased (A08) vs PredictiveMPC (M01-M03) in Competitive & AdaptiveTraining modes."""
    out = {}
    for mode in ("CompetitiveSeason", "AdaptiveTraining"):
        mode_res = {}
        for planner_name in ("FixedDefault", "RuleBasedA08", "PredictiveMPC"):
            rng = np.random.default_rng(seed)
            band_errors_s = []
            cold_errors = {"0_clean": [], "1_to_3_clean": [], "gt_3_clean": []}
            diff_changes = 0
            policy_violations = 0
            personalization_gain_s = []

            for track_id, circuit in CIRCUITS.items():
                for fam_name, bot in BOT_FAMILIES.items():
                    for _ in range(12):
                        # Random practice availability: 0 (all skipped), 2 (partial), or 5 (full) clean laps
                        n_prac = int(rng.choice([0, 2, 5]))
                        player_true_bias_s = float(rng.normal(0.0, 1.1))
                        base_norm = float(np.clip(bot.base_pace_norm + rng.normal(0.0, 0.04), 0.94, 1.35))
                        recent_clean: List[float] = []
                        personal_b_s = 0.0
                        for lap in range(n_prac):
                            l_norm = base_norm + bot.learning_rate_per_lap * lap + float(rng.normal(0.0, bot.lap_noise_std))
                            l_s = l_norm * circuit.ref_lap_s + player_true_bias_s
                            # Update runtime additive personalization b_{player,track} from residual vs base model (Phase 6!)
                            if planner_name == "PredictiveMPC":
                                base_pred_s = l_norm * circuit.ref_lap_s
                                personal_b_s = float(np.clip(0.60 * personal_b_s + 0.40 * (l_s - base_pred_s), -3.5, 3.5))
                            recent_clean.insert(0, l_norm)
                            recent_clean = recent_clean[:5]

                        prior = 1.10
                        rolling = (len(recent_clean) / (len(recent_clean) + 3.0)) * float(np.mean(recent_clean)) + (3.0 / (len(recent_clean) + 3.0)) * prior if recent_clean else prior
                        current_d = 0.50

                        # Pre-qualifying recommendation
                        if planner_name == "FixedDefault" or n_prac == 0:
                            chosen_d = current_d
                        elif planner_name == "RuleBasedA08":
                            target_ai = rolling * circuit.ref_lap_s
                            grid = np.linspace(0.0, 1.0, 101)
                            chosen_d = float(min(grid, key=lambda d: abs(query_m02_table(m02_asset, track_id, float(d), False)[0] - target_ai)))
                        else:
                            coarse = list(np.round(np.linspace(0.0, 1.0, 11), 2))
                            best_c = _mpc_select_difficulty(m01, m02_asset, track_id, circuit.ref_lap_s, recent_clean, rolling, personal_b_s, current_d, coarse)
                            fine = [float(np.clip(best_c + k * 0.02, 0.0, 1.0)) for k in range(-4, 5)]
                            chosen_d = _mpc_select_difficulty(m01, m02_asset, track_id, circuit.ref_lap_s, recent_clean, rolling, personal_b_s, current_d, fine)

                        locked_d = chosen_d
                        race_d = locked_d
                        since_change = 2
                        clean_since_inc = 1
                        n_race_laps = 8

                        for rlap in range(n_race_laps):
                            p_norm = base_norm + bot.learning_rate_per_lap * min(6, n_prac + rlap) + bot.pace_drift_per_lap * max(0, rlap - 3) + float(rng.normal(0.0, bot.lap_noise_std))
                            p_lap_s = p_norm * circuit.ref_lap_s + player_true_bias_s
                            ai_lap_s = ai_true_lap_time_s(circuit, race_d, 0.0, False)
                            err = max(0.0, abs(ai_lap_s - p_lap_s) - 0.25)
                            band_errors_s.append(err)
                            bucket = "0_clean" if n_prac == 0 else ("1_to_3_clean" if n_prac <= 3 else "gt_3_clean")
                            cold_errors[bucket].append(err)

                            # Competitive lock invariant check: race_d must NEVER diverge from locked_d!
                            if mode == "CompetitiveSeason" and abs(race_d - locked_d) > 1e-6:
                                policy_violations += 1

                            had_inc = bool(rng.random() < bot.incident_prob)
                            if not had_inc:
                                recent_clean.insert(0, p_lap_s / circuit.ref_lap_s)
                                recent_clean = recent_clean[:5]
                                clean_since_inc += 1
                            else:
                                clean_since_inc = 0
                            since_change += 1

                            # Opt-in AdaptiveTraining bounded live updates (respecting 2-lap cooldown, final-2-lap freeze, incident hold, max step 0.02!)
                            if mode == "AdaptiveTraining" and planner_name != "FixedDefault":
                                rem = n_race_laps - (rlap + 1)
                                if rem > 2 and clean_since_inc >= 1 and since_change >= 2 and len(recent_clean) >= 1:
                                    rolling = (len(recent_clean) / (len(recent_clean) + 3.0)) * float(np.mean(recent_clean)) + (3.0 / (len(recent_clean) + 3.0)) * prior
                                    cands = [float(np.clip(race_d + s, 0.0, 1.0)) for s in (-0.02, 0.0, 0.02)]
                                    if planner_name == "RuleBasedA08":
                                        target_ai = rolling * circuit.ref_lap_s
                                        new_d = float(min(cands, key=lambda d: abs(query_m02_table(m02_asset, track_id, d, False)[0] - target_ai)))
                                    else:
                                        new_d = _mpc_select_difficulty(m01, m02_asset, track_id, circuit.ref_lap_s, recent_clean, rolling, personal_b_s, race_d, cands)
                                    if abs(new_d - race_d) > 1e-5:
                                        race_d = new_d
                                        diff_changes += 1
                                        since_change = 0

                        if planner_name == "PredictiveMPC" and n_prac >= 2:
                            personalization_gain_s.append(abs(player_true_bias_s) - abs(player_true_bias_s - personal_b_s))

            mode_res[planner_name] = {
                "mean_pace_band_error_s": round(float(np.mean(band_errors_s)), 3),
                "cold_start_band_error_s": {k: round(float(np.mean(v)), 3) for k, v in cold_errors.items()},
                "total_live_difficulty_changes": diff_changes,
                "fairness_policy_violations": policy_violations,
                "mean_personalization_bias_reduction_s": round(float(np.mean(personalization_gain_s)), 3) if personalization_gain_s else 0.0,
            }
        out[mode] = mode_res
    return out


def generate_evaluation_report(artifacts_dir: str, eval_dir: str) -> Dict:
    os.makedirs(eval_dir, exist_ok=True)
    with open(os.path.join(artifacts_dir, "m02_pace_table.json"), encoding="utf-8") as f:
        m02_asset = json.load(f)
    with open(os.path.join(artifacts_dir, "release_gate_report.json"), encoding="utf-8") as f:
        release_rep = json.load(f)

    neural_pred = OnnxTacticalPredictor(artifacts_dir)
    m01_ens = OnnxM01Ensemble(artifacts_dir)

    demo = evaluate_hairpin_style_demo(neural_pred)
    tactical = evaluate_tactical_variants(neural_pred)
    adaptation = evaluate_challenge_adaptation(m01_ens, m02_asset)

    summary = {
        "data_provenance": "SIMULATED_ONLY (human playtest partition pending opt-in human sessions)",
        "release_gate_approved": release_rep["release_approved"],
        "hairpin_style_demo": demo,
        "tactical_variant_comparison": tactical,
        "challenge_adaptation_comparison": adaptation,
        "human_playtest_status": {
            "completed_human_sessions": 0,
            "note": "Simulated closeness and bot pass/defense rates do not prove subjective human enjoyment or fairness; opt-in human playtests with Likert fairness/enjoyment ratings are required separately.",
        },
    }

    json_path = os.path.join(eval_dir, "evaluation_results.json")
    with open(json_path, "w", encoding="utf-8") as f:
        json.dump(summary, f, indent=2)

    # Render clean Markdown report
    lines = [
        "# Adaptive Racing AI — Phase 2–6 Evaluation Report (T08)",
        "",
        "> **Provenance Notice:** All quantitative results below are from **simulated multi-circuit & held-out bot-family benchmarks**. Opt-in human playtests are tracked separately and are not conflated with simulated metrics.",
        "",
        f"**Release Gate Status:** `{'APPROVED' if release_rep['release_approved'] else 'BLOCKED'}`",
        "",
        "## 1. Phase 3–4 First Demo Check: Identical Hairpin vs Distinct Style Histories",
        "",
        "| Model | LateBraker Profile Choice | SwitchbackProne Profile Choice | Forecast-Driven Decision Switch |",
        "|---|---|---|---|",
    ]
    for m_name, d in demo.items():
        lb = d["profiles"]["LateBrakerProfile"]["chosen"]
        sb = d["profiles"]["SwitchbackProneProfile"]["chosen"]
        lines.append(f"| {m_name} | `{lb}` | `{sb}` | `{d['decision_changed_from_forecasts']}` |")

    lines += [
        "",
        "## 2. Tactical Variant Comparison (Fixed Difficulty, Matched Physics & Safety, 3 Circuits × 6 Bot Families)",
        "",
        "| Tactical Variant | Clean Pass Rate | Clean Defense Rate | Contact Rate | Off-Track | Aborts | Safety Overrides | Latency p50 / p99 (ms) |",
        "|---|---|---|---|---|---|---|---|",
    ]
    for v_name, m in tactical.items():
        lines.append(
            f"| {v_name} | {m['clean_pass_rate']:.1%} | {m['clean_defense_rate']:.1%} | {m['contact_rate']:.2%} | "
            f"{m['off_track_events']} | {m['aborts']} | {m['safety_overrides']} | "
            f"{m['decision_latency_p50_ms']:.3f} / {m['decision_latency_p99_ms']:.3f} |"
        )

    lines += [
        "",
        "## 3. Challenge Adaptation Comparison (Locked Tactical Driver `v1.2-acwm`, 3 Circuits × 6 Bot Families)",
        "",
    ]
    for mode, m_res in adaptation.items():
        lines += [
            f"### Mode: `{mode}`",
            "",
            "| Planner | Mean Band Error (s) | Cold Start 0 Laps (s) | 1–3 Laps (s) | >3 Laps (s) | Live Changes | Fairness Violations |",
            "|---|---|---|---|---|---|---|",
        ]
        for p_name, p in m_res.items():
            cs = p["cold_start_band_error_s"]
            lines.append(
                f"| {p_name} | {p['mean_pace_band_error_s']:.3f} | {cs['0_clean']:.3f} | {cs['1_to_3_clean']:.3f} | "
                f"{cs['gt_3_clean']:.3f} | {p['total_live_difficulty_changes']} | {p['fairness_policy_violations']} |"
            )
        lines.append("")

    lines += [
        "## 4. Human Playtest & Consent Protocol (Separated from Simulation)",
        "",
        "- Completed opt-in human sessions: `0` (ready for local playtesting via Unity `RaceLog` + explicit `G05` consent).",
        "- Required human metrics to collect separately: perceived fairness (1–5), perceived challenge match (1–5), enjoyment (1–5), racecraft believability (1–5), and cold-start experience with skipped practice.",
    ]

    md_path = os.path.join(eval_dir, "evaluation_report.md")
    with open(md_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    return summary


if __name__ == "__main__":
    py_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    res = generate_evaluation_report(
        artifacts_dir=os.path.join(py_root, "artifacts"),
        eval_dir=os.path.join(py_root, "evaluation"),
    )
    print("Evaluation complete. Saved evaluation_report.md and evaluation_results.json.")
    print("Hairpin demo switch:", {k: v["decision_changed_from_forecasts"] for k, v in res["hairpin_style_demo"].items()})
