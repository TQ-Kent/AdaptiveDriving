# Unified Racing Game and AI — Complete Structural Graph

**Baseline:** Final architecture v1.1, including skippable practice, qualifying review, competitive difficulty lock, limited-data handling, and fairness enforcement.

This document contains exactly **one integrated Mermaid graph**. The grouped boxes are visual boundaries inside that graph, not separate diagrams. Every component has an ID linked to the component catalogue below. The graph expands the v1.1 responsibilities into implementable services; helper names such as PracticeTransitionHandler and SessionFinalizer are explicit responsibility extractions, not additional features.

## 1. How to read the graph

- **Arrow direction:** producer/caller/control authority to consumer/callee. Read its label to distinguish data, commands, lifecycle events, and approvals.
- **Solid arrows:** runtime control/data, including telemetry and local persistence.
- **Dashed arrows:** editor preprocessing or automated evaluation/offline model-release relationships. Dashed does not necessarily mean network communication.
- **Blue G nodes:** game orchestration, input, simulation, and rules.
- **Green D nodes:** conventional per-car AI driving.
- **Grey O nodes:** observation, storage, integration, and audit.
- **Purple A nodes:** evidence, profile review, planning, and adaptation.
- **Pink M nodes:** predictive submodels and inference artifacts.
- **Yellow T nodes:** automated testing, offline data processing, training, and reports.
- **Red policy nodes:** authoritative adaptation permissions and the only difficulty-write path.

A visual group is not necessarily a Unity scene or assembly. A node is a responsibility boundary: small helpers may initially be methods inside one class. Arrow count does not imply every relationship needs its own event bus or service. Use direct interfaces for requests and bounded events for completed observations.

The rendering is intentionally large. Use a wide Mermaid-capable preview and zoom/pan; fit-to-page will make labels too small. No external PNGs, web resources, or renderer-specific click actions are required.

## 2. One complete structure graph

