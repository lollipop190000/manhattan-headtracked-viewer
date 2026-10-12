# Current streaming integration validation

Recorded on 2026-10-12. This is scoped Windows diagnostic evidence, not a release
or real-head-motion acceptance record.

The performance/media tables below belong to `338c090` and its recorded v4
correspondence. The later `747170e` correction uses v5 correspondence and needs
separate rebuilt-input validation; those earlier numbers do not certify the
new mapping. See [current ownership rules](skyline-ownership.md) and the later
live webcam limits in [viewport validation](physical-viewport-validation.md).

The full v5 rebuild and matched native fixed-pose result are now recorded in
[beige wall validation](beige-wall-validation.md), on runtime `9deb78f`. This
static surface proof does not transfer the older performance/memory acceptance
to that build.

## Current v5 short regional measurements (`9deb78f`)

Two fresh private `FullCityTolerancePlayer` runs measured the complete 260-group
v5 correspondence on source `9deb78fbd1d8e01a52c406c6656f1bb197ae0c33`, with
technology `2a33e1c0115bbc6b9c8e5d9709c3aff7c0dbb329`. They reuse the unchanged
10,038-tile `manhattan-2026-10-10-v2` catalog and original built bundles. The v5
sidecar is 4,394,261 bytes, SHA-256
`bf64909d83f2919896c15e25f4a6dbf8bf870179561972835e6a0e6cf82ae661`;
catalog digests and the matched surface result are in
[beige wall validation](beige-wall-validation.md). Both runs retain the same
`runtime-fingerprints.json` digest
`1b2da7467bb8f68ab98a5c680b8aea46f9898564e03f92af559bc73dd1c4e739`.
The executable digest alone cannot distinguish their managed runtime from older
Unity Players; recorded source/resource fingerprints provide that linkage.

Each run configured 15 seconds of warmup and measured 90 seconds, without a
tracker, timing/counter probes, video capture or collision proxies. Output and
camera resolution were 1920×1200, Windowed, TAA and VSync 1, on Unity 6000.3.22f1,
URP 17.3.0, glTFast 6.20.0 and RTX 4070 Laptop. Percentiles use nearest rank on
the raw four-decimal millisecond CSV. The private `scoped-analysis.json` records
are identified by these run IDs:

| Route / run ID | Frames | Engine delta P95 / P99 / max ms | Streaming P95 / P99 / max ms | Loaded / pending tiles |
|---|---:|---|---|---|
| Midtown `9a936c9df4644016887b8bf59f615b62` | 5,401 | 16.6832 / 16.6833 / 16.6856 | 1.8472 / 2.3562 / 5.1629 | 426–449 / 0–2 |
| Park `311fe36ad27040f48b385fddac84e573` | 5,381 | 16.6831 / 16.6836 / 44.4118 | 1.6024 / 2.1352 / 3.9124 | 368–442 / 0–2 |

Both recorded zero engine-delta or streaming samples `>=50ms` and zero tile-load
failures.
Independent city-position/time checks measured median speeds 99.9977m/s and
99.9976m/s, respectively, with effectively 100% measured time coverage. Each
completed two measured circuits and covered the full production route.

The active-work subset (`pending > 0` or streaming work `>=0.5ms`) contains
2,798 Midtown and 2,276 Park samples. Its streaming P95/P99/max is
2.0992/2.5754/5.1629ms and 1.9353/2.3649/3.9124ms, respectively. Midtown's active
P95 still exceeds 2ms. The correlated `engine >=50ms && streaming >=2ms` count
is zero; causal streaming-stall attribution remains unavailable.

| Route | Exact Player working-set peak GiB | Exact Player private-commit peak GiB | Whole selected GPU peak GiB |
|---|---:|---:|---:|
| Midtown | 2.1066 | 3.9923 | 1.7705 |
| Park | 2.1297 | 3.8899 | 1.7217 |

Windows counters are aligned to the exact executable/PID, not a verified process
tree; the tracker was disabled. Driver memory includes other applications on the
selected GPU and is not Player-only VRAM. Unity's graphics-allocation counter is
unavailable. After the first route circuit, short-run private-commit median
growth was 55.82/37.30MiB and in-Player working-set growth was 45.19/30.15MiB.
These approximately 78-second trend windows do not establish twenty-minute
memory acceptance.

Engine deltas include VSync waiting; they do not establish delivered full-frame
FPS or GPU rendering time. Visible-pixel warmup was not verified. Both builds are
dirty private diagnostics with synthetic calibration and no head tracking;
acceptance remains pending. These measurements do not certify the later
`b341221` source-loader/Surface foundation, whose 241 EditMode and 150 GPU
PlayMode checks are separate correctness evidence. The native source pilot has
not run in this record. Historical `338c090`/v4 performance and long-memory
tables below retain their original scope and do not transfer to v5 or the pilot.

## Source and immutable city inputs

