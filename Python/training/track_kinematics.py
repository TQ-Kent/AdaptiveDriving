"""T01 + T02: Multi-circuit kinematic simulator, Diverse PlayerBot families, and AI-vs-AI tactical duel engine.

Mirrors the shared Unity vehicle physics & Monza-scaled circuit (plus SilverstoneStyle & TightHairpinCircuit for
Phase 6 multi-circuit expansion), enabling fast reproducible AI-vs-AI self-play training data generation and
matched tactical/adaptation evaluation without modifying Unity physics rules.
"""
from __future__ import annotations

import math
from dataclasses import dataclass, field
from typing import Callable, Dict, List, Optional, Sequence, Tuple
import numpy as np

RESPONSE_NAMES = ("HoldLine", "DefendInside", "LateBrake", "Switchback", "YieldBackOut")
RESP_HOLD, RESP_DEFEND, RESP_LATE, RESP_SWITCH, RESP_YIELD = range(5)

DRIVER_VERSION = "v1.2-acwm"


@dataclass(frozen=True)
class CircuitSpec:
    track_id: str
    length_km: float
    ref_lap_s: float
    straight_fraction: float
    mean_curvature: float
    hairpin_apex_v_ms: float
    entry_speed_ms: float
    traffic_penalty_s: float


CIRCUITS: Dict[str, CircuitSpec] = {
    "MonzaScaled": CircuitSpec(
        track_id="MonzaScaled",
        length_km=2.48,
        ref_lap_s=40.6,          # calibrated to real Unity Monza PaceSweep peak clean lap (~40.6s)
        straight_fraction=0.68,
        mean_curvature=0.0085,
        hairpin_apex_v_ms=24.0,
        entry_speed_ms=64.0,
        traffic_penalty_s=0.62,
    ),
    "SilverstoneStyle": CircuitSpec(
        track_id="SilverstoneStyle",
        length_km=3.20,
        ref_lap_s=56.5,
        straight_fraction=0.46,
        mean_curvature=0.0125,
        hairpin_apex_v_ms=28.0,
        entry_speed_ms=68.0,
        traffic_penalty_s=0.78,
    ),
    "TightHairpinCircuit": CircuitSpec(
        track_id="TightHairpinCircuit",
        length_km=2.15,
        ref_lap_s=46.8,
        straight_fraction=0.38,
        mean_curvature=0.0165,
        hairpin_apex_v_ms=19.5,
        entry_speed_ms=58.0,
        traffic_penalty_s=0.95,
    ),
}


@dataclass(frozen=True)
class PlayerBotFamily:
    """T02 Diverse PlayerBot family with distinct tactical tendencies, pace, consistency, learning & traffic sensitivity."""
    family_id: str
    base_pace_norm: float        # ratio to reference lap time (1.00 = Expert AI, 1.25 = slower novice)
    lap_noise_std: float         # clean lap-to-lap variation (normalized)
    learning_rate_per_lap: float # within-weekend improvement rate (negative = gets faster)
    pace_drift_per_lap: float    # fatigue/drift
    traffic_sensitivity_s: float
    incident_prob: float
    holdout: bool                # True => held out permanently from training splits (T04)
    # Tactical response affinities (when defending ahead vs attacking behind)
    tactical_weights_ahead: Tuple[float, float, float, float, float]
    tactical_weights_behind: Tuple[float, float, float, float, float]