```mermaid
flowchart TB
    %% One integrated graph: subgraphs are visual groups, not separate diagrams.
    subgraph ENTRY["1. Startup, configuration, and player interface"]
        direction TB
        G01["G01 · GameBootstrap"]
        G02["G02 · Definition assets"]
        G03["G03 · Session UI and HUD"]
        G04["G04 · PlayerInputProvider"]
        G05["G05 · Consent and preferences"]
    end
    subgraph FLOW["2. Season, session progression, and rules"]
        direction TB
        G06["G06 · SeasonController and championship"]
        G07["G07 · WeekendController"]
        G08["G08 · PracticeTransitionHandler"]
        G09["G09 · SessionController"]
        G10["G10 · SessionFinalizer"]
        G11["G11 · PreQualifyingReview"]
        G12["G12 · ChallengePolicyService"]
        G13["G13 · VehicleRegistry and GridBuilder"]
        G14["G14 · SessionRulesService"]
    end
    subgraph SIM["3. Track and shared vehicle simulation"]
        direction TB
        G15["G15 · TrackDefinition and TrackBakeTool"]
        G16["G16 · TrackRuntimeData"]
        G17["G17 · VehicleCommand and input arbitration"]
        G18["G18 · VehicleMotor and Unity Physics"]
        G19["G19 · VehicleSensors"]
        G20["G20 · TrackProgressService"]
        G21["G21 · RaceTimingService"]
    end
    subgraph DRIVE["4. Per-opponent conventional driving"]
        direction TB
        D01["D01 · AIDriver"]
        D02["D02 · OpponentIdentity"]
        D03["D03 · PaceProfileMapper"]
        D04["D04 · TacticalDriver"]
        D05["D05 · PathFollower"]
        D06["D06 · PaceController"]
        D07["D07 · RecoveryController"]
        D08["D08 · DrivingSafetyFilter"]
    end
    subgraph DATA["5. Observation, persistence, and audit"]
        direction TB
        O01["O01 · QualityGate and ObservationAggregator"]
        O02["O02 · TelemetryRecorder and buffered writer"]
        O03["O03 · SaveGameService and SessionRecords"]
        O04["O04 · PlayerProfileStore"]
        O05["O05 · DecisionAuditLog"]
        O06["O06 · SnapshotBuilder and AIIntegrationBridge"]
    end
    subgraph ADAPT["6. Player evidence, review, and adaptation planning"]
        direction TB
        A01["A01 · CalibrationEvidenceResolver"]
        A02["A02 · PlayerProfileEstimator"]
        A03["A03 · QualifyingReviewService"]
        A04["A04 · ForecastContextBuilder"]
        A05["A05 · CandidateGenerator"]
        A06["A06 · MPCChallengePlanner"]
        A07["A07 · Support and UncertaintyGate"]
        A08["A08 · ConservativeFallback"]
        A09["A09 · AdaptationProposal"]
        A10["A10 · FairnessGovernor"]
        A11["A11 · DifficultyConfigApplier"]
    end
    subgraph MODEL["7. Hybrid PredictiveRaceModel and inference"]
        direction TB
        M01["M01 · PlayerPacePredictor ensemble"]
        M02["M02 · CalibratedAIPaceModel"]
        M03["M03 · AnalyticalTransitionModel"]
        M04["M04 · UnityInferenceAdapter"]
        M05["M05 · ModelRegistry and Manifest"]
    end
    subgraph OFFLINE["8. Automated experiments, offline training, and validation"]
        direction TB
        T01["T01 · EvaluationScenario and BatchSeasonRunner"]
        T02["T02 · Diverse PlayerBots"]
        T03["T03 · Consented DatasetBuilder"]
        T04["T04 · GroupSplitter and FeaturePipeline"]
        T05["T05 · Python Calibration and Training"]
        T06["T06 · Validation and ReleaseGate"]
        T07["T07 · ONNXExport and UnityParityTests"]
        T08["T08 · Metrics and HumanFeedbackReport"]
    end
    G01 -->|"construct"| G06
    G02 -->|"versioned defaults"| G01
    G02 -->|"shared vehicle limits"| G18
    G02 -->|"identity defaults"| D02
    G02 -->|"policy defaults"| G12
    G03 -->|"mode, manual setting, export choice"| G05
    G03 -->|"start or continue"| G07
    G03 -->|"skip or end practice"| G08
    G03 -->|"driving controls"| G04
    G06 -->|"load next weekend"| G07
    G07 -->|"start session"| G09
    G07 -->|"session lifecycle"| G12
    G08 -->|"finalize active FP if present"| G09
    G08 -->|"pending skips and next state"| G10
    G09 -->|"session complete or ended early"| G10
    G10 -->|"final valid evidence; incomplete markers"| O01
    G10 -->|"flush and commit"| O02
    G10 -->|"commit status and next state"| O03
    O03 -->|"commit acknowledged; advance once"| G07
    G07 -->|"always before qualifying"| G11
    G11 -->|"request calibration"| A01
    G11 -->|"recommendation and evidence"| G03
    G03 -->|"confirmed manual or recommended choice"| G12
    G12 -->|"authoritative permissions and lock"| A10
    G12 -->|"qualifying entry only after confirmation"| G09
    G09 -->|"spawn session participants"| G13
    G13 -->|"human driver binding"| G04
    G13 -->|"AI driver binding"| D01
    G09 -->|"session type and rules"| G14
    G09 -->|"start and stop timing"| G21
    G15 -. "bake and validate" .-> G16
    G16 -->|"checkpoints and distance"| G20
    G16 -->|"valid geometry"| D05
    G16 -->|"curvature and reference speeds"| D06
    G16 -->|"track descriptors"| A04
    G04 -->|"human command"| G17
    D08 -->|"filtered AI command"| G17
    G17 -->|"selected command"| G18
    G18 -->|"observable state"| G19
    G19 -->|"position and boundary events"| G20
    G20 -->|"validated crossings and progress"| G21
    G14 -->|"validity and classification rules"| G21
    G21 -->|"qualifying classification to race grid"| G13
    G21 -->|"classified race results to points"| G06
    G21 -->|"timing and standings"| G03
    G19 -->|"vehicle and traffic observations"| D01
    D01 -->|"tactical tick"| D04
    D01 -->|"physics tick"| D05
    D01 -->|"physics tick"| D06
    D01 -->|"recovery checks"| D07
    D02 -->|"stable style"| D04
    D02 -->|"relative skill offset"| D03
    D03 -->|"approved pace profile"| D06
    D04 -->|"feasible lane"| D05
    D05 -->|"raw steering"| D08
    D06 -->|"raw throttle and brake"| D08
    D07 -->|"recovery command"| D08
    G14 -->|"authorized recovery rules"| D07
    G19 -->|"hazards and margins"| D08
    G16 -->|"track bounds"| D08
    G19 -->|"incidents and traffic"| O01
    G21 -->|"raw sector and lap measurements"| O01
    O01 -->|"quality-labeled records"| O02
    O01 -->|"deduplicated observations"| O06
    O06 -->|"practice and historical context"| A01
    O04 -->|"persistent prior"| A01
    A01 -->|"selected evidence with provenance"| A02
    A02 -->|"bounded profile update"| O04
    A02 -->|"immutable profile snapshot"| A04
    O06 -->|"qualifying end event and observations"| A03
    A03 -->|"single-lap evidence only"| A02
    G12 -->|"read-only lock state"| A03
    A03 -->|"review and residual record"| O02
    A04 -->|"forecast state and policy"| A05
    G12 -->|"allowed windows and bounds"| A05
    A05 -->|"feasible action sequences"| A06
    A06 -->|"player forecast request"| M01
    A06 -->|"candidate AI pace request"| M02
    M01 -->|"player pace distribution"| M03
    M02 -->|"AI pace distribution"| M03
    A04 -->|"aligned forecast state"| M03
    M03 -->|"rolled forecast and uncertainty"| A06
    A04 -->|"evidence and input support"| A07
    M01 -->|"ensemble disagreement"| A07
    M02 -->|"calibration support"| A07
    A03 -->|"qualifying residual diagnostics"| A07
    A07 -->|"trust or reject trajectory"| A06
    A07 -->|"insufficient support or evidence"| A08
    G12 -->|"permitted fallback scope"| A08
    A08 -->|"hold or conservative proposal"| A09
    A06 -->|"best supported first action"| A09
    A09 -->|"non-authoritative proposal"| A10
    G05 -->|"mode and explicit user choice"| A10
    A10 -->|"approved scoped configuration"| A11
    A10 -->|"accept, reject, clamp, queue, hold"| O05
    A11 -->|"approved runtime difficulty"| D03
    A11 -->|"actual application record"| O02
    G12 -->|"revalidate lock and policy version"| A11
    O05 -->|"decision history"| O02
    O05 -->|"human-readable explanation"| G03
    G05 -->|"local preferences"| O03
    O03 -->|"load saved profile"| O04
    O04 -->|"persist profile snapshot"| O03
    G01 -->|"load approved release"| M05
    M05 -->|"artifact and feature manifest"| M04
    M04 -->|"execute player predictor"| M01
    M05 -->|"approved calibration asset"| M02
    G02 -. "scenario settings" .-> T01
    T01 -. "automated season setup" .-> G06
    T01 -. "spawn synthetic players" .-> T02
    T02 -. "test-player commands" .-> G17
    O02 -. "versioned observations" .-> T03
    G05 -. "human export consent gate" .-> T03
    T03 -. "provenance-aware dataset" .-> T04
    T04 -. "training partition" .-> T05
    T04 -. "held-out groups" .-> T06
    T05 -. "candidate models and tables" .-> T06
    T06 -. "only validated candidates" .-> T07
    T07 -. "approved artifact and manifest" .-> M05
    T06 -. "validation results" .-> T08
    T01 -. "scenario outcomes" .-> T08
    O05 -. "policy audit results" .-> T08
    G03 -. "optional fairness and enjoyment feedback" .-> T08
    classDef game fill:#e0f2fe,stroke:#0369a1,color:#0c4a6e;
    classDef driving fill:#dcfce7,stroke:#15803d,color:#14532d;
    classDef data fill:#f1f5f9,stroke:#475569,color:#0f172a;
    classDef adapt fill:#ede9fe,stroke:#7c3aed,color:#4c1d95;
    classDef model fill:#fce7f3,stroke:#be185d,color:#831843;
    classDef offline fill:#fef3c7,stroke:#b45309,color:#78350f;
    classDef policy fill:#fee2e2,stroke:#b91c1c,color:#7f1d1d;
    class G01,G02,G03,G04,G05,G06,G07,G08,G09,G10,G11,G12,G13,G14,G15,G16,G17,G18,G19,G20,G21 game;
    class D01,D02,D03,D04,D05,D06,D07,D08 driving;
    class O01,O02,O03,O04,O05,O06 data;
    class A01,A02,A03,A04,A05,A06,A07,A08,A09,A10,A11 adapt;
    class M01,M02,M03,M04,M05 model;
    class T01,T02,T03,T04,T05,T06,T07,T08 offline;
    class G12,A10,A11 policy;
```