The measured scheduling policy is in viewer runtime commit
`338c090bb278ecd0f353f3e9cd8ae3ebf9a805a7`; its package pin is
`2a33e1c0115bbc6b9c8e5d9709c3aff7c0dbb329`. Unity 6000.3.22f1,
URP 17.3.0 and glTFast 6.20.0 are unchanged.

A private project reuses the existing 10,038-tile
`manhattan-2026-10-10-v2` catalog and built bundles through an explicit read-only
path adapter. It compiled a separate complete ownership sidecar from that exact
built catalog; it did not rebake source meshes, textures or bundles.

| Input | Bytes | SHA-256 |
|---|---:|---|
| Source manifest | — | `82bc35bea819762e3832210187232cafa8e16bea9fd92ecf3cf8dc2f683f8bc7` |
| Existing Unity city catalog | 32,127,816 | `1a53e1256a19e0f4ff38921687ed9065f07741e19eb963805381d6de3b69518d` |
| Existing built Addressables catalog | 7,053,858 | `56b7e281f8171c55b5f6049b7b960fd31b6a90a86370c4c08f48053b26b07828` |
| Private complete ownership sidecar, 260 groups | 3,841,898 | `9cb87dd09206c6530d920408b78883375ab5e7a449857cc867be9075bfb1c844` |

The original checkouts and old GOAT dirty files were preserved. Original main's
ownership sidecar was absent; the new sidecar exists only in the private project.
Each diagnostic binary embeds its source identity and `dirtyWorkingTree=true`.
The private read-only path, optional timing observer and optional media helper
are fingerprinted alongside each run. This is not the production build procedure
or fresh-clone release acceptance. Existing immutable license/attribution files
were retained; their original notice manifest has not been reissued as a release
manifest for these diagnostic binaries.

## Reproduction and scheduling change

At 1920×1200 output and camera resolution, Windowed, D3D11, RTX 4070 Laptop,
Midtown 100m/s, no tracker, configured 15-second warmup and 90-second measurement,
the integration before this scheduling change exceeded the 2ms streaming P95
target. All percentiles below use nearest rank, `ceil(p*n)-1`. Raw frame CSV
milliseconds are rounded to four decimals; original JSON preserves float values.

| Policy/source | Measured frames | Streaming P95 ms | Result |
|---|---:|---:|---|
| `34fb631`, entire remaining budget | 5,401 | 2.2097 | Fails 2ms target |
| `34fb631`, repeated matched baseline | 5,401 | 2.1936 | Fails 2ms target |
| `5ef4565`, 30% headroom | 5,401 | 1.9942 | This run passes; little margin |
| `53dbca8`, same 30% policy repeated | 5,401 | 2.0129 | Fails; earlier pass is insufficient |
| Private 40% prototype, explicit modified policy | 5,402 | 1.8950 | This run passes; final-code repetitions separate |
| `0805b6d`, 40% headroom, later controlled repeat | 5,401 | 2.0079 | Fails; headroom alone is insufficient |

These valid runs had zero recorded tile-load failures. A separate early prototype
with 233 failed loads is excluded: its private path adapter missed relative
`StreamingAssets/aa` bundle IDs. It cannot support any performance improvement.
The adapter was repaired before subsequent comparisons. The city input digests
remained unchanged. A separate wrapper startup failed before launching a Player;
it produced no timing samples and is not a benchmark result.

Ownership previously used the entire remaining work allowance before the
post-publication visibility update. Timed ownership can also overshoot one
indivisible preparation step. The final streamer schedules this resumable work
against 60% of the total allowance less work already spent, leaving 40% headroom.
At the default 2ms target this allows preparation up to the 1.2ms scheduling
boundary. Mesh uploads and the validated atomic publication are still
indivisible, so this is a soft scheduling target, not a maximum-frame guarantee.
The previous representation remains published while a cohort waits. Geometry,
material, distance/radius, load priority and rendering quality policies are
unchanged; the tradeoff is potentially later publication.

The failed 40% repeat exposed a second scheduling problem: the final preparation
step could be followed by all atomic revalidation/publication in the same Tick.
Supplemental same-frame timing showed publication-frame streaming P95 2.604ms
and nested ownership P95 2.2225ms. These are CPU stopwatches, not GPU timing or
an allocation diagnosis. `338c090` ends ordinary preparation at an explicit
boundary and starts revalidation/publication on a later Tick. Departing owners,
revoked coverage and gated late Skyline bypass that boundary so recovery can
still publish immediately. Mesh-pointer changes and Core activation remain one
no-yield operation after fresh validation.

Two executed coverage regressions exercise both a tiny allowance and 1.2ms:
the unrelated fallback face remains present on every preparation tick; the owned
face always has fallback or active Core; exhausted work retains the published
Core; fallback is restored before release; imported indices, vertices and
material references remain unchanged. Two further cases verify the preparation
boundary and an unnotified coverage change before publication. An initial
unconditional-boundary prototype failed two existing lifecycle/late-fallback
tests; the recovery bypass fixes them without editing or weakening those tests.
The final runtime source passed 220/220 EditMode
and 126/126 live GPU PlayMode, zero skips. The portable suite passed 148/150,
with two explicit Windows symbolic-link-privilege skips and real junction checks
passing. Eight PowerShell assertions passed.

