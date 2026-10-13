# Runtime and game extension points

The Unity game owns navigation, streamed content, lighting, UI and gameplay metadata. The Git-pinned `com.headtracked.display` package owns observations, filtering, physical display calibration, projection and the reusable screen frame. No city asset or URP dependency belongs in that package.

## Physical camera and navigation

`IMotionInputSource.Sample(float deltaTime)` supplies a `MotionCommand`: local movement direction, look degrees, and an optional absolute city pose. `FreeFlightController.InputSource` is replaceable by keyboard/mouse, a deterministic benchmark source or future hardware. Set `FreeFlightController.Speed` within 1–50m/s. `SetPose` applies explicit teleport/bookmark actions. Feed calibrated hardware motion to this interface; do not write the final render camera transform directly.

The navigation rig updates before the final tracked camera. Its origin is the neutral eye position; `ScreenPlane` sits one measured viewing distance ahead. The head-tracking package composes the measured eye offset with the rig pose and applies the off-axis projection. World and rig stay at unit scale, one Unity unit per metre. Mouse yaw/pitch controls travel direction independently from measured head movement. Tracking OFF freezes the head offset but keeps navigation and filtering running.

An origin shift moves render roots together and preserves city-coordinate navigation. Teleports, origin shifts and calibration changes invalidate TAA history. Gameplay systems should store city positions, not raw floating-origin scene positions.

## Buildings and collision

`CityCatalog` retains `BuildingRecord` data including ID, footprint in XZ, ground height, roof height and bounds. Use the stable building ID for selection, interaction and persistent state. Render materials, mesh partitions and streaming IDs are presentation details and may change when rebaked.

`ITranslationConstraint.Constrain(currentCityPosition, requestedCityPosition)` can limit movement without changing input or projection. Assign it through `FreeFlightController.TranslationConstraint` when adding ground walking, collision or boundaries. Default free flight has no blocking constraint.

Collision proxies are simple nearby building volumes, independent of raised-window or tree meshes. `--collision-proxies` enables their streaming and query workload during benchmarking. Prepare proxy geometry during the bake; spread activation across frames. Raycasts and sweeps must preserve the input/output coordinate contract across origin shifts. Compare the same deterministic route with the feature disabled and enabled; target added CPU P95 is <=1ms.

## Presentation and persistence

Head Tracking, window-frame visibility and Surface detail are three independent comparison controls. Surface OFF uses reversible material variants without touching imported assets and applies to subsequently streamed tiles. A comparison control must never reposition content, reset travel, change lighting/quality or start an animation.

`physical-calibration.json`, `viewer-preferences.json` and `position-bookmark.json` have separate purposes. Save physical measurements independently from presentation preferences and experimental positions. Restoring a position requires an explicit action; startup always uses the agreed initial street view.

## Dependency updates

`ViewerCameraSelection` owns only the Settings interaction with the companion Python tracker's camera-control files. The reusable Python `camera_control.py` owns named device enumeration, backend-specific identity, session validation and selection persistence. Status and requests use `tracker_camera_status.json` / `tracker_camera_request.json`; the tracker alone saves `tracker_camera_preference.json` after a selected device supplies a frame. Selection never writes physical calibration or changes city/navigation/render settings. Stale tracker sessions cannot receive new requests. These files stay in the private calibration directory and out of Git.

`tools/technology-version.json` and Unity's `com.headtracked.display` Git URL must name the same full tested commit. The same commit supplies the launcher and Python tracker in ignored `.tools/head-tracked-display`. After validating a technology change, update the revision, run setup to resolve packages, and commit both `manifest.json` and `packages-lock.json` plus the version file. Never substitute an absolute `file:` dependency in the shared project.
