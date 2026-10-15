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

The final detector follow-up removes a blind spot that excluded every non-colliding swinging sample from the two-second window. A sixth case now distinguishes normal apex travel from persistent stationary web input. Its source compiles; the revised six-case detector suite has not executed in Unity.

## Independent implementation commits

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

## Final validation boundary

The final non-ML managed source was compiled against the installed Editor's existing reference assemblies: seven assemblies, zero C# errors. This checks source/API syntax without starting Unity; it does not compile shaders, execute physics, validate the ML template assembly or confirm a working player.

Python evaluation checks: seven passed, covering time/transition weighting, missing/duplicate schedules, unavailable results, convergence evidence, absent web coverage and chronological merging of parallel worker logs. An untrained hybrid ML-Agents policy was exported through the actual installed trainer; deterministic continuous/discrete ONNX actions matched Torch at batches one and three with action masks. This is a Python export check, not learning or a Unity handshake. Independent GPU-log parser and benchmark-environment parser checks passed five and three assertions respectively.

Final DataOnly ran 171 cases: 167 passed, two failed and two skipped. Both failures were pre-existing junction-creation tests denied by the restricted Windows environment (`test_junction_destination_ancestor_rejected` and `test_junction_source_payload_rejected`). The failures were not suppressed or changed into skips. Since the suite aborts on those failures, source-manifest validation was run separately and passed (`manhattan-2026-10-10-v2`), as were the five GPU-log and three benchmark-environment parser assertions.

The environment later prevented licensed Editor startup: the Licensing Client IPC channel refused connection. The package registry hostname also failed to resolve. Consequently the following are **not tested / not measured**:

- Final Unity EditMode/PlayMode suite, ML package installation/manifest resolution and agent assembly compilation.
- Native matched replay OFF/ON snag and stuck counts, bottom speed, release rise and attachment latency.
- Shader compilation, 1p/3p screenshots, final frame-time comparison and head-tracked comfort.
- Real trainer handshake, PPO learning curves, convergence, 16 × 8 matched-policy and retrained-policy evaluations.
- New Windows Manhattan development build and another person's five-minute feel review.

No numeric table for these pending comparisons exists. Missing telemetry is rejected by the tools; it is not treated as zero events. The preserved development player is still the prior `44a6410` build and does not contain this branch's changes. No release/build identity has been overwritten. Follow [the lab guide](../training/README.md) to resume the native checks in a licensed, connected environment; only then publish the requested completed PR.
