# Workflow

Six people and their AI agents share one Unity project. These rules exist so that `main` always runs and nobody's work silently overwrites anyone else's. They are deliberately few.

## The five rules

1. **`main` is never pushed to directly.** Everything goes through a pull request. (A direct push happened once already, commit `2885088`.)
2. **One branch per feature**, named `<area>/<short-description>`, e.g. `swing/zip-to-point`, `hardware/wrist-udp-wiring`, `audio/ambience-loop`, `docs/gdd-missions`. Areas: `core`, `swing`, `hardware`, `design`, `character`, `audio`, `world`, `docs`. Old `codex/*` and `goat/*` branches stay as they are; do not rename them.
3. **One PR does one thing**, and stays small enough to review in 15 minutes. If it touches another area's files, say so at the top of the PR and tag that owner.
4. **Every PR says what was and was not tested.** The PR template asks. "Not tested" is acceptable; claiming a test you did not run is not.
5. **Decisions are written down.** If you are choosing between options that others will have to live with, add a file to [docs/decisions](../decisions/README.md) in the same PR or before it. Do not re-open an *Accepted* decision without a new record that supersedes it.

## Day to day

```bash
git switch main && git pull
git switch -c swing/zip-to-point
# work, commit small and often
git push -u origin HEAD
# open a PR into main on GitHub
```

- Merge by **squash** so history stays readable. The author merges after the owner approves.
- Rebase or merge `main` into your branch before asking for review if others have merged.
- Draft PRs are welcome for early feedback. Mark ready when the checklist is filled in.
- Review within one day, even if the review is "I will look tomorrow".
- Playtests happen from the Windows release ZIP, not from source. Non-developers never need Unity.

## Unity rules

Unity project files are not friendly to merging. These rules prevent most conflicts.

- **Do not edit `ManhattanViewer.unity`.** It is one big YAML file; two people editing it means a conflict nobody can resolve. Build your feature as a prefab under your area's folder or create it from code (as `ManhattanBootstrap` and `MouseWebShooter` already do). If you truly need a scene change, ask Core to make it.
- **Never commit without the `.meta` file**, and never delete or regenerate a `.meta` of existing files. Moving a file means moving its `.meta` with it (move inside the Unity editor).
- **Do not change** `Packages/`, `ProjectSettings/` or the Unity version unless you are the Core owner. Editor version is pinned: **Unity 6000.3.22f1**.
- **Large binaries** (`.fbx .glb .blend .png .jpg .wav .mp3 .ogg`) go through Git LFS, run `git lfs install` once. Keep single files under 50 MB. Do not commit the city source ZIPs or generated city data (already in `.gitignore`).
- **Every asset needs a licence line.** Add the source, author, licence and link to [THIRD_PARTY.md](../../THIRD_PARTY.md) or the folder's `NOTICE.md` in the same PR. No licence, no merge. CC-BY needs attribution; "free to download" is not a licence.
- **One component, one job.** Follow [architecture](../architecture.md): extend through `IMotionInputSource` and `ITranslationConstraint`; keep `ManhattanBootstrap` to composition.
- **Asmdefs:** each area keeps its own assembly definition so one person's compile error does not block everyone. Game code lives under `Assets/Game/` with its own asmdef.

## Definition of done

A change is done when:

- It builds and runs in the Windows player (not only in the editor), or the PR says why it could not be checked.
- `tools/test.ps1 -DataOnly` passes (it is also run by GitHub Actions).
- Relevant tests were run or the gap is stated. Add tests for pure logic (physics, protocol parsing, mapping).
- If it can change frame time, the PR includes a before and after number from the benchmark route ([validation](../validation.md)). Target is 60 fps or better on the reference machine; comfort matters more than detail ([ROADMAP](ROADMAP.md#comfort)).
- It is **fun or at least not worse**. For anything that affects feel, one other person played it for five minutes and the PR names them.

## Working with AI agents

Everyone may use AI agents (Claude, Codex, Copilot, anything). The rules are about the output, not the tool:

- Agents follow [AGENTS.md](../../AGENTS.md) and this file. An agent working for you is *you* in review: you are responsible for the PR.
- **Personal setup stays personal.** Do not commit your own agent configuration, scratch plans, session logs, local paths, usernames, or tool-specific file naming. Examples of things that stay out: `PLAN.md`, `.claude/`, `.cursor/`, `*.local.*`, vault or wiki notes. If a note is useful to everyone, rewrite it as a proper file under `docs/`.
- Branch and commit names follow rule 2 above, whichever tool created them.
- **One agent, one branch, one area.** Never run two agents on the same files. Agents do not edit areas they were not asked to.
- **Agents do not merge, tag, force-push, change repo settings, or edit shared docs** (this file, ROLES, decisions marked Accepted) unless you told them to for that specific change.
- Do not paste secrets, tokens, or your webcam/calibration data to an agent or into the repo.
- If an agent invents numbers ("tested, 60 fps"), the PR is rejected until the real measurement is there.

## Decisions and disagreements

- Small calls inside your area: just decide, note it in the PR.
- Calls that cross areas or change the game: write a decision record using the template. Status flow: *Proposed → Accepted* (or *Rejected*). It is accepted when the **area owner affected and the Game design owner** both approve the PR that sets the status. If they disagree, the Core owner breaks the tie.
- Chat is for talking; the repo is for deciding. If it is not in `docs/decisions/`, it was not decided.

## Repository settings to turn on (needs a repo admin)

These are not code changes; nothing here has been applied.

- Branch protection on `main`: require a pull request, require the `portable-checks` status check, block force-push, dismiss nothing automatically.
- Default merge method: squash. Auto-delete head branches after merge.
- Optional once the team is comfortable: require code-owner review (see `.github/CODEOWNERS`).
- Labels: one per area (`core`, `swing`, `hardware`, `design`, `character`, `audio`, `world`, `docs`) plus `decision`, `playtest`, `blocked`.
