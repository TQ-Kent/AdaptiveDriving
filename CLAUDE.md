# AGENT.md — Adaptive Racing Game

## 1. Mission

You are the implementation agent for an **F1-inspired adaptive racing game** built with **Unity and C#**, with offline model calibration/training in **Python and PyTorch**.

Build the game according to the supplied architecture. The central design principle is:

> **Adapt the challenge, not the rules.**

AI opponents should provide a suitable, fair challenge without hidden physical advantages, predetermined outcomes, or competitive difficulty changes after the player has confirmed the challenge for qualifying.

The game must remain fully playable when learned models are unavailable or disabled. Implement a reliable conventional-driving and rule-based baseline before adding learned prediction or optimization.

Do not treat architecture diagrams or provisional numeric defaults as measured results.

## 2. Source-of-truth documents

When present in the repository, read these documents before making architectural changes:

1. `adaptive_racing_architecture_v1_2.md` — primary implementation specification. Its v1.2 sections (two predictive loops, D04 tactical responsibilities) supersede the inherited v1.1 text in the same file wherever they differ.
2. `sequence_v1_2.md` — implementation order (phases 0–6 and their exit gates); supersedes the order of the inherited checklist.
3. `unified_racing_game_ai_complete_structure.md` — integrated component graph, component catalogue, runtime paths, message contracts, and acceptance checklist.
4. `architecture_summary.md` — concise explanation of the intended system and player-facing behavior.
5. `adaptive_racing_architecture_v1_1.md` — previous version, kept for reference only.

The v1.2 architecture is authoritative when implementing behavior. The unified structure expands responsibilities and ownership; it does not authorize unrelated features. The summary is an explanatory overview.

If the documents are absent, ask the user to provide them or state clearly which decisions cannot be verified. Do not silently invent missing requirements.

If documents conflict:
- Preserve the explicit executive decisions and fairness invariants in architecture v1.2 (inherited from v1.1).
- Preserve the unified graph's ownership boundaries where they clarify implementation.
- Do not silently resolve a substantive conflict. Explain it and ask for direction.
- Keep provisional settings configurable and label them as unvalidated defaults.

## 3. Product scope and non-goals

### Initial target

- Unity game, implemented in C#.
- Offline training/calibration tools in Python and PyTorch.
- Configurable season of approximately 19–22 weekends.
- Weekend flow: FP1, FP2, FP3, pre-qualifying review, one qualifying run, race.
- Practice sessions are individually skippable; remaining practice can be skipped together.
- Initial playable scope: three circuits, one human player, and three to five AI opponents.
- Conventional AI driving plus hybrid pace prediction and constrained, short-horizon MPC challenge planning.

### Explicitly out of scope unless the user approves a separate architecture change

- End-to-end reinforcement-learning driving policy.
- Mandatory practice sessions.
- Online neural-network weight training during gameplay or races.
- Weather, pit strategy, tyre compounds, fuel strategy, or safety-car systems.
- Hidden engine-power, grip, collision, or classification changes to balance a race.
- A forced winner, forced finish gap, or automatic catch-up behavior in competitive mode.

Do not add these features as “helpful improvements.” First explain the architectural impact and obtain approval.

## 4. Non-negotiable gameplay and fairness invariants

These rules apply to all implementation decisions and tests.

### 4.1 Shared simulation

- Human and AI vehicles use the same vehicle physics and equivalent physical specifications.
- AI may issue driving commands only within the supported physical limits.
- The adaptation planner must never directly modify Rigidbody state, engine specifications, tyre grip, collision rules, track boundaries, classification, championship results, or session locks.
- Difficulty changes must affect calibrated pace/controller settings, not secretly improve physical capabilities.
- Stable opponent identities and personalities must persist. Do not introduce retaliatory pace boosts after an overtake.
- A player who earns a lead is allowed to keep it. Do not optimize toward a predetermined winner or compulsory finish gap.

### 4.2 Competitive difficulty lock

