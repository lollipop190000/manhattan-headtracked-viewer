# Game design document

Working title: **Manhattan Swing** (placeholder, see [decision 0002](../decisions/0002-name-and-ip.md)). Owner: Game design. Keep this under five minutes of reading; put details in linked files.

## One sentence

You sit at your own monitor, which becomes a real window into Manhattan, and swing between skyscrapers by shooting webs, aiming with your hand.

## Why this can be fun

The unusual part is the *window*. Because the screen is tracked to your head, buildings pass with real depth when you lean, and near things (cables, window frames, the web itself) look physically close. A normal game cannot do this. The game should keep reminding you of it. The motivation videos are [Johnny Lee's Wiimote head tracking](https://www.youtube.com/watch?v=Jd3-eiid-Uw) (the technique we use) and a [short about wanting to be Spider-Man](https://www.youtube.com/shorts/FZVpuuolfcY) (the fantasy). The Sketchfab [Manhattan model](https://sketchfab.com/3d-models/new-york-city-manhattan-372bc495b3a941308f4a3198bc45e17b) is reference only for now.

## Pillars

Use these to say yes or no to ideas.

1. **Swinging feels great.** Rhythm, momentum, release at the right moment. If a feature makes the swing worse, cut it.
2. **Nobody gets dizzy.** Comfort beats spectacle. See [comfort](../team/ROADMAP.md#comfort).
3. **Use the window.** Prefer goals that reward leaning, peeking around corners, and depth.
4. **Three-minute sessions.** A run is short, restartable instantly, and easy to show someone at a table.
5. **Anyone can play.** Mouse works; controller and wrist device make it better, never required.

## Core loop (seconds)

Aim, fire, attach, swing, release at the right point, land or fire again. The loop is repeated 50 or more times a run. Everything else is the reason to repeat it.

## Layers on top (minutes)

| Layer | Idea | Cost | Stage |
|---|---|---|---|
| Time trial | Fly a ring route, beat your best time | Low | M1 |
| Free swing | Collect floating tokens across the city, no timer | Low | M2 |
| Rescue / delivery | Reach a rooftop point and carry something to another | Medium | M4 |
| Chase | A target runs below; stay close | High | later |
| Ghost race | Race your own or a friend's recorded run | Medium | later, see [decision 0003](../decisions/0003-multiplayer.md) |

## M1 slice: "First fun"

- One route of about 3 minutes through a dense part of Midtown or Lower Manhattan, 10 rings, rising difficulty.
- Timer, best time saved locally, instant restart on one key.
- Web and swing sound effects, wind that follows speed, one music loop.
- Plain hero hands with a firing pose, a curved web.
- Anchor assist so there is always something to grab.
- Mouse controls first; wrist and controller after.

Exit test: five outside players try it; at least three ask to play again without being asked.

## Controls (today, from [web-swing](../web-swing.md))

| Input | Action |
|---|---|
| Left click | Fire (re-attaches if already swinging) |
| Right click or R | Release |
| E / Q | Reel in / out |
| WASD | Air steer |
| Wrist device | Aim and fire (planned, not wired) |
| Gamepad / BLE | Move (aim and fire not defined yet) |

Open question for design and hardware: which input is primary when several are connected.

## Feedback the player needs

Reticle that shows when a hit is possible; web travel time visible; speed readout; ring pass sound and flash; time delta against best; clear fail and instant retry. No text walls.

## Out of scope for now

Multiplayer, third-person camera, character progression, voice acting, story cutscenes, mobile or web builds. Each can be reopened by a decision record.

## Open decisions

Resolved in [docs/decisions](../decisions/README.md): name and IP, multiplayer, modes and story, hardware contract, asset licences, workflow. None are accepted until the team says so.
