# Traversal flow validation

The baseline is the actual Manhattan implementation. OFF does not reintroduce hypothetical bugs from another controller.

| Commit stage | Fixture | OFF | ON |
|---|---|---:|---:|
| Steering | Release at 40 m/s, hold W for 1 second | 57.5118 m/s horizontal | 39.6020 m/s horizontal |
| Wall | 3 m/s approach, 40 m/s along wall | rope released; 39.2485 m/s | rope retained; 40.0000 m/s |
| Transitions | Ground jump at 40 m/s horizontal | 40 m/s horizontal | 40 m/s horizontal |
| Transitions | Reel for 1 second; radial exit speed | 0.0000 m/s | 23.1742 m/s |
| Wrap | Obstructed 3-point route at 30 m/s | 30 m/s, stays attached | 30 m/s, stays attached |

These are Unity EditMode measurements, not human playtest or PPO results. The transition fixture uses zero gravity and drag to isolate the handoff. Enabled exit minimum is half the 24 m/s reel speed. Contact prevents that minimum from injecting motion into a wall.

`TraversalTuning` exposes the four flow flags in the player inspector. `SnagLogger` writes a unique session CSV to `Application.persistentDataPath/TraversalTelemetry`. Simulation time is used, so increased time scale does not change the rates. Event positions are city coordinates.

Choose **Tools → Traversal → Load Snag CSV** to display red snag and orange stuck markers in Scene view. Set the city origin offset when viewing a rebased scene. The detector uses travelled path length over two seconds, not endpoint displacement. A free swing apex suppresses instantaneous snag classification; its normal travel prevents a two-second stuck event. An attached web that remains stationary with movement input can still report stuck. Vertical landing stops are excluded.

The 13 flow cases and five detector/CSV cases passed in the licensed Editor at their respective stages. These are staged measurements; they do not certify the final revision after later input, VFX, pacing and lab changes.

The detector follow-up removes a blind spot that excluded every non-colliding swinging sample from the two-second window. Six native cases now pass, including the distinction between normal apex travel and persistent stationary web input.

## Independent implementation commits

This table records evidence available at each implementation stage. The executed follow-up checks below supersede its historical pending statuses.

| Stage | Commit | Change / validation evidence |
|---|---|---|
| Flow 1 | `5acd7e4` | Positive acceleration below target; high-speed tangent turning. Three staged Unity cases passed; release/W result above. |
| Flow 2 | `ec93ed1` | Glancing contact preserves web; frontal threshold 5 m/s and approach ratio 0.55. Six accumulated staged flow cases passed. |
| Flow 3 | `fb6c16f` | Preserve jump/reel transitions; 0.5 × 24 m/s minimum reel exit, collision guarded. Eleven accumulated staged cases passed. |
| Flow 4 | `ba2a472` | Reset effective rope length on actual wrap-corner change. Thirteen accumulated staged cases passed. |
| Diagnostics | `7eba328` | Physical-step logger and CSV Scene-view viewer. Five staged detector/CSV cases passed. |
| A1 | `699493f` | Sampled flight: 26 segments / 27 points per leg, shrinking wave and tip knot. GPU appearance not tested. |
| A2 | `d3e8a42` | 0.12 s settle with small signed overshoot. Timing curve fixture authored; native final tests not run. |
| A3 | `10206cc` | Speed/tension tremor capped at configured 0.004 m. Rendered effect not measured. |
| A4 | `1611d17` | Generated 256 × 64 braided texture, five strands, 2 repeats/m, transparent URP shader. Shader compilation/captures not tested. |
| A5 | `3b12296` | 128 × 128 radial mark, surface offset 0.015 m, 1.5 s release fade, 16-mark cap. GPU placement not tested. |
| A6 | `cae650d` | Sample all supported route legs; 512-point buffer. Multi-corner render inspection pending. |
| A7 | `af67b6f` | Explicit first/third-person capture fixture at three stages. No final screenshots captured. |
| B1 | `e185385` | Shared human/agent actions; edge events consumed once. Final native input regression not run. |
| B2 | `be9df87` | Separate seeded map with varied buildings, wings and narrow gap. Determinism tests compile; native execution pending. |
| B3 | `48c4251` | Actual controller agent template, 28 vector observations, ray sensor, hybrid actions. Real ML assembly/handshake not tested. |
| B4 | `2db714e` | Pinned UPM installer and Python/PPO setup. Trainer CLI loads; UPM resolution and training blocked. |
| B5 | `c5bd39c` | Matched seed evaluation, same-policy hashes and retrained-policy comparison. Python aggregation tests pass; no trained-policy results. |
| Pacing 1 | `7fd5a5d` | Inclusive taut boundary; baseline already has unilateral slack behavior. Final native tests pending. |
| Pacing 2 | `f8c6dda` | Optional initial ratio 1.08 and descending reel 0.6 m/s. Ratio 0.90 retained as explicit comparison case; no measured benefit yet. |
| Pacing 3 | `52146f7` | Bounded 12-ray / 12-degree anchor assist, strength 0.35; zero strength retains direct ray. Native ray cases pending. |
| Pacing 4 | `fe4a997` | Deliberate ascending upper-arc release boost, 6 m/s at full assist. Height comparison not measured. |
| Pacing 5 | `6050609` | Exponential excess-speed damping, 2/s. Adapter speed limit remains exposed at 95 m/s. Native comparison pending. |
| Pacing 6 | `34cc785` | 0.04 s physical delay separate from visible flight. Sampled attach latency not measured. |
| Pacing 7 | `815521f` | Independently toggled projection expansion, shake, wind and edge lines; all default OFF. Performance/comfort not measured. |
| Native fixtures | `caf43c2` | Matched fixed-input replay and three pacing cases. Prepared, not executed. |
| Visual/trace hardening | `1859cb9` | Primitive-compatible shader, bounded delay, visible-marker limit. Static C# validation only. |
| Evaluation hardening | `7437116` | Deterministic ONNX/Torch agreement for batches 1 and 3; seven Python aggregation checks. No learned-policy evaluation. |
| Detector follow-up | `086211d` | Do not hide persistent stationary webs behind apex exclusion. Revised native detector suite not run. |
| Attachment clock | `10a4d8e` | Advance flight/attachment on physical substeps and reset pending episode shots. Six distance/FPS combinations authored and compiled, not executed. |

