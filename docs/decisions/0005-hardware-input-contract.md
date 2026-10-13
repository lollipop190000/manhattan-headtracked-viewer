# 0005: Hardware input contract

- Status: Proposed
- Proposed by: @SWTY7
- Affects: hardware, swing, core
- Date: 2026-10-13
- Supersedes: none

## Question

How do physical devices talk to the game, so that hardware and software people can work without waiting for each other?

## What exists (verified in the repo)

- Wrist device: text line `yaw,pitch,fire` over UDP to `127.0.0.1:8766`; fire is `0` none, `1` fire, `2` recalibrate. Parser and aim ray in `Assets/WebShooter/Input`. Direct serial receiver exists but is compiled out under .NET Standard 2.1. Firmware and the serial or Bluetooth to UDP bridge are in a separate personal project, not in this repo. See [wrist-input](../wrist-input.md).
- BLE gamepad: appears as an HID joystick or gamepad, read by `ControllerMotionInput` for movement only.
- Fake sender: `tools/fake_wrist.py`.

## Options

### A. UDP text lines as the single wire format (v1)

Any device or bridge that sends the line works. The game never touches serial or Bluetooth. Add a version prefix when the format changes. Simple, testable with the fake sender. Costs a bridge process and loopback only.

### B. Native serial in the game

Fewer processes, but requires changing the API compatibility level to .NET Framework for the whole project, which is a Core change affecting everyone.

### C. HID for everything

Devices pretend to be gamepads. Works with `ControllerMotionInput` but can only carry buttons and axes; absolute aim angles and calibration events would be forced into axes.

## Recommendation

A. Plus:

- Firmware and the bridge move into `hardware/` with a README (steps, wiring, pins, library versions).
- Add `v1` documentation of the line (ranges, rate, meaning of each value) and add a `device_id` and `battery` field in a v2 line later, rather than changing v1.
- Game-side, introduce one input facade that exposes move, aim ray, fire, release, recenter and device status; the mouse, controller and wrist all implement it. Priority is in [0006](0006-camera-and-input-priority.md).
- Disconnect never freezes the game: fall back to mouse and show status on screen.

## Outcome

Pending.
