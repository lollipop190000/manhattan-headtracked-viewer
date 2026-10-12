# Controlled native source replacement validation

On 2026-10-12, one explicit private Windows Player pilot for source `282030_0`
passed all 18 recorded assertions, completed in 249.699 seconds and exited with
code 0. The owned Player was confirmed absent afterward. All nine native images
were inspected: the selected building and its neighbors were visible, Surface
OFF/ON changed and restored detail, and rollback restored the legacy scene.

This validates the public gate/loader in an isolated fixture with an exclusive
write lease. It supplies no production glTF backend, live CityStreamer
coordinator, normal-startup activation or approved model profile. All
`runtimeReady`, model-delivery, optical-equivalence, performance and release
acceptance fields remain false. See the [import foundation](model-import-foundation.md)
for the remaining production obligations.

## Frozen execution identity

The public foundation was copied byte-for-byte from committed viewer revision
`4133070f6dd290da01898cb29fd8fa34b7f507e9`, with no uncommitted foundation source.
The private project copied only these five runtime files and their matching
metas; it had no normal bootstrap or CityCatalog asset. This execution does not
transfer earlier streaming or headtracking measurements to that revision.

| Public runtime source | SHA-256 |
|---|---|
| `CityCatalog.cs` | `9c5f06cf57b6f7129b389f281eeb9d462b9e03d65e108ceee58f747761bb0234` |
| `SkylineOwnershipData.cs` | `bb899e3b94f8f3d07c869f6da2b98f2509d8bb7a09c6a17b010ed75805929744` |
| `SurfaceDetailComparison.cs` | `d2b783062fcb0cfd284b729bb48eda95a9f63415dec25d707f6a9b6e7d8eb90e` |
| `SourceReplacementGate.cs` | `741da8d2258b74d752a6d18c362725f6e692c216698774105c8e246011b4e4a6` |
| `SourceReplacementLoader.cs` | `00ab7b6b8b5a51f7f6d48f276e3935455dd632a7834714d423c3b66663d1ffed` |

| Frozen helper or evidence | SHA-256 |
|---|---|
| Private legacy fixture | `28dd2c854191a4d2f8d4ee22525da3048ada7cde39dc9b99074ef012e1cc691f` |
| Private native provider | `c5313db928f6daed13269534dd3ca319220c153e086df38fd66c28654a71785a` |
| Private phase controller | `96923375d17b98b15bce0acd4cb769003b7d445f3a7b34c3e9d07f4dda8ec88f` |
| Private Editor builder | `5bc3568d75800041fee21a20ae36f615497732a1380d01217dce04b55e9dcfb4` |
| Exact public `PlayerBuildDispatch.cs` copy | `c16466df016e523dad43040734ae02e99dfcab68559c8b96f90101c779842809` |
| Original grouped H1 GLB | `5bf356dc53151589a13eda2478c582345f11a162b38158de99b31736b44b4872` |
| Source-specific mapping v2 | `ddf50c1a5c367c42b2a1402d8a495cdb6124eaa451f9bcf49fae4c12335b2647` |
| Pinned source optical metadata | `b76af287cda94e567ea993060beb656e2a7d219156fb3a23086f7f5fdb315265` |
| Player executable | `8da50e79b68e979c53cafb639961ca6c34d39bfed8e87b91cf9caf5c12c081fe` |
| Player `Assembly-CSharp.dll` | `5f3f70d032d9fd7446bb014ae6e3e9de7934d556540b2779e20e5b416ad3768a` |
| Final runtime receipt | `a83fe01e280c3f3734733f6fbc13e9165e01981cb7b15445450dcd7fd2f52eb2` |
| Separate actual-PNG inspection receipt | `500c7160c60b606af4618f354913e1aa102ceb1d65e78d9a7484f8ea14d818e6` |

