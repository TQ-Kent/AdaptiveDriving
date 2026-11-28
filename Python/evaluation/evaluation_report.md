# Adaptive Racing AI — Phase 2–6 Evaluation Report (T08)

> **Provenance Notice:** All quantitative results below are from **simulated multi-circuit & held-out bot-family benchmarks**. Opt-in human playtests are tracked separately and are not conflated with simulated metrics.

**Release Gate Status:** `APPROVED`

## 1. Phase 3–4 First Demo Check: Identical Hairpin vs Distinct Style Histories

| Model | LateBraker Profile Choice | SwitchbackProne Profile Choice | Forecast-Driven Decision Switch |
|---|---|---|---|
| ActionConditionedTemplate | `DefendInside` | `HoldLine` | `True` |
| ActionConditionedNeural | `DefendInside` | `HoldLine` | `True` |

## 2. Tactical Variant Comparison (Fixed Difficulty, Matched Physics & Safety, 3 Circuits × 6 Bot Families)

| Tactical Variant | Clean Pass Rate | Clean Defense Rate | Contact Rate | Off-Track | Aborts | Safety Overrides | Latency p50 / p99 (ms) |
|---|---|---|---|---|---|---|---|
| Scripted | 37.4% | 37.6% | 6.57% | 13 | 0 | 501 | 0.001 / 0.002 |
| NonInteractiveLookahead | 37.6% | 41.9% | 1.76% | 21 | 244 | 482 | 1.194 / 2.934 |
| ActionConditionedTemplate | 31.5% | 54.7% | 2.04% | 20 | 197 | 420 | 2.795 / 5.379 |
| ActionConditionedNeural | 32.8% | 63.0% | 1.76% | 21 | 209 | 407 | 2.941 / 5.736 |

## 3. Challenge Adaptation Comparison (Locked Tactical Driver `v1.2-acwm`, 3 Circuits × 6 Bot Families)

### Mode: `CompetitiveSeason`

| Planner | Mean Band Error (s) | Cold Start 0 Laps (s) | 1–3 Laps (s) | >3 Laps (s) | Live Changes | Fairness Violations |
|---|---|---|---|---|---|---|
| FixedDefault | 2.450 | 2.712 | 2.409 | 2.255 | 0 | 0 |
| RuleBasedA08 | 2.065 | 2.712 | 2.028 | 1.525 | 0 | 0 |
| PredictiveMPC | 1.730 | 2.712 | 1.431 | 1.127 | 0 | 0 |

### Mode: `AdaptiveTraining`

| Planner | Mean Band Error (s) | Cold Start 0 Laps (s) | 1–3 Laps (s) | >3 Laps (s) | Live Changes | Fairness Violations |
|---|---|---|---|---|---|---|
| FixedDefault | 2.450 | 2.712 | 2.409 | 2.255 | 0 | 0 |
| RuleBasedA08 | 1.911 | 2.531 | 1.844 | 1.421 | 518 | 0 |
| PredictiveMPC | 1.609 | 2.506 | 1.340 | 1.055 | 524 | 0 |

## 4. Human Playtest & Consent Protocol (Separated from Simulation)

- Completed opt-in human sessions: `0` (ready for local playtesting via Unity `RaceLog` + explicit `G05` consent).
- Required human metrics to collect separately: perceived fairness (1–5), perceived challenge match (1–5), enjoyment (1–5), racecraft believability (1–5), and cold-start experience with skipped practice.
