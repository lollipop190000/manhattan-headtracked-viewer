# Manhattan Head Tracked Viewer

A Windows Unity viewer for exploring a streamed Manhattan city with keyboard, mouse and optional webcam head tracking. A measured physical screen and off-axis projection make the monitor behave like a window into the city. Head tracking is single-viewer and monoscopic.

[Download for Windows](https://github.com/sshs-ai-club/manhattan-headtracked-viewer/releases/latest) · [Windows guide](docs/windows-release.md) · [Report a bug](https://github.com/sshs-ai-club/manhattan-headtracked-viewer/issues) · [MIT code license](LICENSE)

![Manhattan rendered in the Windows viewer](docs/images/manhattan.png)

The city stays fixed while the observer moves. Buildings, roads and trees stream around the viewer; distant skyline coverage remains available. Optional afternoon sunlight moves only when explicitly started in Settings.

## Run on another Windows PC

1. Download **ManhattanViewer-v0.1.1-Windows-x64.zip** from [Releases](https://github.com/sshs-ai-club/manhattan-headtracked-viewer/releases/tag/v0.1.1). This single download supports both modes.
2. Extract the **whole ZIP** into a writable folder such as `C:\Games\ManhattanViewer`. Keep every folder together; do not launch from inside the ZIP.
3. Double-click **Run-HeadTrackedDemo.cmd** and choose:

| Menu | Mode | Included |
|---|---|---|
| **1 — Explore without a webcam** | Keyboard, mouse and city exploration | Player, city and runtime; the webcam is not opened |
| **2 — Use webcam head tracking** | The same city with head-driven perspective | Everything above, plus private Python, tracker packages and the face model |

**No Unity Editor, Python installation, Git, pip, source download or account is required for either mode.** The webcam mode uses the Python bundled in this ZIP; it does not replace or depend on an installed Python. Frames are processed locally and are not uploaded or saved.

Use Windows 10/11 x64, a DirectX 11 GPU and a webcam for mode 2. If a new PC reports a missing Microsoft DLL, choose **4 — Install Microsoft runtime** in the launcher. This downloads the official Microsoft prerequisite and may ask for Windows administrator approval. No prerequisites are installed during normal game startup. Once that Windows prerequisite is present, both modes run offline.

First launch asks for visible screen width, height and eye-to-screen distance in **centimetres**. Measure the display area, then select **Enter Manhattan**. For webcam mode, open Settings → **WEBCAM**, select your camera by name, face forward and use **Capture current face reference**. Allow camera access for desktop apps in Windows if needed. See the [English Windows guide](docs/windows-release.md) or [한국어 빠른 안내](docs/windows-release-ko.md).

| Input | Action |
|---|---|
| W / A / S / D | Move on the horizontal ground plane |
| Q / E | Descend / rise |
| Mouse | Look around |
| Esc / Settings | Release the pointer and open settings |
| Alt+F4 | Quit the viewer; its launcher stops the tracker |

A Bluetooth or USB gamepad/joystick can also move the camera. See Settings → **CONTROLLER** for details.

Click the city to capture the mouse. Flight defaults to 10m/s; Settings supports 1–100m/s. Saved bookmarks load only when requested. A paired HID controller can also move: left stick/D-pad plus shoulder buttons on a gamepad, or stick/hat plus buttons 1/2 on a generic joystick. Add `--no-controller` to the player arguments to disable controller input.

For direct mode selection, open PowerShell **in the extracted release folder**:

```powershell
.\Run-HeadTrackedDemo.cmd -NoTracker
.\Run-HeadTrackedDemo.cmd -Webcam
```

The same entry point supports camera selection, checks and stop:

```powershell
.\Run-HeadTrackedDemo.cmd -Check -ListCameras
.\Run-HeadTrackedDemo.cmd -Camera 1
.\Run-HeadTrackedDemo.cmd -Camera 1 -CameraBackend msmf
.\Run-HeadTrackedDemo.cmd -Stop
```

`-Webcam` restores the saved camera when possible, otherwise uses camera 0. `-Camera` overrides that selection for the launch. Camera changes in Settings are saved separately from physical calibration and bookmarks.

## Comparison controls

| Switch | ON | OFF |
|---|---|---|
| Tracking | Apply the measured eye position | Hold the current head offset; observations and WASD continue |
| Window | Show the physical window frame | Hide only the frame |
| Surface | Show original material detail | Use reversible plain material variants on the same geometry |

These switches preserve city geometry, lighting, camera travel and render quality. Settings contains measurement, camera calibration and diagnostics. **LIGHTING** offers sun play/pause, an afternoon position slider and reset; each launch starts paused. Pause the sun for repeatable comparisons.

## Game team

This repository is becoming a web-swinging game on top of the viewer. Start with the [game design](docs/game/GDD.md), [roadmap](docs/team/ROADMAP.md), [roles](docs/team/ROLES.md), [workflow](docs/team/WORKFLOW.md) and [decision log](docs/decisions/README.md).

## Repository

| Path | Responsibility |
|---|---|
| [ManhattanViewer](ManhattanViewer) | Unity viewer, navigation, city streaming, rendering and settings |
| [tools](tools) | Setup, source verification, bake/build, tests and release packaging |
| [assets](assets) | Source manifest and small validation fixtures; full city downloads stay outside Git |
| [docs](docs) | Architecture, usage, provenance and scoped validation reports |

Reusable projection, observations, calibration and tracking live in the separate [Head Tracked Display](https://github.com/lollipop190000/head-tracked-display) Unity package. This viewer pins the package, launcher and tracker to the same tested commit in [technology-version.json](tools/technology-version.json).

## Build from source

Building is optional; use the Windows ZIP to try the application. Development requires Git + Git LFS, GitHub CLI, Python 3.13.5, Unity CLI, and **Unity 6000.3.22f1** with Windows build support and an active Unity license. The renderer is URP 17.3.0. Full source extraction, generated assets and Unity caches need substantially more disk space than the release ZIP.

Run these commands from a new checkout. Setup downloads the public city source release and verifies its manifest. The current download helper uses GitHub CLI authentication.

```powershell
gh auth login
git lfs install
git clone --branch v0.1.1 https://github.com/sshs-ai-club/manhattan-headtracked-viewer.git
cd manhattan-headtracked-viewer
git lfs pull
powershell -NoProfile -ExecutionPolicy Bypass -File tools/setup.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/bake.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run.ps1 -NoTracker
```

Wait for each step to finish successfully. `build.ps1` builds city bundles, generates their matching Skyline ownership data, builds the player and copies attribution notices. Output: `Builds/ManhattanViewer/ManhattanViewer.exe`. Use `-EditorPath 'C:\Path\To\Editor\Unity.exe'` if automatic Editor discovery fails. Setup accepts `-PythonExe`; source files and generated tiles are not included in Git's source ZIP.

To reproduce the application-local webcam ZIP, follow the [portable distribution build guide](docs/portable-release-build.md). v0.1.1 is a packaging update using the unchanged, verified v0.1.0 city player; the distribution and embedded player revisions are recorded separately.

## Validation and contributing

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test.ps1 -DataOnly
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test.ps1 -EditorPlayMode
```

The second command needs Unity and a graphics-capable desktop for rendered tests. Exact release checks and remaining limits are in the [v0.1.1 validation report](docs/release-v0.1.1.md). Historical reports remain tied to their recorded revisions; targets are not measured results.

Experimental movement toggles, snag diagnostics and reproducible PPO/fixed-input comparisons are described in the [traversal lab guide](training/README.md), with measured and pending checks in the [flow validation report](docs/traversal-flow-validation.md).

Open an issue with the release version, GPU, resolution, camera/no-camera mode, screenshot and reproduction steps. For code changes, branch from `main`, preserve Unity `.meta` files, run the relevant tests and open a pull request. See [CONTRIBUTING](CONTRIBUTING.md) and the [architecture guide](docs/architecture.md). Experimental model-import tools are explicit developer workflows and do not change normal startup content.

## License and credits

Team-authored code is [MIT licensed](LICENSE). The city uses NYC open datasets with procedural facade, roof and vegetation detail; it is not survey-accurate facade imagery. City data, Unity packages and adapted URP shaders retain their own terms. Keep the bundled `Notices/` directory when redistributing the Windows player. See [THIRD_PARTY](THIRD_PARTY.md) for sources, credits and licenses.