Build ID: `c0a695fcedbe4bf3a845eaa9702622ef`. Runtime ID:
`477810911ce645ee8591c309b19c6962`. The build succeeded with zero errors and eight
warnings, retaining 62 ON/OFF shader templates for 31 actual imported materials.
Its output was 134,568,266 bytes and the build took 12.3445 seconds. The executable
hash alone does not identify managed code: Unity's launcher can have identical
bytes across different builds. The managed DLL, copied source/helper fingerprints,
resource receipts and build/run identities must be considered together.
The Player used Unity 6000.3.22f1, URP 17.3.0, glTFast 6.20.0 and Direct3D11,
with strict shader variant matching. Addressables was pinned to 2.7.6.

Original model/dependency bytes, notices, bundles, raw receipts and PNGs remain
private and unchanged. The 22 staged H1 files were rehashed against the separately
pinned input table. Mapping v2 corrected diagnostic material names from typed
material records and preserves the original v1 artifact and its provenance.
No source model, legacy bundle, texture asset or personal calibration was edited.

## Fixture, geometry and material evidence

The fixture loaded the exact original Core, Near and Skyline prefabs through a
read-only catalog redirect. Catalog/bundle bytes, raw mesh buffers, layout,
material assignments, texture bytes and transforms were independently verified.
The source-specific mapping identifies seven filters and exactly 492 old
triangles: 34 Core body, 10 Skyline body and 448 additive Near trim. Each private
mask retained every other index in its original order and preserved vertices,
normals, tangents, all UV channels, bounds and layout. All 19 unmapped neighboring
filters retained their imported pointers and geometry hashes; original legacy
and neighboring materials also retained their fingerprints. No whole tile was
hidden and no CityStreamer or competing ownership writer ran in the fixture.

All six selected native meshes, totaling 6,068 triangles and 31 distinct material
bindings, were checked against the independently hash-pinned GLB BIN data.
Maximum measured position, normal and UV errors were zero in all three imports.
Ancestry, metre units, source placement and exactly one handedness conversion
were verified. Tangents followed the stock source requirement: five normal-mapped
meshes had generated tangents; the 48-vertex Service mesh had no authored TANGENT
or normal texture and correctly had zero tangents. No recalculation was forced.
The legacy body base is 0.22 m and the candidate base is 0 m; ground metadata
was retained without adding a height shift.

The pinned GLB contains one sampler with magFilter 9729, minFilter 9987 and
omitted wrap fields whose schema default is Repeat. All 16 imported texture
records initially had Bilinear filtering. The private backend validated every
texture/sampler alias before applying the typed source sampler to its owned
runtime textures. All 19 material-bound slots then used Trilinear/Repeat in all
three imports. Raw texture hashes/lengths, format, dimensions, gamma/color space,
readability, mip count, anisotropy and wrapW remained unchanged. Material factors,
alpha, optics, emission, exact texture pointers and UV scale/offset remained
unchanged. The 19 slots comprise five sRGB base-color, nine linear normal and
five linear metallic/roughness bindings. No authored AO, emissive or transmission
texture map was present, so those map channels were not exercised.

All 31 stock shaders were supported. Ordinary alpha, metallic, roughness,
normal-scale and sidedness values matched source semantics. Stock base RGB uses
linear-to-gamma conversion rather than numerical equality with source linear
RGB. Three authored emissive factors were retained with `_EMISSIVE`; this source
had no `KHR_materials_emissive_strength`. Both glass materials retain source
transmission 1, alpha 1 and IOR approximately 1.48 in evidence. Their native
path uses `_TRANSMISSION`, transparent surface/queue 3000 and double-sided culling,
while source OPAQUE alpha mode remains metadata. The shader has no IOR property:
`shaderIORRendered` is false and the unsupported-extension warning is retained.
Opaque screen-color transmission is a qualified approximation, with no faithful
refraction, volume or overlapping-transparent-layer equivalence claimed.

Each import reproduced these measured hashes, pinned after sampler application:

| Actual Player evidence | SHA-256 |
|---|---|
| Native geometry | `956f6cbe3288085abda5bb22c5e1973286d2f00794700cde9c5a0c3252f15363` |
| Original native materials | `2131f8a03ed00467933c56fa74edf7c101d18c0040d7817609af42df9f48b6b4` |
| Qualified measured profile | `21172b1e46a31fb9e6d1c40ceca451f40d3b839b4be68f56221bcb9fd060fe2f` |

## Executed controls and assertions

The opt-in private controller required `--source-unit-pilot`, `--no-tracker` and
a fresh explicit output directory. It configured one untracked perspective
camera and fixed light/quality state before the baseline. The initial pose was
8 m outside the source's street edge, 1 m laterally displaced, eye height 6 m and
vertical FOV 84 degrees. It was outside all 44,567 original footprints; five CPU
first-hit rays at different facade heights reached only mapped source faces.
CPU rays and renderer/frustum flags are supporting evidence, not pixel proofs.
The private baseline used AA None, VSync 0, no postprocessing, HDR, MSAA, shadows,
fog or skybox; near/far were 0.1/600 m. Linear color and full-resolution opaque
texture sampling were enabled. These settings were fixed before the first capture
and were not changed by a comparison switch or transferred to normal startup.

The imported candidate remained hidden while its original source data and actual
profile were verified. A closed private offer kept the asynchronous load pending
through the fallback capture and preservation checks. The first permit was
released only after those checks, at 67.497 seconds. Fresh reentry cancelled a
second offer before any permit, awaited its completion and disposed the inactive
caller-owned candidate. A third prepared provider explicitly released its permit
and reproduced publication. This proves the controlled prewarmed offer lifecycle;
it does not prove cancellation during cold native import.

All 18 receipt assertions passed, in recorded order. Repeated geometry and
rollback assertions belong to separate entries, rather than additional sources.

| # | Recorded assertion |
|---|---|
| 1 | One private perspective camera and one fixed lighting/quality baseline were configured before every capture |
| 2 | Initial street camera is outside every original footprint and five actual legacy-buffer rays reach only mapped source faces |
| 3 | All 6068 actual native triangles, normals, UVs, parent/source poses and handedness independently match the hash-pinned original source BIN |
| 4 | Pending retained exact baseline pixels |
| 5 | Prewarmed pending load retained source body, additive Near trim, all neighbors and inactive candidate |
| 6 | Indirect native visibility: source renderers intersect the fixed camera and publication changes the actual backbuffer |
| 7 | Actual native flat variants remove detail maps and retain coverage/transmission/base RGBA/UV/blend/depth state |
| 8 | Surface ON native pixels restored |
| 9 | Common-frame translation preserved native pixels |
| 10 | Common-frame translation restored |
| 11 | Rollback acknowledged exact fallback body/trim and restored source pointers before provider release |
| 12 | Rollback native baseline pixels restored |
| 13 | All 6068 actual native triangles, normals, UVs, parent/source poses and handedness independently match the hash-pinned original source BIN |
| 14 | Prewarmed pending cancellation retained fallback; cold native import cancellation remains unproved |
| 15 | All 6068 actual native triangles, normals, UVs, parent/source poses and handedness independently match the hash-pinned original source BIN |
| 16 | Fresh native reentry reproduced fixed-scene pixels |
| 17 | Rollback acknowledged exact fallback body/trim and restored source pointers before provider release |
| 18 | Fresh real legacy fixture and new native providers reproduced publication, cancellation, rollback and complete scoped cleanup |

The assertion 4 label says “exact,” but its actual `exactDecodedPixels` result
is false: every pixel comparison below is tolerance-based. Native captures were
1920x1200, with 2,304,000 decoded pixels each. All nine passed the nonblack,
nonuniform and zero-magenta guards, and all 17 referenced artifact/PNG hashes
matched their recorded values. Their camera, projection, dimensions, lighting
and quality hash was identical throughout:
`ad5fe6231d5e3cc8a1146abe9c29d5cab612a381138da8b873bbb62975aefdeb`.

