# Bilateral charged launch and surface-rope validation

This is an experimental follow-up on PR16. The preserved chain/continuity/full-city players remain separate. No tracked-display package, Unity version, main scene, full city inputs or personal calibration is replaced.

## Controls and behavior

Click the window once to capture input. Hold **Q** when buildings are visible on both sides. Two actual surface hits are required. The two procedural hands clench as charge rises; a small render-scoped tremor grows then settles near full charge. Release Q to launch. Hold LMB after launch to enter web swing; its existing hold/release behavior remains. RMB/E reels, **Z now pays out**, Space jumps/releases/jumps off walls, R toggles flight. Free-flight Q/E elevation is retained. The header, traversal overlay, README and control table agree.

Default full charge is1.35s; launch speed is24..82m/s, with40degree elevation. The two anchors determine horizontal direction; sight can refine it by a bounded amount. Release checks swept ballistic capsules and chooses an unobstructed nearby direction or rejects the launch. Charging does not move the body, shorten a body constraint or turn gravity off. It stores energy for release. Faster incoming inertia is retained. Inspector settings expose enable, charge time, speed, direction weighting, preview and charge feedback; `--launch-off` and `--no-charge-shake` provide comparisons.

## Geometry and continuity

The former world-bounds corner graph placed a knot5.936839m from a rotated facade in the captured reproduction. The replacement follows actual box/sharp mesh edges, with a15mm surface offset, bounded graph and visibility-based unwrapping. It caches mesh topology and preserves city coordinates across rebases. Unsupported geometry safely releases instead of inventing an AABB bend. The visual line and physics use the same route points. An independent rotated-box volume test then caught single-sided mesh exits that forward-only rays missed (1.69214034m interior depth). Scoped backface queries with a1mm intermediate-node tolerance reduce the measured interior depth to0m and restore the previous global query flag after each call; real surface attachment points retain their separate6cm numerical margin; measured mesh knot gap is0.01060689m.

A short wrapped radius is paid out conservatively. Assisted predicted rope-wall contact releases before penetration and retains tangent inertia. Excessive positional rope projection detaches and integrates that substep from the prior velocity. A deliberately shortened attachment reproduced89.56494m of frame travel without the guard versus0.07836914m with it. This is a specific reproduction, not proof that every reported intermittent screen jump had this cause.

URP's baseBlendFactor is the previous-frame weight:0.95 was a95% history contribution. During body/head translation, head rotation, charge and recent wall contact, the new component reduces it toward0.55 while preserving AA preference and stationary behavior. The theoretical history half-life changes from13.5134 to1.1594 frames (at60fps,225.2ms to19.3ms). These are filter-setting calculations, not measured input latency or a pixel-quality score. The user's exact half-second rollback has not been reproduced as a recorded live-head event. A240-frame allocation-free ring saves a CSV anomaly tail under persistentDataPath/Continuity if a pose jump/reset/constraint snap guard occurs; it never replays positions into motion.

## Replays and interpretation

The surface-v2 closed-loop pilot uses ordinary movement/look/fire/reel actions. It initializes once; ground cases charge/release Q through the same adapter. No pose or velocity rescue is allowed after initialization. The pilot chooses visible anchors, predicts hazards and plans the turn lead across preceding links. New catalog left-turn cases, and right-turn anchors rejected by a copied-controller hold check, additionally choose real surface hits and0.12/0.45s reel inputs using copied-controller0.5s hold/0.2s coast previews (surface-v2+controller-preview). The actual run receives only input actions; a copy-isolation/equivalence check guards against state rescue. This is a planning pilot, not evidence of human ease. It differs from the archived legacy-v1 pilot, which aimed at old bounding-box support points. Legacy-v1 remains available explicitly; failed intermediate reports are preserved. Do not interpret the new success counts versus archived v1 counts as a physics-only causal comparison.

