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
- Predict the requested curved horizontal path using swept capsules. Average the segment velocities rather than assuming the turn already happened; use a physical-radius sweep as well because expanded sweeps miss starting overlaps. Temporarily guide a corner cut along a visible wall tangent and decelerate only excess horizontal speed toward 38m/s at a bounded 60m/s짼. Brake only the inward wall component at a bounded180m/s짼 and prevent fallback steering from adding it back. Collision response still runs; no invulnerability or pose rescue.
- Permit first-airborne shots to use the existing bounded fresh-ray miss buffer/cone and 0.04s physical attachment. A 12m acquisition volume is only broad phase: require a real unobstructed ray inside the existing angular limit. Direct hits remain exact. Consider usable overhead/side support by retained speed and estimated pendulum bottom, not only forward anchors.
- At speed20m/s or greater within18m of an effective anchor, limit powered winching while retaining manual slack take-up. Cancel its exit kick as well; do not reset velocity.
- Reeling may take up unloaded slack during the first 0.35s. It adds no lift/launch velocity; released mouse and zero assistance do not trigger it. No powered lift or automatic floor rescue.
- Defer proximity-only wall entry during a web flight/attachment and up to 1.2s after release. Actual swept contacts retain their existing response. Expose the grace duration and clear the corner preview on reactivation.
- Rotate only the new development build's cross corridors by 28.46516 degrees to follow the actual Manhattan streets. Keep the 450m circle, 1800m arms and 150m half-width, full inputs and older players. Default yaw0 retains legacy selection; rotated AABB/OBB intersection retains touching bounds. Match the catalog replay to the same development region.
- Compare complete 2..10-link and immediate/2+2..10+10 right-turn cases through the actual input/movement adapter and PhysX. Keep controlled crossings and relocated Manhattan footprint diagnostics separate. No-contact results do not establish human ease or full streamed-tile coverage.

## Validation

### Bilateral launch and surface-corner follow-up

- At the user's explicit request Q hold/release becomes a two-hand charged launch; move payout to Z. Keep LMB hold/release, RMB/E, Space and R. Free-flight Q/E elevation remains unchanged. Human and scripted input share launch edges/hold through TraversalActions.
- Require real, unobstructed left and right surface hits. Charge stores an impulse, without constraining position, reeling the body or suspending gravity. Default full charge1.35s, launch24..82m/s, elevation40degrees. Derive direction from the two anchors, with bounded sight refinement; preview a swept ballistic capsule and reject blocked launch directions. Preserve faster incoming inertia.
- Expose optional render-scoped charge tremor, increasing then settling near full charge. One camera effect owner restores the pose after rendering. Keep tracked projection, saved calibration and camera roll unchanged. Both procedural hands clench and emit separate textured lines and surface marks.
- Use real BoxCollider edges or welded sharp triangle-mesh edges for rope bends, rather than world AABB corners. Bound candidates and path nodes, cache mesh topology, and keep visibility-based unwrapping. Unsupported/unreadable surfaces fail safely instead of inventing floating bends. Scoped backface ray queries with1mm intermediate-node tolerance reject inside-to-outside single-sided mesh paths; restore the query flag after each synchronous call and verify with an independent rotated-volume segment test.
- Pay out short wrapped radii and detach before an assisted rope curve would pull the capsule into a wall. Excessive geometric rope projection falls back to the same ballistic substep, preserving prior momentum instead of snapping the view. Record anomaly context in a bounded frame ring; disk writes occur only on an anomaly.
- URP baseBlendFactor is history weight. Temporarily reduce the previous0.95 weight toward0.55 during body/head translation, head rotation, charge and recent wall contact; retain the saved AA mode and restore stationary/disabled behavior. This addresses a known ghosting contributor, not proof of the user's exact reported half-second rollback.
- Extend no-contact replay to ground launch, both turn signs and alternating turns. For catalog left turns and unsafe right-turn anchor fallbacks, an opt-in fixture pilot predicts0.5s hold/0.2s coast on copied shipping controllers before choosing a visible surface and reel input. It never applies predicted poses to the live body; validate copy isolation and identical-action prediction. The surface-v2 pilot selects visible safe anchors and plans turn lead across the preceding links; it changes the old input policy. Retain legacy-v1 as an explicit replay option and report its failed intermediates. Do not compare v2 success counts to archived v1 counts as a physics-only causal result.

Record measured outcomes, failed checks and training limitations in the PR and the traversal validation document. Do not describe a scripted route as trained-agent evaluation or a training smoke run as convergence. Other-person comfort playtesting remains a human review step.

## Outcome

Pending review of this implementation and its measurements. Existing accepted project constraints remain in force.