BOT_FAMILIES: Dict[str, PlayerBotFamily] = {
    "NormalLine": PlayerBotFamily(
        family_id="NormalLine",
        base_pace_norm=1.08, lap_noise_std=0.014, learning_rate_per_lap=-0.004, pace_drift_per_lap=0.001,
        traffic_sensitivity_s=0.55, incident_prob=0.04, holdout=False,
        tactical_weights_ahead=(0.56, 0.18, 0.08, 0.08, 0.10),
        tactical_weights_behind=(0.48, 0.04, 0.18, 0.16, 0.14),
    ),
    "LateBraker": PlayerBotFamily(
        family_id="LateBraker",
        base_pace_norm=1.03, lap_noise_std=0.018, learning_rate_per_lap=-0.003, pace_drift_per_lap=0.002,
        traffic_sensitivity_s=0.40, incident_prob=0.08, holdout=False,
        tactical_weights_ahead=(0.18, 0.44, 0.24, 0.09, 0.05),
        tactical_weights_behind=(0.12, 0.03, 0.64, 0.14, 0.07),
    ),
    "SwitchbackProne": PlayerBotFamily(
        family_id="SwitchbackProne",
        base_pace_norm=1.05, lap_noise_std=0.013, learning_rate_per_lap=-0.005, pace_drift_per_lap=0.000,
        traffic_sensitivity_s=0.45, incident_prob=0.03, holdout=False,
        tactical_weights_ahead=(0.24, 0.14, 0.08, 0.44, 0.10),
        tactical_weights_behind=(0.18, 0.03, 0.12, 0.58, 0.09),
    ),
    "AggressiveDefender": PlayerBotFamily(
        family_id="AggressiveDefender",
        base_pace_norm=1.12, lap_noise_std=0.020, learning_rate_per_lap=-0.002, pace_drift_per_lap=0.002,
        traffic_sensitivity_s=0.65, incident_prob=0.09, holdout=False,
        tactical_weights_ahead=(0.14, 0.58, 0.16, 0.08, 0.04),
        tactical_weights_behind=(0.22, 0.08, 0.45, 0.17, 0.08),
    ),
    # Permanently held-out bot families for T04/T06 generalization testing!
    "CautiousLearner": PlayerBotFamily(
        family_id="CautiousLearner",
        base_pace_norm=1.18, lap_noise_std=0.022, learning_rate_per_lap=-0.010, pace_drift_per_lap=0.000,
        traffic_sensitivity_s=0.85, incident_prob=0.05, holdout=True,
        tactical_weights_ahead=(0.45, 0.10, 0.05, 0.12, 0.28),
        tactical_weights_behind=(0.40, 0.02, 0.10, 0.18, 0.30),
    ),
    "ErraticDrifter": PlayerBotFamily(
        family_id="ErraticDrifter",
        base_pace_norm=1.09, lap_noise_std=0.032, learning_rate_per_lap=-0.001, pace_drift_per_lap=0.005,
        traffic_sensitivity_s=0.75, incident_prob=0.14, holdout=True,
        tactical_weights_ahead=(0.22, 0.28, 0.22, 0.18, 0.10),
        tactical_weights_behind=(0.16, 0.04, 0.42, 0.28, 0.10),
    ),
}


def ai_true_lap_time_s(circuit: CircuitSpec, difficulty: float, skill_offset: float = 0.0, traffic: bool = False) -> float:
    """Ground-truth smooth non-linear AI pace curve matched to real Unity Monza PaceSweep (48.7s at d=0 -> 40.6s at d=1)."""
    d = float(np.clip(difficulty + skill_offset, 0.0, 1.0))
    clean_ratio = 1.200 - 0.200 * (d ** 0.72)
    curv_scale = 1.0 + (circuit.mean_curvature - 0.0085) * 4.0 * (1.0 - d)
    base = circuit.ref_lap_s * clean_ratio * curv_scale
    if traffic:
        base += circuit.traffic_penalty_s * (0.75 + 0.45 * d)
    return base


def template_response_probs(
    opponent_is_ahead: bool,
    closing_ms: float,
    cand_maneuver: str,
    style_probs: Sequence[float],
    style_uncertainty: float,
) -> np.ndarray:
    """Exact Python mirror of Unity TemplateTacticalResponseModel.Predict (Phase 2-3 baseline)."""
    if not opponent_is_ahead:
        if cand_maneuver == "DefendInside":
            templ = np.array([0.24, 0.04, 0.12, 0.42, 0.18], dtype=np.float64)
        else:
            templ = np.array([0.28, 0.05, 0.42, 0.12, 0.13], dtype=np.float64)
    else:
        if cand_maneuver == "AttackInside":
            templ = np.array([0.26, 0.34, 0.16, 0.16, 0.08], dtype=np.float64)
        elif cand_maneuver == "AttackOutside":
            templ = np.array([0.36, 0.22, 0.22, 0.10, 0.10], dtype=np.float64)
        elif cand_maneuver == "Switchback":
            templ = np.array([0.22, 0.36, 0.22, 0.10, 0.10], dtype=np.float64)
        else:
            templ = np.array([0.50, 0.18, 0.10, 0.08, 0.14], dtype=np.float64)

    close_boost = float(np.clip(closing_ms / 15.0, 0.0, 1.0)) * 0.12
    if not opponent_is_ahead and cand_maneuver == "HoldLine":
        templ[RESP_LATE] += close_boost
    if opponent_is_ahead and cand_maneuver == "AttackInside":
        templ[RESP_DEFEND] += close_boost

    is_prior = style_uncertainty >= 0.99
    sw = 0.25 if is_prior else (0.35 + (0.78 - 0.35) * (1.0 - float(np.clip(style_uncertainty, 0.0, 1.0))))
    bw = 1.0 - sw
    st = np.maximum(0.01, np.asarray(style_probs, dtype=np.float64))
    p = (np.maximum(0.01, templ) ** bw) * (st ** sw)
    return p / max(1e-8, float(p.sum()))