Configured values in this table are implementation parameters, not measurements. All seven pacing flags remain OFF by default. A rope ratio below one initially tightens toward the anchor; it does not provide drop clearance. Compare the 1.08 and 0.90 cases before selecting a gameplay default.

## Executed checks on 2026-10-16

The unrestricted retry exposed separate infrastructure problems: registry DNS/TCP access had failed, the account OAuth session was stale, and the Editor had no usable Personal entitlement. Official account login and user-approved Personal reactivation restored the license. The Licensing Client was kept alive during activation through its documented helper option. Following a later host/network change, official reactivation refreshed the stale machine binding. No licensing files or package verification were bypassed.

- DataOnly: **171 cases, 169 passed, zero failures, two skipped**; source manifest valid; five GPU-log parser and three benchmark-environment parser assertions passed. The earlier sandbox junction failures are resolved.
- Full native Unity suites: **389 EditMode and 184 PlayMode tests, zero failures**. A later focused three-case input/Inspector suite also passed, including changing all four serialized flags on an existing body. Those three cases overlap the full suite in two places.
- Focused flow/lab suite: **96/96 passed**, including six distance/FPS attachment cases. At 20 m and 200 m, across 30/60/120 fps, fast physical attachment measured **41.667 ms**.
- The real ML-Agents 4.1.0/inference 2.6.1 packages resolved; the native agent assembly compiled. Genuine four-environment PPO learning and model exports run with trainer 1.1.0. Short 2,000-step handshake runs are smoke checks, not learned-policy quality evidence.
- Seven Python evaluation tests passed. Deterministic ONNX actions matched the trainer's Torch policy at batches one and three with action masks. Evaluation now registers and drains the ML statistics channel.
- New reduced-city Windows player built from clean **809ddc95e6e8836a033abeee5b5842026cdf04a4**, pinned tracking revision **c5ddab14d12dcc8acc8b31048f5752686984f3ab**. Its development profile contains **612/10038 tiles**. Source profile backups were restored after building; the main scene SHA-256 stayed unchanged.
- Shipping Windows validation: **34 checks passed**, including continuous attachment/release, focus cancellation, real-mesh and unloaded-tile collision, wall jumping, rooftop friction and passive pendulum energy.
- Shipping GPU inspection captured idle, left/right gesture/grip and release. One hand is visible during attachment, wrist depth is -0.150 m, and hands disappear after release. The scripted fixture stands still; it does not certify moving hardware comfort.
- Six isolated native GPU captures cover first/third-person firing, settling and attachment. The third-person capsule is a test proxy. Procedural texture center alpha measured **0.549020**, braid contrast **0.278431**, radial-mark maximum alpha **1.0**. A native-only material-property-block construction error and incorrect smooth-step texture falloffs found during this retry were repaired.