- The player sees the recommendation and evidence before qualifying.
- The player can accept the recommendation or choose a manual difficulty.
- The accepted main difficulty is locked when qualifying begins and remains the same through the competitive race.
- Qualifying can update single-lap evidence, prediction residuals, confidence, and future recommendations.
- **Qualifying review must never write or change the current competitive race difficulty.**
- No hidden catch-up or difficulty adjustment is allowed in competitive season mode.
- Any post-qualifying recalibration must be an explicit separate workflow, such as restarting the weekend/repeating qualifying. Never retain a grid earned under one challenge while silently changing the race challenge.

### 4.3 Optional practice and missing data

- FP1, FP2, and FP3 are optional.
- The player can skip one pending practice, skip all remaining practice, or end an active practice early.
- Skipping practice must not punish the player or imply low skill.
- The pre-qualifying review always runs, even when every practice session was skipped.
- Missing observations are represented as missing/status flags, never as zero-second times or fabricated samples.
- With no usable history, offer a clearly labeled manual/default setting and preserve high uncertainty.
- Never require FP3 specifically.

### 4.4 Incidents and evidence quality

- A crash, spin, traffic-limited lap, invalid lap, or incomplete lap must not be treated as clean pace evidence.
- Keep incident statistics separately; do not erase incident information entirely.
- One incident must not immediately lower the player's clean-pace estimate or difficulty.
- Preserve observation provenance, quality labels, age, validity masks, and observation IDs.
- Do not double-count sector evidence when its completed lap is later recorded.
- An incomplete lap remains incomplete. Never extrapolate it into a fabricated full-lap time.
- Completed clean sectors may support sector-level estimates, but sectors from one lap are not independent full-lap samples.

### 4.5 Adaptation authorization

The adaptation pipeline is:

**Model predicts → planner proposes → FairnessGovernor authorizes → DifficultyConfigApplier applies.**

- The planner is never authoritative.
- `FairnessGovernor` is independent of the optimizer.
- `DifficultyConfigApplier` is the only adaptation difficulty-write path.
- Every proposal must be checked against mode, session, lock, consent/confirmation, evidence quality, model support, incident holds, cooldown, bounds, timing boundary, expiry, and policy/session versions as applicable.
- Queued proposals must be revalidated at application time.
- Stale, expired, unsupported, or unauthorized proposals must be rejected or held.
- If inference fails, times out, returns invalid values, or is incompatible, retain the last approved configuration and use the conservative fallback.
- Log accepted, rejected, clamped, queued, and held decisions, including reasons.

### 4.6 Adaptive training mode

Live adaptation is opt-in and separate from competitive season mode.

- Only apply changes at explicitly permitted decision boundaries, normally a designated player-lap boundary.
- Never trigger adaptation independently on every opponent lap.
- Use fresh timestamped snapshots and policy versions.
- Provisional v1.1 defaults: maximum normalized difficulty step `0.02`, cooldown of two completed player laps, freeze during the final two laps, and incident hold until at least one subsequent clean lap. These are configurable starting points, not proven optima.
- Validate the effect in actual pace/time units, not only normalized difficulty units.
- Short races may allow no live changes.

## 5. Architecture and ownership

Keep the system modular, but do not create a class or service for every diagram node automatically. A component is a responsibility boundary; small helpers can begin as methods until separate ownership, lifecycle, or tests justify extraction.

### Assemblies / modules

Use these logical boundaries unless the existing repository has a compatible structure that should be preserved:

| Assembly | Responsibility |
|---|---|
| `Racing.Contracts` | IDs, enums, immutable snapshots, message/interface contracts |
| `Racing.Vehicle` | Vehicle motor, sensors, player input |
| `Racing.Track` | Track geometry, limits, checkpoints, progress |
| `Racing.Gameplay` | Season/weekend/session controllers, timing, rules |
| `Racing.Policy` | Challenge policy, fairness governor, approval records |
| `Racing.AI.Driving` | Tactical driver, path follower, pace controller, safety filter, recovery |
| `Racing.AI.Adaptation` | Evidence resolution, profile estimation, qualifying review, prediction, MPC |
| `Racing.Integration` | Snapshot builder, AI bridge, difficulty configuration applier |
| `Racing.Telemetry` | Quality gate, aggregation, buffered writing, schema validation |
| `Racing.UI` | Menus, HUD, explanations, explicit user commands |
| `Racing.Evaluation` | Automated batch runners and scenario assertions |
| `Racing.Editor` | Track baking/calibration tools; editor-only |

