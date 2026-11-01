A VR rehabilitation game for patients recovering from knee injury or surgery. The patient sits on a beach
and trains **knee flexion** (pulling the heel back). A wave runs up the sand toward their feet; they must bend
the knee to a target angle and hold it until the wave pulls back. The therapist configures the session per
patient on a setup screen, and sees a results summary and leaderboard at the end.

Engine: Unity (URP) with the Meta XR / OpenXR stack (Meta Quest). All gameplay scripts are in
`Assets/Scripts/WaveGame/` plus `Assets/Scripts/SeatedHeadLock.cs`. The scene is `Assets/Scenes/BeachScene.unity`.

---

## 1. How the game is used (workflow)

1. **Therapist setup screen** (PC monitor, mouse + keyboard) appears when the game starts and after every session.
   The therapist enters:
   - **Patient name** (also used in the data files and leaderboard)
   - **Leg to train**: Left, Right, or Both
   - **Min target angle** and **Max target angle** (the patient's own range, in degrees)
   - **Number of targets** (how many waves, 1–50)
   - **Starting hold time** and **Max hold time** (seconds)

   Pressing **START** validates the numbers (see 4.1) and begins the session.
2. **Training.** The patient, seated with the headset, sees the beach, a large panel with instructions, their
   virtual leg, and the wave. They train one leg per "part".
3. **Both legs mode.** The patient is told "Part 1 of 2: RIGHT LEG, then the left leg", with a countdown
   ("Press A / Space when ready · starting in 10"). The leg starts when they press A/Space or when the 10 s
   countdown ends. After the right leg, a short rest message, then "Part 2 of 2: LEFT LEG" with the same
   countdown. The right leg always goes first.
4. **Results screen** (PC monitor): per-leg results, overall rating, comparison between legs.
5. **Leaderboard** (button on the results screen): top scores of all patients, with the current patient highlighted.
6. **NEW SESSION** returns to the setup screen.

Session data is also written to CSV files (section 6).

---

## 2. Core game loop (one wave)

Implemented in `WaveGameManager.RunWave`.

1. **Target chosen** from the patient's range (section 3). The HUD shows the target marker on the angle gauge.
2. **Wave approaches** (4 s). The water surges up the beach and stops just in front of where the heel would be
   if the knee were at the target angle. The patient must bend the knee to reach/past the target.
3. **Hold phase** (hold time, starts at 2 s). While the water sits at the feet the knee angle must stay
   **at or above `target − 3°`** and **not above the safety limit**. The game accumulates the time the patient is
   "holding". A progress bar shows the hold time.
4. **Result.** The wave is **cleared** if the patient held for at least **80 %** of the hold time.
   - Cleared: points, sound, the next target gets harder.
   - Not cleared: "Splash!" with a splash effect and sound, the next target gets easier.
5. **Wave retreats** (backwash sound), then a short "Relax your leg" rest (2.5 s), then the next wave.
6. Every wave is logged to the CSV file straight away.

### Scoring
- Cleared wave: **100 + 20 × current streak** points (first clear 100, second in a row 120, third 140, …).
- Missed wave: **fraction held × 40** points (0–39), and the streak resets to 0.

### Safety
- The **safety limit** is 110° (`safetyCeilingAngle`). The game **never asks** for it: targets are capped at
  **safety limit − 5° = 105°**.
- If the knee goes past 110° the game shows a red warning ("Too far! Ease your knee back gently"), plays an
  alert beep, counts a **safety alert**, and that time does not count as holding.

---

## 3. How the targets change (difficulty adaptation)

**Short answer: the target starts in the easy-to-middle part of the patient's range and adapts after every wave.
It goes harder when the patient succeeds and easier when they fail. It is not a fixed easy-to-hard ramp.**

The therapist gives a **range**: a minimum angle (easiest) and a maximum angle (hardest, the patient's
comfortable maximum). The game keeps a difficulty **level** between 0 and 1 (0 = minimum angle, 1 = maximum angle):

```
target angle = min + level × (max − min)        (never above 105°, never below 5°)
```

| Setting | Default | Meaning |
|---|---|---|
| Starting level | 0.30 | The first wave is 30 % of the way from min to max |
| Level step | 0.15 | After each wave the level moves by 15 % of the range |
| Level limits | 0 … 1 | The target never leaves the therapist's range |

Rules after each wave:
- **Wave cleared → level + 0.15** (target gets harder), capped at 1 (the maximum angle).
- **Wave missed → level − 0.15** (target gets easier), floored at 0 (the minimum angle).
- **Hold time** also adapts: it starts at the therapist's starting hold (default 2 s) and **increases by 0.25 s
  after every cleared wave**, up to the max hold (default 4 s). It does **not** decrease after a miss.

**Worked example** (min 30°, max 90°, 10 waves, patient clears everything):

| Wave | 1 | 2 | 3 | 4 | 5 | 6 | 7–10 |
|---|---|---|---|---|---|---|---|
| Level | 0.30 | 0.45 | 0.60 | 0.75 | 0.90 | 1.00 | 1.00 |
| Target | 48° | 57° | 66° | 75° | 84° | 90° | 90° |
| Hold (s) | 2.0 | 2.25 | 2.5 | 2.75 | 3.0 | 3.25 | 3.5 → 4.0 |

If the patient misses wave 3 (target 66°), wave 4's level drops back to 0.45 (57°) and the hold time stays the
same, so the game stays challenging but achievable: the patient keeps working near the edge of what they can do.

Why it is designed this way: each patient has a different range because of how injured the knee is, so the
therapist sets the range, and the game automatically finds the patient's working level inside it without
frustrating them (too-hard targets are followed by easier ones) or boring them (success is followed by more).

---

## 4. Components

### 4.1 Therapist setup screen — `DoctorPanel.cs` (new)
- Built at runtime (no scene objects needed); screen-space overlay, scales with the monitor resolution.
- Creates an EventSystem with the Input System UI module if the scene has none.
- Validation before starting: name not empty; min ≥ 5°; max > min; max ≤ 105° (safety limit − 5°);
  1–50 targets; hold ≥ 0.5 s; max hold ≥ starting hold. Errors show in red on the panel.
- On START it writes the values into `WaveGameManager.ApplyDoctorSettings(...)`.
- Shows the safety limit as a hint, and a note when "Both" is selected.
- Can be turned off with `useDoctorPanel` on the manager (then A/Space starts a session with inspector values).

### 4.2 Game manager — `WaveGameManager.cs` (extended)
- Runs the session: `RunSession` → one or two `RunLeg` stages → `RunWave` for each target.
- `LegMode` (Left / Right / Both). For Both it runs Right then Left, calls `SwitchToLeg` (only the trained leg
  follows the sensor; the other rests in a relaxed pose) and shows the intro/countdown (`ShowStageIntro`).
- Records a `StageResult` per leg (waves, cleared, score, best angle, highest target cleared, average hold
  accuracy, safety alerts) and saves a leaderboard entry at the end.
- Wave visuals (see 4.6), sounds (see 4.7), HUD updates, CSV logging.

### 4.3 Results screen — `ResultsPanel.cs` (new)
- Header: patient, date and time, target range.
- **Overall rating** from the share of waves cleared across all legs trained:
  ≥ 80 % "Excellent work!", ≥ 50 % "Good progress", otherwise "Keep practising — every session helps".
- **One card per leg** (right and left side by side in Both mode):
  waves cleared (with a coloured bar), score, best knee bend, highest target reached, hold accuracy
  (average share of each hold that was on target), safety alerts (red if above 0).
- **Comparison** in Both mode, e.g. "The right leg bent 12° further than the left leg".
- A note if the safety limit was passed.
- Buttons: **VIEW LEADERBOARD / VIEW RESULTS** (toggle) and **NEW SESSION**.

### 4.4 Leaderboard — `Leaderboard.cs` (new)
- Stored as JSON: `[persistentDataPath]/leaderboard.json` (the best 100 entries are kept).
- Entry: patient, total score, waves cleared / total, best angle, mode (Left/Right/Both), date.
- Ranked by **total session score** (in Both mode the two legs' scores are added, so a Both session usually
  scores higher than a single-leg session).
- The board shows the top 8; the current patient is highlighted, and their row is added below the list if they
  placed lower. A line says "<name> placed #N of M".

### 4.5 Patient HUD — `WaveHUD.cs` (existing)
A large world-space panel in front of the patient, built at runtime: instruction/status text, current knee
angle vs target, an angle gauge with target and safety-limit markers, a hold-progress bar and a warning line.

### 4.6 Wave realism — in `WaveGameManager.cs`
- The imported Blender wave mesh is copied into a plain mesh with faces pointing up and follows the sloping sand.
- **New:** the leading edge is deformed every frame (a travelling, uneven front line made from two sine waves
  plus a small swell behind it), so it looks like moving water instead of a straight sheet.
- **New motion:** the wave surges in fast and slows (cubic ease-out), then drains back slowly and speeds up
  (ease-in backwash).

### 4.7 Sound
- **New generated surf sound:** a 4.5 s noise-based wave that swells, brightens at the crest and hisses out
  (`Surf`), played when the wave starts; a quieter 3 s backwash when it retreats.
- Existing generated sounds: clear chime, splash, safety-alert beeps.
- **New ambience** (`BeachLife.cs`): the three audio files in `Assets/Sounds/` now loop:
  `Atmosphere.mp3` (sea ambience, 2D), `birds.mp3` (birds, partly 3D), `people and ato.mp3` (crowd, 3D,
  placed where the people are). Before, the scene's audio sources had no clips assigned.
  (The file contents were assumed from their names.)

### 4.8 Beach life — `BeachLife.cs` and `BeachPersonRig.cs` (new)
- Placeholder people are built from simple shapes and placed from an editable **Spots** list on the component
  (activity, position, rotation, size, speed). Activities:
  - **Throw**: two people play catch with a ball.
  - **Walk**: people stroll along the water's edge, back and forth.
  - **Splash**: children hopping in the shallows.
  - **Wave**: a person waving at the patient.
  - **Sunbathe**: lying down (behind the patient, seen if they turn around).
  - **Sit**: a parent sitting on the sand strip.
  - **BuildCastle**: a child sitting beside a stepped sandcastle (with bucket and spade), scooping with both
    hands; the castle slowly grows, then restarts. Two such families, one on each side.
- The 5 seagulls in the scene now circle.
- **Replacing the models later:** put realistic character prefabs in the **Person Prefabs** array. Optionally add
  the `BeachPersonRig` component to a prefab and assign arm/leg bones (procedural motion) or an Animator with
  states named `Walk`, `Throw`, `Splash`, `Wave`, `Sunbathe`, `Sit`, `BuildCastle` (they are cross-faded when
  present). With neither, the model is simply moved around. Positions are not hard-coded, they come from the
  Spots list. (Not yet tested with a real prefab.)

### 4.9 Existing parts (written before this work)
- `JointAngleCalculator.cs`: knee flexion angle from hip, knee and ankle joint positions
  (`180° − angle between thigh and shin`; 0° = straight leg) with a frame-rate-independent low-pass filter
  (smoothing 0.6).
- `SimulatedLegJoints.cs`: stands in for the Azure Kinect: moves hip/knee/ankle points. **S / Down** bends the
  knee, **W / Up** straightens; thumbstick back/forward in the headset. 0–140°, 45°/s, small jitter to mimic
  sensor noise. The game cannot tell the difference, so the real sensor can replace it later.
- `VirtualLegPose.cs`: bends the virtual shin in the scene to match the measured angle and tells the manager
  where the heel would be at any angle (used to place the water).
- `RehabInput.cs`: all input through Unity's Input System. Start: A/X (controllers), Space/Enter, gamepad A.
  Bend: thumbstick or W/S.
- `SeatedHeadLock.cs`: the head can look around but cannot move away from the seat.
- `SessionLogger.cs`: CSV log (section 6).

---

## 5. Controls summary

| Action | Keyboard | Headset / gamepad |
|---|---|---|
| Bend the knee (simulated) | S / Down arrow | Thumbstick back |
| Straighten the knee | W / Up arrow | Thumbstick forward |
| Continue (leg intro) | Space / Enter | A or X / gamepad A |
| Setup, results, leaderboard | Mouse + keyboard | — (see limitation 1) |

---

## 6. Data files

Location: `[persistentDataPath]` (on Windows `C:\Users\<user>\AppData\LocalLow\DefaultCompany\GP2`).

- `Sessions/wave_session_<patient>_<yyyyMMdd_HHmmss>.csv`: one file per leg per session, one row per wave,
  flushed immediately. Columns: `session_id, patient_id, operated_leg, wave, time_utc, target_percent,
  target_deg, hold_s, min_angle_in_hold, max_angle_in_hold, held_fraction, cleared, points, total_score,
  session_max_angle, safety_alerts`. Note: `target_percent` now stores the 0–1 difficulty **level** (section 3).
- `leaderboard.json`: all-time high scores.

---

## 7. Summary of what was added in this work

1. Therapist setup panel with patient name, leg mode, per-patient min/max target range, number of targets and
   hold times, with validation.
2. Target logic changed from "percentage of one comfortable maximum" to "level inside the therapist's
   min–max range".
3. Left / Right / Both training with instruction screens, auto-start countdown, and per-leg results.
4. Results panel (per leg, overall rating, comparison) and persistent leaderboard.
5. More realistic wave (edge deformation, surge/backwash motion) and generated surf/backwash sounds.
6. Ambient audio (sea, birds, crowd) wired up and looping.
7. People on the beach: catch, walking, splashing, waving, sunbathing, parents sitting and children building
   sandcastles on the sand strip; seagulls circling; designed to be replaced by realistic models.

---

## 8. Known limitations / not done

1. **The setup, results and leaderboard screens are on the PC monitor only.** They are screen-space overlays;
   inside the headset the patient sees the world-space HUD (which shows the instructions and the short result
   line). The therapist operates the PC while the patient wears the headset.
2. **No 3-minute timing yet.** A session ends after the chosen number of waves, not after a time limit
   (waiting on the team's decision).
3. **Sitting / standing posture guidance** was skipped on request (the game assumes the seated patient).
4. The knee angle still comes from the **keyboard / thumbstick simulator**; the Azure Kinect is not connected.
5. People are placeholder shapes; seagull facing direction and the audio file contents were not verified by ear
   or eye; the game has not been run on a real headset in this work.
6. The sand strip is shallow, so most beach people stand in the shallow water in front of the patient.
7. Leaderboard ranks by total score only (not by leg mode, and not by difficulty/range).
