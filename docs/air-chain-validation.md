# Airborne chain and crossroad replay

This is a scripted input/PhysX regression, not a trained policy or a manual ease/comfort review. It exercises the shipping `WebSwingMotionInput`, buffered action interface, rope routing, collision sweeps and resident footprint recovery. Every case receives one initial airborne pose at eye Y45m and forward speed 36..40m/s. No subsequent position/velocity reset, floor rescue, collision suppression or gravity override is allowed.

## Declared matrix

- Straight chains: each length 2 through 10.
- Right turns: immediate turns with one and with two swings, then each N-before + N-after pair for N=2..10 (including 10+10).
- Controlled crossing: 30/44/60m road widths, finite 200m-high solid blocks, 60m nominal link spacing, 30/60/120fps, five seeds. Each condition has 900 cases (20 stages x3 widths x3 frame rates x5 seeds).
- Additional Manhattan diagnostic: catalog footprints/heights rigidly relocated from intersection city X1188.7766/Z3518.1879, incoming yaw28.46516 degrees; 50m nominal link spacing, three seeds and the same three frame rates/stages. Each condition has 180 cases (20 stages x3 frame rates x3 seeds). The fixture applies the same rotated development-region bounds as the new player before selecting its nearby buildings. These extruded sidewalls plus resident solids are not the streamed rendered triangle meshes.
- Both conditions use the same pilot code, seeds, geometry and flow features; only `chain.enabled` differs. A closed-loop pilot may produce different controls after the physics diverges. This is not an identical open-loop input tape.
- Seeded aim error is +/-3 degrees, release timing error +/-80ms and inter-shot coast 120..200ms. The pilot uses W/lane correction, look yaw up to120 degrees/s, a 0.12s RMB burst on every attachment, extended up to0.45s while low, LMB hold/release and actual visible facade rays. Catalog aiming scans real visible surfaces; it is test code, not an unrestricted game auto-targeter.

Each case fails on any swept/resident contact, grounded step, wall-running step, feet height <=0.05m, overlap with a building volume at player height, missing attachment, too-short attached duration, or incomplete right turn. Controlled cases also fail if they fly above the 200m blocks. Separate release/coast periods and at least0.3s actual attached duration prevent counting instant shots as swings. Each link must also remain taut for at least0.05s. A floor-touch counter before rope correction rejects contacts that a later correction could conceal; a reset counter must remain exactly one. A final released flight of at least0.35s is included; the catalog pilot can continue to1.2s while settling its turn, without another shot. The catalog pilot may release/refire earlier when a swept ballistic/rope forecast sees a wall, floor or roof volume. Those are ordinary test-input actions, not game collision suppression or automatic gameplay release; their counts are logged. Counts alone do not certify a valid trajectory.

## Reproduction

Run the repository DataOnly/EditMode/GPU PlayMode checks using `tools/test.ps1 -EditorPlayMode`. `AirChainReplayTests` asserts the controlled ON matrix. `ManhattanAirborneChainsAndRightTurns` records all city successes **and failures** and asserts every ON case. Both controlled and city matrices must pass.

After a clean Windows development build, run these separately using disposable calibration and the pinned launcher:

```powershell
# Existing movement, wall and device regressions
ManhattanViewer.exe --no-tracker -batchmode -nographics --validate-traversal --traversal-report C:\Reports\traversal.json
# Full OFF/ON air-chain collection
ManhattanViewer.exe --no-tracker -batchmode -nographics --validate-airchain --airchain-report C:\Reports\AirChain
```

The air-chain collector writes `validation.json`, `cases.csv`, and representative full `trace.csv` paths and the final two seconds of every failed run in `failure-trace.csv`. `completed`, `controlledRegressionPassed`, and `allRoutesPassed` are separate fields. Its process exit code requires completed collection and every assisted controlled/city case to pass. `allRoutesPassed` describes the ON conditions; OFF failures remain part of the comparison. Repeat from an independently extracted archive and compare per-case metrics. Preserve failed experiments alongside the final report.

## Measurements and limits

Final measured results and clean runtime revision are recorded in the delivery follow-up. Powered winch lift, automatic initial take-up and catalog-pilot velocity damping worsened outcomes and were removed. A standalone released-bank trial also worsened contacts; the released bank was reconsidered only after fixing slack steering, conservative previews and inward braking. All earlier reports are retained. The retained early slack take-up requires deliberate RMB/E input and adds no lift velocity. The zero-gravity fixture checks a120-degree/s horizontal bank without reducing horizontal40m/s or changing vertical7m/s; quarter input/assist scales the bank and zero disables it. First airborne physical attachment is bounded to five1/120s steps, regardless of shot range25/100/200m. Release preserves the measured velocity.

The controlled map establishes reproducible collision-free chains under the declared inputs. The catalog diagnostic is additional coverage, not proof for every Manhattan location. Route/pilot iterations are development experiments rather than held-out policy evaluation. The new development cross follows the real road yaw28.46516; legacy axis-aligned builds remain separate. Motion-sample CPU time excludes rendering, tracking, input acquisition and test-pilot/catalog scanning; it is not frame time or an FPS claim. Moving head/eye/wrist hardware, five-minute independent human feel review, and ease of reproducing ten swings by a person remain untested. Historical negative/unconverged PPO results remain unchanged.


## Frozen pre-build verification (2026-10-18)

DataOnly:170 passed, two skipped, zero failed. Unity:714 EditMode and184 GPU PlayMode passed. The final ON matrices below are assertions, including the Manhattan routes; earlier diagnostic-only reports remain experiments.

| Shared pilot / strict criteria | OFF valid routes | ON valid routes |
|---|---:|---:|
| Controlled crossings, 20 stages x3 widths x3 frame rates x5 seeds |170/900|900/900|
| Manhattan development footprints, 20 stages x3 frame rates x3 seeds |16/180|180/180|

All1,080 ON cases have zero collision/grounded/wall-running/floor-touch counts, exactly one initialization, complete attachment/release counts, at least0.3s attachment and0.05s taut duration per link. Failed OFF routes remain recorded rather than being retried until they pass. These are closed-loop scripted cases, not human success rates or held-out generalization.

A separate zero-gravity forward-input fixture changes vertical velocity from-21.49517m/s to0, lateral velocity from-1.79127m/s to0 after1.5s, while keeping forward40m/s. Close-anchor manual reeling changes unintended lift from0.93196m/s to0. Three actual adapter/PhysX approaches at30/60/120fps change from one contact to zero, with2.01551m minimum wall clearance and44.69403m/s final forward speed. Turning previews now include the segment's incoming velocity and the physical sweep, and fallback steering cannot restore the inward component after braking.

The catalog pilot updates look and movement consistently through release, avoids unsafe close/rear anchors, and can anticipate a constrained rope arc. It uses ordinary inputs and never changes an in-flight pose/velocity directly. Catalog roof overflight is permitted; landing on a roof is a contact and fails. Controlled blocks explicitly prohibit above-roof escape. The minimum28m corridor used for catalog lane targets is not a claim that every Manhattan street has that width. Local reports retain earlier naive pilots, failed powered-lift/steering experiments and stricter-criteria revisions. Windows and archive results are recorded after building from the clean source revision.
