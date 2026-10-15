# Traversal experiments

This lab adapts the requested traversal comparisons to `SwingPhysics` and `WebSwingMotionInput`. It does not change the main Manhattan scene, tracking package revision, calibration, or documented controls. The lab's third-person camera is a visual test fixture, not a new gameplay camera mode.

Implementation and measured results have different status: see [validation](../docs/traversal-flow-validation.md). A configured training run is not evidence of convergence. Do not publish the supplied untrained export check as a learned policy.

## Versions and installation

Keep this project on **Unity 6000.3.22f1**. Unity's [6.6 package list](https://docs.unity.com/en-us/engine/6000.6/manual/packages-list/packages-all/pack-safe/com-unity-ml-agents) identifies ML-Agents **4.1.0**; its [installation guide](https://docs.unity3d.com/Packages/com.unity.ml-agents@4.1/manual/Installation.html) pairs Python **3.10.12** with trainer **1.1.0**. These are the selected pins, not a claim that this project's 6000.3 integration has passed. A successful package install, C# compilation and real trainer handshake are still required.

The official registry metadata for `com.unity.ml-agents@4.1.0` was retrieved successfully on the retry: it declares minimum Unity `6000.0` and dependency `com.unity.ai.inference@2.6.1`. The pinned Editor satisfies that version constraint. This confirms package availability and the declared constraint, not native integration or a trainer handshake.

`requirements-lock.txt` pins the installed Windows training dependencies, including `mlagents==1.1.0`, `mlagents-envs==1.1.0`, `torch==2.2.2`, `numpy==1.23.5`, `onnx==1.15.0` and `tensorboard==2.15.2`. Evaluation uses the pinned ONNX reference evaluator on CPU; it needs no separate ONNX Runtime installation. Python 3.13 used for the repository's data tools is not the training interpreter.

From the repository root, with Python 3.10.12 x64 and a licensed Editor installed:

```powershell
$labPython = (Resolve-Path '..\work\traversal-python\Scripts\python.exe').Path
$labEditor = 'C:\Path\To\Unity\6000.3.22f1\Editor\Unity.exe'
powershell -NoProfile -ExecutionPolicy Bypass -File tools/setup_traversal_lab.ps1 -EditorPath $labEditor -PythonExe $labPython -Build
```

Create that environment first with `py -3.10 -m venv ..\work\traversal-python` and verify its exact patch version. Keep the environment outside Git. The setup command installs Python pins and uses `UnityEditor.PackageManager.Client.AddAndRemove` to install `com.unity.ml-agents@4.1.0`; it does not hand-edit the UPM manifest. Review and commit the generated manifest/lock only after successful resolution. The setup command must run with the Editor closed for this project.

Agent sources are stored in `Assets/TraversalLab/ML~`, ignored by Unity until the package is available. The installer copies them to the ignored `MLGenerated` directory and validates the exact package version. To activate a package already installed, choose **Tools → Traversal → Activate installed ML-Agents sources**, then let Unity compile. Build from `TraversalLabBuild.BuildAgent`. The separate `TraversalLab.unity` is generated through the Editor API for the build; `ManhattanViewer.unity` is untouched. Default executable: `../outputs/TraversalLabPlayer/TraversalLab.exe`. Set `MANHATTAN_LAB_BUILD` to choose another location outside the checkout.

Without the package, `TraversalLabBuild.BuildScripted` can build the lab's fixed-input and pacing fixtures. Invoke either build method with a batch-mode Editor process and wait for its exit code. Package installation and activation must finish before starting the agent build.

## Inputs, task and observations

Human and agent controls implement `ITraversalInputSource`. Human keys remain Space jump, held LMB attach, release LMB detach, RMB release, E reel in, Q reel out, R flight. Head pose does not become steering input.

The seeded map has nine varied buildings, optional offset wings, an explicit narrow alley, street starts, and 20% rooftop goals. Each seed and episode selects the same layout and endpoints. This is a separate small test environment; it does not replace the saved Manhattan data or edit the demo scene.

The agent receives 28 vector values: local velocity, goal direction/distance, each hand's selected rope state, five movement states, ground/tension, aim distance, anchor direction, pitch and previous actions. A child `RayPerceptionSensorComponent3D` supplies 11 horizontal building rays. The shared controller currently supports one active web; the two hand observations reflect its selected emitter, not two independent simultaneous ropes.

Actions are four continuous axes (move X/Y, yaw/pitch rate) and discrete branches `[3,3,2,2]` (release/hold/refire edge, neutral/in/out reel, jump, selected hand). Decisions occur every five 60 Hz environment steps; actual movement still substeps at 1/120 s. Gravity and other baseline traversal settings match the actual adapter, not the request's hypothetical controller.

An episode ends on a goal within 2 m, bounds/fall, or 45 seconds. Reward is distance progress divided by initial distance, -0.002 per simulated second, +2 success, -1 bounds/fall. Snag/stuck event counts never enter reward. They remain diagnostics, including when progress fails.

## Training and convergence

```powershell
$labPlayer = (Resolve-Path '..\outputs\TraversalLabPlayer\TraversalLab.exe').Path
& $labPython tools/verify_traversal_policy.py --output ..\work\policy-schema-check
powershell -NoProfile -ExecutionPolicy Bypass -File tools/train_traversal.ps1 -Player $labPlayer -PythonExe $labPython -RunPrefix flow-comparison -Steps 600000 -NumEnvs 4
& $labPython -m tensorboard.main --logdir ..\outputs\TraversalTraining
```

