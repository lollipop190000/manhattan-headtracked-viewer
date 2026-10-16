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

All three PPO budgets completed: mixed 2,037 episodes, OFF 2,412, ON 2,010 (**6,459 total**). Final five 100-episode success windows were mixed `[.82,.83,.81,.84,.85]`, OFF `[.92,.94,.93,.88,.92]`, ON `[.75,.82,.77,.71,.83]`. None meets the declared stability criterion; OFF misses the maximum five-point spread by one point. Whole-run swing fractions are 2.58%, 4.42%, 2.31% respectively. No reward changes or selective episode removal were made. TensorBoard histories and model hashes accompany the external results.

Held-out evaluation completed **16 maps × 8 episodes × four conditions = 512 episodes**. All schedules validated, and the two common-policy hashes are identical.

| Condition | Goal success | Snag/min | Stuck/min | Mean speed m/s | Air % | Transition loss m/s | Successful goal time s |
|---|---:|---:|---:|---:|---:|---:|---:|
| Same mixed policy, OFF | 26.6% | 0.000 | 0.543 | 0.648 | 2.62 | 2.297 | 25.048 |
| Same mixed policy, ON | 21.9% | 0.116 | 0.686 | 0.581 | 1.99 | 5.035 | 23.679 |
| Separately trained OFF | 52.3% | 0.016 | 0.873 | 1.744 | 11.17 | 2.017 | 17.070 |
| Separately trained ON | 28.1% | 0.000 | 0.719 | 1.284 | 4.47 | 3.595 | 23.654 |

Raw snag/stuck counts are 0/46, 10/59, 1/56, 0/56. ON is not a general improvement: same-policy counts/speed/success worsen, and retrained ON has lower success and speed despite fewer snags and lower stuck/min. All common-policy ON events were grounded. Deterministic evaluation swing fractions were only 0.12%, 0.24%, 1.95%, 0.55%, so these policies do not validate skilled swinging or identify each flag's separate causal effect. The input diagnostic and limited overhead ray observations suggest action/exploration and aiming coverage should be investigated before another learning run. Rewards remain unchanged.

## Human reconnect assistance

The author's play feedback reported little perceptible difference, especially in controls, and identified releasing a web then finding the next attachment as the main difficulty. This is not an independent five-minute review; duration and hardware comfort were not established.

A separate human/sensor reconnect setting addresses that action sequence without changing keys, steering from head pose, adding a launch impulse or automatically releasing held webs. Within 1.2 seconds of an airborne detachment, a missed next press can remain buffered for 0.35 seconds while held. Retries use the current frame's ray at most about 30 times/second. Releasing the button, losing focus, pausing or expiry cancels the request. Sensor event calls never cache and replay an old ray.

A direct hit remains exactly where aimed. A miss can sample at most 12 degrees around the aim, accepting only real overhead/forward collider hits within 250 m. A successful reconnect can attach physically after 0.04 seconds while retaining visible projectile travel. This affects subsequent airborne shots, not the first shot. Buffer, cone, delay, window and enabled state are serialized; `--reconnect-off` disables the assistance. The composition enables it for human/sensor input, while new motion adapters and the agent lab default it OFF. The completed PPO comparison therefore does not include or certify this later human assistance.

Initial native swing/input checks passed **107/107**, including nine reconnect cases. The final full run passed **401 EditMode / 184 PlayMode**, now including eleven reconnect cases and the shared action-adapter/global-zero-assist checks. DataOnly passed **170/172, two skipped, zero failures**; eight Python evaluation tests passed. A near-miss fixture selected a real hit 8.8066 degrees from aim; buffered physical attachment measured **41.667 ms**. Exact direct hits, incoming velocity, original first-shot timing, range/cone bounds, fresh aim, release/focus cancellation, expiry and disabled mode are checked. Global assist zero disables the reconnect aid. An explicit shipping-player fixture also exercises these paths; its final build result is recorded with the delivered player.

Two preceding rendered PlayMode runs each had one synthetic measurement first-hold failure (183/184): the workflow correctly returned to A after an observation-stream restart. The fixture now permits at most two explicit A retries, each reacquiring the full fresh two-second readiness window, while retaining freshness/sample-count/report assertions. Production tracking guards and the pinned package are unchanged. Raw failed reports are preserved. The final 184/184 run needed no logged retry; the exact cause of the earlier stream interruptions was not established.

The clean reconnect player was built from **41fa3a6f13609ad8bea02548c1c310f1175c7846** and passed **40 native Windows checks** through the delivered launcher. Reconnect attachment measured **41.667 ms**, with **40.000 m/s before and after firing**. A second launcher run confirmed `--reconnect-off` reaches the player composition. The six extra scripted checks enable assistance explicitly, so that second run's fixture result is not evidence that OFF behaves like ON. Six fresh shipping captures show both hand gestures/grips and release in the actual city; these are stationary fixtures. The old `809ddc9` player is preserved separately. The standalone NoTracker ZIP contains 893 files, each checked against its integrity receipt; its extracted executable independently passed all 40 native checks. ZIP size is 187,136,665 bytes; SHA-256 is `b37994595f219affddcd81d85f96ce75cbee2db63c5a0d1f70bc1235ad366acb`.