## Matched final runtime measurements

Both diagnostic Players use surrounding runtime source `338c090` and the same
private observers/capture adapter, catalog, sidecar and rendering configuration.
The baseline selectively substitutes only `SkylineGeometryOwnership.cs` from
`0805b6d`; it is not a pristine checkout of that earlier commit. Runtime file
fingerprints confirm that exactly this one file differs. Both builds completed
with zero errors/warnings. Candidate executable SHA-256 is
`8da50e79b68e979c53cafb639961ca6c34d39bfed8e87b91cf9caf5c12c081fe`.
The Unity executable alone does not identify its managed runtime: the candidate
`Manhattan.Viewer.Runtime.dll` SHA-256 is
`f90de214ca7a9ef5430713fa86d7eb64ee06d7e30585303b556e4465c219ac96`,
versus baseline
`4eb2709d0dcd0eaf4958b5e1051f7fc4dd190cacaf7426e8913c75697c7805a8`.
Source fingerprints and embedded build identity are retained with every run.

Each primary row below is a separate 90-second measurement without timing,
profiler-counter or video observers, at the configuration stated above. Both
Midtown repetitions and Park use the production benchmark routes. Air-down is an
explicit private motion-input wrapper: `(1225,400,3585)` to
`(1725,400,3585)` and back, 100m/s, 5 seconds each way, fixed Euler
`(65,208.5,0)`. It changes the observer input, not the final tracked camera.
Each air run recorded eight complete measured circuits and independently
verified median speed 100m/s. Configured warmup is still separate from verified
visible-pixel warmup.

| Policy / route / run ID | Frames | Engine delta P95 / P99 / max ms | Streaming P95 / P99 / max ms |
|---|---:|---|---|
| Baseline, Midtown `fdb0ffcc` | 5,401 | 16.6831 / 16.6833 / 16.6851 | 1.9418 / 2.3486 / 6.6639 |
| Candidate, Midtown `96410b07` | 5,401 | 16.6832 / 16.6833 / 16.6858 | 1.8049 / 2.2560 / 4.6863 |
| Candidate, Midtown repeat `43ea1cfd` | 5,401 | 16.6832 / 16.6833 / 16.6850 | 1.8441 / 2.2168 / 6.1638 |
| Candidate, Park `4f86eb90` | 5,401 | 16.6832 / 16.6834 / 16.6846 | 1.7101 / 2.1701 / 4.6178 |
| Candidate, air-down proxies OFF `7647c3eb` | 5,401 | 16.6832 / 16.6834 / 16.6875 | 1.3470 / 1.9100 / 5.3233 |
| Candidate, air-down proxies ON `194b8f84` | 5,401 | 16.6831 / 16.6837 / 16.6873 | 1.4054 / 1.8952 / 4.9498 |

All six runs recorded zero tile-load failures and zero all-engine-delta frames
`>=50ms`. All passed independent city-position/time speed checks. The baseline
also passes the 2ms P95 target in this later pair: earlier failures and run-to-run
variation must not be hidden. These ordered measurements are scoped evidence,
not a statistical or fixed-thermal-state performance guarantee.
Collision-proxy work P95/P99/max was 0/0/0ms with proxies OFF and
0.0170/0.0308/1.0445ms with proxies ON.

The active-work subset is defined as `pending > 0` or streaming work `>=0.5ms`.
Its streaming P95/P99/max was 2.1794/2.6941/6.6639ms for baseline (2,597 samples),
2.0689/2.4074/4.6863ms for candidate (2,689),
2.0697/2.4981/6.1638ms for candidate repeat (2,704),
2.0241/2.3731/4.6178ms for Park (2,344),
1.8478/2.9056/5.3233ms for air-down OFF (1,379), and
1.8326/2.2085/4.9498ms for air-down ON (1,440). The active Midtown/Park P95
still exceeds 2ms. The subset is diagnostic, not causal attribution. Its
correlated `engine >=50ms && streaming >=2ms` count is zero in all six;
causally attributed streaming stalls remain unavailable.

## Timing interpretation and remaining acceptance

Engine delta is not GPU execution or CPU frame time. Separate candidate timing
runs at the same settings recorded the following P95/P99/max milliseconds:

| Quantity | Midtown `2fd3412e` | Air-down `90034790` |
|---|---|---|
| CPU total frame | 16.8011 / 17.0257 / 33.3117 | 16.8027 / 17.0752 / 33.2755 |
| CPU main thread | 3.3218 / 4.2891 / 11.3643 | 2.6482 / 3.3186 / 8.3425 |
| CPU render thread | 1.0396 / 2.0742 / 7.3158 | 0.5548 / 0.7553 / 2.1411 |
| Present wait | 15.5817 / 15.7046 / 31.5140 | 15.7716 / 15.9042 / 31.8837 |
| GPU | 5.217280 / 5.722112 / 7.507968 | 5.338112 / 5.767168 / 6.722560 |

