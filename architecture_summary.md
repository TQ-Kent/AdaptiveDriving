# Adaptive Racing: Architecture Summary

A racing game where the AI opponents adjust to how good the player is, without cheating and without changing the difficulty in the middle of a competitive race.

---

## Part 1: The main components in plain words

The system has **8 groups**. Each group has one job.

### 1. Startup and player interface (G01–G05)
**What it does:** Starts the game, loads the settings, shows the menus and HUD, and reads the keyboard or gamepad.
- **GameBootstrap**: starts up all the other parts.
- **Definition assets**: the default settings for seasons, cars, rules and opponents.
- **UI / HUD**: menus, results and on-screen info. It shows things but doesn't make decisions.
- **PlayerInputProvider**: turns button presses into "steer / throttle / brake".
- **Consent and preferences**: the player chooses the mode and difficulty, and whether their data can be shared.

### 2. Season and session flow (G06–G14)
**What it does:** Runs the race weekend in order: Practice 1–3, then review, then qualifying, then the race.
- **Season / Weekend controllers**: track the calendar and the points.
- **Practice handling**: practice can be skipped or ended early, and the game still works.
- **Pre-qualifying review**: shows the player a recommended difficulty. The player accepts it or picks their own.
- **ChallengePolicyService** *(the rule keeper)*: **locks** the difficulty from qualifying until the end of the race.
- **Rules / Grid builder**: lap validity, penalties and the starting grid. The same rules apply to humans and AI.

### 3. Track and car simulation (G15–G21)
**What it does:** The physical world, meaning the track, the car physics, sensors and lap timing.
- Every car, human or AI, uses **the same physics**. The AI never gets a secret speed boost.
- The track is pre-processed into data (centre line, corners, checkpoints).
- Checkpoints stop players from taking shortcuts. The timing system records lap and sector times.

### 4. AI opponent driving (D01–D08)
**What it does:** Drives each AI car using normal, non-learned driving logic.
- **AIDriver**: the "brain" that coordinates the parts below.
- **OpponentIdentity**: each opponent has its own personality and skill level.
- **TacticalDriver**: decides whether to follow, overtake or give way.
- **PathFollower / PaceController**: steers along the racing line and controls speed into corners.
- **RecoveryController**: gets the car going again after a spin or getting stuck.
- **SafetyFilter**: does a final check that blocks dangerous commands.

### 5. Data, saving and audit (O01–O06)
**What it does:** Collects clean data about the player, saves progress, and logs every decision.
- **QualityGate**: removes bad data such as spins, crashes and incomplete laps.
- **Telemetry / Save game / Player profile**: stores lap data, progress and the player's skill estimate.
- **DecisionAuditLog**: records *why* the difficulty changed or stayed the same.
- **AIIntegrationBridge**: the only safe door from the game into the AI.

### 6. Player analysis and difficulty planning (A01–A11)
**What it does:** Estimates how good the player is and picks a fair challenge level.
- **Evidence resolver**: uses practice data if there is any. If not, it falls back to history or a safe default.
- **PlayerProfileEstimator**: updates the player's skill estimate carefully, a little at a time.
- **QualifyingReview**: learns from qualifying but **cannot** change the race difficulty.
- **MPC Planner**: looks a few laps ahead and picks the difficulty that keeps the race close and smooth.
- **Uncertainty gate / Fallback**: if the AI isn't confident, it uses a simple, safe choice instead.
- **FairnessGovernor** *(the referee)*: approves or rejects every difficulty change.
- **DifficultyConfigApplier**: the **only** part allowed to actually change the difficulty.

### 7. Prediction models (M01–M05)
**What it does:** Small machine-learning models predict lap times.
- **PlayerPacePredictor**: predicts how fast the player will be.
- **AIPaceModel**: predicts how fast an AI car will be at a given difficulty.
- **TransitionModel**: simple maths that predicts the time gap between the player and the AI.
- **Inference adapter / Model registry**: runs the models in Unity and only loads approved versions.

### 8. Offline training and testing (T01–T08)
**What it does:** Trains and tests the models **outside** the game, never during a race.
- **Bot runner / PlayerBots**: fake players race automatically to generate lots of test data.
- **DatasetBuilder**: real player data is only used with their consent.
- **Python training → Validation → ONNX export**: models are trained, tested, and only then put into the game.
- **Reports**: measure accuracy, fairness and fun.

---

## Part 2: Slide-ready summary

### Slide 1: The idea
- A racing game with **AI opponents that adapt to the player's skill**
- Goal: **close, fair and fun races**, with no cheating AI
- Hybrid approach: classic AI driving plus small ML prediction models

### Slide 2: The architecture at a glance (8 groups)
| Group | Role |
|---|---|
| Startup and UI | Menus, input, settings, consent |
| Season flow | Practice → Review → Qualifying → Race |
| Simulation | Track, shared car physics, timing |
| AI driving | Steering, speed, overtaking, safety |
| Data | Clean data, saving, decision log |
| Adaptation | Estimate skill and plan the difficulty |
| Prediction | ML models predict lap times |
| Offline | Train and test models outside the game |

### Slide 3: How adaptation works
1. The player drives in practice (or skips it)
2. The system estimates the player's skill from **clean laps only**
3. The ML models predict lap times, and the planner picks a fair difficulty
4. The player **sees and confirms** the recommendation
5. The difficulty is **locked** for qualifying and the race

### Slide 4: Fairness by design
- Same car physics for everyone, so **no hidden boosts**
- Difficulty **locked** during a competitive race
- One crash or spin doesn't lower your rating
- Skipping practice is **not** punished
- Every change is checked by the **FairnessGovernor** and logged

### Slide 5: Dealing with little player data
- Safe default when there's no data, then small updates over time
- Bots generate large amounts of training data
- Uncertainty check: when not confident, the system falls back to a simple, safe choice
- Models are trained **offline** and validated before release

### Slide 6: Key takeaway
> The AI **adapts the challenge, not the rules**: it is personal, transparent, and fair.
