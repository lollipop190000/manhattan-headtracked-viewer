# Team development guidance

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
