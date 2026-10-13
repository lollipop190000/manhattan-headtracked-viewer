# 0003: Multiplayer

- Status: Proposed
- Proposed by: @SWTY7
- Affects: core, swing, design, world
- Date: 2026-10-13
- Supersedes: none

## Question

Will the game support more than one player?

## Constraints that matter

The view is head-tracked for exactly one viewer on one screen (README: "single-viewer and monoscopic"). Two people cannot share one tracked screen. The city uses a floating origin and streams per camera, and the swing is local, with no networking code today. Each player needs their own tracker, calibration, and ideally their own PC.

## Options

### A. Single player only

All effort goes into feel and content. Fastest to a fun game.

### B. Single player now, async competition later

Record a run as inputs or poses; replay as a ghost; local or shared leaderboard file. Social and replayable at low cost, and works with one screen. Needs deterministic or pose-based replay (the swing is already deterministic pure code, which helps).

### C. Live online multiplayer

Two or more players see each other swinging. Needs networking, sync of swing state, per-player streaming and large testing effort. High risk for a six person team.

### D. Local split screen / hot seat

Breaks head tracking (one viewer). Hot seat (take turns on one machine) is cheap and fits a show table.

## Recommendation

A for M1 to M3, then B (ghost runs) and D (hot seat scoreboard) as the multiplayer story. Revisit C only after M5.

## Outcome

Pending.