def sample_true_opponent_response(
    rng: np.random.Generator,
    bot: PlayerBotFamily,
    opponent_is_ahead: bool,
    gap_m: float,
    closing_ms: float,
    brake_in_m: float,
    curvature: float,
    cand_maneuver: str,
    target_side: float,
    corner_side: float,
) -> int:
    """Stochastic ground-truth rival response dynamics in AI-vs-AI / bot-vs-AI duels (action-conditioned!).

    Notice how the rival's true behavior depends nonlinearly on BOTH its persistent style family AND the
    AI's executed maneuver + corner geometry (giving the neural MLP real action-conditioned structure to learn!).
    """
    base = np.array(bot.tactical_weights_ahead if opponent_is_ahead else bot.tactical_weights_behind, dtype=np.float64)
    logits = np.log(np.maximum(1e-3, base))

    prox = float(np.clip(1.0 - abs(gap_m) / 35.0, 0.0, 1.0))
    close_n = float(np.clip(closing_ms / 12.0, -1.0, 1.5))
    sharp = float(np.clip(abs(curvature) * 60.0, 0.0, 1.5))
    late_zone = 1.0 if brake_in_m < 65.0 else 0.0

    if not opponent_is_ahead:
        # Rival is behind us!
        if cand_maneuver == "DefendInside" or (corner_side != 0.0 and abs(target_side - corner_side) < 0.1):
            # Inside is covered: rival cannot easily lunge inside, strongly shifts to Switchback or Yield!
            logits[RESP_LATE] -= 1.35
            logits[RESP_SWITCH] += 1.05 + 0.45 * sharp
            logits[RESP_YIELD] += 0.45 - 0.30 * close_n
            logits[RESP_DEFEND] -= 1.5
        else:
            # Inside door left open: closing late-brakers lunge inside!
            logits[RESP_LATE] += 0.95 * prox + 0.70 * close_n + 0.35 * late_zone
            logits[RESP_SWITCH] -= 0.35
            logits[RESP_DEFEND] -= 1.5
    else:
        # Rival is ahead of us!
        if cand_maneuver == "AttackInside":
            logits[RESP_DEFEND] += 0.85 * prox + 0.40 * (1.0 - late_zone)
            logits[RESP_SWITCH] += 0.45 * sharp + 0.35 * late_zone
            logits[RESP_LATE] += 0.30 * close_n
            logits[RESP_YIELD] += 0.25 if closing_ms > 8.0 else -0.20
        elif cand_maneuver == "AttackOutside":
            logits[RESP_HOLD] += 0.45
            logits[RESP_LATE] += 0.40 * prox
            logits[RESP_DEFEND] -= 0.20
        elif cand_maneuver == "Switchback":
            logits[RESP_DEFEND] += 0.70 * prox
            logits[RESP_LATE] += 0.45 * sharp
            logits[RESP_SWITCH] -= 0.50
        else:
            logits[RESP_HOLD] += 0.75
            logits[RESP_YIELD] += 0.20 * max(0.0, close_n)

    logits -= np.max(logits)
    probs = np.exp(logits)
    probs /= probs.sum()
    return int(rng.choice(5, p=probs))


