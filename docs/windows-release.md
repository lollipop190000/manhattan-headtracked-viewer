# Windows quick start — v0.1.1

One Windows x64 ZIP includes the city viewer and optional webcam head tracking.
You do not need to install Unity, Python, pip, Git or source assets to use it.
The webcam mode runs a private Python included with this application.

## Download and launch

1. Open the [v0.1.1 Release](https://github.com/sshs-ai-club/manhattan-headtracked-viewer/releases/tag/v0.1.1).
2. Download **ManhattanViewer-v0.1.1-Windows-x64.zip**, not `Source code.zip`.
3. Extract the whole archive into a writable, reasonably short path, for example
   `C:\Games\ManhattanViewer`. Keep `ManhattanViewer_Data`, `.runtime`, `tools`
   and `Notices` beside the executable. Do not run from inside the ZIP.
4. Double-click **Run-HeadTrackedDemo.cmd**.
5. Choose **1** to explore without a webcam, or **2** for webcam head tracking.

No webcam is opened in mode 1. Mode 2 starts the viewer and its bundled tracker;
closing the game with Alt+F4 also stops the tracker. Keep the launcher window
open while using the game. Menu **5** stops this copy of the application.

Supported target: Windows 10/11 x64 and a DirectX 11 graphics device. Other
operating systems are not included in this build. GPU performance varies.

## A new PC reports a missing DLL

Choose menu **4 — Install Microsoft runtime**. The launcher downloads the pinned
Microsoft Visual C++ Redistributable directly from Microsoft, checks its SHA-256
and Microsoft digital signature, then opens the Microsoft installer. Internet
and Windows administrator approval may be needed for this prerequisite. Restart
if Windows requests it, then run the launcher again. Normal game startup does
not install software or download a model.

Once the Windows prerequisite is installed, both modes work offline. A webcam's
driver and Windows camera permission must also be available for mode 2.

## First launch and physical measurement

Enter visible screen **width**, **height** and **eye-to-screen distance**, all in
**cm**, then select **Enter Manhattan**. Measure the display area rather than its
resolution or the outside bezel. The package uses those physical dimensions for
off-axis projection; it is single-viewer and monoscopic.

In webcam mode, open **Settings → WEBCAM**, choose your camera by name, face
forward and select **Capture current face reference**. Detailed lens calibration,
camera position, IPD and diagnostics remain in the right-hand Settings drawer.
If you change cameras, recheck their position and calibration.

For dimensions measured across the full monitor while playing in a window,
enable **Dimensions are full monitor panel** in Viewport check. Keep the entire
window on the measured monitor. In the usual measured render-surface mode,
measure the application's visible display area and recheck after resizing it.

Use **Save physical calibration** explicitly. Calibration, comparison preferences
and position bookmarks are separate files. A bookmark is never auto-loaded at
startup. Default personal location:
`%USERPROFILE%\AppData\LocalLow\HeadTrackedDisplay\ManhattanViewer`.
Moving the game folder does not copy another person's calibration into it.

## Controls

| Input | Action |
|---|---|
| Click the city | Capture mouse input |
| W/S, A/D | Move forward/back and sideways on the horizontal plane |
| Q/E | Descend/rise |
| Mouse | Look around |
| Esc / Settings | Release the pointer and open settings |
| Alt+F4 | Quit the viewer and its tracker |

Speed defaults to 10m/s and is adjustable from 1–100m/s. HID gamepads use the
left stick/D-pad and left/right shoulder buttons; generic joysticks use stick/hat
and buttons 1/2, subject to the device's mapping. Keyboard and mouse continue to
work. Tracking, Window and Surface each change only their named feature. Pause
the optional sun movement in **LIGHTING** for repeatable comparisons. Each
launch starts with the sun paused at the original afternoon position.

## Commands and troubleshooting

Open PowerShell **in the extracted release folder**. Choose one launch command:

```powershell
.\Run-HeadTrackedDemo.cmd -NoTracker
.\Run-HeadTrackedDemo.cmd -Webcam
```

Useful actions through the same launcher:

```powershell
.\Run-HeadTrackedDemo.cmd -Check -ListCameras
.\Run-HeadTrackedDemo.cmd -Camera 1
.\Run-HeadTrackedDemo.cmd -Camera 1 -CameraBackend msmf
.\Run-HeadTrackedDemo.cmd -Stop
.\Run-HeadTrackedDemo.cmd -Verify
```

`-Check` loads the bundled model on the CPU without opening a webcam.
`-ListCameras` lists device names and backend-specific indices without recording
frames. `-Webcam` restores the saved camera selection when possible, otherwise
uses camera 0. Explicit `-Camera` takes precedence. The game Settings also offers
**Refresh webcam list** and camera selection by name. EShare or OBS virtual
cameras may produce black frames until their source application is connected.
Select a physical camera to check the built-in webcam first.

If a webcam fails, close other camera applications, enable **camera access for
desktop apps** in Windows Privacy settings, and select the correct device.
Try `msmf` if `dshow` fails with your camera. Tracking OFF holds the current head
offset while observations and keyboard travel continue; it is not a webcam
privacy switch. Quit the application to release its webcam, or use mode 1.

Missing files: extract the whole ZIP into a new folder. For white-line stability,
leave **Temporal anti-aliasing** enabled in **FLIGHT & IMAGE**. For a runtime
error, choose menu 3 first and review the printed message. Session logs are
`Player.log`, `tracker-output.log` and `tracker-error.log` beside the executable.
Do not post personal calibration or raw webcam frames in bug reports.

An optional private calibration directory can be supplied from PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run_demo.ps1 -Webcam -CalibrationDirectory "$env:LOCALAPPDATA\ManhattanViewer-Private"
```

Use that same directory with `-Stop` and subsequent launches. For explicit QA
player arguments, call this backing PowerShell script directly to preserve
quoted paths; the single public CMD entry point remains Run-HeadTrackedDemo.cmd.

## What is included

The ZIP contains the player, city bundles, Unity runtime, an isolated Python
3.13.5 distribution, pinned tracker dependencies, the Face Landmarker model and
license notices. System Python, Git and Unity are not consulted during play.
`INTEGRITY.json` records every shipped file and distinguishes the distribution
source revision from the unchanged v0.1.0 player revision. The `.sha256` beside
the download checks the entire ZIP. Keep `Notices/` when redistributing.

This is a packaging update to the tested v0.1.0 city player. Exact checks and
their scope are in [release validation](release-v0.1.1.md). Building from source
is a separate developer workflow in the [README](../README.md#build-from-source).
