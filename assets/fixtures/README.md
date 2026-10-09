# Nearest Manhattan detail fixture

`NearestNearDetail.glb` is a **383,800-byte exact mesh subset** of canonical `Near_-03_-08.glb`, the nearest 500m near-detail tile to the initial Blender position `(-1225, -3585, 1.85)`. The whole tile is 26.73MB; this fixture retains its smallest independent glazing-mullion material mesh: 14,172 vertices and 7,086 triangles. Source position, normal and index bytes plus material values remain unchanged. There is no recentering, axis conversion or rescaling in the fixture generator.

`fixture-manifest.json` records the original archive/path/SHA-256 and the subset SHA-256, source mesh, GLB and expected Unity bounds. `tools/create_fixture.py` reproduces the file from the manifest-verified original source packs. `tools/tests/test_city_fixture.py` decodes the actual binary positions and indices, verifies bounds and confirms the initial city location has the correct signs after one glTF-to-Unity conversion.

The fixture is managed by Git LFS. Run `git lfs pull` before tests; a pointer file is not a GLB. It is a small validation artifact, not a replacement for the full city source release or a dense-street rendering benchmark.

This derivative retains the original source attribution in [THIRD_PARTY.md](../../THIRD_PARTY.md). Underlying geometry comes from NYC Open Data; facade detail is original procedural approximation in the provided source pack. Preserve the NYC terms and release provenance when redistributing derivatives.
