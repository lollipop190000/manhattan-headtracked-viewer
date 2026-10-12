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