## 3. Component catalogue: meaning, action, and purpose

### 1. Startup, configuration, and player interface

| ID / component | Meaning: what it is | Action: what it does | Purpose: why it exists |
|---|---|---|---|
| `G01` GameBootstrap | Composition root for the application. | Loads configuration, creates services, and injects references. | Establish one explicit ownership and dependency graph. |
| `G02` Definition assets | Immutable authoring defaults: season, weekend, rules, vehicles, identities, and challenge policy. | Supply versioned settings copied into runtime state. | Keep content data-driven without mutating shared assets. |
| `G03` Session UI and HUD | Menus, confirmations, results, privacy controls, and live display. | Submits start/skip/accept commands and displays authoritative state. | Give the player control and transparent explanations without owning game state. |
| `G04` PlayerInputProvider | Human driving input adapter. | Converts keyboard/gamepad input into normalized vehicle commands. | Use the same vehicle command interface as AI. |
| `G05` Consent and preferences | Local settings for mode, manual difficulty, and research export. | Records explicit choices and controls permitted data export. | Separate playing the game from agreeing to research data sharing. |

### 2. Season, session progression, and rules

| ID / component | Meaning: what it is | Action: what it does | Purpose: why it exists |
|---|---|---|---|
| `G06` SeasonController and championship | Season-level calendar and points owner. | Loads each of 19–22 weekends and applies classified results. | Maintain long-term progression independently of adaptive difficulty. |
| `G07` WeekendController | Authoritative weekend state machine. | Orders FP1–FP3, review, qualifying, race, and results. | Make every normal and skipped path explicit. |
| `G08` PracticeTransitionHandler | Idempotent start/end/skip operation handler within weekend orchestration. | Validates commands, finalizes active practice, and marks only pending FP sessions skipped. | Prevent duplicate transitions and accidental skipping of qualifying or race. |
| `G09` SessionController | Owner of an active practice, qualifying, or race. | Spawns participants, starts/stops timing, and finalizes observations. | Use one lifecycle for every session type. |
| `G10` SessionFinalizer | Boundary transaction coordinator. | Flushes completed evidence, marks incomplete laps, commits status and next state. | Retain valid partial practice without fabricated times or double advancement. |
| `G11` PreQualifyingReview | Mandatory review step, including when all practice is skipped. | Displays recommendation/evidence and waits for manual selection or acceptance. | Create an explicit challenge agreement before qualifying. |
| `G12` ChallengePolicyService | Authoritative mode, lock, incident-hold, cooldown, and change-window state. | Locks qualifying/race challenge and exposes immutable policy snapshots. | Make adaptation permissions enforceable rather than suggestions. |
| `G13` VehicleRegistry and GridBuilder | Participant spawning and grid arrangement. | Creates cars and places the race grid using authoritative classification. | Keep grid formation separate from profile estimation. |
| `G14` SessionRulesService | Validity, penalties, completion, and classification authority. | Applies checkpoint, invalid-lap, qualifying no-time, reset, and points rules. | Apply equivalent sporting rules to humans and AI. |