Each had 5,399 unique positive CPU timestamps, no absent timestamps and no
duplicates. Midtown had 5,397 positive GPU values and two unavailable/nonpositive
GPU values; air-down had 5,399 positive GPU values. Available CPU/GPU timings
recorded zero `>=50ms` samples; missing GPU values cannot certify all GPU frames.
These instrumented runs had 5,400 engine samples, zero load failures, and engine
maxima 33.3329/33.3336ms respectively. CPU total P95 exceeds 16.7ms; the engine
delta result must not be substituted for that quantity. CPU total includes
synchronization, and these quantities are separate rather than added together.
See the [Unity FrameTiming API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/FrameTiming.html).
The latest-timing API is asynchronous. Samples are deduplicated by CPU timestamp,
and phase membership is determined at receipt time, which can lag the rendered
frame. Missing/nonpositive timings remain unavailable rather than zero cost.
This observer's results do not replace the runs without the observer. In the
candidate Midtown timing run, publication-frame streaming P95 was 2.4399ms and
nested ownership P95 2.0302ms across 289 publication frames. Maximum nested
ownership work was 8.005ms; the atomic operation still has no hard deadline.

A separate 60-second counter run `d6745d92` recorded 3,600 filled receipt-phase
samples per available counter. Draw calls/batches P50/P95/max were
866/1,039/1,204; SetPass calls were 76/91/120. Triangles were
1,376,851/1,897,356/2,593,403 and vertices 2,801,274/4,545,397/6,186,031.
Vertex-buffer upload bytes were 352/2,462,080/8,419,744 and index-buffer bytes
12/144,324/507,942; both upload-count counters were 1/7/50. All these counters had
zero missing samples. `GC Allocated In Frame` was not available in this Player;
allocation bytes and separate upload-duration costs remain unmeasured. Counters
usually describe the preceding frame at receipt, sum recorded threads, and add
observer overhead. They are not GPU durations or primary performance evidence.

The strict all-frame `>=50ms` count and a correlated streaming subset are retained
separately in local analyses. A correlated subset is not causal attribution. The
valid short runs listed above recorded zero all-engine-delta frames `>=50ms`;
their streaming timings and active-work subsets are reported separately.

The `benchmark.json` rendering fields are end-of-run snapshots. Final-code
measurements and native moving-media checks are recorded separately below.
Configured warmup is 15 seconds; this does not certify 15 seconds of visible,
fully resident city rendering. The instrumented record identifies the first
positive GPU timing receipt separately; visible-pixel warmup remains unverified.
Unity graphics-allocation telemetry was unavailable in these runs; aligned
driver memory is a whole-GPU measurement including other applications, not exact
player VRAM. No webcam was opened, and valid tracking fraction was zero.

## Twenty-minute repeat and memory limits

A separate uninstrumented candidate Midtown 100m/s run `c231207b` used the same
binary/configuration and 15-second configured warmup. It measured
1200.007202 seconds, 72,001 engine frames and 44 complete measured circuits.
Engine delta P95/P99/max was 16.6832/16.6833/33.3328ms, with zero `>=50ms`
frames. Streaming P95/P99/max was 1.7647/2.2300/10.8870ms. There were zero
tile-load failures; loaded tiles ranged 422–450 and pending loads 0–2.
The 36,077 active-work samples had streaming P95/P99/max
2.0200/2.4242/10.8870ms. Independent speed P10/median/P90 was
99.9606/99.9971/100.0343m/s over 72,000 intervals and full recorded-time coverage.
The correlated slow-frame subset was zero; causal attribution remains unavailable.

Process samples target the exact Player PID. They are aligned to the reported
measurement interval, then exclude its first 11.4036 seconds to finish the circuit
that began during warmup. The following first/last 60-second medians use that
post-first-circuit interval; slopes use ordinary least squares against time.
Memory sizes below are bytes; the final column is a process/driver trend in
bytes per second, not an allocation rate from Unity:

| Quantity | Post-first-circuit samples | Peak | First 60s median | Last 60s median | Median increase | Last-five-minute slope, bytes/s |
|---|---:|---:|---:|---:|---:|---:|
| Player working set | 1,164 | 2,315,853,824 | 2,246,955,008 | 2,312,409,088 | 65,454,080 | 67,871.05 |
| Player private commit | 1,164 | 4,325,122,048 | 4,255,711,232 | 4,302,819,328 | 47,108,096 | 7,577.61 |
| Whole selected GPU driver memory | 1,111 | 1,903,165,440 | 1,886,388,224 | 1,887,436,800 | 1,048,576 | 5,559.07 |