The first command exports a **random, untrained** network and compares its deterministic ONNX actions with Torch. It checks serialization, not the Unity connection or skill. The train command runs three PPO policies with identical hyperparameters and trainer seed 17: mixed flow conditions, OFF only, ON only. Runs use `-batchmode -nographics`, four environments, CPU Torch and time scale 20. A collision/event trace uses simulated seconds even at accelerated time scale. 600,000 steps is a budget, not guaranteed convergence; repeat with a new prefix or use the trainer's explicit resume workflow when justified.

`flow-comparison-models.json` records model and telemetry locations. TensorBoard contains standard mean reward plus `Traversal/SuccessRate`, `Traversal/EpisodeSeconds`, `Traversal/GoalSeconds` and speed statistics. Inspect all three policies:

```powershell
& $labPython tools/analyze_traversal_training.py ..\outputs\TraversalTraining\telemetry\flow-comparison-mixed --output ..\outputs\TraversalTraining\mixed-analysis.json
```

The reporting criterion is at least 500 completed episodes, each of the final five 100-episode windows at least 85% successful, with at most five percentage points between windows. Worker CSVs are ordered by UTC completion timestamp; time-normalized metrics still use simulated seconds. This is a declared criterion, not a measured result. Missing attachment or low swing coverage is reported even if walking navigation succeeds. For nonconvergence, inspect timeout/bounds, pitch/ray coverage, aim/fire edges, target altitude and exploration before proposing changes. The analyzer never edits rewards. Report the cause before changing the task or reward structure.

## Matched evaluation

```powershell
& $labPython tools/evaluate_traversal.py --env $labPlayer --common ..\outputs\TraversalTraining\flow-comparison-mixed\TraversalFlow.onnx --off ..\outputs\TraversalTraining\flow-comparison-off\TraversalFlow.onnx --on ..\outputs\TraversalTraining\flow-comparison-on\TraversalFlow.onnx --maps 16 --episodes 8 --seed 1000 --output ..\outputs\TraversalEvaluation
```

Use a new output directory per evaluation. Four runs compare the exact same mixed policy with flow OFF/ON, then the separately retrained OFF/ON policies. Models are hashed from the bytes actually loaded. All conditions use the same 16 map seeds and eight episode endpoints per map; duplicate/missing episodes fail validation. Pacing experiments and camera effects remain OFF in this flow-only comparison.

Outputs include raw per-episode CSVs, merged event CSVs, `summary.csv`, `summary.json` and a Markdown table: snag/minute, stuck/minute, duration-weighted speed/air fraction, transition-count-weighted speed loss, success rate and successful goal time. Failed episodes remain in rates and speed metrics; unsuccessful goal time is blank. The transition metric is the positive speed drop across a physical substep that changes state; it also includes any forces acting during that substep. It does not prove that the handoff alone caused the loss.

Load `events.csv` through **Tools → Traversal → Load Snag CSV**. Marker positions are city metres; enter the current floating-origin offset. Scene drawing caps visible markers and nearby labels for large logs.

## Native fixed-input, pacing and visual checks

Run the built lab with `--scripted-flow --map-seed 1000 --map-count 8 --telemetry-dir <absolute-output-folder>` plus `-batchmode -nographics`. This replays the same input timeline per seed with the four flow flags OFF/ON. The resulting trajectories may differ; it is not PPO evaluation. Summarize with:

```powershell
& $labPython tools/summarize_traversal_replay.py ..\outputs\TraversalReplay --output ..\outputs\TraversalReplay\summary.json
```

Run `--measure-pacing --pacing-report <absolute-json-path>` headless to measure baseline, enabled pacing with ratio 1.08, and enabled pacing with ratio 0.90. The fixture fixes anchor-assist strength at zero so it compares the same actual anchor, and logs that anchor, attach delay, initial drop, first bottom speed, early position correction, speed before/after an ascending release, and three-second post-release rise. Test cone selection separately. Failed shots/rope loss/unreached phases are reported as failures. A shorter rope than attachment distance tightens/snaps toward the anchor; it cannot create slack. The default drop experiment uses 1.08 and remains optional. Do not assert a benefit until these native measurements exist.

Run `--web-visual-lab --visual-output <absolute-capture-folder>` **with graphics**, without `-nographics`, for first/third-person firing, settling and attached screenshots. Inspect the captured images and shader diagnostics. The visual code exposes flight segments, amplitude, strands, thickness, tremor and mark parameters; it supports all existing wrap corners. Physical fast attachment is separate from visible arrival and impact marks.

Seven pacing flags are independent from the four flow flags in `TraversalTuning`: unilateral rope, initial drop, descending reel, bounded anchor selection, phase release boost, soft speed limit and fixed physical attach delay. The first flag makes the taut boundary inclusive; the actual baseline already supports a slack rope. The fixed delay defaults to 0.04 s, clamped to 0.01–0.05 s, sampled on physical substeps instead of render frames; the observed event time is recorded directly. All pacing flags default OFF. `TraversalSpeedEffects` exposes independent FOV, shake, wind and edge-line toggles, also OFF. `--speed-fx` is an explicit test override. Do not enable them globally for head-tracked use before comfort and frame-time checks.

Before a ready PR: run DataOnly and final Unity tests; compile/install the real ML integration; obtain native replay/pacing numbers, inspect both visual views, train/analyze/evaluate actual policies, measure the fixed benchmark route and test a new Windows Manhattan development player. Keep the preserved player available and record another person's five-minute feel review as required by the team workflow.