def encode_tactical_features(
    opponent_is_ahead: bool,
    gap_m: float,
    self_speed_ms: float,
    opp_speed_ms: float,
    closing_ms: float,
    self_side: float,
    opp_side: float,
    corner_side: float,
    brake_in_m: float,
    curvature: float,
    traffic_count: int,
    style_probs: Sequence[float],
    style_uncertainty: float,
    target_side: float,
    late_braking: bool,
    maneuver: str,
) -> np.ndarray:
    """Exact 22-feature vector matching Unity `NeuralTacticalResponseModel.EncodeFeatures` (T07 parity)."""
    return np.array([
        1.0 if opponent_is_ahead else 0.0,
        float(np.clip(gap_m, -60.0, 60.0)) / 60.0,
        float(np.clip(self_speed_ms, 0.0, 110.0)) / 100.0,
        float(np.clip(opp_speed_ms, 0.0, 110.0)) / 100.0,
        float(np.clip(closing_ms, -40.0, 40.0)) / 25.0,
        float(np.clip(self_side, -1.0, 1.0)),
        float(np.clip(opp_side, -1.0, 1.0)),
        float(np.clip(corner_side, -1.0, 1.0)),
        float(np.clip(brake_in_m, 0.0, 250.0)) / 200.0,
        float(np.clip(abs(curvature) * 100.0, 0.0, 5.0)),
        float(np.clip(traffic_count, 0, 5)) / 5.0,
        float(style_probs[0]),
        float(style_probs[1]),
        float(style_probs[2]),
        float(style_probs[3]),
        float(style_probs[4]),
        1.0 - float(np.clip(style_uncertainty, 0.0, 1.0)),
        float(np.clip(target_side, -1.0, 1.0)),
        1.0 if late_braking else 0.0,
        1.0 if maneuver == "DefendInside" else 0.0,
        1.0 if maneuver in ("AttackInside", "AttackOutside", "PassStraight") else 0.0,
        1.0 if maneuver == "Switchback" else 0.0,
    ], dtype=np.float32)