The process interval spans 1187.9277 seconds; the GPU interval spans 1187.2301.
The last five minutes contain 294 process and 281 GPU samples. Whole-interval
slopes were 32,688.59/17,819.06/-430.51 bytes/s respectively. Unity graphics
allocation telemetry was unavailable; whole-GPU memory also includes other
applications. Working set and private commit increased, and the final working-set
slope remains positive. This completed trace therefore **does not establish a
plateau or absence of a leak**. Cache residency, managed/native allocations and
ownership-specific retention need further separation. Runtime/analyzer release
acceptance remains false because the private build, windowed/synthetic/no-tracker
conditions and remaining acceptance requirements do not qualify.

## Native media and limits

An initial media run was excluded from final native-media acceptance. Its private
wrapper left a capture environment variable active for
the first attempted long benchmark. That component replaced benchmark movement
and attempted to rewrite the prior native PNGs; the trace became stationary and
the owned Player was stopped. Its raw records were retained with explicit
exclusions. The wrapper now restores its owned environment even on failure, and
benchmarks reject an inherited capture variable before launch. A fresh 15-second
verification recorded 901 frames with independent median/P10/P90 speed
99.9986/99.9606/100.0326m/s, correct Midtown movement and no capture exceptions.
That short verification is not regional or memory acceptance. The valid
twenty-minute trace above and fresh native media below were rerun in a clean
sequence, with media last; the wrapper's environment restoration was verified.

The candidate Player with the same executable digest recorded native street and
roof PNGs, an exact restored-start PNG, and a separate requested 100m/s air-down
clip (`c2645168`). These use the normal visible Player backbuffer with neutral
synthetic eye, TAA/VSync 1, and the same city/sidecar. UI canvases were explicitly
hidden for media only. No webcam or desktop was captured, and no mesh/material
was changed for filming. Capture overhead is excluded from primary measurements.

The video contains 301 actual 1920×1200 frames over 10.0105746 seconds
(29.9683 mean captured fps). Variable-frame-rate encoding preserves recorded
timestamps; maximum decoded timestamp error is 0.0006050 seconds. There is no
interpolation, trimming, acceleration or orientation transform. An independent
native still matches the raw orientation anchor with mean absolute difference
zero. Readback errors and skipped requests were zero. The movie is 13,868,483
bytes with SHA-256
`43cb9661477d1a34d08349e6d27a469cdb26d41b6c74762871b2a37852a8e28e`.

Recorded frames span x=1225 to 1724.2476m and finish at x=1225.6270m; a separate
settled end PNG confirms the exact restored x=1225m pose. The moving capture
records request-time timestamps after end-of-frame, whereas the observer reads
the coroutine-provided pose on its earlier Update. Instantaneous pose/request-time
speed P10/median/P90 is 90.6777/100.8356/109.6043m/s; the clip therefore does not
pass the benchmark's 5% instantaneous-speed check and is visual evidence only.
The production benchmark traces above independently pass their speed checks.
During media, loaded tiles ranged 329–340, pending loads 0–2, failure count was
zero, and 48 moving samples had ownership work pending. Unsupported meshes were
zero. Loading was not artificially frozen for filming.

All nine decoded QA stills and the native street/roof stills were visually
inspected. No gross missing-building or pink-material issue was observed in
those samples. Static samples cannot certify temporal flicker, every surface,
head-motion TAA, or the user's specific offending roofs/sides. Media and raw
metadata remain private; no captures or large assets are tracked.

General city rendering/flicker, the user's offending surfaces, physical
head-motion perception, full regional speed/proxy acceptance, a clean release
build and the source-ID replacement pilot remain open. The 580-part delivery
retains `runtimeReady=false` and no new model is automatically activated.

## Source-ID pilot status

The existing isolated Howard GLB was inspected read-only. The six-building file
is 12,370,148 bytes, SHA-256
`5bf356dc53151589a13eda2478c582345f11a162b38158de99b31736b44b4872`.
Three materials carry transmission 1 and IOR approximately 1.48 while their alpha
mode is OPAQUE. The separate 91,872-byte street GLB has no such optical materials.
These source facts are not a native material/profile approval, and neither file
was activated in the measured city.

The current `CityBake.BakeModel` partitions triangles into spatial tiles and
combines them by material. `CityTileRecord` retains tile replacement relationships,
not exact per-building Core-body and additive-trim triangle ownership. The
separate `BuildingRecord.id`/footprint alone does not prove a render-mesh mapping.
The Skyline/Core sidecar above binds a different, tile/hull replacement contract;
it cannot be reused as a six-source body/trim mapping. `SourceReplacementGate`
still has no production caller. Its 32 cases were executed as part of baseline
EditMode validation, but a native import/material adapter, authoritative legacy
mapping and actual loader publication/rollback pilot are not implemented here.
Whole-tile suppression, guessed ownership and an unsupported optical override
would violate that contract. See [import foundation](model-import-foundation.md).

A newly listed regional delivery was observed through Drive metadata only.
Its ZIP bytes, digest and source-ID selection were not independently verified or
imported, and the accepted 580-part catalog was not enlarged. Model authoring and
sealed packages remain unchanged.

## Fresh v5 native roof roundtrip (`63b3c056`)