### 3. Track and shared vehicle simulation

| ID / component | Meaning: what it is | Action: what it does | Purpose: why it exists |
|---|---|---|---|
| `G15` TrackDefinition and TrackBakeTool | Circuit authoring and editor-time preprocessing. | Bakes centerline samples, widths, curvature, sectors, checkpoints, and descriptors. | Turn authored geometry into efficient runtime data. |
| `G16` TrackRuntimeData | Read-only baked geometry and track-reference data. | Provides valid paths, distances, reference pace, and feature descriptors. | Give timing, driving, and prediction a consistent track representation. |
| `G17` VehicleCommand and input arbitration | Normalized steering/throttle/brake command contract. | Selects human or AI command source and validates finite values/ranges. | Avoid multiple drivers writing to the same motor. |
| `G18` VehicleMotor and Unity Physics | Shared vehicle dynamics implementation. | Applies commands under the same physical specification for equivalent cars. | Ensure balancing cannot secretly change physical capabilities. |
| `G19` VehicleSensors | Observable vehicle and environment measurements. | Reports pose, velocity, track proximity, incidents, and nearby car motion. | Provide current observations without future player inputs. |
| `G20` TrackProgressService | Validated checkpoint/sector/lap progress. | Tracks checkpoint order, lap count, sector transitions, and legal distance. | Prevent shortcuts from becoming valid progress. |
| `G21` RaceTimingService | Authoritative raw timing, common-boundary gaps, and race order. | Records crossing times, valid sectors/laps, finish order, and classifications. | Avoid unreliable gap estimates based on instantaneous speed. |

### 4. Per-opponent conventional driving

| ID / component | Meaning: what it is | Action: what it does | Purpose: why it exists |
|---|---|---|---|
| `D01` AIDriver | Per-vehicle low-level control coordinator. | Schedules tactics, path following, pace control, recovery, and filtering. | Encapsulate driving without giving the adaptation planner direct motor access. |
| `D02` OpponentIdentity | Stable relative skill and personality. | Supplies skill offset, overtaking preference, consistency, and risk tolerance. | Preserve recognizable opponents instead of retaliatory pace boosts. |
| `D03` PaceProfileMapper | Calibration-backed approved-difficulty mapping. | Maps shared session difficulty plus identity offset into validated pace settings. | Keep difficulty interpretable and within calibrated physical limits. |
| `D04` TacticalDriver | Follow/overtake/yield decision component. | Chooses a feasible lane using observable traffic and hysteresis. | Provide believable racing without unstable lane switching. |
| `D05` PathFollower | Geometric steering controller. | Tracks a speed-dependent look-ahead target on the chosen valid path. | Produce stable steering from waypoints rather than learned end-to-end control. |
| `D06` PaceController | Target-speed and longitudinal command controller. | Uses curvature, backward braking constraints, and approved pace settings. | Reach corners safely while matching the selected challenge envelope. |
| `D07` RecoveryController | Stuck/reversed/off-track recovery state machine. | Proposes recovery commands or requests a rule-authorized reset. | Return to play without an invisible recovery advantage. |
| `D08` DrivingSafetyFilter | Independent low-level command restriction. | Limits raw commands using margins, closing speed, and hazards. | Prioritize safety over tactics and pace; it cannot guarantee zero collisions. |