### Dependency rules

- Contracts must not depend on gameplay or AI implementations.
- Vehicle physics must not depend on the adaptation model.
- The optimizer must not bypass `Racing.Policy`.
- UI displays authoritative state and submits commands; it does not own game state.
- Integration connects gameplay, policy, and AI through typed interfaces.
- Use an explicit composition root and injected service references. Avoid hidden global state and service-locator shortcuts.
- Do not allow the AI planner to access or mutate authoritative gameplay state directly.
- Use immutable, timestamped snapshots across the game/AI boundary.
- Keep ScriptableObjects as authoring defaults. Copy them into per-session/per-vehicle runtime state; do not mutate shared assets to change one opponent.

### Component groups

Respect the responsibility groups and IDs in the unified structure document:

- `G01–G05`: startup, definitions, UI, player input, consent/preferences.
- `G06–G14`: season/weekend/session progression, practice transitions, finalization, pre-qualifying review, policy, grid, rules.
- `G15–G21`: track authoring/runtime geometry, vehicle commands/motor/sensors, progress, timing.
- `D01–D08`: per-opponent driving, identity, pace mapping, tactics, path following, pace control, recovery, safety filter.
- `O01–O06`: quality gate, telemetry, saves, profile store, audit log, integration bridge/snapshot builder.
- `A01–A11`: evidence resolver, profile estimator, qualifying review, forecast context, candidate generation, MPC planner, uncertainty gate, fallback, proposal, governor, applier.
- `M01–M05`: player predictor, calibrated AI pace model, analytical transition model, Unity inference adapter, model registry/manifest.
- `T01–T08`: evaluation scenarios, player bots, consented dataset builder, group splitter/feature pipeline, Python training, validation/release gate, ONNX export/parity tests, metrics/reporting.

## 6. Runtime behavior requirements

### 6.1 Vehicle command path

Use one normalized command contract:

- Steering: `[-1, 1]`
- Throttle: `[0, 1]`
- Brake: `[0, 1]`

Reject NaN/infinite values and clamp or reject out-of-range values according to the contract. Ensure exactly one selected command source controls each vehicle at a time.

The driving pipeline should use:
- D04 tactical layer behind `ITacticalDriver` for follow/overtake/yield decisions, with stable decisions and hysteresis. `ScriptedTacticalDriver` stays a selectable baseline; v1.2 lookahead/world-model variants (HoldLine, DefendInside, BackOut first; attacks later) plug in behind the same interface and only propose maneuvers. A late inside attack must have an abort path. The tactical loop races for position within fixed permissions and never touches difficulty (v1.2 §1).
- `PathFollower` for speed-dependent look-ahead steering on a valid path/lane.
- `PaceController` for target speed and braking look-ahead.
- `DrivingSafetyFilter` as the independent final command restriction.
- `RecoveryController` for stuck/reversed/off-track recovery under shared rules.

Safety overrides tactics and pace. A safety filter reduces risk but does not guarantee collision-free behavior.

Begin with a tunable arcade Rigidbody model unless detailed wheel behavior is explicitly made a core requirement.

### 6.2 Track and timing

Track baking should produce the runtime data required by driving and timing:
- Centerline samples, positions, tangents, curvature, cumulative distance.
- Drivable widths and track boundaries.
- Checkpoints, sectors, and reference speeds.
- Track descriptors such as length, straight fraction, and corner distribution.

New circuits require geometry, baking, reference calibration, and validation—not merely a new track ID.

`TrackProgressService` owns validated progress and checkpoint order. `RaceTimingService` owns crossing timestamps, valid lap/sector times, classification inputs, and timestamped gaps.