Follow-up commits: `2fcd4d2` keeps low-swing coverage warnings independent of navigation convergence (eight Python tests); `6b8ac4d` adds the bounded fresh-A fixture retry; `41fa3a6` adds reconnect assistance and its regression/native checks. The full 401/184 run covers the code in the clean reconnect player. Later reporting-only commits do not change its embedded identity.

## Fixed frame-time comparison

With all training/evaluation processes stopped, the old `44a6410` player and new `41fa3a6` player ran sequentially on the same Midtown route: 15 seconds warm-up, 60 measured seconds, 50 m/s, 1920 × 1200 windowed, VSync 1, TAA, collision proxies enabled, no webcam. Hardware was an RTX 4070 Laptop GPU / Core Ultra 9 185H on AC, driver 591.74; the driver reported an 80 W enforced GPU limit. That reading does not establish a fixed laptop power profile.

| Build | Frames | Frame p95 / p99 ms | Max frame ms | Frames >50 ms | Streaming p95 ms | Collision p95 ms | Max process working set MiB |
|---|---:|---:|---:|---:|---:|---:|---:|
| `44a6410` | 3600 | 17.641 / 33.308 | 119.059 | 3 | 1.2145 | 0.0906 | 1779.906 |
| `41fa3a6` | 3613 | 17.454 / 32.233 | 48.675 | 0 | 1.2060 | 0.0748 | 1794.848 |

Both reports recorded zero streaming failures and full sampled route coverage, but zero complete measured circuits. One run per build is insufficient to establish a small performance improvement. Both p95 values exceed the 16.667 ms frame budget, and the full performance target remains false with tracking disabled. The benchmark bypasses the web-shooter installation; it measures streaming/collision/render cost, **not active web VFX, reconnect cost, moving head tracking or comfort**. Dedicated graphics-memory counters were unavailable. Raw environment metadata, per-frame CSVs, screenshots and native identity are delivered alongside the report.

An independent five-minute human feel review remains pending. A ready-for-merge claim requires that human review; automated validation does not replace it. Hardware wrist input, live eye tracking and moving head-tracked comfort have not been exercised by the native fixtures. The author has not yet tested the new reconnect player.

## Continuity review follow-up

Wall entry now shares cooldown/exhaustion guards between contact and proximity paths. Repeated contact cannot reset the two-second timer. A valid web attachment exits wall running, preserves velocity and inhibits immediate wall reentry for 0.3 seconds. Roof contact also ends wall running. Six native EditMode cases passed: contact was blocked immediately after jumping, allowed after 0.308333 s, and blocked after 2.083333 s of exhausted wall contact until leaving the wall. A newly attached web reports Swinging and full-gravity vertical velocity -0.200000 m/s after 1/120 s, versus the earlier mixed wall/web state and -0.040000 m/s. These are isolated physics measurements, not a human comfort test.

High-speed turn rate now scales with effective input magnitude and assist strength. The serialized air-turn ratio defaults to 0.35 of the swing rate. Nine native EditMode cases passed: full air input turns 22.749990 degrees over one second, 25% assist turns 5.687501, 1% turns 0.227500; a 10% movement axis reduces each by another factor of ten. Zero input/assist produces zero rotation. Speed remains 40.000 m/s in the zero-gravity/drag fixture. The pre-fix rate was 65 degrees for all nonzero cases.

Assisted misses now score predicted retained speed after removing only outward radial velocity. Serialized momentum weight defaults to 1.5; zero disables that score term. Exact reconnect direct hits remain unchanged, and the rope solver adds no replacement energy. Pure vertical falling now derives the assist's horizontal heading from the aim ray instead of rejecting every candidate with a zero projected velocity. Six native cases passed. In the same 40 m/s downward reconnect fixture, geometric-only versus momentum scoring selected real surfaces 3.723627 versus 9.301638 degrees from aim (both within 12 degrees), and retained **11.379970 versus 15.206100 m/s** after physical attachment. This is a bounded fixture improvement, not preservation of all 40 m/s or proof of better human feel. An initial 5/6 run used a second target too small for the finite sampled rays to hit; its failed result is preserved. The target was enlarged to intersect those rays, without changing scoring parameters or the improvement assertion. The original inelastic constraint still applies to deliberate direct hits.

At the user's request, held right mouse now reels in at 24 m/s; it no longer emits a release action. E remains a keyboard alias, Q pays out, and releasing left mouse still detaches. The human input's existing focus requirement is supplied by the composition's eligibility predicate. Four native Input System cases passed, including a real shot/route followed by a quarter-second right hold shortening the rope **6.000023 m** while remaining attached; releasing right stops reeling. Earlier probe failures used unsupported button delta events, then an incomplete synthetic attachment with no shot collider/route. The final test uses complete device states and the actual fire path. Those failed reports are retained. The shipping validation fixture now includes state, steering, anchor and actual mouse-action checks; its result is recorded after building.

