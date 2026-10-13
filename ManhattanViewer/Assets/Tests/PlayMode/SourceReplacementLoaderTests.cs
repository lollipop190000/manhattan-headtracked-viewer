using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Manhattan.Viewer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Gate = Manhattan.Viewer.SourceReplacementGate;
using Object = UnityEngine.Object;

public sealed class SourceReplacementLoaderTests
{
    private sealed class Lease : ISourcePrimitiveWriteLease
    {
        public bool valid = true;
        public readonly HashSet<MeshFilter> filters = new HashSet<MeshFilter>();
        public bool IsValid => valid;
        public bool Covers(MeshFilter filter) => filters.Contains(filter);
    }
    private sealed class Provider : ISourceReplacementProvider
    {
        public readonly TaskCompletionSource<SourceReplacementStage> pending = new TaskCompletionSource<SourceReplacementStage>();
        public CancellationToken token;
        public Task<SourceReplacementStage> LoadAsync(Gate.Request request, CancellationToken cancellationToken)
        { token = cancellationToken; return pending.Task; }
    }
    private sealed class ImmediateProvider : ISourceReplacementProvider
    {
        private readonly SourceReplacementStage stage;
        public ImmediateProvider(SourceReplacementStage stage) { this.stage = stage; }
        public Task<SourceReplacementStage> LoadAsync(Gate.Request request, CancellationToken cancellationToken) => Task.FromResult(stage);
    }
    private sealed class FlatFactory : IFlatSurfaceVariantFactory
    {
        public void ConfigureFlatVariant(Material variant)
        {
            variant.SetTexture("_BaseMap", null);
            variant.SetTexture("_EmissionMap", null);
            variant.SetColor("_EmissionColor", Color.black);
            variant.DisableKeyword("_EMISSION");
        }
    }
    private sealed class Candidate
    {
        public GameObject root;
        public MeshRenderer renderer;
        public Mesh mesh;
        public Material original;
        public Gate.Request request;
        public SourceReplacementStage stage;
        public SourcePayloadEvidence geometryEvidence;
        public int releases;
        public bool requireRestoredFallbackAtRelease;
    }
    private readonly List<Object> objects = new List<Object>();
    private readonly List<Candidate> candidates = new List<Candidate>();
    private GameObject frame, observerRig, control;
    private GameObject[] roots;
    private MeshFilter[] filters;
    private Mesh[] originals;
    private Material material;
    private Texture2D texture;
    private SourcePrimitiveBinding[] bindings;
    private Gate.LegacyBinding legacy;
    private SourcePayloadEvidence[] evidence;
    private SurfaceDetailComparison surfaces;
    private SourceReplacementLoader loader;
    private Lease lease;
    private long clock;