### 5. Observation, persistence, and audit

| ID / component | Meaning: what it is | Action: what it does | Purpose: why it exists |
|---|---|---|---|
| `O01` QualityGate and ObservationAggregator | Timing/sensor-to-evidence conversion. | Labels clean/traffic/incident/invalid/missing data, preserves masks, deduplicates sectors. | Prevent spins, incomplete laps, or repeated sectors from corrupting skill estimates. |
| `O02` TelemetryRecorder and buffered writer | Versioned append-oriented diagnostic records. | Writes raw times, references, applied actions, session statuses, hashes, and review outcomes. | Support reproducible experiments without blocking physics. |
| `O03` SaveGameService and SessionRecords | Durable local progression state. | Commits session statuses, next state, accepted difficulty, and telemetry commit IDs. | Reload skipped sessions correctly and make transitions recoverable. |
| `O04` PlayerProfileStore | Persistent local player estimates and preferences. | Stores separate single-lap/sustained pace, uncertainty, familiarity, and corrections. | Allow cold-start transfer and personalization across weekends. |
| `O05` DecisionAuditLog | Explanation record for every proposal and result. | Records old/proposed/approved values, evidence, uncertainty, reasons, and versions. | Make fairness decisions inspectable and testable. |
| `O06` SnapshotBuilder and AIIntegrationBridge | Typed gameplay-to-AI boundary. | Creates immutable, timestamped snapshots and invokes AI at permitted events. | Avoid unrestricted access to authoritative game state. |

### 6. Player evidence, review, and adaptation planning

| ID / component | Meaning: what it is | Action: what it does | Purpose: why it exists |
|---|---|---|---|
| `A01` CalibrationEvidenceResolver | Available-evidence and fallback selector. | Selects clean practice, partial sectors, track history, global prior, or manual/default fallback. | Make FP sessions genuinely optional rather than a hidden requirement. |
| `A02` PlayerProfileEstimator | Quality-weighted online estimation, not network training. | Updates pace/consistency, separates incidents, shrinks sparse evidence toward priors. | Personalize conservatively with little human data. |
| `A03` QualifyingReviewService | Post-qualifying evidence and prediction review. | Updates single-lap profile and residual diagnostics; never writes difficulty. | Learn from qualifying without moving the competitive goalposts. |
| `A04` ForecastContextBuilder | Explicit model-state construction. | Combines profile, track, observed conditions, supported identities, and policy snapshot. | Define units, masks, time alignment, and forecast state before planning. |
| `A05` CandidateGenerator | Policy-aware feasible action search space. | Creates fixed pre-session candidates or permitted live sequences respecting cooldowns. | Avoid optimizing actions that cannot legally be applied. |
| `A06` MPCChallengePlanner | Short-horizon challenge optimizer. | Scores supported forecasts for pace-band match, smoothness, and uncertainty. | Select sustainable challenge rather than force a desired finishing gap. |
| `A07` Support and UncertaintyGate | Model trust assessment. | Checks ensemble disagreement, calibration support, feature ranges, and recent residuals. | Prevent confident-looking extrapolation from weak human evidence. |
| `A08` ConservativeFallback | Non-neural recommendation/hold path. | Uses manual/current setting, prior, or clean-pace lookup under the same policy. | Keep the game usable if evidence or inference is inadequate. |
| `A09` AdaptationProposal | Non-authoritative candidate decision message. | Carries proposed difficulty, prediction, evidence count, reason, expiry, and version IDs. | Separate choosing an action from permission to execute it. |
| `A10` FairnessGovernor | Independent authorization service in Racing.Policy. | Checks lock, consent/confirmation, evidence, incidents, support, rate limits, and timing. | Reject unfair proposals even if they score well in the optimizer. |
| `A11` DifficultyConfigApplier | Only adaptation write path. | Rechecks approval/session/version/expiry and updates per-vehicle runtime configuration. | Block stale queued changes and shared-asset mutation. |

### 7. Hybrid PredictiveRaceModel and inference

| ID / component | Meaning: what it is | Action: what it does | Purpose: why it exists |
|---|---|---|---|
| `M01` PlayerPacePredictor ensemble | Compact learned prediction of human pace. | Predicts conditional clean pace and variance using masked history and corrections. | Use limited human data without assuming one lap proves sustained skill. |
| `M02` CalibratedAIPaceModel | Measured mapping from settings to opponent pace. | Predicts AI lap time, variation, and support from track, difficulty, and identity. | Learn controllable game behavior largely from automated runs. |
| `M03` AnalyticalTransitionModel | Explicit forecast progression. | Updates matched-boundary gap and modeled state without inventing evidence. | Make multi-step simulation mathematically defined and inspectable. |
| `M04` UnityInferenceAdapter | Runtime model execution boundary. | Loads compatible models, normalizes features, runs bounded jobs, and checks outputs. | Keep inference details out of driving and hold the current setting on failure. |
| `M05` ModelRegistry and Manifest | Approved model/version authority. | Checks schema, normalization, support domain, hashes, and rollback references. | Deploy only validated compatible artifacts; never retrain weights during a race. |