Use time differences at common timing boundaries for authoritative gap measurements. Do not treat distance/current-speed approximations as authoritative when cars are stopped or in different corners.

Apply equivalent track-limit, lap-validity, completion, penalty, and recovery rules to human and AI.

### 6.3 Weekend/session lifecycle

Expected order:

`FP1 → FP2 → FP3 → PreQualifyingReview → Qualifying → QualifyingReview → Race → Results`

The player may skip sessions or end active practice early, but practice skip operations must never skip qualifying or the race.

Session statuses:
- `NotStarted`
- `InProgress`
- `Completed`
- `EndedEarly`
- `Skipped`

Rules:
- Never-started practice bypassed by the player becomes `Skipped`.
- Started practice exited early becomes `EndedEarly`, even with zero completed laps.
- Normal expiry becomes `Completed`.
- Skip/end requests must be idempotent. Repeated clicks cannot advance the weekend twice.
- Ending an active session must finalize it through the normal session-finalization path.
- Mark only later pending FP sessions as skipped when “skip remaining” is requested.
- Commit session records, telemetry, accepted difficulty, and next progression state coherently before advancing.
- A failed save must allow retry without a second transition.
- For the initial implementation, session-boundary saves are sufficient; an interrupted active session may restart on reload. Do not claim exact mid-session physics restoration unless implemented.

### 6.4 Pre-qualifying review

The review always runs. It must display:
- Evidence source and amount/quality.
- Recommendation and uncertainty.
- Whether the system is using current practice, partial sectors, same-track history, global prior, or cold-start fallback.
- Manual difficulty selection as an available option.

The player's confirmation/manual selection is required before competitive qualifying begins and the lock is established.

### 6.5 Qualifying review

After qualifying:
1. Validate observations and classify the grid using authoritative rules.
2. Compare observed qualifying pace with its prediction.
3. Update single-lap evidence, confidence, and mismatch diagnostics.
4. Preserve the competitive difficulty lock.
5. Persist evidence for later analysis and future recommendations.

Do not let one lap automatically overwrite multiple clean practice laps. Keep single-lap potential distinct from sustainable race pace. Traffic-limited, incident-affected, invalid, and no-time outcomes must retain their labels and follow configured classification fallback rules.

## 7. Player profile, evidence, prediction, and planning

### 7.1 Player profile

Keep distinct estimates for:
- Single-lap pace.
- Sustainable race pace.
- Consistency / clean-lap variation.
- Sector variation.
- Incident rates.
- Track familiarity and recent improvement.
- Global and track-specific personal corrections.
- Preferences, mode, accepted difficulty, and adaptation consent.

Normalize times using versioned track references. Preserve raw times and the reference version.

For sparse observations, shrink estimates toward a prior. Effective sample count must account for quality and dependence; multiple sectors from one lap must not be treated as independent laps. Tune shrinkage using held-out data.

### 7.2 Evidence resolution

`CalibrationEvidenceResolver` must support:
1. Enough current-track clean practice: blend with prior using quality-appropriate weights.
2. Limited practice or clean sectors: use partial evidence with uncertainty and shrinkage.
3. No practice but compatible same-track history: use history with age/configuration checks.
4. No track history but global history: conservative transfer using track features.
5. No usable history: manual difficulty or clearly labeled default.

Keep these responsibilities separate:
- Resolver selects evidence and fallback.
- Estimator builds/updates the profile.
- Rule baseline or planner produces a recommendation.
- Player accepts or overrides it.
- Governor authorizes application.

Skipped sessions remain missing, not zero pace or poor skill.

### 7.3 Predictive models

The runtime prediction system is hybrid:

- `M01 PlayerPacePredictor`: compact learned prediction of conditional clean player pace and variance.
- `M02 CalibratedAIPaceModel`: calibrated mapping from track, difficulty, identity, and supported conditions to AI lap time/variation.
- `M03 AnalyticalTransitionModel`: explicit progression of forecast state and matched-boundary time gap.
- `M04 UnityInferenceAdapter`: bounded runtime inference with input/output validation and compatible normalization.
- `M05 ModelRegistry`: approved model/schema/normalization/support/hash/version authority and rollback references.