A separate fresh moving capture used the unchanged `FullCityTolerancePlayer`
on `9deb78fbd1d8e01a52c406c6656f1bb197ae0c33`, technology
`2a33e1c0115bbc6b9c8e5d9709c3aff7c0dbb329`, and the complete v5 sidecar recorded
above. Its managed `Manhattan.Viewer.Runtime.dll` SHA-256 is
`ab04ad6d23c82c2ad93217c9e8c99fe1c06498e94f5d2e5fdde8a7dd5989b2f4`;
the copied source/resource fingerprint receipt remains
`1b2da7467bb8f68ab98a5c680b8aea46f9898564e03f92af559bc73dd1c4e739`.
This is source-scoped evidence for that private build, not appearance validation
of the later `4133070` runtime or the source-ID pilot.

The explicit motion input traversed `(1225,400,3585)` toward
`(1725,400,3585)` and back, with five seconds per horizontal leg and fixed Euler
`(65,208.5,0)`. The normal visible Player backbuffer was captured at 1920×1200;
reported camera AA was TAA and VSync was 1. Tracking OFF and no webcam source
were enforced by the frozen capture helper. Synthetic calibration and hidden UI
were limited to this media run; streaming remained active. The report does not
include a direct tracking flag or separate camera-pixel dimensions.

All 301 raw motion frames were present and decoded from the variable-frame-rate
movie. Their request-time span is 10.0083494 seconds; maximum decoded timestamp
error is 0.0005887 seconds. The independent native anchor matches raw orientation
with mean RGB error zero. No orientation transform, interpolation, trimming or
acceleration was applied. Four readback buffers reported zero GPU readback
errors and zero skipped capture requests. The movie is 13,879,249 bytes,
SHA-256 `f460f2fe89b0a6478baeede1e5a6d70d5472b47cafa63c016a93e44401f83c09`.

Loaded tiles ranged 329–341, pending loads 0–2, and tile-load failures and
unsupported meshes were zero. Ownership work was pending in 48 motion samples;
masked Skyline triangles ranged 38,019–52,944. Recorded x reached 1724.1456m,
then returned to 1225.6636m in the last moving frame. A later settled native
still records the exact start pose. Its RGB pixels are not identical to the
settled initial roof still: mean absolute byte error is 0.4502, maximum 93,
and 1.4256% of pixels differ by more than eight in any channel. The same pose
therefore does not establish exact pixel restoration or identify a cause.

The nine decoded QA frames, including first/last, and both settled roof stills
were visually inspected. No obvious magenta error material or gross building
disappearance was observed in those samples, and orientation was consistent.
Full-frame decoding and selected visual samples do not certify every temporal
surface, single-frame flicker or real head-motion perception. Capture cadence is
not delivered FPS, and capture overhead is excluded from performance evidence.
Raw inputs, native PNGs and failed earlier records remain private and retained;
general flicker, physical tracking, performance and release acceptance remain
open.

## Fresh current-runtime diagnostic build (`4133070`)

`FullCityVerifiedPlayer` was built from the exact 25 public runtime C# files and
their 25 metadata files on `4133070f6dd290da01898cb29fd8fa34b7f507e9`.
The seven private diagnostic helpers were retained. The runtime assembly
definition, package manifest/lock and technology pin
`2a33e1c0115bbc6b9c8e5d9709c3aff7c0dbb329` were unchanged. The native Win64 build
succeeded with zero errors and zero warnings, 114,748,742 reported bytes and
15.3977 seconds of build time. The owned Editor exited before any subsequent
native work; this build did not launch a benchmark Player.

Before/after SHA-256 checks verified the original built catalog and all 10,039
Addressables bundles, including the complete path inventory. The original
`catalog.bin` remains
`56b7e281f8171c55b5f6049b7b960fd31b6a90a86370c4c08f48053b26b07828`.
The 10,038-tile private catalog and complete 260-group v5 sidecar were retained;
the sidecar remains
`bf64909d83f2919896c15e25f4a6dbf8bf870179561972835e6a0e6cf82ae661`.
No original city asset or bundle was rebuilt or copied into the public project.

The new managed `Manhattan.Viewer.Runtime.dll` SHA-256 is
`a4059318a254e0db31b1f432daf75a13abf854f59b1861e9d9e5276ef567bb88`.
The 37-entry source/resource fingerprint receipt is
`f79dc2e4fa6567fb5b189d8b2398e09815b9dee75273b412c923a32948b2bef8`.
All 67 existing Notices manifest entries verified against their byte counts and
hashes, preserving the original license and attribution texts. Those copied
Notices still record technology `960d5de`; their historical metadata is a
diagnostic provenance limitation and does not constitute a fresh release
manifest. The build identity and runtime fingerprint receipt record the current
viewer and technology revisions separately.

This receipt establishes a successful build and its input identity. CPU/GPU
timings, delivered FPS, source-pilot publication, visual and temporal acceptance
for this new build remain unmeasured. The preceding `9deb78f` timing and moving
records retain their original source scope.

