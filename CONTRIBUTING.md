# Contributing

Bug reports, reproducible visual cases, documentation improvements and focused pull requests are welcome.

## Report a problem

Include the release/tag or commit, Windows/GPU, resolution, whether a webcam is running, comparison switch states and the steps that reproduce it. For rendering issues include a screenshot, whether the camera is stationary, and a saved position if available. Remove personal calibration, camera identifiers and private paths from logs before posting.

## Team workflow

This is a team game project. Read [docs/team/WORKFLOW.md](docs/team/WORKFLOW.md) (branches, PRs, Unity rules, AI agents) and [docs/team/ROLES.md](docs/team/ROLES.md) (who owns which area) before your first change. Branches are named `<area>/<short-name>`.

## Make a change

1. Follow the source setup in [README](README.md), then branch from `main`.
2. Keep reusable tracking and physical projection in [Head Tracked Display](https://github.com/lollipop190000/head-tracked-display). Viewer streaming, navigation and UI belong here.
3. Preserve the existing comparison-switch behavior and separate calibration/preferences/bookmarks. Commit `.meta` files for authored Unity assets.
4. Run `tools/test.ps1 -DataOnly`, relevant Unity tests and a native Windows check for rendering or streaming changes. Describe actual results and limits in the PR.
5. Open a pull request with the problem, resulting behavior and validation. Keep unrelated changes separate.

Do not commit generated city data, downloaded archives/models, builds, caches, virtual environments, logs or personal calibration. Small binary fixtures use Git LFS and need provenance. Team-authored contributions use the repository's MIT license; preserve third-party notices.