### 8. Automated experiments, offline training, and validation

| ID / component | Meaning: what it is | Action: what it does | Purpose: why it exists |
|---|---|---|---|
| `T01` EvaluationScenario and BatchSeasonRunner | Automated Unity experiment orchestration. | Runs versioned tracks, seeds, modes, skill sweeps, and skip patterns. | Generate controlled coverage and repeatable comparisons. |
| `T02` Diverse PlayerBots | Synthetic human-driver substitutes for testing. | Vary pace, braking, consistency, learning, traffic response, and correlated errors. | Exercise cold starts and unusual scenarios without claiming to replace humans. |
| `T03` Consented DatasetBuilder | Export and dataset provenance boundary. | Combines synthetic runs with opted-in pseudonymous human records and masks. | Keep personal exports consented, labeled, and separate from local saves. |
| `T04` GroupSplitter and FeaturePipeline | Leakage-resistant preparation. | Splits by player/run/track/bot family; fits training-only normalization. | Prevent adjacent-lap leakage and preserve honest human holdouts. |
| `T05` Python Calibration and Training | Offline parameter fitting. | Builds pace tables and trains compact predictors and corrections. | Move expensive learning out of the live game. |
| `T06` Validation and ReleaseGate | Candidate approval tests. | Checks held-out/multi-step accuracy, uncertainty, safety, fairness, and resource use. | Reject regressions before a model can reach players. |
| `T07` ONNXExport and UnityParityTests | Deployment conversion and numerical check. | Exports supported operators and compares Python/Unity outputs. | Catch shape, schema, normalization, and runtime compatibility errors. |
| `T08` Metrics and HumanFeedbackReport | Evaluation outputs, not gameplay control. | Summarizes prediction, cold start, latency, incidents, fairness, and enjoyment. | Measure real benefits and limitations rather than assume closeness equals fun. |

## 4. Runtime execution: follow these paths through the same graph

### 4.1 Startup and loading

1. G01 reads G02, restores local progress through O03/O04, and loads approved artifacts through M05.
2. G06 selects the weekend; G07 owns its state and G09 owns the active session.
3. G13 binds the human or AI command source for each spawned car.
4. G12 establishes the mode, confirmed difficulty, and permitted change windows.

Bootstrap constructs additional services even where creation arrows are omitted; the diagram emphasizes operational relationships rather than drawing an initialization edge to every node.

### 4.2 Every physics tick

For the human: G04 → G17 → G18. For an opponent: D01 coordinates D04/D05/D06/D07, D08 restricts commands, then G17 → G18. G19 observes the result. Track geometry comes from G16. Each car has a separate command-source binding so human and AI commands do not compete for one motor.

D04 normally runs at a lower frequency than steering/longitudinal control. Overtaking never overrides D08. Recovery remains subject to G14. A safety filter reduces risk; it does not make collision avoidance infallible.

### 4.3 Completing or skipping practice

1. G03 sends skip/end intent to G08, not directly to the profile or database.
2. If active, G09 finalizes through G10. Completed valid sectors/laps are retained; incomplete laps are labeled.
3. G08 marks later pending FP sessions skipped only when requested. A never-started session is Skipped; an already-started early exit is EndedEarly.
4. G10 commits telemetry and progression through O02/O03; only acknowledgement allows G07 to advance.
5. O01 deduplicates sectors and their eventual lap aggregate. Skipped sessions generate session status, not fabricated player/AI lap times.
6. G11 runs even when all practice was skipped.

The same transition token must make retries idempotent. A failed save cannot cause a double advance. Session-boundary saves do not promise exact mid-lap physics restoration.

### 4.4 Resolving evidence before qualifying

G11 requests A01. A01 chooses current clean practice, clean partial sectors, compatible same-track history, global prior, or a manual/default cold start. A02 produces a conservative profile; it does not train new network weights.

A04 builds a forecast. A05/A06 evaluate supported choices through M01/M02/M03. A07 may reject weakly supported planning and select A08. The result reaches the player through G11/G03. G12 records acceptance or manual override. Application still passes A09 → A10 → A11 → D03.

Recommendation display is not permission to apply it. Qualifying entry waits until the chosen configuration is approved and the competitive lock is established.

### 4.5 Qualifying and review

G21 and G14 determine valid times and classification; G13 builds the grid. O01/O06 send qualifying evidence to A03. A03 updates single-lap evidence through A02 and logs residuals. It reads the lock but has no path to A11.

A faster-than-expected lap can improve future estimates; it cannot silently raise the upcoming competitive race difficulty. An invalid/incident lap is not evidence that the player permanently lost skill. Single-lap potential is not automatically sustainable race pace.