## Fresh 413 scoped regional CPU/GPU measurements

Four later, separate 90-second runs add timing evidence to the build-only receipt
above. They used the same `FullCityVerifiedPlayer` binary in this order: Midtown
uninstrumented (`eb5b417a`), Midtown timing (`32c42ade`), Park uninstrumented
(`2531289b`), and Park timing (`0341cca8`). Each owned Player and its collectors
exited before the next launch. The actual embedded `4133070`/`2a33e1c` identity,
the exact `a4059318` managed DLL and `f79dc2e4` source/resource receipt were
cross-checked separately for every run. The catalog, 260-group v5 sidecar,
immutable city bundles and physical calibration source were retained.

Actual measured rendering was 1920×1200 Windowed, TAA, VSync 1 and target-frame-rate
setting 60. No tracker, video, counter probe or collision proxies were enabled.
Warmup was configured for 15 seconds; a separate rendered-warmup trace was not
recorded. Each route completed two fully measured circuits with full recorded
route coverage. Median position-derived speed was within 0.0001m/s of 100m/s.
The GPU was an RTX 4070 Laptop GPU and CPU a Core Ultra 9 185H.

All four strict engine-frame P95 checks **failed the 16.7ms criterion**. Both Park
P99 checks **failed the 25ms criterion**. Those results are not rounded into a
pass. This is a private synthetic/no-tracker diagnostic, without general FPS,
off-axis, webcam, visual, temporal, 20-minute memory or release qualification.

All timing cells below are **P95/P99/maximum in milliseconds**. Percentiles use
nearest rank `ceil(p*n)-1`; raw engine/stream CSV values have four decimal places.
The active-work subset means `pending > 0` or `streaming_ms >= 0.5`, not a causal
assignment of a slow frame to streaming.

| Run | Scope / samples | Engine frame ms | Streaming work ms | ≥50ms engine / stream |
|---|---:|---|---|---:|
| Midtown uninstrumented | Whole / 5400 | 16.7263/16.7865/33.3354 | 1.7903/2.2288/5.1230 | 0 / 0 |
| Midtown uninstrumented | Active work / 2706 | 16.7300/16.7958/33.3354 | 2.0505/2.4633/5.1230 | 0 / 0 |
| Midtown timing | Whole / 5407 | 16.7291/16.8036/33.3198 | 1.6599/2.1484/4.2601 | 0 / 0 |
| Midtown timing | Active work / 2635 | 16.7329/16.8063/33.3198 | 1.8947/2.3493/4.2601 | 0 / 0 |
| Park uninstrumented | Whole / 5519 | 16.7795/33.3203/33.8243 | 1.6218/2.1067/4.4695 | 0 / 0 |
| Park uninstrumented | Active work / 2334 | 16.7955/33.3298/33.8243 | 1.9026/2.4210/4.4695 | 0 / 0 |
| Park timing | Whole / 5479 | 16.8134/30.9496/33.4917 | 1.7133/2.2454/4.5672 | 0 / 0 |
| Park timing | Active work / 2417 | 16.8664/33.2086/33.4917 | 2.0229/2.5633/4.5672 | 0 / 0 |

Loaded tile ranges were 426–449, 426–450, 368–441 and 368–442 respectively;
pending loads were 0–2 and tile-load failures were zero in all four runs.

The two timing runs had 5,406 and 5,478 measurement-phase receipts respectively.
Their nonzero CPU frame-start timestamps were unique: zero missing timestamp/sample
rows and zero duplicates. FrameTimingManager was enabled in every such receipt;
CPU/GPU timer frequencies were 10MHz/1GHz. Only finite positive values contribute
to the following FTM statistics. CPU fields were available in all deduplicated
samples. GPU time was zero/unavailable in 6 Midtown and 92 Park samples, excluded
rather than counted as a measured zero. Active-work GPU exclusions were 2 and 40.

| Timing run | Scope | Metric | Valid samples | P95/P99/max ms | Available samples ≥50ms |
|---|---|---|---:|---|---:|
| Midtown | Whole | CPU frame | 5406 | 16.7341/16.8096/33.3498 | 0 |
| Midtown | Whole | GPU frame | 5400 | 5.2204/5.6576/6.7564 | 0 |
| Midtown | Whole | CPU main | 5406 | 3.0583/3.7392/14.7485 | 0 |
| Midtown | Whole | CPU render | 5406 | 0.9443/2.1265/7.0779 | 0 |
| Midtown | Whole | Present wait | 5406 | 15.6258/15.7300/30.6681 | 0 |
| Midtown | Active work | CPU frame | 2634 | 16.7335/16.8120/29.4764 | 0 |
| Midtown | Active work | GPU frame | 2632 | 5.2664/5.8061/6.7564 | 0 |
| Midtown | Active work | CPU main | 2634 | 3.0334/3.6508/14.7485 | 0 |
| Midtown | Active work | CPU render | 2634 | 0.9521/1.9323/5.4176 | 0 |
| Midtown | Active work | Present wait | 2634 | 15.6344/15.7475/27.3797 | 0 |
| Park | Whole | CPU frame | 5478 | 16.8233/30.9609/33.5081 | 0 |
| Park | Whole | GPU frame | 5386 | 5.2265/6.9898/11.1002 | 0 |
| Park | Whole | CPU main | 5478 | 3.3283/4.5770/8.0234 | 0 |
| Park | Whole | CPU render | 5478 | 1.0237/2.4398/8.6751 | 0 |
| Park | Whole | Present wait | 5478 | 15.6404/29.3627/32.4197 | 0 |
| Park | Active work | CPU frame | 2416 | 16.8353/31.5825/33.5081 | 0 |
| Park | Active work | GPU frame | 2376 | 5.9679/7.2950/9.7024 | 0 |
| Park | Active work | CPU main | 2416 | 3.5226/4.5696/7.8827 | 0 |
| Park | Active work | CPU render | 2416 | 1.0903/2.1337/6.5220 | 0 |
| Park | Active work | Present wait | 2416 | 15.6486/29.8933/32.4197 | 0 |

