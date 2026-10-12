# City detail shader attribution

`Assets/Scripts/Resources/SkylineFallbackLit.shader` is also adapted from the
same pinned `Lit.shader`, under the same Unity Companion License below. It
retains the stock passes and introduces `Offset 0, 8` for camera depth ties on
private legacy Skyline hull variants. Shadow, Meta and 2D passes explicitly
retain zero offset. No slope bias is used. The depth unit is GPU dependent;
it is not a fixed distance in metres. Repeat native depth-priority and material
round-trip tests after changing Unity, URP, graphics API or depth format.

`CityDetailLit.shader` and `CityDetailLitInput.hlsl` are adapted from `Shaders/Lit.shader` and `Shaders/LitInput.hlsl` in Unity's `com.unity.render-pipelines.universal` **17.3.0**, as installed with Unity **6000.3.22f1**. The package's original license notice is retained verbatim in `LICENSE-Unity-URP.md`. These adapted Unity sources are governed by the [Unity Companion License](https://unity.com/legal/licenses/unity-companion-license), separately from this repository's MIT code.

Copyright (c) 2020 Unity Technologies ApS.

Upstream SHA-256: `Lit.shader` = `6f4648b6b5271132cfed1d7d7c771ba0a73ef7972470d6694047804d259c0997`; `LitInput.hlsl` = `4790ecd2f2c8093113b702fdc106486a63308754802c58995f2f0a8121b5b516`.

The adaptation retains the stock Lit properties, PBR calculations, texture/UV sampling, alpha test and blend settings, instancing declarations and pass implementations. It adds `_CityDetailOpacity` to `UnityPerMaterial` and DOTS material metadata, then redirects the stock pass clipping call sites to that property. A value of `0` removes all coverage; `1` retains full coverage; values between them use a stable screen-pixel hash. The coverage operation always compiles and does not depend on the ordinary LOD keyword, UnityPerDraw LOD data or a global dither texture. Set the custom float on runtime Near material clones through a MaterialPropertyBlock. Original imported city materials remain unchanged.

All color, shadow, depth, depth-normal, deferred and object-motion-vector passes use the same hook. The stock Meta/lightmap-baking and Universal2D passes remain unchanged; the viewer uses the 3D URP Forward renderer. Stock pass files are included directly from the pinned URP package. When updating URP, review both adapted files against the new upstream Lit sources and repeat the standalone GPU coverage and round-trip checks.
