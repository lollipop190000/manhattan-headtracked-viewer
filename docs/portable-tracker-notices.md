# Portable webcam tracker notices

The Windows application distribution includes an isolated CPython 3.13.5
embeddable runtime, the pinned Head Tracked Display Python tracker, its exact
wheel dependencies, a canonical face model and the Face Landmarker task model.
These components are generated into release files and are not checked into Git.

- CPython: Python Software Foundation license and bundled third-party terms in
  `LICENSE-Python.txt`; `python.spdx.json` is the unmodified upstream SBOM.
  [Official distribution](https://www.python.org/downloads/release/python-3135/).
- Tracker code: MIT, pinned to the revision in `.runtime/tracker/portable-tracker.json`.
  Its license is retained as `LICENSE-HeadTrackedDisplay.txt`.
- Canonical face geometry and MediaPipe: Apache 2.0, retained in
  `LICENSE-MediaPipe.txt` and the dependency notices. The canonical OBJ is unmodified.
- [Face Landmarker](https://developers.google.com/edge/mediapipe/solutions/vision/face_landmarker):
  the unmodified float16 version-1 task bundle is pinned by SHA-256. The official
  FaceDetector, FaceMesh and Blendshape model cards each state Apache License 2.0.
  Their original PDFs are retained beside this notice. The model is for facial
  landmarks used in local head tracking; it is not a face-identification model.
- Wheel dependency licenses and bundled-component notices are copied unmodified
  into `Dependencies/` and remain in the installed `.dist-info` directories.
  Exact versions, official PyPI wheel URLs and SHA-256 digests are recorded in
  `.runtime/tracker/portable-tracker.json`.

Keep this directory and the rest of `Notices/` when redistributing the application.
Webcam frames are processed locally; the tracker sends observations to Unity over
localhost. The application does not upload or save webcam frames.

The Microsoft Visual C++ Redistributable is **not included** in this ZIP. The
launcher's optional prerequisite action downloads it directly from Microsoft,
verifies the pinned SHA-256 and Microsoft Authenticode signer, and runs the
Microsoft installer. Its terms are shown by that installer. The publisher does
not relicense that component. See [Microsoft's runtime guidance](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist).