def rollout_duel_branch_outcome(
    circuit: CircuitSpec,
    opponent_is_ahead: bool,
    gap_m: float,
    self_speed_ms: float,
    opp_speed_ms: float,
    self_side: float,
    opp_side: float,
    corner_side: float,
    cand_maneuver: str,
    target_side: float,
    late_braking: bool,
    response_idx: int,
) -> Tuple[float, float, float]:
    """Kinematic 2D corner rollout returning (exit_margin_s, exit_speed_adv_ms, risk_score) matching Unity D04d."""
    apex_v = circuit.hairpin_apex_v_ms
    horizon_m = 160.0
    dt = 0.10

    def plan_v(side: float, late: bool, s_m: float, defending_inside: bool = False) -> float:
        d_apex = abs(s_m - 85.0)
        # Defending the tight inside of a hairpin forces a sharper radius -> slower apex and compromised exit acceleration.
        if side == corner_side and corner_side != 0.0:
            apex_scale = 0.87 if defending_inside else 0.90
            exit_cap = 0.94 if (defending_inside and s_m > 85.0) else 0.97
        elif side != 0.0:
            apex_scale = 0.95
            exit_cap = 0.98
        else:
            apex_scale = 1.04
            exit_cap = 1.02
        base = apex_v * apex_scale
        v = base + min(1.0, d_apex / 45.0) * (circuit.entry_speed_ms * exit_cap - base)
        if late and s_m < 92.0:
            v *= 1.13
        return v

    # Resolve rival branch behavior
    opp_t_side = opp_side
    opp_late = False
    opp_exit_boost = 1.0
    opp_scale = 1.0
    opp_sw_late = False
    opp_defending_inside = False
    if response_idx == RESP_DEFEND:
        opp_t_side = corner_side if corner_side != 0.0 else opp_side
        opp_defending_inside = True
        opp_scale = 0.96
    elif response_idx == RESP_LATE:
        if not opponent_is_ahead:
            opp_t_side = corner_side if (corner_side != 0.0 and target_side != corner_side) else (-corner_side if corner_side != 0.0 else -target_side)
        opp_late = True
        # Late inside lunge when door is open gains strong entry/apex position; forced outside lunge loses time.
        opp_scale = 1.08 if (not opponent_is_ahead and opp_t_side == corner_side) else 0.93
    elif response_idx == RESP_SWITCH:
        if corner_side != 0.0:
            line_open = (target_side == corner_side)
            opp_t_side = -corner_side
            opp_sw_late = line_open
            # Cutting back onto the racing line against a car pinched on the tight inside gives major exit traction!
            opp_exit_boost = 1.15 if line_open else 1.01
    elif response_idx == RESP_YIELD:
        opp_scale = 0.88

    self_s, opp_s = 0.0, gap_m
    self_v, opp_v = max(5.0, self_speed_ms), max(5.0, opp_speed_ms)
    self_lat, opp_lat = self_side * 3.5, opp_side * 3.5
    self_t_fin, opp_t_fin = math.inf, math.inf
    self_v_fin, opp_v_fin = self_v, opp_v
    min_clr = math.inf

    t = 0.0
    while t <= 4.2 and (math.isinf(self_t_fin) or math.isinf(opp_t_fin)):
        a_self_side = 0.0 if (cand_maneuver == "Switchback" and self_s > 90.0) else target_side
        a_opp_side = 0.0 if (opp_sw_late and opp_s > 80.0) else opp_t_side

        self_lat += float(np.clip(a_self_side * 3.5 - self_lat, -0.32, 0.32))
        opp_lat += float(np.clip(a_opp_side * 3.5 - opp_lat, -0.32, 0.32))

        sv_p = plan_v(a_self_side, late_braking, self_s, cand_maneuver == "DefendInside") * (1.06 if cand_maneuver == "Switchback" and self_s > 95.0 else 1.0)
        ov_p = plan_v(a_opp_side, opp_late, opp_s, opp_defending_inside) * opp_scale * (opp_exit_boost if opp_s > 85.0 else 1.0)

        sa = max(2.0, 11.0 - 0.08 * self_v)
        oa = max(2.0, 11.0 - 0.08 * opp_v)
        self_v = min(sv_p, self_v + sa * dt) if self_v < sv_p else max(sv_p, self_v - 26.0 * dt)
        opp_v = min(ov_p, opp_v + oa * dt) if opp_v < ov_p else max(ov_p, opp_v - 26.0 * dt)

        # Arc-length difference applies ONLY through the actual corner arc (65..105 m), never on straights!
        in_arc_s = 65.0 <= self_s <= 105.0
        in_arc_o = 65.0 <= opp_s <= 105.0
        k_s = (0.93 if a_self_side == corner_side else 1.07 if a_self_side == -corner_side else 1.0) if (in_arc_s and corner_side != 0.0) else 1.0
        k_o = (0.93 if a_opp_side == corner_side else 1.07 if a_opp_side == -corner_side else 1.0) if (in_arc_o and corner_side != 0.0) else 1.0

        prev_s, prev_o = self_s, opp_s
        self_s += self_v * dt / k_s
        opp_s += opp_v * dt / k_o
        t += dt

        if math.isinf(self_t_fin) and self_s >= horizon_m:
            frac = (horizon_m - prev_s) / max(1e-4, self_s - prev_s)
            self_t_fin = (t - dt) + frac * dt
            self_v_fin = self_v
        if math.isinf(opp_t_fin) and opp_s >= horizon_m:
            frac = (horizon_m - prev_o) / max(1e-4, opp_s - prev_o)
            opp_t_fin = (t - dt) + frac * dt
            opp_v_fin = opp_v

        ds = abs(opp_s - self_s)
        dl = abs(opp_lat - self_lat)
        clr = max(0.0, dl - 2.0) if ds < 5.0 else math.hypot(max(0.0, ds - 5.0), max(0.0, dl - 2.0))
        if clr < min_clr:
            min_clr = clr

    if math.isinf(self_t_fin):
        self_t_fin = t + max(0.0, horizon_m - self_s) / max(5.0, self_v)
        self_v_fin = self_v
    if math.isinf(opp_t_fin):
        opp_t_fin = t + max(0.0, horizon_m - opp_s) / max(5.0, opp_v)
        opp_v_fin = opp_v

    margin_s = opp_t_fin - self_t_fin
    exit_adv = self_v_fin - opp_v_fin
    clr_risk = max(0.0, (1.15 - min_clr) / 1.15) * 0.45 if min_clr < 1.15 else 0.0
    risk = clr_risk + (0.08 if late_braking else 0.0)
    return margin_s, exit_adv, risk