The follow-up's clean Windows build is **71077d18653ee48ae557902a3f2c6e6b9ac1b3f2**. Final DataOnly: **170 passed, two skipped, zero failed**. Final native Unity suites: **426 EditMode / 184 PlayMode**, all passed. The delivered PowerShell launcher and the independently extracted ZIP each passed **61 shipping checks**. Windows measurements: full/quarter-assist air turn **22.749989 / 5.687501 degrees** over one second at retained 40 m/s; geometric/momentum anchor selection **11.380040 / 15.206191 m/s**; quarter-second right hold **6.000023 m**; physical reconnect **41.667 ms**. Collision, rooftop stopping/jump, continuous swing, left-up release and right-up stop checks remain green. These are scripted native checks, not a manual human playtest.

The ZIP has **893 hashed files**, is **187,139,088 bytes**, and SHA-256 `6be478a4030032fe99497101a6db7446e8c51ebd982f0e220cfecc7fc10122f3`. All file hashes, 77 packaged notices, embedded clean identity, unchanged main scene and restored full source catalog/profile were checked. Seven new GPU captures include the normal traversal control HUD and six stationary hand stages; the right-mouse reel hint is visible and released hands disappear. The older interactive player was left running and untouched; no new idle-machine or active-web FPS claim is made. Earlier PPO policies/results predate this follow-up and were not retrained as evidence for it. Independent feel review, moving head-tracked comfort and live hardware verification remain pending.

### Wall clearance follow-up: real support (2026-10-17)

Resident footprint clearance now matches the swept capsule (0.30m + 0.02m skin). Roof recovery requires the eye projection to lie inside the footprint; outside proximity cannot manufacture support. Focused Unity EditMode: four cases passed. At gaps 0.8/1.2/1.8m, falling from Y104 at -20m/s with gravity24 for 0.5s ends at Y90.950, vertical speed -32.000m/s, zero supported steps, with and without resident geometry. Previously resident 0.8/1.2m gaps produced Y101.870 and 49 supported steps. Reversing the 1.2m-gap catalog now leaves X0 and zero contacts (previously +/-0.201m, 32 contacts).

### Shared overlap start

Resident and PhysX now recover the same starting pose before applying only the intended travel delta. Five focused EditMode cases passed. At X-4.9 inside a box with face X-5, resident-only and combined recovery both finish at X-5.321; extra displacement is 0.000m (previously 0.420m). Wall detection remains available and a 2m retreat is unobstructed.

### Wall entry and retention

Wall sensing checks all horizontal directions, chooses the closest hit for entry, and retains a still-reachable current wall independently of view yaw. An unattached runner needs at least 3m/s horizontal tangential speed for parallel entry; attached glancing webs and vertical-only falls still require frontal entry. Twelve focused wall/transition cases passed, including existing 0.3s cooldown and 2s exhaustion guards. At 20m/s parallel approach, entry and the state after 1s are WallRunning; looking directly away still returns the physical wall normal and retains WallRunning (previously Airborne). No camera roll, forced view, or tracking package change.

### Low-speed escape after impact

S retains braking above 4m/s; below that speed an unattached airborne player can approach a bounded 3m/s backward target with a 0.2s response, using the existing steering assistance strength. SwingSettings.lowSpeedReverse permits comparison; reverseSpeed/reverseTransitionSpeed expose tuning. Seven focused cases passed. After a 70m/s frontal collision, holding S for 1s retreats 2.424737m, ends at horizontal velocity -2.976068m/s, remains Airborne, and never penetrates the face (previously 0m retreat). RMB/E reel, LMB release, Q payout, and R flight are unchanged.

### Shared shipping wall scenarios

WallTraversalChecks runs the same real movement adapter / PhysX cases in EditMode and opt-in Windows validation. Twelve gap cases (0.65/0.8/1.2/1.8m times 30/60/120fps) all end at relative Y90.9502, vertical speed -32.00005m/s, zero supported frames. A 70m/s normal plus 20m/s tangential impact keeps 0.321m eye-to-wall clearance and 20.000m/s tangent speed at all three frame rates. Frontal impact followed by S retreats 2.428482m in 1s without drag, ending at 2.981840m/s backward. Twenty-four yaw directions retain the physical wall; edge exit, nearest wall selection, overlap recovery, reverse-off and zero-assist checks are included. The 40m/s S fixture slows to 35.49995m/s in 0.25s without reversal. Final source checks: DataOnly 170 passed / two skipped / zero failed; Unity 434 EditMode and 184 GPU PlayMode passed. Synthetic geometry and scripted input are not a human comfort review. Windows results are recorded in the release follow-up after building.
