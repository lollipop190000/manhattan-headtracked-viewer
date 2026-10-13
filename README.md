# Manhattan Head Tracked Viewer

A Windows Unity viewer for exploring a streamed Manhattan city with keyboard, mouse and optional webcam head tracking. A measured physical screen and off-axis projection make the monitor behave like a window into the city. Head tracking is single-viewer and monoscopic.

[Download for Windows](https://github.com/sshs-ai-club/manhattan-headtracked-viewer/releases/latest) · [한국어 실행 안내](docs/windows-release.md) · [Report a bug](https://github.com/sshs-ai-club/manhattan-headtracked-viewer/issues) · [MIT code license](LICENSE)

![Manhattan rendered in the Windows viewer](docs/images/manhattan.png)

The city stays fixed while the observer moves. Buildings, roads and trees stream around the viewer; distant skyline coverage remains available. Optional afternoon sunlight moves only when explicitly started in Settings.

## Run on Windows

1. Download **ManhattanViewer-v0.1.0-Windows-x64.zip** from [Releases](https://github.com/sshs-ai-club/manhattan-headtracked-viewer/releases/tag/v0.1.0).
2. Extract the **whole ZIP** into a writable folder, for example `C:\Games\ManhattanViewer`. Keep the executable, data folders and `Notices` together.
3. Open PowerShell in that folder and run:

```powershell
.\ManhattanViewer.exe --no-tracker
```

This includes the city and Unity runtime. **No Unity Editor, Python, Git, source assets or account is needed to explore without a webcam.** After extraction, this mode works offline. Use Windows x64 with DirectX 11; the measured machine is an RTX 4070 Laptop at 1920×1200. Other GPUs need their own performance check.

First launch asks for visible screen width, height and eye-to-screen distance in **centimetres**. Measure the display area, then select **Enter Manhattan**. Click the city to capture the mouse; `Esc` releases it and opens Settings. `Alt+F4` exits.

| Input | Action |
|---|---|
| W / A / S / D | Move on the horizontal ground plane |
| Q / E | Descend / rise |
| Mouse | Look around |
| Esc / Settings | Release the pointer and open settings |
| Alt+F4 | Quit |

The default flight speed is 10m/s; Settings supports 1–100m/s. Saved bookmarks load only when requested. Keep personal calibration outside Git.

A Windows-paired HID controller can also move through the city. On a gamepad, use the left stick or D-pad to move, and the left/right shoulder buttons to descend/rise. Generic HID joysticks use the stick or hat and buttons 1/2 for descend/rise; device mappings can vary. Keyboard and mouse remain available. Add `--no-controller` to disable controller input.

## Add webcam head tracking

Install **Git** and **Python 3.13.5** first. Internet is required once to download the pinned tracker dependencies and face model. The webcam is processed locally; the tracker sends observations to Unity over localhost.

Clone the matching release, prepare the tracker, then point the launcher at your extracted player:

```powershell
git clone --branch v0.1.0 https://github.com/sshs-ai-club/manhattan-headtracked-viewer.git
cd manhattan-headtracked-viewer
powershell -NoProfile -ExecutionPolicy Bypass -File tools/setup.ps1 -SkipSource -SkipUnity
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run.ps1 -PlayerPath 'C:\Games\ManhattanViewer\ManhattanViewer.exe' -Camera 0
```

Change `-PlayerPath` to your actual absolute path. If `python` points to another version, add `-PythonExe 'C:\Path\To\Python313\python.exe'` to setup. Select another webcam with `-Camera 1` or the Settings **WEBCAM** section. After measuring the display, face forward and use **Capture current face reference**.

The launcher manages both processes and stops its tracker when the player closes. To stop that session explicitly:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run.ps1 -PlayerPath 'C:\Games\ManhattanViewer\ManhattanViewer.exe' -Stop
```

See the [Windows guide](docs/windows-release.md) for camera permissions, saved calibration, camera switching, logs and troubleshooting. Webcam operation depends on the camera and physical calibration; this release's rendering checks use a synthetic observer.

## Comparison controls

| Switch | ON | OFF |
|---|---|---|
| Tracking | Apply the measured eye position | Hold the current head offset; observations and WASD continue |
| Window | Show the physical window frame | Hide only the frame |
| Surface | Show original material detail | Use reversible plain material variants on the same geometry |

These switches preserve city geometry, lighting, camera travel and render quality. Settings contains measurement, camera calibration and diagnostics. **LIGHTING** offers sun play/pause, an afternoon position slider and reset; each launch starts paused. Pause the sun for repeatable comparisons.

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
git clone --branch v0.1.0 https://github.com/sshs-ai-club/manhattan-headtracked-viewer.git
cd manhattan-headtracked-viewer
git lfs pull
powershell -NoProfile -ExecutionPolicy Bypass -File tools/setup.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/bake.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run.ps1 -NoTracker
```

Wait for each step to finish successfully. `build.ps1` builds city bundles, generates their matching Skyline ownership data, builds the player and copies attribution notices. Output: `Builds/ManhattanViewer/ManhattanViewer.exe`. Use `-EditorPath 'C:\Path\To\Editor\Unity.exe'` if automatic Editor discovery fails. Setup accepts `-PythonExe`; source files and generated tiles are not included in Git's source ZIP.

## Validation and contributing

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test.ps1 -DataOnly
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test.ps1 -EditorPlayMode
```

The second command needs Unity and a graphics-capable desktop for rendered tests. Exact release checks and remaining limits are in the [v0.1.0 validation report](docs/release-v0.1.0.md). Historical reports remain tied to their recorded revisions; targets are not measured results.

Open an issue with the release version, GPU, resolution, camera/no-camera mode, screenshot and reproduction steps. For code changes, branch from `main`, preserve Unity `.meta` files, run the relevant tests and open a pull request. See [CONTRIBUTING](CONTRIBUTING.md) and the [architecture guide](docs/architecture.md). Experimental model-import tools are explicit developer workflows and do not change normal startup content.

## License and credits

Team-authored code is [MIT licensed](LICENSE). The city uses NYC open datasets with procedural facade, roof and vegetation detail; it is not survey-accurate facade imagery. City data, Unity packages and adapted URP shaders retain their own terms. Keep the bundled `Notices/` directory when redistributing the Windows player. See [THIRD_PARTY](THIRD_PARTY.md) for sources, credits and licenses.
