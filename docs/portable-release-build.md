# Reproduce the portable Windows distribution

This is a maintainer workflow. End users only download the release ZIP and run
`Run-HeadTrackedDemo.cmd`.

Use Windows x64, Python 3.13.5 with pip, Git and the pinned technology checkout.
The city player is the unchanged, verified v0.1.0 artifact pinned in
`tools/portable-player.json`. Download that ZIP from its recorded public URL.
Keep it outside source control. Prepare the technology checkout with
`tools/setup.ps1 -SkipSource -SkipUnity`, or use a clean checkout at the exact
revision in `tools/technology-version.json`.

```powershell
python tools/build_portable_tracker.py --output .cache/portable-stage --technology-root .tools/head-tracked-display
Copy-Item docs/windows-release.md .cache/portable-stage/README-Windows.md
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test.ps1 -DataOnly
```

The builder verifies Python/model/notice archives against `portable-runtime.json`,
downloads the pinned Windows wheels, verifies their digests against official PyPI
metadata, installs into a new staging directory and copies only pinned tracker
and launcher source. It does not reuse a local `.venv`. Use a fresh staging path
for each run; the builder will not overwrite an existing environment.

Commit any tooling/documentation changes before packaging. `package_portable_windows.py`
requires a clean checkout and verifies that the Unity source, source manifest and
technology pin match the base player's source revision.

```powershell
python tools/package_portable_windows.py --base-player-zip C:\Downloads\ManhattanViewer-v0.1.0-Windows-x64.zip --runtime-stage .cache/portable-stage --output Builds/Distribution/ManhattanViewer-v0.1.1-Windows-x64.zip
```

The command validates the base ZIP's pinned SHA-256 and clean embedded identity,
combines it with allowlisted portable runtime files, replaces the guide with the
English Windows guide, and writes a ZIP, `.sha256` and `.integrity.json` receipt.
It hashes bytes again during copying, rejects unsafe paths/aliases and unlisted
staging files, and excludes logs, generated caches and personal calibration.
The intentional redistributable face model and newly assembled embedded runtime
are release inputs; neither is committed to Git.

Before publication, extract the exact ZIP into a separate folder, verify every
receipt entry, run both launcher modes with installed Python/Git absent from PATH,
check named camera enumeration and lifecycle cleanup, and retain a scoped report.
Use a separate calibration directory during QA and preserve personal settings.
Do not publish raw camera frames, personal calibration or machine-specific logs.

For a future changed Unity player, build and validate that player normally,
update `portable-player.json` to its exact artifact/revision/digest, then prepare
the portable distribution. Never rewrite the embedded player identity to make
an older binary appear to come from a newer source commit.
