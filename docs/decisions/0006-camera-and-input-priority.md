# 0006: Camera view and input priority

- Status: Proposed
- Proposed by: @SWTY7
- Affects: core, swing, hardware, character
- Date: 2026-10-13
- Supersedes: none

## Question

Is the view first person or third person, and which device wins when several are connected?

## Options for the view

### A. First person only

The camera is the player's eyes, which is exactly what the head-tracked projection models. Hero is seen as hands, arms and the web. Cheapest and the most faithful to the window idea.

### B. Third person

You see the hero swing; strongest for animation and the swing fantasy. The tracked eye then looks at a character, not through the window. Needs a new camera model and harms the depth effect. Also more animation work.

### C. Both, switchable

Double the animation and camera effort. Later.

## Options for input priority

### X. Last used device wins

Simple, forgiving. Can flicker if two devices send noise.

### Y. Fixed priority: wrist for aim and fire, mouse as fallback, controller or keyboard for movement

Predictable, with a visible status. Needs clear rules for what happens when the wrist disconnects.

## Recommendation

View A for v1. Input Y, with the status shown on screen and `X` available as a setting for testing.

## Outcome

Pending.