The initial player predictor may use an ensemble of three small MLPs with two hidden layers of approximately 64 units. This is a starting configuration to benchmark, not an assumed optimum. The AI pace model may begin as a calibrated lookup/interpolation asset.

Do not assume opponent difficulty causally improves human pace without evidence. Do not add unsupported collision-risk or traffic dynamics to the forecast objective. If conditions are outside model support, shorten the horizon, widen uncertainty, or fall back.

For aligned timing boundaries:

`gap_t = player cumulative time - AI cumulative time`

`gap_(t+1) = gap_t + predicted_player_lap_time - predicted_AI_lap_time`

Positive gap means the player is behind. If snapshots are not aligned, mark the gap unavailable or use a documented alignment method. Predicted values must carry prediction masks and must never increase real evidence counts.

Validate multi-step forecast behavior, not only one-step prediction error.

### 7.4 MPC challenge planner

Use short-horizon constrained MPC to choose a sustainable challenge. The objective should combine:
- Pace-band mismatch.
- Difficulty-change/smoothness cost.
- Predictive uncertainty.

Do not optimize for a forced finishing gap or predetermined winner.

Candidate generation must respect policy constraints before optimization. The planner may only propose supported, feasible actions. The uncertainty/support gate may reject a trajectory and invoke a conservative fallback.

A proposal should include, at minimum:
- Candidate difficulty/action.
- Evidence summary and quality-weighted count.
- Prediction and uncertainty.
- Reason/diagnostics.
- Scope, expiry, session/policy versions, and model version.

### 7.5 Fallback behavior

A conservative fallback may use the current/manual setting, prior, or clean-pace lookup under the same policy. It must not bypass the governor or applier.

If model inference is late, unavailable, incompatible, or invalid:
- Keep the last approved configuration.
- Do not block physics or vehicle control.
- Record the failure and fallback reason.
- Do not apply stale queued actions.

## 8. Offline data, training, and model release

Training and network-weight updates happen offline, never during a race.

Expected pipeline:

`Unity Evaluation → Synthetic PlayerBots → DatasetBuilder → GroupSplitter/FeaturePipeline → Python/PyTorch Training → Validation/ReleaseGate → ONNX Export → Unity Parity Tests → ModelRegistry`

Requirements:
- Synthetic bots vary pace, braking, consistency, learning, traffic response, and correlated errors.
- Synthetic data is useful for coverage and opponent calibration; it does not replace held-out human validation or prove human enjoyment.
- Human research data is exported only with explicit consent.
- Keep research exports separate from local saves, pseudonymize identifiers, and never upload by default.
- Provide profile reset/deletion.
- Preserve provenance, quality labels, missingness masks, player/run/track/bot-family identifiers, build/config hashes, seeds, and schema/model versions as appropriate.
- Split by player/run/track/bot family to prevent leakage. Fit normalization on training data only.
- Validate held-out and multi-step accuracy, uncertainty calibration, fairness, safety, and runtime/resource behavior.
- Compare Python and Unity inference numerically, including tensor order, units, masks, and normalization.
- Deploy only artifacts that pass all release gates. Retain the current approved model if a candidate fails.
- Do not retrain or update deployed network weights because a weekend completed. Runtime profile correction and offline weight updates are separate operations.

## 9. Telemetry, persistence, privacy, and audit

Record enough information to reconstruct why a decision was made and what was actually applied.

Expected records include:
- `RunManifest`: build, track/config hashes, seeds, schema/model versions.
- `SessionRecord`: session status, end reason, evidence totals, next state, telemetry commit ID.
- `SectorSummary` / `LapSummary`: raw times, reference times/version, validity, traffic/incidents, masks, provenance, observation IDs.
- `DifficultyApplication`: actual applied setting and approval ID.
- `QualifyingReviewRecord`: quality decision, residual, profile update, lock-preserved outcome.
- `DecisionAuditRecord`: old/proposed/approved values, evidence, uncertainty, versions, and reason.