Raw test XML, native logs/captures and build identity are delivered outside Git. The preserved prior `44a6410` player remains available separately.

## Matched fixed-input replay

Eight seeded maps used the same 32-second input timeline per condition. Bounds exits can end an episode sooner. This is a scripted baseline, not a trained policy or a successful goal route.

| Four flow flags | Snag count | Stuck count | Stuck/min | Mean speed m/s | Air % | Mean transition loss m/s |
|---|---:|---:|---:|---:|---:|---:|
| OFF | 0 | 8 | 2.835 | 3.899 | 7.35 | 2.357 |
| ON | 0 | 15 | 3.952 | 3.751 | 7.76 | 6.325 |

ON performs worse on this timeline. Event rows primarily show grounded movement into buildings, and swing time is only 1.37% OFF / 2.80% ON. This route cannot substantiate a general swing improvement. Counts are retained, including the unfavorable result; no reward or detector threshold was changed to conceal it.

## Pacing measurements

All three cases start at y=100 m with velocity `(0,-21.2132,21.2132)` m/s, target the same elevated anchor `(0,149.5,49.5)`, use gravity 24 m/s², and have the four flow flags ON. Anchor-selection strength is zero to keep the actual attachment fixed. The elevated block leaves the measured pendulum plane unobstructed. An earlier full-height-wall/sideways fixture failed to reach the specified upper release phase; its failed output is preserved separately.

| Pacing flags | Physical attach ms | Drop before bottom m | First bottom m/s | Release before/after m/s | Rise over next 3 s m | Early constraint correction m |
|---|---:|---:|---:|---:|---:|---:|
| OFF | 266.667 | 21.577 | 42.526 | 23.698 / 23.698 | 6.645 | 0.072 |
| ON, length ratio 1.08 | 41.667 | 25.544 | 41.151 | 19.554 / 25.445 | 6.972 | 0.000 |
| ON, length ratio 0.90 | 41.667 | 12.763 | 41.662 | 25.468 / 31.354 | 10.770 | 7.017 |

Fast attachment is verified. The combined 1.08 setup does **not** increase bottom speed in this fixture; the higher release speed comes from the explicit phase boost. Combining flags prevents attributing the bottom-speed change to gentle reel alone. Ratio 0.90 creates a seven-metre positional correction and is unsuitable as a default. All pacing flags and speed effects remain opt-in; four flow flags default ON in the Windows player and can be disabled with `--flow-off`.

## Learning and final evaluation

Three real PPO runs use 600,000 steps each, four headless environments, trainer seed 17, time scale 20 and unchanged reward configuration. Final comparisons use held-out map seeds beginning at 900000: 16 maps × 8 episodes × four conditions. The same mixed policy is compared OFF/ON, followed by separately retrained OFF/ON policies. Report convergence and swing coverage independently; do not equate navigation success with competent web use.

The mixed run completed 2,037 episodes. Its final five 100-episode success rates were 82%, 83%, 81%, 84%, 85%, below the declared convergence criterion. Final-window failures comprised 73 timeouts and 12 earlier bounds/fall exits; only 6.54% of 37,011 shots attached. Whole-run swing fraction was 2.58%. These observations indicate limited effective aiming/web exploration and remaining long-route failures. They do not establish a single causal explanation. Rewards were not changed.

A separate eight-episode deterministic action diagnostic recorded 4,320 decisions: mean movement-axis magnitude 0.172, grounded observations 97.85%, 12 jump rising edges and 64 fire/refire edges. All eight timed out. This confirms small movement and sparse effective action edges in this diagnostic policy; it does not prove a controller input defect. Training samples stochastic actions, whereas matched evaluation uses deterministic exported actions. The marked difference must be retained when interpreting training success. Policy/value losses remained finite; the mixed run's final reported entropy was 4.918 and value loss 0.0664. Do not replace this failed deterministic evaluation with training success rates. This diagnostic subset is excluded from the 16 × 8 schedule.

OFF/ON retraining, held-out evaluation, an idle-machine frame-time comparison and an independent five-minute human feel review are tracked separately. A ready-for-merge claim requires that human review; automated validation does not replace it. Hardware wrist input, live eye tracking and moving head-tracked comfort have not been exercised by the native fixtures.
