# 0001: Branches, PRs and AI agent rules

- Status: Proposed
- Proposed by: @SWTY7
- Affects: all
- Date: 2026-10-13
- Supersedes: none

## Question

How do six people and their AI agents change one Unity project without breaking `main`?

## Options

### A. Adopt [WORKFLOW.md](../team/WORKFLOW.md)

PR for everything, `<area>/<name>` branches, squash merge, frozen main scene, licence line for every asset, agents do not merge. Cost: a little process. Reviews can queue if owners are away; one-day review expectation mitigates it.

### B. Light rules: PR only, no naming or scene rules

Faster to start. The scene file will conflict the first time two people edit it, and nobody will be able to tell whose branch is whose.

### C. Everyone commits to `main`

Not viable: it already broke the "main stays runnable" rule once (`2885088`).

## Recommendation

A. Revisit after M1 if it slows people down.

## Outcome

Pending.
