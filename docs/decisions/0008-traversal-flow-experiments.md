# 0008: Reproducible traversal experiments

- Status: Proposed
- Affects: core, swing, character, audio, world
- Supersedes: none

## Context

Compare velocity continuity and obstruction events with the same input and policy. The actual project uses `SwingPhysics` and `WebSwingMotionInput`, not `SpiderController`, and pins Unity 6000.3.22f1. Its baseline already preserves jump/release inertia and supports bounded rope wrapping. Baselines must retain those existing behaviors.

## Implementation for review

- Keep the existing motion and translation interfaces. Share a traversal action interface between human and agent adapters; keep the documented keys.
- The requested human-input revision maps held right mouse to reeling (E remains an alias); releasing left mouse detaches. Keep agent release actions separate and update control documentation/HUD together.
- Expose independent serialized feature flags. Store event positions in city metres and sample after collision resolution at each physical substep.
- Add a seeded, separate training scene. Use the actual movement adapter and collision queries, not a surrogate physics implementation.
- Snag and stuck are diagnostics only; neither changes the training reward. Compare one policy across both conditions and separately retrained policies across conditions.
- Keep the Unity version and tracking package pinned. Confirm the ML-Agents package and Python trainer pairing through installation and a real handshake.
- Procedural web textures, impact patterns and noise audio are original code-generated assets under the repository MIT licence.
- Speed FOV and shake are separate optional gameplay effects and default off for the physical head-tracked view. They do not alter saved screen calibration. Third-person cameras exist in the test scene, not as a replacement for the tracked viewer.
- A rope shorter than attachment distance removes initial drop clearance; it does not create slack. Expose the requested ratio as an experiment and measure it alongside a slack ratio greater than one. Keep automatic reeling and release boost optional.

- Wall clearance follows the physical capsule plus skin, while roof support requires an actual footprint. Resident and PhysX recovery share one corrected start, retaining the intended travel delta.
- Retain a reachable current wall independently of view yaw; choose the closest surrounding wall for entry. Parallel unwebbed entry requires horizontal tangential speed; glancing attached swings and vertical-only falls keep frontal entry guards.
- Preserve S braking at speed, allowing a bounded backward target near rest in unattached air. Expose the low-speed option and thresholds; keep existing buttons and tracking projection unchanged.

### Airborne chain follow-up

- Add a separate serialized AirChainAssistance group, enabled by human traversal defaults and disabled by `--chain-off`. Bare/lab settings keep it disabled. Preserve RMB/E reeling and LMB-up release.
- Respect an unloaded rope when choosing the steering/cruise plane. Slack W input must not create vertical or lateral velocity with gravity disabled. Finish the horizontal alignment instead of falling through to the old rope-plane correction at a small angle.
- While attached, deliberate movement input may bank horizontal velocity at up to 120 degrees/s for 1.1s after attachment; preserve horizontal magnitude and vertical speed before the normal rope constraint. A 1.2s released grace uses 60% of that rate (72 degrees/s). Zero input/assist does not bank.
- Reject very close high-speed anchors (distance below speed times0.3s) and use the existing bounded visible-surface cone if available. Leave satisfactory direct hits exact.
- Predict the requested curved horizontal path using swept capsules. Average the segment velocities rather than assuming the turn already happened; use a physical-radius sweep as well because expanded sweeps miss starting overlaps. Temporarily guide a corner cut along a visible wall tangent and decelerate only excess horizontal speed toward 38m/s at a bounded 60m/s². Brake only the inward wall component at a bounded180m/s² and prevent fallback steering from adding it back. Collision response still runs; no invulnerability or pose rescue.
- Permit first-airborne shots to use the existing bounded fresh-ray miss buffer/cone and 0.04s physical attachment. A 12m acquisition volume is only broad phase: require a real unobstructed ray inside the existing angular limit. Direct hits remain exact. Consider usable overhead/side support by retained speed and estimated pendulum bottom, not only forward anchors.
- At speed20m/s or greater within18m of an effective anchor, limit powered winching while retaining manual slack take-up. Cancel its exit kick as well; do not reset velocity.
- Reeling may take up unloaded slack during the first 0.35s. It adds no lift/launch velocity; released mouse and zero assistance do not trigger it. No powered lift or automatic floor rescue.
- Defer proximity-only wall entry during a web flight/attachment and up to 1.2s after release. Actual swept contacts retain their existing response. Expose the grace duration and clear the corner preview on reactivation.
- Rotate only the new development build's cross corridors by 28.46516 degrees to follow the actual Manhattan streets. Keep the 450m circle, 1800m arms and 150m half-width, full inputs and older players. Default yaw0 retains legacy selection; rotated AABB/OBB intersection retains touching bounds. Match the catalog replay to the same development region.
- Compare complete 2..10-link and immediate/2+2..10+10 right-turn cases through the actual input/movement adapter and PhysX. Keep controlled crossings and relocated Manhattan footprint diagnostics separate. No-contact results do not establish human ease or full streamed-tile coverage.

## Validation

Record measured outcomes, failed checks and training limitations in the PR and the traversal validation document. Do not describe a scripted route as trained-agent evaluation or a training smoke run as convergence. Other-person comfort playtesting remains a human review step.

## Outcome

Pending review of this implementation and its measurements. Existing accepted project constraints remain in force.
