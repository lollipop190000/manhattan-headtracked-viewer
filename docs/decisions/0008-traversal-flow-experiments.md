# 0008: Reproducible traversal experiments

- Status: Proposed
- Affects: core, swing, character, audio, world
- Supersedes: none

## Context

Compare velocity continuity and obstruction events with the same input and policy. The actual project uses `SwingPhysics` and `WebSwingMotionInput`, not `SpiderController`, and pins Unity 6000.3.22f1. Its baseline already preserves jump/release inertia and supports bounded rope wrapping. Baselines must retain those existing behaviors.

## Implementation for review

- Keep the existing motion and translation interfaces. Share a traversal action interface between human and agent adapters; keep the documented keys.
- Expose independent serialized feature flags. Store event positions in city metres and sample after collision resolution at each physical substep.
- Add a seeded, separate training scene. Use the actual movement adapter and collision queries, not a surrogate physics implementation.
- Snag and stuck are diagnostics only; neither changes the training reward. Compare one policy across both conditions and separately retrained policies across conditions.
- Keep the Unity version and tracking package pinned. Confirm the ML-Agents package and Python trainer pairing through installation and a real handshake.
- Procedural web textures, impact patterns and noise audio are original code-generated assets under the repository MIT licence.
- Speed FOV and shake are separate optional gameplay effects and default off for the physical head-tracked view. They do not alter saved screen calibration. Third-person cameras exist in the test scene, not as a replacement for the tracked viewer.
- A rope shorter than attachment distance removes initial drop clearance; it does not create slack. Expose the requested ratio as an experiment and measure it alongside a slack ratio greater than one. Keep automatic reeling and release boost optional.

## Validation

Record measured outcomes, failed checks and training limitations in the PR and the traversal validation document. Do not describe a scripted route as trained-agent evaluation or a training smoke run as convergence. Other-person comfort playtesting remains a human review step.

## Outcome

Pending review of this implementation and its measurements. Existing accepted project constraints remain in force.