Rules:
- Log actual applications, not only proposals.
- Record accepted, rejected, clamped, queued, and held decisions.
- Mark laps containing mixed difficulty/actions.
- Buffer disk writes; do not block the physics loop on telemetry I/O.
- Use stable IDs and deduplication to avoid double-counting.
- Keep local saves and optional research exports separate.
- Never upload telemetry by default.

## 10. Performance and execution constraints

- Physics, sensors, path following, pace control, safety filtering, and motor commands run on the fixed physics cadence.
- Tactical decisions run at a lower frequency where appropriate.
- Aggregate observations at sector/lap/session boundaries.
- Run profile updates and planning at explicit permitted events, not arbitrary per-opponent callbacks.
- Keep model inference outside the physics critical path. Give inference a deadline.
- Unity object access stays on the main thread unless the specific API explicitly supports another execution model.
- Fixed timesteps and independent seeded random streams improve reproducibility but do not guarantee identical physics across devices.
- If an inference job misses its deadline, retain the last approved setting.

Do not introduce concurrency, event buses, or global frameworks without a concrete need. Prefer direct interfaces and bounded events for completed observations.

## 11. Implementation workflow for Claude Code

Follow the phase order and exit gates in `sequence_v1_2.md` (do not finalize M02 calibration before the tactical driver is selected). Within that order, use this sequence unless the user explicitly requests a different bounded task:

1. **Inspect before editing**
   - Inspect the existing repository, Unity version, packages, assemblies, scenes, input setup, and current code.
   - Identify what already exists; do not recreate working systems.
   - Read the architecture source documents.
   - Report assumptions and any conflict with the specification.

2. **Establish a working baseline**
   - Confirm the project opens/compiles.
   - Implement contracts and composition root.
   - Implement shared vehicle command, motor, sensors, track progress, timing, and rules.
   - Implement a conventional rule-based AI driver.
   - Ensure the game runs without learned models.

3. **Implement gameplay lifecycle**
   - Season/weekend/session state machines.
   - Practice skip/end operations and idempotent transitions.
   - Session finalization, telemetry commit, and save/reload behavior.
   - Pre-qualifying review and competitive lock.
   - Qualifying classification/review and race flow.

4. **Implement evidence and policy**
   - Quality gate and observation aggregation.
   - Player profile and evidence resolver.
   - Challenge policy, fairness governor, approval contracts, and the sole configuration applier.
   - Audit logging and fallback behavior.

5. **Implement adaptation and prediction**
   - Calibrated AI pace mapping.
   - Explicit forecast state and analytical transition model.
   - Baseline candidate generation and MPC.
   - Player predictor and Unity inference adapter only after contracts and baselines are tested.

6. **Implement offline tooling**
   - Evaluation scenarios and diverse bots.
   - Provenance-aware dataset pipeline and group splits.
   - Python calibration/training, validation, ONNX export, and Unity parity tests.

7. **Verify end-to-end**
   - Run acceptance scenarios from the source architecture.
   - Test no-model and inference-failure behavior.
   - Verify no unauthorized difficulty writes.
   - Report what was actually run and what remains unverified.

Work in small, reviewable increments. After each increment, compile/build and run the narrowest relevant tests before proceeding.

## 12. Coding and change discipline

- Match the repository's established naming, formatting, namespace, and serialization conventions unless they conflict with these architectural invariants.
- Use explicit units in names or types where ambiguity is possible (seconds, meters, meters/second, normalized difficulty, lap count).
- Validate external inputs and all model outputs. Reject NaN, infinity, invalid IDs, incompatible versions, and out-of-domain values.
- Prefer small, cohesive classes and immutable data-transfer objects.
- Avoid speculative abstraction and oversized manager classes.
- Avoid hidden static mutable state, scene lookups as service discovery, and direct cross-assembly references that violate dependency rules.
- Do not add packages or change Unity/package versions without checking compatibility and explaining why the change is needed.
- Do not overwrite user work, delete assets, regenerate scenes, or perform broad refactors without inspecting the impact.
- Preserve serialized field compatibility when modifying Unity components and ScriptableObjects. Explain migrations when unavoidable.
- Keep editor-only code out of runtime assemblies.
- Do not claim a build, test, scene, or runtime behavior succeeded unless you actually ran and observed it.
- If blocked by missing Unity editor/license/platform dependencies, state the exact limitation and still run available static or unit checks.

