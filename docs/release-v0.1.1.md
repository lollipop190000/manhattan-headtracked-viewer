# v0.1.1 — portable webcam distribution

This is a distribution update to the tested v0.1.0 Windows city player. The
Unity player is reused byte-for-byte from source commit
`4ee77dde99662470ad91820f8e05035d57d1c7a8`. Rendering, city geometry and the
reusable tracking package are unchanged. The distribution revision is recorded
separately in `INTEGRITY.json`; it is not substituted for the embedded player
identity. The previous [v0.1.0 rendering report](release-v0.1.0.md) remains the
scoped evidence for the city and its 180 PlayMode / 263 EditMode checks.

## Portable tracker

The ZIP includes CPython 3.13.5's official Windows x64 embeddable distribution,
the exact 20 wheel dependencies in `tools/tracker-requirements.lock.txt`, the
tracker based on `c5ddab14d12dcc8acc8b31048f5752686984f3ab`, canonical
face geometry and the SHA-256-pinned Face Landmarker task model. The runtime is
built afresh from the verified archives and wheels, not copied from an existing
development virtual environment. Python's `_pth` contains only application-local
relative paths and does not import site initialization; it ignores installed
Python, the registry and Python environment variables.

One guarded Windows compatibility change loads the same verified face-model
bytes through MediaPipe's `model_asset_buffer` instead of native file-path
loading, which fails for Unicode extraction paths. The original source digest,
patched source digest and change are recorded in the portable tracker manifest.
Tracking calculations and all inference options remain unchanged.

The interpreter archive was checked against Python's upstream SHA-256 in its
SBOM. Every wheel was checked against the filename/digest in the official PyPI
version metadata. Model and notice downloads are hash-pinned. The ZIP retains
licenses, original wheel component notices, Python's SBOM and the model cards.
The optional Microsoft prerequisite is downloaded directly from Microsoft only
when explicitly requested; its checksum and Microsoft Authenticode signature
are checked before launching the installer.

## Checks performed

- The application-local interpreter loaded all pinned packages and the model
  with PATH restricted to Windows directories and deliberately invalid
  `PYTHONHOME` / `PYTHONPATH`. CPU inference and named camera enumeration passed.
- The first isolated-path check found a Windows trailing-dot normalization issue
  in the tracker path. Relative paths now end in separators, and disabling site
  initialization prevents user packages being appended. The corrected runtime
  passed the check and the physical webcam run.
- A native player and the bundled tracker were launched together using a
  **Camera Sensor OV02C10** webcam through DirectShow. The tracker connected to
  Unity over localhost. During 20 seconds after warmup, the player reported
  tracking on **100% of 1,198 measured frames** and zero city streaming failures.
  Closing the player released the webcam and left no owned tracker process or
  heartbeat file. Webcam frames were not saved.
- Nine focused packaging tests check exact file receipts, personal-data
  exclusion, unlisted files, model changes, nonportable paths, base ZIP/identity
  changes, archive traversal, Windows case aliases, CMD exit propagation and
  Unicode model loading with an upstream-drift guard. They passed.

The exact published ZIP has its final file count, hashes and relocation checks
in the accompanying `ManhattanViewer-v0.1.1-validation.json`. An unchanged player
does not require repeating the entire rendering suite for this packaging update.

## Limits

This is one Windows machine and one physical camera, not certification of every
GPU, webcam, driver or screen calibration. The short webcam run is a connection
and lifecycle check, not a whole-route or long-session performance certification.
Its route covered less than 1%; a reported performance target failure is not
relabelled as a full performance pass. Users still measure their display and
capture their own face reference on the target PC. Windows 10/11 x64, suitable
graphics drivers, camera permissions and any missing Microsoft runtime remain
target-PC prerequisites. Install that runtime through the optional launcher
action when needed.