| Pixel comparison | Mean absolute RGB byte error | Pixels above 8 byte levels | Fraction above 8 | Maximum RGB byte difference |
|---|---:|---:|---:|---:|
| Baseline -> pending fallback | 0.06330556 | 0 | 0 | 8 |
| Native ON -> restored ON | 0.08854847 | 19 | 0.0000082465 | 16 |
| Restored ON -> translated common frame | 0.09721557 | 305 | 0.0001323785 | 100 |
| Restored ON -> restored common frame | 0.08941117 | 14 | 0.0000060764 | 10 |
| Baseline -> rollback baseline | 0.06430787 | 0 | 0 | 8 |
| First native ON -> fresh native publication | 0.08971962 | 23 | 0.0000099826 | 16 |

The declared stability limits were mean error <=0.25 RGB byte and fraction above
8 byte levels <=0.001. Publication changed 612,242 pixels above eight levels;
that global difference plus six frustum-visible source renderers is indirect
visibility evidence, not proof of each material's pixel coverage. Surface OFF
removed 33 renderer detail-texture slots while preserving two transmission slots;
ON restored the exact original material pointers. Geometry, all masks, imported
materials and neighbor buffers passed guards in ON/OFF/ON and fresh publication.

An explicit common-frame translation of (32, 0, -16) m moved the scene and rig
together, followed by restoration. Content and rig local frames remained fixed;
no comparison switch changed camera pose, transforms, scene, lighting or quality.
Both rollbacks restored exact legacy body/trim pointers before provider release.
Loader, provider and fixture were then disposed in that order. Both cleanup
receipts verified the original ordered locator/provider references, retained the
legitimate initialized Addressables startup baseline and restored the previous
internal-ID transform. Final runtime material variant count was zero.

## Retained failures and remaining limits

Earlier raw evidence is retained and excluded from the final pass:

- A hidden native window produced a black baseline; those pixels supported no
  scene claim. The final run used a visible window and explicit frame guards.
- The former 20 m camera pose was inside neighboring source `4939_0`, below its
  roof, and showed opposite walls. The final pose was established before every
  comparison using footprint exclusion and actual loaded-mesh ray evidence.
- A private stopwatch defer agent stalled stock image import because its budget
  renewed only on a breakpoint while that path waited on `ShouldDefer`.
  Stock `UninterruptedDeferAgent` and cooperative delivery of queued jobs replaced
  it. Possible import hitches remain; no timing or performance approval follows.
- Editor-only `imageContentsHash` could not support the Player compile/evidence
  path. The committed foundation now hashes readable raw Texture2D bytes with
  runtime-valid format/color-space/mip/sampler metadata; unreadable types fail.
- A timed six-second offer completed during PNG encoding and preservation checks,
  so its pending-capture assertion failed. The final offer uses an explicit
  permit held closed until those checks finish, with cancellation before permit.
- Initial stock image variants retained Bilinear despite the pinned source's
  minFilter 9987. The private backend now applies the verified authored sampler
  before fingerprints and records byte/state preservation. That correction does
  not alter original assets or silently accept the mismatch.
- A blanket tangent expectation incorrectly included the unnormal-mapped Service
  mesh. The final requirement derives from authored attributes and material
  normal-texture bindings, preserving its valid zero-tangent result.
- Addressables reentry initially attempted to restore an empty pre-initialization
  registry. Cleanup now preserves the legitimate initialized reference/order
  baseline, removes only explicit scope additions and never resets private flags.

Timeout handling retains pending importer/job resources until owned process exit;
it does not dispose queued native work or claim complete restoration while held.
The final successful run exercised settled cancellation and cleanup instead.
This one-source fixed-view pilot does not establish the other five H1 sources,
179-source activation, cold-load failure/cancellation, live writer coordination,
complete optical equivalence, metric webcam depth, moving roof/side flicker,
streaming latency, FPS, long-run memory or release readiness. Those obligations
remain in the foundation and their separate validation records.
