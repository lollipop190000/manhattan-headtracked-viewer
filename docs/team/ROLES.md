# Roles and ownership

Status: **proposed**. Nothing here is final until each person confirms their row (see [Claiming a role](#claiming-a-role)). Owners for areas 1 to 3 come from git history. Owners for areas 4 to 7 were assigned by @SWTY7 and each person should confirm.

The game is a head-tracked window into Manhattan where you swing through the city. That splits into seven areas. Each area has one **owner** (decides, reviews, answers questions) and any number of **contributors**. Owning an area does not mean doing all the work; it means being the one who says "yes, merge".

## The seven areas

| # | Area | What it covers | Owner | Already built (verified in git) |
|---|---|---|---|---|
| 1 | **Core and tracking** | Unity project health, city streaming, off-axis projection, head-tracking pipeline, tracker package, Windows release | @lollipop190000 (confirmed: 73 commits, all rendering/tracking PRs) | `ManhattanViewer/Assets/Scripts/*`, `tools/*`, `head-tracked-display` package |
| 2 | **Swing and movement** | Web physics, aiming, attach and release, speed and feel, collision with buildings | @1213anthony (author of PR #12; commits appear as `srchae99`, to be confirmed) | `Assets/WebShooter/Swing/*` |
| 3 | **Hardware and input** | Wrist device, BLE controller, serial/UDP bridge, firmware, pairing UX | @mirnim (BLE controller) and @1213anthony (wrist device, PR #13) | `ControllerMotionInput.cs`, `Assets/WebShooter/Input/*`, `tools/fake_wrist.py` |
| 4 | **Game design and producer** | Game design doc, missions, scoring, difficulty, decision log, playtests, task board | @ailfor2-look (assigned by @SWTY7, to confirm) | nothing yet |
| 5 | **Character, animation and VFX** | Hero model and hands/arms in view, web visuals, swing poses, impact and speed effects | @Polaris-unreal (assigned by @SWTY7, to confirm) | web line is a temporary cylinder in `MouseWebShooter.cs` |
| 6 | **Audio** | Music, ambience, web and swing sounds, mixing, audio licences | @edward0613 (assigned by @SWTY7, to confirm; not yet a repo collaborator) | nothing (only the default `AudioManager.asset`) |
| 7 | **World and level** | Routes, landmarks, collectables, mission spots, skyline set dressing, extra 3D models | @SWTY7 (to confirm) | NYC city data in `assets/`, streaming in `CityStreamer.cs` |

All seven areas now have a proposed owner. Anyone can still contribute to any area; the owner decides and reviews.

AI agents read this file at session start to tell their user their role ([AGENT-ONBOARDING](AGENT-ONBOARDING.md)). Keep the Owner column to GitHub handles so it can be matched.

## Cross-cutting duties

These are not areas; they rotate or are shared.

| Duty | Who | Notes |
|---|---|---|
| Release build and playtest ZIP | Core owner | Only the Core owner cuts a tagged release. Others playtest from the ZIP. |
| Review of PRs | Area owner first, Core owner for anything touching shared files | See [WORKFLOW](WORKFLOW.md). |
| Decision log | Game design owner keeps the index tidy; anyone proposes | See [decisions](../decisions/README.md). |
| AI agent hygiene | Every person for their own agents | See [AGENTS.md](../../AGENTS.md). |

## Who may touch what

"Own" means edit freely on a branch. "Ask" means open an issue or ping the owner **before** editing; the PR still needs the owner's review.

| Path | Owner area | Others |
|---|---|---|
| `ManhattanViewer/Assets/Scripts/`, `Editor/`, `Shaders/`, `Settings/` | 1 Core | Ask |
| `ManhattanViewer/Assets/Scenes/ManhattanViewer.unity` | 1 Core | **Do not edit.** Add things from code or prefabs. See [WORKFLOW](WORKFLOW.md#unity-rules). |
| `ManhattanViewer/Packages/`, `ProjectSettings/` | 1 Core | Ask (these change everyone's editor) |
| `ManhattanViewer/Assets/WebShooter/Swing/` | 2 Swing | Ask |
| `ManhattanViewer/Assets/WebShooter/Input/`, `hardware/`, `tools/fake_wrist.py` | 3 Hardware | Ask |
| `ManhattanViewer/Assets/Game/` (new, game logic: missions, score, UI) | 4 Game design | Contributors welcome |
| `ManhattanViewer/Assets/Art/` (new: models, materials, animation, VFX) | 5 Character | Contributors welcome |
| `ManhattanViewer/Assets/Audio/` (new) | 6 Audio | Contributors welcome |
| `ManhattanViewer/Assets/World/` (new: routes, collectables, set dressing) | 7 World | Contributors welcome |
| `docs/` | Whoever writes it; game docs 4, hardware docs 3 | Typos and clarifications: just do it |

Interfaces between areas (change these only with a decision record):

- Movement: `IMotionInputSource` and `ITranslationConstraint` (Core owns, Swing implements).
- Aim and fire: `HandAimController.Fired(Ray)` calls `WebSwingMotionInput.TryFire(Ray)` (Hardware calls into Swing). Not wired yet; this is the first integration task.
- Hardware wire format: [decision 0005](../decisions/0005-hardware-input-contract.md).
- Audio and VFX hooks: Swing raises events such as attach, release, land; Audio and Character listen. Event list is defined in [ROADMAP](ROADMAP.md#audio).

## Claiming a role

1. Edit the table above in a PR named `docs/roles-<yourname>`, remove "to confirm" from your row, and add one line on what you will do first.
2. The Core owner approves. Disagreements go to a decision record, not a chat thread.
3. If you go quiet for two weeks, the area reverts to open. Say so if you are busy; nobody will mind.

## Bus factor

Core is currently one person. That is the main project risk. Two actions: (a) Swing and Hardware owners should be able to build and run the player without help (see [setup](../team-quickstart.md)); (b) Core owner writes down anything only they know in `docs/` as they go.