    [SetUp]
    public void Setup()
    {
        frame = NewObject("shared city frame");
        observerRig = NewObject("observer rig", frame.transform);
        observerRig.transform.localPosition = new Vector3(1, 1.85f, 3);
        control = NewObject("comparison controller"); surfaces = control.AddComponent<SurfaceDetailComparison>();
        texture = new Texture2D(2, 2) { name = "pinned source texture" }; objects.Add(texture);
        texture.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white }); texture.Apply();
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "legacy imported material" }; objects.Add(material);
        material.SetTexture("_BaseMap", texture); material.SetTexture("_EmissionMap", texture);
        material.SetColor("_EmissionColor", Color.yellow); material.EnableKeyword("_EMISSION");
        roots = new[] { NewObject("Core", frame.transform), NewObject("Skyline", frame.transform), NewObject("Near", frame.transform) };
        filters = roots.Select((root, index) => NewPart(root.transform, index.ToString(), material)).ToArray();
        originals = filters.Select(filter => filter.sharedMesh).ToArray();
        for (int i = 0; i < roots.Length; i++) roots[i].transform.localPosition = new Vector3(10 + i, 0, 20 + i);
        foreach (var root in roots) surfaces.Register(root);
        var legacySource = new Gate.SourceIdentity("source-282030_0", "legacy-reviewed", new string('a', 64));
        bindings = filters.Select((filter, index) => new SourcePrimitiveBinding(legacySource,
            index == 2 ? SourcePrimitiveRole.Trim : SourcePrimitiveRole.Body, roots[index].name,
            new string((char)('b' + index), 64), roots[index].transform, filter, originals[index],
            new[] { new[] { 0 } }, surfaces, frame.transform)).ToArray();
        var kinds = new[] { Gate.PayloadKind.LegacyFallback, Gate.PayloadKind.LegacyBodyMapping, Gate.PayloadKind.LegacyTrimMapping };
        evidence = kinds.Select(kind =>
        {
            var bytes = SourceReplacementLoader.CanonicalLegacyEvidenceBytes(legacySource, kind, bindings);
            return new SourcePayloadEvidence(new Gate.PayloadIdentity(legacySource, kind.ToString(), SourceReplacementLoader.HashBytes(bytes), kind), bytes);
        }).ToArray();
        legacy = new Gate.LegacyBinding(evidence[0].Identity, evidence[1].Identity, evidence[2].Identity);
        lease = new Lease(); foreach (var filter in filters) lease.filters.Add(filter);
        loader = CreateLoader(lease);
    }

    private SourceReplacementLoader CreateLoader(ISourcePrimitiveWriteLease writer) => new SourceReplacementLoader(
        legacy, bindings, evidence[0], evidence[1], evidence[2], surfaces, writer, new FlatFactory(), frame.transform, () => clock);
    private GameObject NewObject(string name, Transform parent = null)
    { var root = new GameObject(name); objects.Add(root); if (parent != null) root.transform.SetParent(parent, false); return root; }
    private MeshFilter NewPart(Transform parent, string name, Material sourceMaterial)
    {
        var part = NewObject(name, parent);
        var mesh = new Mesh { name = name + " immutable source", vertices = new[] {
            Vector3.zero, Vector3.right, Vector3.up, new Vector3(10, 0, 0), new Vector3(11, 0, 0), new Vector3(10, 1, 0) },
            triangles = new[] { 0, 1, 2, 3, 4, 5 }, uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.zero, Vector2.right, Vector2.up } };
        mesh.RecalculateNormals(); objects.Add(mesh);
        var filter = part.AddComponent<MeshFilter>(); filter.sharedMesh = mesh;
        part.AddComponent<MeshRenderer>().sharedMaterial = sourceMaterial; return filter;
    }
    private Candidate NewCandidate(string version = "candidate-v1", string profile = "urp-source-preserving-v1", string optics = "{}", string[] limitations = null, Action<Candidate> beforeReceipt = null)
    {
        var candidate = new Candidate { root = NewObject("native " + version, frame.transform) };
        candidate.root.SetActive(false);
        candidate.original = new Material(material) { name = "native imported " + version }; objects.Add(candidate.original);
        var filter = NewPart(candidate.root.transform, "native mesh", candidate.original);
        candidate.renderer = filter.GetComponent<MeshRenderer>(); candidate.mesh = filter.sharedMesh;
        beforeReceipt?.Invoke(candidate);
        var bytes = Encoding.UTF8.GetBytes("independently pinned synthetic source geometry " + version);
        var source = new Gate.SourceIdentity(legacy.Fallback.Source.SourceId, version, SourceReplacementLoader.HashBytes(bytes));
        var descriptor = new SourceCandidateDescriptor {
            sourceId = source.SourceId, version = source.Version, contentHash = source.ContentHash,
            nativeGeometryFingerprint = SourceReplacementLoader.GeometryFingerprint(candidate.root),
            nativeMaterialFingerprint = SourceReplacementLoader.MaterialFingerprint(candidate.root),
            profileId = profile, sourceOpticalMetadataJson = optics, approximationLimitations = limitations ?? Array.Empty<string>(),
            sourceFrameTransform = SkylineOwnershipBinding.LocalTransform(candidate.root.transform, frame.transform) };
        var profileBytes = SourceReplacementLoader.ProfileBytes(descriptor);
        var geometry = new Gate.PayloadIdentity(source, "native-geometry", SourceReplacementLoader.HashBytes(bytes), Gate.PayloadKind.Geometry);
        var optical = new Gate.PayloadIdentity(source, "native-profile", SourceReplacementLoader.HashBytes(profileBytes), Gate.PayloadKind.OpticalMaterialProfile);
        candidate.request = new Gate.Request(source, Gate.Semantics.ReplaceLegacy, geometry, optical);
        candidate.geometryEvidence = new SourcePayloadEvidence(geometry, bytes);
        candidate.stage = new SourceReplacementStage(candidate.root,
            new[] { candidate.geometryEvidence, new SourcePayloadEvidence(optical, profileBytes) }, descriptor, () => {
                if (candidate.requireRestoredFallbackAtRelease) AssertFallback();
                Assert.That(candidate.root.activeSelf, Is.False, "Provider release must never receive a visible candidate.");
                candidate.releases++;
            });
        candidates.Add(candidate); return candidate;
    }
    private void Publish(Candidate candidate)
    {
        loader.Begin(candidate.request, new ImmediateProvider(candidate.stage), clock);
        Assert.That(loader.PendingLoadTask.IsCompleted, Is.True);
        loader.Tick(clock);
        Assert.That(loader.PublishedRoot, Is.SameAs(candidate.root)); candidate.requireRestoredFallbackAtRelease = true;
    }
    private void AssertFallback()
    {
        for (int i = 0; i < filters.Length; i++)
        {
            Assert.That(filters[i].sharedMesh, Is.SameAs(originals[i]));
            Assert.That(originals[i].triangles, Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5 }));
            Assert.That(roots[i].activeSelf, Is.True);
        }
    }
    private IEnumerator AwaitLoad(Task task)
    {
        for (int frameIndex = 0; frameIndex < 30 && !task.IsCompleted; frameIndex++) yield return null;
        Assert.That(task.IsCompleted, Is.True, "The main-thread supplied completion must settle.");
        Assert.That(task.IsFaulted, Is.False);
    }

    [Test]
    public void ExactBodySkylineAndTrimPublicationPreservesNeighborPrimitivesAndImportedBuffers()
    {
        var candidate = NewCandidate();
        var meshHashes = originals.Select(SourceReplacementLoader.MeshFingerprint).ToArray();
        var materials = filters.Select(filter => filter.GetComponent<MeshRenderer>().sharedMaterial).ToArray();
        Publish(candidate);
        for (int i = 0; i < filters.Length; i++)
        {
            Assert.That(filters[i].sharedMesh, Is.Not.SameAs(originals[i]));
            Assert.That(filters[i].sharedMesh.triangles, Is.EqualTo(new[] { 3, 4, 5 }), "Only the exact mapped source primitive is retired.");
            Assert.That(SourceReplacementLoader.MeshFingerprint(originals[i]), Is.EqualTo(meshHashes[i]));
            Assert.That(filters[i].GetComponent<MeshRenderer>().sharedMaterial, Is.SameAs(materials[i]));
        }
        Assert.That(candidate.root.activeSelf, Is.True); Assert.That(candidate.releases, Is.Zero);
        Assert.That(loader.TryRollback(++clock), Is.True);
        AssertFallback(); Assert.That(candidate.releases, Is.EqualTo(1)); Assert.That(loader.CanDispose, Is.True);
    }

    [UnityTest]
    public IEnumerator LoadingFailureRetainsCompleteLegacyAndDoesNotPublishPartialPayloads()
    {
        var candidate = NewCandidate(); var provider = new Provider();
        loader.Begin(candidate.request, provider, clock);
        loader.Tick(++clock); AssertFallback(); Assert.That(loader.PublishedRoot, Is.Null);
        provider.pending.SetException(new InvalidOperationException("synthetic independent loading failure"));
        yield return AwaitLoad(loader.PendingLoadTask);
        loader.Tick(++clock); AssertFallback();
        Assert.That(loader.LastStopReason, Is.EqualTo(Gate.StopReason.PayloadFailed)); Assert.That(candidate.releases, Is.Zero);
    }

    [Test]
    public void MissingWholeSourceProfilePayloadRejectsAnOtherwiseRenderableStage()
    {
        var candidate = NewCandidate();
        var incomplete = new SourceReplacementStage(candidate.root, new[] { candidate.geometryEvidence }, candidate.stage.Descriptor,
            () => { Assert.That(candidate.root.activeSelf, Is.False); candidate.releases++; });
        loader.Begin(candidate.request, new ImmediateProvider(incomplete), clock); loader.Tick(clock);
        AssertFallback(); Assert.That(loader.PublishedRoot, Is.Null); Assert.That(candidate.releases, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator CancelledLateCompletionReleasesInactiveStageWithoutReplacingFallback()
    {
        var candidate = NewCandidate(); var provider = new Provider();
        loader.Begin(candidate.request, provider, clock); loader.Cancel(++clock);
        Assert.That(provider.token.IsCancellationRequested, Is.True); AssertFallback();
        provider.pending.SetResult(candidate.stage); yield return AwaitLoad(loader.PendingLoadTask);
        loader.Tick(++clock); AssertFallback(); Assert.That(candidate.releases, Is.EqualTo(1));
        Assert.That(candidate.root.activeSelf, Is.False); Assert.That(loader.PublishedRoot, Is.Null);
    }

    [UnityTest]
    public IEnumerator StaleGenerationCompletionCannotReplaceNewerPublishedSource()
    {
        var old = NewCandidate("v1"); var provider = new Provider();
        loader.Begin(old.request, provider, clock); var firstTask = loader.PendingLoadTask;
        var next = NewCandidate("v2"); clock++; Publish(next);
        var publishedMeshes = filters.Select(filter => filter.sharedMesh).ToArray();
        provider.pending.SetResult(old.stage); yield return AwaitLoad(firstTask);
        loader.Tick(++clock);
        Assert.That(loader.PublishedRoot, Is.SameAs(next.root)); Assert.That(next.root.activeSelf, Is.True);
        Assert.That(old.root.activeSelf, Is.False); Assert.That(old.releases, Is.EqualTo(1));
        Assert.That(filters.Select(filter => filter.sharedMesh), Is.EqualTo(publishedMeshes));
    }

    [UnityTest]
    public IEnumerator CompletionAfterDeadlineCannotPublishEvenWithoutAnIntermediateTick()
    {
        var candidate = NewCandidate(); var provider = new Provider();
        loader.Begin(candidate.request, provider, clock, 10);
        clock = 11; provider.pending.SetResult(candidate.stage); yield return AwaitLoad(loader.PendingLoadTask);
        loader.Tick(clock); AssertFallback(); Assert.That(loader.PublishedRoot, Is.Null);
        Assert.That(candidate.releases, Is.EqualTo(1)); Assert.That(loader.LastStopReason, Is.EqualTo(Gate.StopReason.TimedOut));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingOrInvalidExclusiveLeaseRejectsPublication(bool missing)
    {
        loader.Dispose(); lease.valid = false; loader = CreateLoader(missing ? null : lease);
        var candidate = NewCandidate(); loader.Begin(candidate.request, new ImmediateProvider(candidate.stage), clock); loader.Tick(clock);
        AssertFallback(); Assert.That(loader.PublishedRoot, Is.Null); Assert.That(candidate.root.activeSelf, Is.False);
        Assert.That(candidate.releases, Is.EqualTo(1));
    }

    [Test]
    public void ForeignMeshPointerHoldsPublishedSourceUntilExactFallbackCanBeRestored()
    {
        var candidate = NewCandidate(); Publish(candidate);
        var masked = filters[0].sharedMesh; var foreign = Object.Instantiate(originals[0]); objects.Add(foreign);
        filters[0].sharedMesh = foreign; loader.Tick(++clock);
        Assert.That(filters[0].sharedMesh, Is.SameAs(foreign), "A concurrent writer's pointer must never be overwritten.");
        Assert.That(loader.PublishedRoot, Is.SameAs(candidate.root)); Assert.That(candidate.root.activeSelf, Is.True);
        Assert.That(candidate.releases, Is.Zero); Assert.That(loader.CanDispose, Is.False);
        Assert.Throws<InvalidOperationException>(() => loader.Dispose());
        filters[0].sharedMesh = masked;
        Assert.That(loader.TryRollback(++clock), Is.True); AssertFallback(); Assert.That(candidate.releases, Is.EqualTo(1));
    }

    [Test]
    public void ForeignMaterialPointerBetweenStagingAndPublicationRejectsCandidate()
    {
        var candidate = NewCandidate(); loader.Begin(candidate.request, new ImmediateProvider(candidate.stage), clock);
        var foreign = new Material(candidate.original) { name = "unapproved material with identical properties" }; objects.Add(foreign);
        candidate.renderer.sharedMaterial = foreign;
        loader.Tick(++clock);
        AssertFallback(); Assert.That(loader.PublishedRoot, Is.Null); Assert.That(candidate.releases, Is.EqualTo(1));
    }

    [Test]
    public void SurfaceOffOnRoundTripKeepsPublicationMasksGeometryOpticsAndOriginalPointers()
    {
        var candidate = NewCandidate(); Publish(candidate);
        var masked = filters.Select(filter => filter.sharedMesh).ToArray();
        var pose = candidate.root.transform.localToWorldMatrix;
        string nativeGeometry = SourceReplacementLoader.GeometryFingerprint(candidate.root);
        surfaces.EnabledDetails = false; loader.Tick(++clock);
        Assert.That(candidate.renderer.sharedMaterial, Is.Not.SameAs(candidate.original));
        Assert.That(candidate.renderer.sharedMaterial.GetTexture("_BaseMap"), Is.Null);
        Assert.That(candidate.renderer.sharedMaterial.GetTexture("_EmissionMap"), Is.Null);
        Assert.That(candidate.renderer.sharedMaterial.GetFloat("_Surface"), Is.EqualTo(candidate.original.GetFloat("_Surface")));
        Assert.That(loader.PublishedRoot, Is.SameAs(candidate.root)); Assert.That(candidate.root.activeSelf, Is.True);
        Assert.That(filters.Select(filter => filter.sharedMesh), Is.EqualTo(masked));
        surfaces.EnabledDetails = true; loader.Tick(++clock);
        Assert.That(candidate.renderer.sharedMaterial, Is.SameAs(candidate.original));
        Assert.That(candidate.original.GetTexture("_EmissionMap"), Is.SameAs(texture));
        Assert.That(candidate.original.GetColor("_EmissionColor"), Is.EqualTo(Color.yellow));
        Assert.That(candidate.root.transform.localToWorldMatrix, Is.EqualTo(pose));
        Assert.That(SourceReplacementLoader.GeometryFingerprint(candidate.root), Is.EqualTo(nativeGeometry));
        Assert.That(filters.Select(filter => filter.sharedMesh), Is.EqualTo(masked));
        Assert.That(candidate.releases, Is.Zero); Assert.That(loader.TryRollback(++clock), Is.True); AssertFallback();
    }

    [Test]
    public void SharedFrameTranslationPreservesSourceAndObserverWhileIndependentRootMovementInvalidates()
    {
        var candidate = NewCandidate(); Publish(candidate);
        var masks = filters.Select(filter => filter.sharedMesh).ToArray(); var observerLocal = observerRig.transform.localPosition;
        var nativeLocal = candidate.root.transform.localPosition;
        frame.transform.position += new Vector3(-5000, 0, -7000); loader.Tick(++clock);
        Assert.That(loader.PublishedRoot, Is.SameAs(candidate.root)); Assert.That(candidate.releases, Is.Zero);
        Assert.That(filters.Select(filter => filter.sharedMesh), Is.EqualTo(masks));
        Assert.That(observerRig.transform.localPosition, Is.EqualTo(observerLocal));
        Assert.That(candidate.root.transform.localPosition, Is.EqualTo(nativeLocal));
        roots[0].transform.localPosition += Vector3.right; loader.Tick(++clock);
        Assert.That(loader.PublishedRoot, Is.SameAs(candidate.root)); Assert.That(candidate.releases, Is.Zero);
        Assert.That(loader.CanDispose, Is.False);
        roots[0].transform.localPosition -= Vector3.right;
        Assert.That(loader.TryRollback(++clock), Is.True); AssertFallback(); Assert.That(candidate.releases, Is.EqualTo(1));
    }

    [Test]
    public void VersionSwitchRestoresFallbackBeforeOldProviderReleaseAndNewPublication()
    {
        var old = NewCandidate("v1"); Publish(old);
        var next = NewCandidate("v2"); clock++;
        loader.Begin(next.request, new ImmediateProvider(next.stage), clock);
        AssertFallback(); Assert.That(old.releases, Is.EqualTo(1)); Assert.That(old.root.activeSelf, Is.False);
        Assert.That(next.root.activeSelf, Is.False); Assert.That(loader.PublishedRoot, Is.Null);
        loader.Tick(clock); Assert.That(loader.PublishedRoot, Is.SameAs(next.root)); next.requireRestoredFallbackAtRelease = true;
        Assert.That(next.releases, Is.Zero); Assert.That(loader.TryRollback(++clock), Is.True); Assert.That(next.releases, Is.EqualTo(1));
    }

    [Test]
    public void TransmissionAndIorRequirePinnedQualifiedProfileWithExplicitLimitations()
    {
        const string opticalMetadata = "{\"extensions\":{\"KHR_materials_transmission\":{\"transmissionFactor\":1},\"KHR_materials_ior\":{\"ior\":1.48}}}";
        var unqualified = NewCandidate("v1", optics: opticalMetadata);
        loader.Begin(unqualified.request, new ImmediateProvider(unqualified.stage), clock); loader.Tick(clock);
        AssertFallback(); Assert.That(loader.PublishedRoot, Is.Null); Assert.That(unqualified.releases, Is.EqualTo(1));
        var qualified = NewCandidate("v2", "urp-qualified-optical-approximation-v1", opticalMetadata,
            new[] { "Native URP transmission uses the opaque color buffer; no measured thick glass or exact IOR equivalence is claimed." });
        clock++; Publish(qualified);
        Assert.That(qualified.stage.Descriptor.sourceOpticalMetadataJson, Is.EqualTo(opticalMetadata));
        Assert.That(qualified.stage.Descriptor.approximationLimitations, Has.Length.EqualTo(1));
    }

    [Test]
    public void NativeMutationAfterReceiptCreationFailsBeforePublication()
    {
        var candidate = NewCandidate(); var vertices = candidate.mesh.vertices; vertices[0] += Vector3.right; candidate.mesh.vertices = vertices;
        loader.Begin(candidate.request, new ImmediateProvider(candidate.stage), clock); loader.Tick(clock);
        AssertFallback(); Assert.That(loader.PublishedRoot, Is.Null); Assert.That(candidate.root.activeSelf, Is.False);
        Assert.That(candidate.releases, Is.EqualTo(1));
    }

    [Test]
    public void SameTextureDescriptorWithChangedPixelsFailsFreshPublicationValidation()
    {
        var nativeTexture = Object.Instantiate(texture); nativeTexture.name = texture.name; objects.Add(nativeTexture);
        var candidate = NewCandidate(beforeReceipt: native => native.original.SetTexture("_BaseMap", nativeTexture));
        loader.Begin(candidate.request, new ImmediateProvider(candidate.stage), clock);
        Assert.That(loader.PublishedRoot, Is.Null);
        nativeTexture.SetPixel(0, 0, Color.black); nativeTexture.Apply();
        Assert.That(nativeTexture.name, Is.EqualTo(texture.name)); Assert.That(nativeTexture.width, Is.EqualTo(texture.width));
        Assert.That(nativeTexture.height, Is.EqualTo(texture.height)); Assert.That(nativeTexture.format, Is.EqualTo(texture.format));
        Assert.That(SourceReplacementLoader.MaterialFingerprint(candidate.root, surfaces), Is.Not.EqualTo(candidate.stage.Descriptor.nativeMaterialFingerprint));
        loader.Tick(++clock);
        AssertFallback(); Assert.That(loader.PublishedRoot, Is.Null); Assert.That(candidate.releases, Is.EqualTo(1));
    }

    [Test]
    public void UnreadableTextureFailsClosedEvenWithPreviouslyValidReceipt()
    {
        var nativeTexture = Object.Instantiate(texture); objects.Add(nativeTexture);
        var candidate = NewCandidate(beforeReceipt: native => native.original.SetTexture("_BaseMap", nativeTexture));
        nativeTexture.Apply(false, true);
        Assert.That(nativeTexture.isReadable, Is.False);
        Assert.Throws<InvalidOperationException>(() => SourceReplacementLoader.MaterialFingerprint(candidate.root));
        loader.Begin(candidate.request, new ImmediateProvider(candidate.stage), clock); loader.Tick(clock);
        AssertFallback(); Assert.That(loader.PublishedRoot, Is.Null); Assert.That(candidate.releases, Is.EqualTo(1));
    }

    [Test]
    public void RenderTextureCannotSubstituteForVerifiedTexture2DContent()
    {
        var candidate = NewCandidate();
        var unsupported = new RenderTexture(texture.width, texture.height, 0) { name = texture.name }; objects.Add(unsupported);
        candidate.original.SetTexture("_BaseMap", unsupported);
        Assert.Throws<InvalidOperationException>(() => SourceReplacementLoader.MaterialFingerprint(candidate.root));
        loader.Begin(candidate.request, new ImmediateProvider(candidate.stage), clock); loader.Tick(clock);
        AssertFallback(); Assert.That(loader.PublishedRoot, Is.Null); Assert.That(candidate.releases, Is.EqualTo(1));
    }

    [TestCase("empty_vertices")]
    [TestCase("empty_indices")]
    [TestCase("empty_materials")]
    [TestCase("shadows_only")]
    [TestCase("line_renderer")]
    [TestCase("error_shader")]
    public void ReceiptMatchingInvisibleOrUnsupportedStageCannotRetireLegacy(string invalidLayout)
    {
        var candidate = NewCandidate(beforeReceipt: native => {
            switch (invalidLayout)
            {
                case "empty_vertices": native.mesh.Clear(); break;
                case "empty_indices": native.mesh.triangles = Array.Empty<int>(); break;
                case "empty_materials": native.renderer.sharedMaterials = Array.Empty<Material>(); break;
                case "shadows_only": native.renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly; break;
                case "line_renderer": NewObject("unsupported dynamic renderer", native.root.transform).AddComponent<LineRenderer>().sharedMaterial = native.original; break;
                case "error_shader":
                    var errorShader = Shader.Find("Hidden/InternalErrorShader");
                    Assert.That(errorShader, Is.Not.Null, "The GPU runner must expose Unity's standard error shader.");
                    var errorMaterial = new Material(errorShader) { name = "invalid native error material" }; objects.Add(errorMaterial);
                    native.renderer.sharedMaterial = errorMaterial; break;
            }
        });
        Assert.That(candidate.stage.Descriptor.nativeGeometryFingerprint, Is.EqualTo(SourceReplacementLoader.GeometryFingerprint(candidate.root)), "Geometry receipt deliberately matches the invalid layout.");
        Assert.That(candidate.stage.Descriptor.nativeMaterialFingerprint, Is.EqualTo(SourceReplacementLoader.MaterialFingerprint(candidate.root)), "Material receipt deliberately matches the invalid layout.");
        loader.Begin(candidate.request, new ImmediateProvider(candidate.stage), clock); loader.Tick(clock);
        AssertFallback(); Assert.That(loader.PublishedRoot, Is.Null); Assert.That(candidate.root.activeSelf, Is.False);
        Assert.That(candidate.releases, Is.EqualTo(1)); Assert.That(loader.LastStopReason, Is.EqualTo(Gate.StopReason.PayloadFailed));
    }

    [TearDown]
    public void Cleanup()
    {
        if (loader != null)
        {
            lease.valid = true; frame.transform.rotation = Quaternion.identity; frame.transform.localScale = Vector3.one;
            for (int i = 0; i < filters.Length; i++)
            {
                roots[i].transform.localPosition = new Vector3(10 + i, 0, 20 + i);
                // The only foreign pointer test restores its exact mask before rollback.
            }
            loader.TryRollback(++clock); loader.Dispose();
        }
        foreach (var root in roots ?? Array.Empty<GameObject>()) surfaces.Unregister(root);
        foreach (var candidate in candidates) if (candidate.root != null) surfaces.Unregister(candidate.root);
        for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear(); candidates.Clear(); loader = null; clock = 0;
    }
}