### 4.6 Competitive race

G12 locks main difficulty from qualifying through race completion. Driving tactics remain active; main challenge calibration does not. A10 rejects action-changing proposals and A11 independently checks the lock before writing. New observations can update profiles, diagnostics, and future recommendations without changing the current agreed challenge.

A large earned lead is permitted. The architecture contains no target winner or compulsory two-second finish.

### 4.7 Optional adaptive training race

At a designated player-lap boundary, O06 invokes the planning path with a fresh policy snapshot. A05 respects step limits, cooldown, incident holds, and the closing-race freeze. A10 can hold, reject, clamp within support, or queue. A11 revalidates a queued proposal at actual application time.

Provisional v1.1 defaults are a 0.02 maximum normalized step, two-player-lap cooldown, final-two-lap freeze, and at least one subsequent clean lap after an incident. Validate these in actual pace units and human tests; short races may allow no updates.

### 4.8 Offline learning and release

T01 drives automated scenarios with T02 and normal game rules. O02 supplies logs to T03. Human research/export data is admitted only through G05 consent; synthetic runs do not require human export consent. T04 separates training and holdout groups. T05 trains, T06 validates, T07 exports and checks parity, and M05 accepts only approved artifacts.

If any release gate fails, retain the current model. Never update deployed network weights merely because a new weekend completed. Local profile corrections and offline network-weight updates are separate operations.

## 5. Key message contracts on graph edges

| Message | Main route | Required content and rule |
|---|---|---|
| VehicleCommand | G04 or D08 → G17 → G18 | Steering [-1,1], throttle/brake [0,1]; finite values; one selected source per car |
| VehicleObservation | G19 → driving/O01 | Vehicle ID, timestamp, pose/speed, observable neighbors, incidents |
| SectorSummary / LapSummary | G21/O01 → O06/O02 | Raw times, reference version, source session, validity/traffic masks, observation IDs |
| SessionRecord | G10 → O03 | Status, completed observations, incomplete markers, end reason, next state, commit ID |
| CalibrationEvidence | A01 → A02 | Evidence provenance, effective sample count, quality/age, fallback source |
| PlayerProfileSnapshot | A02 → A04 | Separate single-lap/sustained estimates, uncertainty, familiarity, correction, version |
| ChallengeContext | G12 → planner/governor/applier | Mode, session ID, policy version, lock, accepted setting, update windows |
| AdaptationProposal | A06/A08 → A09 → A10 | Candidate difficulty, evidence, uncertainty, reason, request scope, expiry/version |
| ApprovedDifficultyConfig | A10 → A11 → D03 | Approval ID, session scope, accepted setting, expiry, lock/policy version |
| QualifyingReviewResult | A03 → profile/logging | Quality decision, single-lap update, residual, mismatch flag, lock-preserved result |
| ModelManifest | M05 → M04 | Schema, tensor order, units, masks, normalization, support domain, hash/version |

## 6. Prediction and planning: what happens inside the pink/purple groups

### Explicit hybrid dynamics

M01 predicts player pace; M02 predicts opponent pace for a setting. M03 advances state at matched lap/checkpoint timing boundaries:

\[
g_t = \text{player cumulative time} - \text{AI cumulative time}
\]

\[
g_{t+1}=g_t+\hat T_{player,t+1}-\hat T_{AI,t+1}
\]

Positive g means the player is behind. O06/A04 must align timing boundaries or flag the gap unavailable. Imagined observations never increase real evidence counts. Predicted history carries a prediction mask; unsupported traffic dynamics require a shorter horizon or greater uncertainty.

### Objective

A06 minimizes a short-horizon combination of pace-band mismatch, difficulty-change cost, and predictive uncertainty. It does not minimize deviation from a forced finishing gap. Between sessions, a candidate is a fixed difficulty for the next agreed period. Live mode can search sequences only at permitted decision points.

M01 can initially be three small MLPs using masked recent history; M02 can be a calibrated lookup/interpolation asset. These are implementation starting points, not benchmarked optimal sizes. M04 must use the same normalization and units as T04/T05.

### Failure path

If M04 times out, returns invalid values, or cannot load a compatible model, A07/A08 select hold or conservative recommendation. The current approved driving configuration remains usable. No fallback bypasses A10 or rewrites the locked competitive race.

## 7. How scarce human data is countered inside the graph

| Risk | Responsible nodes | Enforced behavior |
|---|---|---|
| No practice | G08, G11, A01 | Missing data is expected; always offer manual/prior calibration |
| Only partial practice | G10, O01, A02 | Use valid segments without inventing laps or double-counting sectors |
| New player | A01, A02, A08 | Shrink toward a conservative prior/manual setting; preserve uncertainty |
| Limited human examples | M02, T01, T02 | Learn opponent controllability from simulations and exercise varied scenarios |
| Human/bot domain mismatch | A07, T04, T06 | Hold out humans/bot families/tracks; reject unsupported deployment |
| Fast local personalization | A02, O04 | Bounded profile correction, not live neural-network retraining |
| Qualifying is the only lap | A03, A02 | Treat as uncertain single-lap evidence, not proof of sustained race consistency |
| Unreliable confidence | A07 | Combine disagreement, support checks, and real prediction residuals |

