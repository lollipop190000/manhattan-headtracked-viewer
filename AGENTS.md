# Team development guidance

**AI agents: at the start of every session, before anything else, follow [docs/team/AGENT-ONBOARDING.md](docs/team/AGENT-ONBOARDING.md). Work out who you are working for, then open your first reply by telling them their role, their paths and what is open for them.**

- The Unity project is `ManhattanViewer`, pinned to Unity 6000.3.22f1. Commit package locks, project settings and all `.meta` files for authored assets.
- Reusable tracking, projection and physical-screen behavior belongs in the separate `head-tracked-display` package. Keep its tested Git revision identical in the UPM manifest/lock and `tools/technology-version.json`.
- Keep `ManhattanBootstrap` limited to composition. Input, streaming, rendering, UI, persistence and city conversion have separate components.
- Use meters at scale 1. Preserve city coordinates separately from the floating rendering origin. Imported vertices must be tile-local and converted exactly once.
- Tracking, window frame and surface switches each change only their named feature. Tracking OFF freezes the head offset while observations and flight continue. Surface changes must be reversible and must apply to newly streamed tiles.
- Save physical calibration, viewer preferences and position bookmarks separately. Load bookmarks only after an explicit action.
- Generated city assets and expanded source packs are never committed. Verify source SHA-256 before baking; preserve provenance and source terms. Small validation and team-authored binary assets use Git LFS.
- `tools/run.ps1` delegates to the pinned technology repository's one public CMD entry point. Do not introduce project-specific tracking logic or another CMD launcher.
- Extend motion through `IMotionInputSource` and constraints through `ITranslationConstraint`. Keep optional collision proxies disabled for normal free flight.
- Run `tools/test.ps1 -DataOnly` for portable checks and `tools/test.ps1` for Unity checks. Validate a Windows player and update measured reports when changing streaming/rendering behavior; targets are not measured results.
- Do not commit builds, caches, logs, tracker environments, downloaded face models, credentials or personal calibration.
- Keep `main` runnable. Develop features on branches and use pull requests for review.

## Game team rules (for every person and every AI agent)

Full text: [docs/team/WORKFLOW.md](docs/team/WORKFLOW.md). Who owns what: [docs/team/ROLES.md](docs/team/ROLES.md). Plan: [docs/team/ROADMAP.md](docs/team/ROADMAP.md). Game: [docs/game/GDD.md](docs/game/GDD.md). Decisions: [docs/decisions/](docs/decisions/README.md).

- Never push to `main`. Work on `<area>/<short-name>` (areas: core, swing, hardware, design, character, audio, world, docs) and open a PR.
- Stay in the area you were asked to work on. Check `docs/team/ROLES.md` before editing another area's paths; ask the owner first.
- Do not edit `ManhattanViewer/Assets/Scenes/ManhattanViewer.unity`, `Packages/` or `ProjectSettings/` unless you are the Core owner. Create content from code or prefabs.
- Every asset needs a licence row and binaries go through LFS. Do not use Marvel/Sony-branded material ([decision 0002](docs/decisions/0002-name-and-ip.md)).
- Do not commit personal or tool-specific files (agent config, plans, session notes, local paths, calibration). Do not invent test results; write "not tested" instead.
- Do not merge, tag, force-push or change repository settings. A human does that.
- If a choice affects other people, write a decision record under `docs/decisions/` and do not reopen an accepted one.