The new matrix contains1080 controlled outcomes:30/44/60m roads 횞30/60/120fps 횞 ground/air starts 횞20 routes 횞3 seeds. It includes2/3/5/10 straight links; immediate and3/5/10-link approaches to both turn signs; and left-right/right-left sequences with one or three links between turns, including three prior straight swings. Alternating routes use separate grid crossings100m or180m apart. The72 catalog cases use968 rigidly relocated Manhattan footprints and the actual movement/PhysX path, with both turn signs and fixed seeds. Catalog cases retain airborne initialization; the rendered QA separately checks a ground launch in streamed Manhattan. Ground-route traces/counters begin after the initial0.3s coast. That first flight is covered separately by the1s launch/no-contact checks, conservative0.65s capsule preview and streamed-city rendered check (contacts counted from release).

Every passing route requires zero floor/wall/roof contacts, zero grounded/wall-running steps, one initialization, route progress of at least60% of planned distance (and clearance past the intersection), a distinct release between links, minimum0.3s attachment and0.05s geometrically taut duration per link, no reset and final heading within15degrees after0.35..2s of final detached flight. The catalog preview scores every candidate for forward progress instead of accepting the first safe candidate; an in-place looping intermediate is retained as a failed result. The old2160-condition OFF/ON matrix is also collected using the explicitly named v2 pilot. Snag/stuck PPO rewards and archived nonconverged policies are unchanged. No policy was retrained to certify this feature.

## Final measured evidence

Clean runtime **a4b9db0c59ce2b687f52880ce0d08199c4814132**, following feature commit **88a846b**. The final documentation child does not change runtime. Local receipt: `outputs/traversal-slingshot-build-verification.json`; raw reports, traces, plots and inspected images: `outputs/TraversalSlingshotReview/`. Failed intermediates remain in its `Experiments/` directory, including the overly strict endpoint-margin regression and the1.69m mesh-interior reproduction.

| Check | Measured result |
|---|---:|
| DataOnly |170 passed /2 skipped|
| Unity EditMode / GPU PlayMode |1084 /184 passed|
| Windows existing / new launch assertions |116 /104 passed|
| Added controlled ground/air routes |1080/1080|
| Added catalog left/right routes |72/72|
| Existing assisted matrix controlled / catalog |900/900 /180/180|
| Same current pilot, chain group OFF controlled / catalog |368/900 /28/180|
| Editor/Windows matching outcomes |3312/3312|
| Original/extracted archive identical non-timing case metrics |3312/3312|

All1152 added outcomes have zero recorded floor/wall/roof contacts and grounded/wall-running steps, one initialization, complete shot/attachment/release counts and progress/heading/duration gates. Their minimum feet height is10.07704m, minimum wall clearance1.78594m, peak95.00001m/s, maximum frame travel3.27126m (30Hz input) and maximum physics-substep travel0.82316m. Every ground charge remained stationary and released at82m/s. Initial launch flight is separately measured as described above. The OFF comparison uses the same current pilot code and seeds; it is distinct from the archived legacy-v1 baseline and is not a comparison of every new structural fix OFF/ON.

The actual streamed-city rendered check shows two hands/two lines,0m charge displacement,82m/s launch and one next-web attachment, with zero contacts/floor touches/resets counted from release. Tremor amplitude rises to0.00105507m then settles to0.000144m. Maximum successive pose travel is1.36352m at the scripted60Hz cadence. Eight exported captures were inspected. This reuses the existing procedural glove model; it is not a new realistic anatomy/model asset.

The separate sequential rendering pair used the preserved chain player and new player,1920x1200 windowed,5m/s,15s warm-up plus60s measured, within the shared450m circle. Frame p95 is16.68317/16.68308ms, p99 is16.68347/16.68347ms; streaming failures0/0. Both performance target flags remain false. VSync is enabled. This pair bypasses active webs/collisions/tracking; it is not an active-swing FPS, latency or comfort result.

All38 profile input states restored,657/10038 development tiles retained, unchanged main scene/technology pin and77 notices verified. The ZIP has938 files,195787659bytes, SHA256 `e7f4e381496115c7c75ec7fe108b6e6a6c2becf764ee909baa8c63c2ea25c014`. Every hash was checked before separate extraction; extracted native suites also passed. Players, archives, raw logs/CSVs and calibration remain outside Git.

Independent five-minute human feel review, moving head/eye tracking comfort, live wrist hardware and full streamed-mesh coverage of every route remain pending. These are automated test-pilot results, not a claim of universal manual ease.
