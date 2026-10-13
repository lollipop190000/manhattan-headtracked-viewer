# Hardware

Home for everything needed to rebuild the physical devices: firmware, wiring, and the bridge that turns device data into the game's input format. Owner: Hardware (see [ROLES](../docs/team/ROLES.md)).

Status: **empty on purpose.** The wrist device firmware and the serial or Bluetooth to UDP bridge currently live in a personal project and are not reproducible from this repository. Moving them here is the first task for this folder.

## Wanted layout

```text
hardware/
  wrist/
    firmware/     Arduino sketch, pinned libraries, flashing steps
    bridge/       serial or BLE to UDP script (standard library or pinned requirements)
    wiring.md     parts list, pin table, photo or diagram
  controller/     BLE gamepad firmware and pairing steps (ESP32 or other)
  PROTOCOL.md     wire format versions, with examples
```

## Rules for this folder

- Anyone with the parts list should be able to rebuild a device in one session. If a step lives only in someone's head, write it down.
- Pin the library versions and board package versions. "Latest" breaks later.
- No personal paths, COM port numbers, Wi-Fi names or passwords in committed files; use a documented config file that is ignored by Git.
- Photos and diagrams are LFS (`.png`, `.jpg`); keep them small.
- Test without hardware first: `python tools/fake_wrist.py` sends the same lines as a real device (see [wrist-input](../docs/wrist-input.md)).
- Contract: [decision 0005](../docs/decisions/0005-hardware-input-contract.md).