## 13. Required tests and acceptance checklist

Add automated tests where practical. The following are product acceptance requirements, not optional examples.

### Weekend/session flow
- [ ] A complete weekend runs with learned models disabled.
- [ ] Skip FP1: FP1 is `Skipped`; FP2 is available.
- [ ] Skip all practice: pre-qualifying review still opens.
- [ ] End active practice after valid laps: valid observations are retained and status is `EndedEarly`.
- [ ] Exit mid-lap: no fabricated full-lap time is created.
- [ ] Skip retries/double clicks cause only one transition.
- [ ] Reload after a skip restores the correct status and next state.
- [ ] Save failure can be retried without double advancement.
- [ ] Practice skip never skips qualifying or race.

### Calibration and qualifying
- [ ] No practice/no profile offers a manual/default difficulty.
- [ ] Skipped practice is treated as missing, not poor skill.
- [ ] Partial sectors are retained without being counted as independent full laps.
- [ ] Duplicate sector/lap evidence is deduplicated.
- [ ] Invalid/no-time qualifying follows documented grid fallback rules.
- [ ] Qualifying updates single-lap evidence and diagnostics.
- [ ] Qualifying cannot change the locked competitive race difficulty.
- [ ] A strong qualifying lap does not silently boost race difficulty.

### Fairness and adaptation
- [ ] Shared physical specifications apply to equivalent human/AI vehicles.
- [ ] Competitive mode has zero main-difficulty writes after lock.
- [ ] A player-earned lead is never erased by catch-up logic.
- [ ] No hidden engine, grip, collision, or classification changes are used for balancing.
- [ ] Incidents do not immediately lower clean-pace estimates or difficulty.
- [ ] Unsupported/uncertain predictions use fallback.
- [ ] Inference timeout/unavailability leaves the current approved setting usable.
- [ ] Stale/expired proposals cannot be applied to a new session or policy version.
- [ ] Only `DifficultyConfigApplier` can write adaptation difficulty, after governor approval.

### Data and release
- [ ] Telemetry records applied actions and decision reasons.
- [ ] Local saves work without research export consent.
- [ ] Research export requires explicit consent and is off by default.
- [ ] Dataset splits prevent player/run/track/bot-family leakage.
- [ ] Training-only normalization is used.
- [ ] Model release checks held-out/multi-step accuracy, uncertainty, safety, fairness, and resource limits.
- [ ] Python/Unity parity tests validate model inputs and outputs.
- [ ] Human fairness/enjoyment results are reported separately from synthetic bot race-closeness metrics.

## 14. Definition of done for each task

Before declaring a task complete, report:

1. What changed and why.
2. Which architecture components/contracts were implemented or modified.
3. Files created, changed, or removed.
4. Tests/builds actually run and their results.
5. Acceptance criteria satisfied.
6. Remaining limitations, assumptions, and follow-up work.

A task is not complete merely because code was written. It must compile where possible, pass relevant tests, preserve the invariants above, and leave the repository in a coherent state.

## 15. How to respond to ambiguous requests

- Ask one focused clarification when a missing decision materially affects architecture, gameplay behavior, or data safety.
- Otherwise choose the smallest reversible implementation consistent with the source documents and state the assumption.
- Do not silently expand scope.
- Do not claim that provisional values are optimal or empirically validated.
- Separate source requirements, implementation decisions, and experimental hypotheses in explanations.
- When suggesting a change to an invariant, explain the trade-off and wait for approval before implementing it.
