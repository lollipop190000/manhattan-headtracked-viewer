# Roadmap

Plan for turning the Manhattan head-tracked viewer into a fun web-swinging game. Read [GDD](../game/GDD.md) for what the game is and [ROLES](ROLES.md) for who owns each stream. Checked boxes mean verified in the repository at the time of writing; everything else is a proposal.

## Where we are

Verified from code and git history:

- [x] Streamed Manhattan with off-axis head-tracked projection, webcam tracker, Windows release ZIP (Core).
- [x] Free flight with keyboard, mouse, HID gamepad/BLE controller (Core, Hardware).
- [x] Mouse-fired web swing on a pendulum model with 250 m range (Swing, PR #12). Not played in depth after the final merge; the doc says so.
- [x] Wrist device input components: parser, calibration, aim ray, UDP receiver, fake sender (Hardware, PR #13). **Not connected to anything**, and no real device test.
- [ ] Anything that makes it a game: goals, score, feedback, sound, a hero, a reason to keep playing.

The next milestone is therefore not more rendering. It is the first five minutes of fun.

## Milestones

| | Name | Done when | Streams involved |
|---|---|---|---|
| M0 | **One team** | Roles claimed, `main` protected, everyone has run the release ZIP, decisions 0001–0007 resolved | all |
| M1 | **First fun** | One 3-minute route with 10 rings, a timer, best time, web and swing sounds, one music loop, a pair of hero hands. Mouse only. Five people played it and said which part was fun | design, swing, audio, character, world |
| M2 | **Comfortable** | Comfort test passes (below). Frame rate target met on the reference machine | core, swing |
| M3 | **Hardware in** | Wrist device fires webs in-game, BLE controller drives movement, pairing status shown on screen, mouse fallback. Two real devices tested | hardware, swing |
| M4 | **Game** | Missions or modes beyond the time trial, per the accepted story decision. Menu, settings, quit, a Windows release | design, world, all |
| M5 | **Show day** | Demo-ready build, 10-minute kiosk loop, setup checklist, backup laptop | all |

Dates are not set. Sprint length and review cadence are for the group to decide at M0.

## First week, in order

1. Merge this docs PR. Repo admin turns on branch protection ([WORKFLOW](WORKFLOW.md#repository-settings-to-turn-on-needs-a-repo-admin)).
2. Everyone claims a role in [ROLES](ROLES.md) and answers the open decisions in [docs/decisions](../decisions/README.md) (vote in the PR comments).
3. Everyone runs the Windows release ZIP and writes down on an issue: GPU, resolution, whether it felt dizzy, one thing that was fun.
4. Hardware: wire `HandAimController.Fired` to `WebSwingMotionInput.TryFire` (first integration PR). Test with `tools/fake_wrist.py` before any real device.
5. Design: turn the M1 route into a spec (ring positions in city coordinates, timer rules).
6. Audio and Character: placeholders first (one web-shoot sound, one swing loop, plain hands) so design can feel the loop.

## Streams

### Comfort

Goal: someone with no VR experience plays ten minutes without feeling dizzy. This outranks every feature.

Likely causes, so we can test one at a time:

| Cause | What to try | Owner |
|---|---|---|
| Head-tracking jitter and latency | Measure end-to-end latency (move head, see change). Check the existing filter in the `head-tracked-display` package (verify what it does before changing it). Candidate: 1-Euro filter and short prediction. Keep a recorded-head-motion test so changes are comparable | Core |
| The world moves, your body does not (vection) | Keep the physical window frame visible as a rest reference. Never roll the camera. Cap angular acceleration when swinging. Optional comfort vignette at speed, off by default and exposed as a setting, not a hidden effect | Swing + Core |
| Low or uneven frame rate | 60 fps floor on the reference machine, frame-time spikes tracked (not only average). See Rendering | Core |
| Sudden direction changes at attach and release | Ease the attach pull, smooth the release, tune `attachPull` and rope length ratchet | Swing |
| Fast mouse look decoupled from head | Mouse look smoothing and a sensitivity default that is lower than now | Swing |
| Tracking loss (face leaves camera) | Hold the last pose, fade back in; never snap | Core |

Comfort test protocol (owner: Game design): the same route, ten minutes, fresh player, they rate nausea, eye strain and disorientation 0 to 3 at the start, at five minutes and at the end. Pass is no rating above 1 for at least four of five players. Record GPU, frame rate and settings with it.

### Rendering and performance

Source of truth: [validation](../validation.md), reference machine RTX 4070 Laptop 8 GB, 1920x1200. Swing speed (up to 70 m/s per `SwingSettings.maxSpeed`) is far above the old 10 m/s walking speed the streamer was tuned for.

- [ ] Benchmark route at swing speeds (30, 50, 70 m/s). Does streaming keep the city loaded, and does the skyline fallback hide pop-in? (Core)
- [ ] Collision proxies now stay on for swinging. Measure their cost; target from [architecture](../architecture.md) is at most 1 ms added P95 CPU. (Core + Swing)
- [ ] Frame-time budget table (CPU, GPU, streaming) per feature, so new features have a limit to fit in. (Core)
- [ ] Temporal anti-aliasing ghosting at speed; shadow cost; texture memory. Cheap quality presets for weaker demo machines. (Core)
- [ ] Hero and VFX budget: hero model under 20k triangles, one material, no per-frame allocations. (Character)
- [ ] Do not change comparison switches (Tracking, Window, Surface) behaviour; those are accepted contracts in [AGENTS.md](../../AGENTS.md).

### Swing, movement and animation

Goal: swinging feels good within the first ten seconds and gets better with skill.

- [ ] Playtest the existing swing end to end; list the five worst things. (Swing, with a second player)
- [ ] Always something to grab: anchor assist (snap the aim ray to the best attach point inside a small cone) and a visible reticle that turns solid when a hit is possible. (Swing)
- [ ] Speed techniques that reward skill: release boost at the bottom of the arc, web zip to a point, air dive. Add one at a time, each with a decision or a feel note. (Swing, Design)
- [ ] Ground start without attach-pull jerk; landing and rooftop running. (Swing)
- [ ] Hero in view: first person only for v1 (the camera is the eyes, so head tracking stays correct). Arms and hands with web shooters, a firing pose, a swing pose, arms that reach toward the anchor. (Character)
- [ ] Web visuals: a curved rope with a short travel time, a hit splat on the anchor, a release flick. Replace the temporary cylinder. (Character, Swing)
- [ ] Speed feel: wind sound, camera shake kept off (comfort), particle streaks at the edges. (Character, Audio)
- [ ] Hero model: either original, or an existing one with a licence that allows use. See [decision 0002](../decisions/0002-name-and-ip.md).

### Hardware and software pairing

Current state: BLE gamepad as an HID joystick (`ControllerMotionInput`), wrist device protocol over UDP (`WristUdpReceiver`), serial receiver compiled out under .NET Standard 2.1, bridge and firmware kept outside the repo. See [decision 0005](../decisions/0005-hardware-input-contract.md) and [hardware/README](../../hardware/README.md).

- [ ] Put firmware and the serial/BLE-to-UDP bridge in `hardware/` with pinned dependencies, so anyone can reproduce the device. (Hardware)
- [ ] Wire aim and fire into the swing (`HandAimController.Fired` to `WebSwingMotionInput.TryFire`). (Hardware + Swing)
- [ ] One input facade so the game asks "what is the player doing" (move, aim ray, fire, release, recenter) and does not care which device answers. Priority: wrist, then mouse; controller for movement. (Core defines, Hardware implements)
- [ ] On-screen device status: connected, calibrated, last packet age, battery if available, with one-key recalibration. (Hardware + Design)
- [ ] Latency and drop test with a real device: packets per second, worst gap, behaviour on disconnect (fall back to mouse, never freeze). (Hardware)
- [ ] Pairing guide for a new person in under ten minutes, tested by someone who did not write it. (Hardware)
- [ ] Wrist aim over a head-tracked screen: the ray goes from the *tracked eye* through the screen point, so leaning changes what you aim at. Decide whether that is wanted or confusing. (Hardware + Design playtest)

### Audio

Goal: sound makes the swing feel physical. No audio exists yet.

Events other areas must raise (Swing owns the list, Audio listens): `WebFired`, `WebAttached`, `WebMissed`, `WebReleased`, `Landed`, `SpeedChanged(speed)`, `RingCollected`, `RunStarted`, `RunFinished`.

- [ ] Placeholder SFX for web fire, attach, release, landing, ring pickup. (Audio)
- [ ] Wind loop whose volume and pitch follow speed. (Audio)
- [ ] City ambience: traffic, distant sirens, rooftop wind; positional so it follows the tracked head. (Audio)
- [ ] One music loop for M1, then adaptive layers that fade in with speed or combo. (Audio)
- [ ] Mix: master loudness target and ducking so SFX stay readable over music. (Audio)
- [ ] Every sound recorded in the licence table with source and licence. CC0 or original preferred; CC-BY needs attribution. (Audio)
- [ ] Formats: short effects as `.wav`, music and long loops as `.ogg`. Both tracked by LFS.

### World and levels

- [ ] A route is a list of ring positions in city coordinates (never the floating-origin scene position). Store as data, not scene edits, so several people can add routes without conflicts. (World)
- [ ] Find areas where the swing works well: tall dense blocks. Mark dead zones: parks, rivers, gaps where no building can be attached. (World + Swing)
- [ ] Landmarks to route through (bridges, towers, the park edge) as player-visible goals. (World)
- [ ] Collectables, mission spots, rooftop landing pads. (World + Design)
- [ ] Optional extra models: the Sketchfab Manhattan model linked by the team (CC-BY, 336k faces) could be used for landmarks only; check it with Core before importing since it is a different pipeline from the NYC data. See [decision 0007](../decisions/0007-assets-and-licences.md).

### Game design and production

- [ ] Own the [GDD](../game/GDD.md) and keep it short enough to read in five minutes.
- [ ] Keep the decision index in [docs/decisions](../decisions/README.md) current; close open ones at M0.
- [ ] Task board (GitHub Projects or Issues with area labels). One issue per task, one owner per issue.
- [ ] Playtest every milestone with people outside the team. Write what they did, not what they said they liked.
- [ ] Decide the story and mode set per [decision 0004](../decisions/0004-modes-and-story.md).

## Risks

| Risk | Why it matters | Mitigation |
|---|---|---|
| Dizziness | Single biggest threat to a head-tracked game | Comfort stream first; protocol above; comfort options |
| Core bus factor | One person knows rendering and tracking | Docs as you go; second person builds from source early |
| Merge conflicts in Unity files | Scene and `.meta` files do not merge | Scene frozen; prefabs and code; small PRs |
| Using Spider-Man branding | Trademark and licence issue if the game is shared widely | [Decision 0002](../decisions/0002-name-and-ip.md) |
| Asset licences | Public repo, public release | Licence line in every PR; LFS for binaries |
| Hardware not reproducible | Firmware and bridge live in a personal project | Move into `hardware/` |
| Too many agents, too few reviewers | Review becomes the bottleneck | Small PRs, area owners review, agents never merge |
| Heavy setup excludes people | Source build needs several GB and a Unity licence | Playtest from the ZIP; only builders need Unity |
| Scope growth | Multiplayer and story can swallow the schedule | Decisions 0003 and 0004; M1 is a single route |
| Windows only | Release and tracker are Windows x64 | Accept and say so; do not promise Mac or web |
