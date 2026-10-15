# Decision records

One file per decision. This is where "we decided" lives. Chat is not a record.

## How it works

1. Copy [TEMPLATE.md](TEMPLATE.md) to `NNNN-short-name.md` (next free number).
2. Fill in the question, the options with honest trade-offs, and your recommendation. Status: **Proposed**.
3. Open a PR. People comment on the PR with their choice.
4. When the affected area owner and the Game design owner approve, change status to **Accepted** (or **Rejected**) and merge. See [WORKFLOW](../team/WORKFLOW.md#decisions-and-disagreements).
5. To change an accepted decision, write a new record that says it supersedes the old one. Never edit the old answer.

One file per decision means two people never edit the same file.

## Index

| # | Decision | Status | Recommended |
|---|---|---|---|
| [0001](0001-workflow.md) | Branches, PRs and AI agent rules | Proposed | Adopt WORKFLOW.md as written |
| [0002](0002-name-and-ip.md) | Name and Spider-Man branding | Proposed | Spider-Man-inspired original hero, neutral public name |
| [0003](0003-multiplayer.md) | Multiplayer | Proposed | Single player for v1, ghost runs later |
| [0004](0004-modes-and-story.md) | Modes and story | Proposed | Time trial first, light story, no cutscenes |
| [0005](0005-hardware-input-contract.md) | Hardware input contract | Proposed | UDP text v1, one input facade |
| [0006](0006-camera-and-input-priority.md) | Camera view and input priority | Proposed | First person only, wrist over mouse |
| [0007](0007-assets-and-licences.md) | Asset sources and licences | Proposed | CC0 or original; licence line per PR |

Already settled before this log (do not reopen): the rules in [AGENTS.md](../../AGENTS.md) and [architecture](../architecture.md), such as Unity 6000.3.22f1, meters at scale 1, the three comparison switches, separate calibration and bookmarks, and no generated city data in Git.