FTM reports the asynchronous latest rendered-frame sample. The active-work
classification uses its first measurement-phase receipt and can lag the timed
frame. CPU frame, CPU main, CPU render, present wait and GPU time are distinct
observations. These sequential timing runs have instrumentation overhead and
one run per condition; changes between them do not isolate that overhead or a
performance cause. The available GPU maxima do not bound the unavailable samples.

Ownership diagnostics use current receipt-frame counters, retaining legitimate
zero-work samples. The arithmetic remainder is
`max(0, streaming_ms - ownership_tick_ms)`. It includes work before ownership Tick
and visibility work after publication, so it is not a purely pre-Tick timing or
a causal explanation. No negative unclamped remainders were observed. Ownership
was pending in 1,146 Midtown and 1,253 Park measurement receipts. These counters
were not recorded in the uninstrumented runs.

| Timing run | Receipt scope / samples | Ownership Tick P95/P99/max ms | Work excluding Tick P95/P99/max ms | ≥50ms tick / remainder |
|---|---:|---|---|---:|
| Midtown | Whole / 5406 | 1.1712/1.4828/3.8957 | 1.5111/2.0228/4.1477 | 0 / 0 |
| Midtown | Active work / 2634 | 1.3036/1.6684/3.8957 | 1.7514/2.1959/4.1477 | 0 / 0 |
| Park | Whole / 5478 | 1.2031/1.6555/3.5217 | 1.5013/2.0768/4.5672 | 0 / 0 |
| Park | Active work / 2416 | 1.4320/2.0968/3.5217 | 1.8448/2.2424/4.5672 | 0 / 0 |

Park recorded more engine updates than a nominal 60Hz count. The short intervals
below account for part of that trace shape, without establishing a presentation
rate or a cause. City-space routes remained at y=1.85m in Midtown and y=20m in
Park; route-cycle counters ranged 0–3 in every run. Floating-origin offsets,
foreground focus and individual frame UTC timestamps were not recorded. UTC
measurement starts are derived from report-end UTC minus measured engine time.

| Run | Frame rows | <8ms / <1ms / ≥25ms | Sum of engine deltas (s) | Recorded time span (s) | Reported measurement (s) |
|---|---:|---:|---:|---:|---:|
| Midtown uninstrumented | 5400 | 0 / 0 / 1 | 90.015795 | 89.999086 | 90.015404 |
| Midtown timing | 5407 | 4 / 0 / 3 | 90.016359 | 89.999667 | 90.011604 |
| Park uninstrumented | 5519 | 118 / 2 / 143 | 90.006993 | 89.998672 | 90.004616 |
| Park timing | 5479 | 91 / 0 / 84 | 90.015764 | 89.999119 | 90.009087 |

Time-aligned Windows memory samples were pinned to each exact Player PID. Driver
memory is for the whole selected GPU, including other applications; the Unity
graphics-allocation counter was unavailable in all four runs. Peaks below are
GiB. Trend deltas cover roughly 78 seconds after the initial circuit window and
cannot establish 20-minute memory stability.

| Run | Player working set / private commit peak GiB | Whole GPU peak GiB | Working set / private trend Δ MiB |
|---|---|---:|---|
| Midtown uninstrumented | 2.1135 / 3.9780 | 1.7715 | +46.92 / +27.43 |
| Midtown timing | 2.1164 / 3.9825 | 1.7695 | +57.10 / +38.26 |
| Park uninstrumented | 2.0453 / 3.8373 | 1.7207 | +30.72 / +23.23 |
| Park timing | 2.0612 / 3.8493 | 1.7207 | +40.33 / +34.44 |

Raw CSVs, environment snapshots, exact input hashes, process lifecycle receipts
and guarded analyses remain private. The root benchmark wrapper and analyzer
were retained unchanged. These new measurements belong to this exact current
runtime/v5 build; the earlier `9deb78f` media and performance records keep their
original scope.