Synthetic success is not proof of human enjoyment. T08 must state what was tested on humans and what remains simulated.

## 8. How unfair behavior is prevented inside the graph

| Invariant | Enforcement |
|---|---|
| No hidden physical boost | G18 uses shared specifications; A11 writes pace configuration, never engine/grip limits |
| No moving competitive goalposts | G12 locks, A10 rejects, A11 rechecks |
| Qualifying review cannot change race setting | A03 has profile/diagnostic outputs only |
| No punishment for skipping practice | A01 treats absent observations as missing, not low skill |
| No instant adjustment after a spin | O01 separates incidents; A02 protects clean pace; G12/A10 hold changes |
| No retaliatory boosting after an overtake | D02 stays stable; A06 has no retaliation/desired-winner objective |
| No stale queued writes | A11 validates session, policy version, expiry, and current permission |
| Player knows what is happening | G03 explains mode and recommendation; G05 records choice; O05 supplies reason |
| Evidence is auditable | O02 records actual applied actions; O05 records rejected and accepted proposals |

## 9. Lifetimes, deployment, and update frequencies

| Scope | Components / work |
|---|---|
| Editor/content build | G15; authoring assets G02 |
| Application lifetime | G01, M05, consent/preferences services, storage infrastructure |
| Season lifetime | G06 and persisted championship |
| Weekend lifetime | G07/G08, accepted challenge, G11 |
| Session lifetime | G09/G10, grid participants, timing, lock version |
| Per vehicle | G17–G20 instances, D01 and low-level driving runtime state |
| Every physics tick | Sensors, steering/longitudinal control, safety filtering, motor commands |
| Lower-frequency tactical tick | D04 lane/overtake decisions |
| Observation boundary | O01/O06, A02, telemetry and local profile updates |
| Pre-qualifying review | A01/A04–A10 recommendation and explicit confirmation |
| Qualifying completion | A03 review plus G14/G21 classification |
| Permitted player-lap boundary | Optional live A05–A11 processing |
| Offline batch/release | T01–T08; approved artifacts installed through M05 |

The graph is not a promise of parallel execution. Keep Unity object access on the main thread unless the selected API explicitly supports another execution model. Model jobs must have a deadline and must not stall vehicle physics. Fixed timesteps and controlled seed streams improve repeatability but do not guarantee cross-platform identical physics.

## 10. Suggested implementation ownership

- G-prefix responsibilities: primarily Racing.Gameplay, Racing.Vehicle, Racing.Track, UI, and editor assemblies.
- D-prefix responsibilities: Racing.AI.Driving.
- O-prefix responsibilities: Racing.Telemetry, Racing.Integration, and persistence.
- A01–A09: Racing.AI.Adaptation, except player-facing review orchestration in gameplay/UI.
- G12 and A10: Racing.Policy. A11: Racing.Integration with policy-validated write access.
- M01–M05: adaptation prediction, inference adapter, and approved model assets.
- T01/T02: Unity evaluation; T03–T07: offline data/training/export tooling with Unity parity harness; T08: analysis/reporting.

A component is a clear owner, not a requirement for an oversized class hierarchy. Begin with direct interfaces, small immutable snapshots, and a working non-neural baseline. Split helpers further only when responsibility, tests, or lifecycle justify it.

## 11. Acceptance checklist for the whole graph

- [ ] A complete weekend runs without a learned model.
- [ ] Any/all practice can be skipped and reload resumes the correct state.
- [ ] Ending practice early retains only actual valid observations.
- [ ] Practice skip retries cannot advance twice.
- [ ] No-data calibration allows a manual/default setting.
- [ ] Qualifying classifies the grid and reviews evidence without changing competitive difficulty.
- [ ] Competitive mode has zero main-difficulty writes after the lock.
- [ ] Large leads remain earned; no target winner exists.
- [ ] Invalid/late inference holds a usable approved setting.
- [ ] Stale proposals cannot be applied to a new session.
- [ ] Exports honor consent and maintain player/run/track/bot-family holdouts.
- [ ] New models pass numerical parity, prediction, safety, fairness, and resource tests before deployment.
- [ ] Human fairness/enjoyment are reported separately from bot race-closeness metrics.

## Source and scope note

This is a structural expansion of the supplied architecture v1.1. It preserves the agreed behavior while extracting implementation helpers, showing interfaces explicitly, and separating runtime decisions from offline learning. It does not add end-to-end RL, pit/tyre/weather strategy, automatic race-time model training, or mandatory practice.
