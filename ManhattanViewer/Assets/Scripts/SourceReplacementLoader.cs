using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Gate = Manhattan.Viewer.SourceReplacementGate;

namespace Manhattan.Viewer
{
    public interface ISourceReplacementProvider
    {
        Task<SourceReplacementStage> LoadAsync(Gate.Request request, CancellationToken cancellationToken);
    }

    /// <summary>Caller-held exclusive writer lease for exactly these legacy mesh filters.
    /// The caller must exclude CityStreamer/Skyline ownership, independent root movement, buffer mutation,
    /// and other representation writers until acknowledged fallback restoration. A missing
    /// lease fails closed; this adapter does not implement a live-streamer coordinator.</summary>
    public interface ISourcePrimitiveWriteLease
    {
        bool IsValid { get; }
        bool Covers(MeshFilter filter);
    }

    public sealed class SourcePayloadEvidence
    {
        public Gate.PayloadIdentity Identity { get; }
        public string MeasuredHash { get; }
        internal readonly byte[] Data;
        public byte[] Bytes => (byte[])Data.Clone();
        public SourcePayloadEvidence(Gate.PayloadIdentity identity, byte[] actualBytes)
        {
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            Data = actualBytes == null ? throw new ArgumentNullException(nameof(actualBytes)) : (byte[])actualBytes.Clone();
            MeasuredHash = SourceReplacementLoader.HashBytes(Data);
        }
    }

    [Serializable]
    public sealed class SourceCandidateDescriptor
    {
        public int schema = 1;
        public string sourceId, version, contentHash, nativeGeometryFingerprint, nativeMaterialFingerprint;
        public string profileId, sourceOpticalMetadataJson;
        public string[] approximationLimitations = Array.Empty<string>();
        public Matrix4x4 sourceFrameTransform = Matrix4x4.identity;
    }

    /// <summary>Inactive, static scene instance, not an imported prefab asset. Evidence
    /// binds the actual supplied bytes. A profile receipt is hash-pinned independently
    /// by the request and retains source optics plus any qualified approximation limits.</summary>
    public sealed class SourceReplacementStage
    {
        public GameObject Root { get; }
        internal readonly SourcePayloadEvidence[] Evidence;
        internal readonly SourceCandidateDescriptor Description;
        private readonly Action release;
        private bool released;
        public SourceCandidateDescriptor Descriptor => JsonUtility.FromJson<SourceCandidateDescriptor>(JsonUtility.ToJson(Description));
        public SourceReplacementStage(GameObject inactiveSceneRoot, SourcePayloadEvidence[] evidence,
            SourceCandidateDescriptor descriptor, Action release)
        {
            Root = inactiveSceneRoot != null ? inactiveSceneRoot : throw new ArgumentNullException(nameof(inactiveSceneRoot));
            if (!Root.scene.IsValid() || Root.activeSelf) throw new ArgumentException("The staged root must be an inactive scene instance.");
            Evidence = evidence == null ? throw new ArgumentNullException(nameof(evidence)) : (SourcePayloadEvidence[])evidence.Clone();
            Description = descriptor == null ? throw new ArgumentNullException(nameof(descriptor)) : JsonUtility.FromJson<SourceCandidateDescriptor>(JsonUtility.ToJson(descriptor));
            this.release = release ?? throw new ArgumentNullException(nameof(release));
        }
        internal void Release()
        {
            if (released) return;
            if (Root != null && Root.activeSelf) throw new InvalidOperationException("A visible source cannot be released.");
            released = true; release();
        }
    }

    public enum SourcePrimitiveRole { Body, Trim }

    /// <summary>Exact reviewed primitive ownership, never a whole-tile visibility flag.
    /// Source identity and bundle lineage must come from the authoritative mapping.
    /// Imported buffers remain immutable for the duration of the write lease.</summary>
    public sealed class SourcePrimitiveBinding
    {
        public Gate.SourceIdentity Source { get; }
        public SourcePrimitiveRole Role { get; }
        public string TileId { get; }
        public string BundleSignature { get; }
        public Transform Root { get; }
        public MeshFilter Filter { get; }
        public Mesh ImportedMesh { get; }
        public Transform SourceFrame { get; }
        internal readonly int[][] Ordinals;
        internal readonly Material[] ImportedMaterials;
        internal readonly Matrix4x4 LocalFrame, RootFrame;
        internal readonly MeshRenderer Renderer;
        internal readonly string MeshDigest, MaterialDigest;
        public SourcePrimitiveBinding(Gate.SourceIdentity source, SourcePrimitiveRole role, string tileId,
            string bundleSignature, Transform root, MeshFilter filter, Mesh importedMesh, int[][] triangleOrdinals,
            SurfaceDetailComparison comparisons = null, Transform sourceFrame = null)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            if (!Enum.IsDefined(typeof(SourcePrimitiveRole), role)) throw new ArgumentOutOfRangeException(nameof(role));
            if (string.IsNullOrWhiteSpace(tileId) || string.IsNullOrWhiteSpace(bundleSignature)) throw new ArgumentException("Exact tile and built bundle identities are required.");
            Role = role; TileId = tileId; BundleSignature = bundleSignature;
            Root = root != null ? root : throw new ArgumentNullException(nameof(root));
            Filter = filter != null ? filter : throw new ArgumentNullException(nameof(filter));
            ImportedMesh = importedMesh != null ? importedMesh : throw new ArgumentNullException(nameof(importedMesh));
            SourceFrame = sourceFrame;
            if (sourceFrame != null && root != sourceFrame && !root.IsChildOf(sourceFrame)) throw new ArgumentException("Legacy root is outside the explicit shared source frame.");
            if (!filter.transform.IsChildOf(root) && filter.transform != root) throw new ArgumentException("Mapped filter is outside its pinned root.");
            if (filter.sharedMesh != importedMesh || !importedMesh.isReadable || importedMesh.blendShapeCount != 0) throw new ArgumentException("A readable immutable imported static mesh is required.");
            Renderer = filter.GetComponent<MeshRenderer>(); if (Renderer == null) throw new ArgumentException("Mapped filter has no renderer.");
            if (triangleOrdinals == null || triangleOrdinals.Length != importedMesh.subMeshCount) throw new ArgumentException("Every imported submesh requires an explicit ordinal list.");
            Ordinals = new int[triangleOrdinals.Length][];
            for (int sub = 0; sub < Ordinals.Length; sub++)
            {
                if (importedMesh.GetTopology(sub) != UnityEngine.MeshTopology.Triangles) throw new ArgumentException("Only triangle primitives are supported.");
                var values = triangleOrdinals[sub] ?? throw new ArgumentException("Missing submesh ordinal list.");
                Ordinals[sub] = values.OrderBy(i => i).ToArray();
                if (Ordinals[sub].Distinct().Count() != Ordinals[sub].Length || Ordinals[sub].Any(i => i < 0 || i >= importedMesh.GetIndexCount(sub) / 3)) throw new ArgumentException("Mapped ordinals are duplicated or out of range.");
            }
            if (Ordinals.Sum(values => values.Length) == 0) throw new ArgumentException("A binding must prove at least one owned primitive.");
            if (comparisons != null && comparisons.TryGetOriginalMaterials(root.gameObject, Renderer, out var originals)) ImportedMaterials = originals;
            else ImportedMaterials = Renderer.sharedMaterials;
            LocalFrame = SkylineOwnershipBinding.LocalTransform(filter.transform, root);
            RootFrame = sourceFrame != null ? SkylineOwnershipBinding.LocalTransform(root, sourceFrame) : root.localToWorldMatrix;
            MeshDigest = SourceReplacementLoader.MeshFingerprint(importedMesh);
            MaterialDigest = SourceReplacementLoader.MaterialArrayFingerprint(ImportedMaterials);
        }
    }

    /// <summary>Explicit main-thread adapter for a controlled source pilot. Nothing
    /// discovers a catalog, subscribes to startup, approves an unreviewed profile, or
    /// enables runtimeReady=false content. The provider supplies staged native content;
    /// this class owns only private legacy primitive masks and publication resources.
    /// The trusted provider/caller must independently validate material capability and
    /// camera visibility before pinning the optical receipt. Retained metadata and the
    /// conservative extension blocker below do not prove IOR/transmission equivalence.</summary>
    public sealed class SourceReplacementLoader : IDisposable
    {
        [Serializable] private sealed class LegacyArtifact
        {
            public int schema = 1;
            public string sourceId, version, contentHash, kind;
            public PrimitiveArtifact[] bindings;
        }
        [Serializable] private sealed class PrimitiveArtifact
        {
            public string role, tileId, bundleSignature, path, meshFingerprint, materialFingerprint;
            public Matrix4x4 localFrame, rootFrame;
            public OrdinalArtifact[] submeshes;
        }
        [Serializable] private sealed class OrdinalArtifact { public int submesh; public int[] ordinals; }
        private sealed class Mask
        {
            public MeshFilter filter; public Mesh original, variant;
            public SourcePrimitiveBinding[] bindings;
        }
        private sealed class Candidate
        {
            public Gate.Generation token; public CancellationTokenSource cancellation;
            public SourceReplacementStage stage; public bool registered;
            public Matrix4x4 rootFrame;
            public MeshFilter[] filters; public Mesh[] meshes; public Matrix4x4[] localFrames;
            public MeshRenderer[] renderers; public Material[][] originals;
            public long startedAt, startedTimestamp;
        }
        private readonly Gate gate;
        private readonly Gate.LegacyBinding legacy;
        private readonly SourcePrimitiveBinding[] bindings;
        private readonly SourcePayloadEvidence[] legacyEvidence;
        private readonly SurfaceDetailComparison comparisons;
        private readonly ISourcePrimitiveWriteLease writeLease;
        private readonly IFlatSurfaceVariantFactory flatFactory;
        private readonly Transform sourceFrame;
        private readonly Quaternion sourceFrameRotation;
        private readonly Vector3 sourceFrameScale;
        private readonly Func<long> completionClockMilliseconds;
        private readonly int ownerThread;
        private readonly List<Mask> masks = new List<Mask>();
        private readonly List<Candidate> candidates = new List<Candidate>();
        private Candidate current, published;
        private bool disposed;
        private long now;
        public Task PendingLoadTask { get; private set; } = Task.CompletedTask;
        public string Status { get; private set; } = "Idle; explicit source request required";
        public Gate.StopReason LastStopReason { get; private set; }
        public Gate.Generation CurrentGeneration => current?.token;
        public GameObject PublishedRoot => published?.stage?.Root;
        public bool CanDispose => published == null && candidates.All(candidate => candidate.stage == null || gate.CanReleaseReplacement(candidate.token));

        public SourceReplacementLoader(Gate.LegacyBinding legacyBinding, SourcePrimitiveBinding[] exactBindings,
            SourcePayloadEvidence fallbackEvidence, SourcePayloadEvidence bodyEvidence, SourcePayloadEvidence trimEvidence,
            SurfaceDetailComparison comparisons, ISourcePrimitiveWriteLease writeLease,
            IFlatSurfaceVariantFactory flatFactory = null, Transform sourceFrame = null,
            Func<long> completionClockMilliseconds = null)
        {
            legacy = legacyBinding ?? throw new ArgumentNullException(nameof(legacyBinding));
            bindings = exactBindings == null ? throw new ArgumentNullException(nameof(exactBindings)) : (SourcePrimitiveBinding[])exactBindings.Clone();
            if (bindings.Length == 0 || !bindings.Any(binding => binding != null && binding.Role == SourcePrimitiveRole.Body) || bindings.Any(binding => binding == null || !binding.Source.Equals(legacy.Fallback.Source))) throw new ArgumentException("Exact body and any explicit trim bindings must identify the pinned legacy source.");
            this.comparisons = comparisons != null ? comparisons : throw new ArgumentNullException(nameof(comparisons));
            this.writeLease = writeLease; this.flatFactory = flatFactory;
            this.sourceFrame = sourceFrame; this.completionClockMilliseconds = completionClockMilliseconds;
            if (bindings.Any(binding => binding.SourceFrame != sourceFrame)) throw new ArgumentException("All exact bindings must use the same explicitly supplied source frame.");
            if (sourceFrame != null)
            {
                sourceFrameRotation = sourceFrame.rotation; sourceFrameScale = sourceFrame.lossyScale;
                if (!sourceFrameScale.Equals(Vector3.one)) throw new ArgumentException("The common source frame must preserve metre scale 1.");
            }
            legacyEvidence = new[] { fallbackEvidence, bodyEvidence, trimEvidence };
            var identities = new[] { legacy.Fallback, legacy.BodyMapping, legacy.TrimMapping };
            for (int i = 0; i < identities.Length; i++)
                if (legacyEvidence[i] == null || !legacyEvidence[i].Identity.Equals(identities[i]) || legacyEvidence[i].MeasuredHash != identities[i].ContentHash || HashBytes(CanonicalLegacyEvidenceBytes(legacy.Fallback.Source, identities[i].Kind, bindings)) != legacyEvidence[i].MeasuredHash) throw new ArgumentException("Legacy evidence does not match the exact canonical primitive bindings.");
            foreach (var group in bindings.GroupBy(binding => binding.Filter))
            {
                var records = group.ToArray(); var imported = records[0].ImportedMesh;
                if (records.Any(record => record.ImportedMesh != imported)) throw new ArgumentException("One filter cannot bind different imported meshes.");
                for (int sub = 0; sub < imported.subMeshCount; sub++)
                {
                    var values = records.SelectMany(record => record.Ordinals[sub]).ToArray();
                    if (values.Distinct().Count() != values.Length) throw new ArgumentException("Body/trim ownership overlaps within a filter.");
                }
                masks.Add(new Mask { filter = group.Key, original = imported, bindings = records });
            }
            gate = new Gate(legacy.Fallback.Source.SourceId, legacy);
            ownerThread = Thread.CurrentThread.ManagedThreadId;
        }

        public Gate.Generation Begin(Gate.Request request, ISourceReplacementProvider provider, long nowMilliseconds, long waitMilliseconds = 30000)
        {
            AssertThread(); if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (request == null || request.Mode != Gate.Semantics.ReplaceLegacy) throw new ArgumentException("This explicit adapter supports complete source replacement only.");
            var token = gate.Begin(request, nowMilliseconds, waitMilliseconds);
            now = nowMilliseconds;
            current?.cancellation.Cancel();
            current = new Candidate { token = token, cancellation = new CancellationTokenSource(), startedAt = nowMilliseconds, startedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp() }; candidates.Add(current);
            // A version switch first restores the retained replacement's legacy baseline.
            Tick(nowMilliseconds);
            PendingLoadTask = LoadAsync(current, provider);
            return token;
        }

        private async Task LoadAsync(Candidate candidate, ISourceReplacementProvider provider)
        {
            SourceReplacementStage supplied = null;
            try
            {
                supplied = await provider.LoadAsync(candidate.token.Request, candidate.cancellation.Token);
                AssertOwnerThread();
                if (disposed || current != candidate || candidate.cancellation.IsCancellationRequested)
                { if (supplied != null) supplied.Release(); return; }
                ValidateStage(candidate.token.Request, supplied);
                candidate.stage = supplied;
                comparisons.Register(supplied.Root, false, flatFactory); candidate.registered = true;
                CaptureCandidate(candidate);
                now = CompletionNow(candidate);
                foreach (var evidence in supplied.Evidence)
                    if (!gate.ReportValidatedReady(candidate.token, evidence.Identity, evidence.MeasuredHash, now)) throw new InvalidOperationException("Candidate payload identity was rejected.");
                Status = "Candidate staged and validated; awaiting atomic publication";
            }
            catch (Exception error)
            {
                AssertOwnerThread();
                if (current == candidate && !disposed)
                {
                    now = CompletionNow(candidate);
                    gate.ReportFailure(candidate.token, candidate.token.Request.RequiredPayloads.First(), now);
                    Status = error is OperationCanceledException ? "Candidate load cancelled" : "Candidate load or validation failed: " + error.GetType().Name;
                }
                if (supplied != null && candidate.stage == null) supplied.Release();
                if (candidate.stage != null && gate.CanReleaseReplacement(candidate.token)) Release(candidate);
            }
        }

        public void Cancel(long nowMilliseconds)
        {
            AssertThread(); now = nowMilliseconds;
            if (current == null) return;
            current.cancellation.Cancel(); gate.Cancel(current.token, nowMilliseconds); Tick(nowMilliseconds);
        }
        public bool TryRollback(long nowMilliseconds)
        {
            Cancel(nowMilliseconds); return published == null;
        }
        public void Tick(long nowMilliseconds)
        {
            AssertThread(); now = nowMilliseconds;
            if (current == null) return;
            bool legacyValid = SafeLegacyValid(false);
            if (legacyValid)
            {
                foreach (var evidence in legacyEvidence) gate.ReportValidatedReady(current.token, evidence.Identity, evidence.MeasuredHash, nowMilliseconds);
            }
            else gate.ReportFailure(current.token, legacy.Fallback, nowMilliseconds);
            if (current.stage != null && (!SafeCandidateValid(current, published != current) ||
                (published == current && !PublishedMasksIntact())))
                gate.ReportFailure(current.token, current.token.Request.RequiredPayloads.First(), nowMilliseconds);
            var decision = gate.Evaluate(nowMilliseconds);
            if (decision.Visibility == Gate.Visibility.Replacement && published != current && !SafeLegacyValid(true))
            {
                gate.ReportFailure(current.token, legacy.Fallback, nowMilliseconds);
                decision = gate.Evaluate(nowMilliseconds); legacyValid = false;
            }
            LastStopReason = decision.Reason;
            if (decision.Visibility == Gate.Visibility.KeepFallback && legacyValid)
            {
                if (!SafeLegacyValid(true)) { gate.ReportFailure(current.token, legacy.Fallback, nowMilliseconds); Status = "Hold published source; fallback evidence changed"; return; }
                RestoreMasks();
                if (published?.stage?.Root != null) published.stage.Root.SetActive(false);
                if (!gate.AcknowledgePublished(decision, nowMilliseconds)) throw new InvalidOperationException("Fallback publication acknowledgment rejected.");
                published = null; Status = "Verified legacy body and trim retained";
                ReleaseMasks();
            }
            else if (decision.Visibility == Gate.Visibility.Replacement && current.stage != null && published != current)
            {
                try
                {
                    PrepareMasks();
                    // The static candidate has no managed activation callbacks. No yield or
                    // user callback occurs between mesh pointer publication and acknowledgment.
                    published = current;
                    foreach (var mask in masks) mask.filter.sharedMesh = mask.variant;
                    current.stage.Root.SetActive(true);
                    if (!gate.AcknowledgePublished(decision, nowMilliseconds)) throw new InvalidOperationException("Replacement publication acknowledgment rejected.");
                    Status = "Explicit source published with exact legacy primitive masks";
                }
                catch (Exception)
                {
                    gate.ReportFailure(current.token, current.token.Request.RequiredPayloads.First(), nowMilliseconds);
                    var fallback = gate.Evaluate(nowMilliseconds);
                    if (fallback.Visibility == Gate.Visibility.KeepFallback && SafeLegacyValid(false))
                    {
                        RestoreMasks(); current.stage.Root.SetActive(false);
                        if (!gate.AcknowledgePublished(fallback, nowMilliseconds)) throw new InvalidOperationException("Failed publication could not acknowledge restored fallback.");
                        published = null; ReleaseMasks(); Status = "Publication failed; exact legacy fallback restored";
                    }
                    else Status = "Publication failed; resources held until verified fallback restoration";
                }
            }
            else if (decision.Visibility == Gate.Visibility.HoldPublished) Status = "Hold published representation; verified fallback or write lease unavailable";
            foreach (var candidate in candidates.ToArray())
                if (candidate != published && candidate.stage != null && gate.CanReleaseReplacement(candidate.token)) Release(candidate);
        }

        private bool SafeLegacyValid(bool verifyBuffers)
        { try { return LegacyValid(verifyBuffers); } catch (Exception) { return false; } }
        private bool SafeCandidateValid(Candidate candidate, bool verifyBuffers)
        { try { return CandidateValid(candidate, verifyBuffers); } catch (Exception) { return false; } }
        private bool PublishedMasksIntact()
        { return masks.All(mask => mask.filter != null && mask.variant != null && mask.filter.sharedMesh == mask.variant); }
        private bool LegacyValid(bool verifyBuffers)
        {
            if (writeLease == null || !writeLease.IsValid) return false;
            if (sourceFrame != null && (!sourceFrame.rotation.Equals(sourceFrameRotation) || !sourceFrame.lossyScale.Equals(sourceFrameScale))) return false;
            foreach (var binding in bindings)
            {
                if (binding.Filter == null || binding.Root == null || binding.ImportedMesh == null || binding.Renderer == null || !writeLease.Covers(binding.Filter)) return false;
                if (sourceFrame != null && binding.Root != sourceFrame && !binding.Root.IsChildOf(sourceFrame)) return false;
                var mask = masks.First(item => item.filter == binding.Filter);
                if (binding.Filter.sharedMesh != binding.ImportedMesh && binding.Filter.sharedMesh != mask.variant) return false;
                if (!binding.Renderer.enabled || binding.Renderer.forceRenderingOff || !binding.Filter.gameObject.activeInHierarchy) return false;
                var rootFrame = sourceFrame != null ? SkylineOwnershipBinding.LocalTransform(binding.Root, sourceFrame) : binding.Root.localToWorldMatrix;
                if (!rootFrame.Equals(binding.RootFrame) || !SkylineOwnershipBinding.LocalTransform(binding.Filter.transform, binding.Root).Equals(binding.LocalFrame)) return false;
                if (!MaterialsApproved(binding.Root.gameObject, binding.Renderer, binding.ImportedMaterials)) return false;
                if (verifyBuffers && (MeshFingerprint(binding.ImportedMesh) != binding.MeshDigest || MaterialArrayFingerprint(binding.ImportedMaterials) != binding.MaterialDigest)) return false;
            }
            return true;
        }
        private bool MaterialsApproved(GameObject root, Renderer renderer, Material[] originals)
        {
            if (comparisons.TryGetOriginalMaterials(root, renderer, out var registered)) return SameMaterials(registered, originals) && comparisons.HasApprovedMaterials(root, renderer);
            return SameMaterials(renderer.sharedMaterials, originals);
        }
        private static bool SameMaterials(Material[] left, Material[] right) => left.Length == right.Length && left.Select((material, index) => ReferenceEquals(material, right[index])).All(value => value);

        private static void ValidateStage(Gate.Request request, SourceReplacementStage stage)
        {
            if (stage == null || stage.Root == null || !stage.Root.scene.IsValid() || stage.Root.activeSelf) throw new InvalidOperationException("Provider must supply an inactive native scene instance.");
            if (!StaticCandidate(stage.Root)) throw new InvalidOperationException("Only a static candidate with no managed activation callbacks is supported.");
            foreach (var filter in stage.Root.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (!RenderableCandidateMesh(filter, renderer) || !ActiveBelowRoot(filter.transform, stage.Root.transform)) throw new InvalidOperationException("Every selected candidate mesh requires nonempty triangle geometry and valid visible material slots.");
            }
            var description = stage.Description;
            if (description.schema != 1 || description.sourceId != request.Source.SourceId || description.version != request.Source.Version || !string.Equals(description.contentHash, request.Source.ContentHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Candidate descriptor source identity mismatch.");
            var required = request.RequiredPayloads;
            if (stage.Evidence.Length != required.Length || stage.Evidence.Any(evidence => evidence == null) || stage.Evidence.Select(evidence => evidence.Identity.PayloadId).Distinct().Count() != required.Length) throw new InvalidOperationException("Whole-source payload evidence is incomplete.");
            foreach (var payload in required)
            {
                var evidence = stage.Evidence.SingleOrDefault(item => item.Identity.Equals(payload));
                if (evidence == null || evidence.MeasuredHash != payload.ContentHash) throw new InvalidOperationException("Loaded payload bytes or identity mismatch.");
                if (payload.Kind == Gate.PayloadKind.OpticalMaterialProfile && evidence.MeasuredHash != HashBytes(ProfileBytes(description))) throw new InvalidOperationException("Profile receipt does not identify this native candidate.");
            }
            if (GeometryFingerprint(stage.Root) != description.nativeGeometryFingerprint || MaterialFingerprint(stage.Root) != description.nativeMaterialFingerprint) throw new InvalidOperationException("Native geometry or imported materials differ from the pinned receipt.");
            if (string.IsNullOrWhiteSpace(description.sourceOpticalMetadataJson)) throw new InvalidOperationException("Source optical metadata must be retained in the profile receipt.");
            bool opticalExtensions = description.sourceOpticalMetadataJson.IndexOf("KHR_materials_transmission", StringComparison.Ordinal) >= 0 || description.sourceOpticalMetadataJson.IndexOf("KHR_materials_ior", StringComparison.Ordinal) >= 0;
            if (description.profileId == "urp-source-preserving-v1")
            { if (opticalExtensions) throw new InvalidOperationException("Transmission/IOR cannot be approved from ordinary native alpha."); }
            else if (description.profileId == "urp-qualified-optical-approximation-v1")
            { if (description.approximationLimitations == null || description.approximationLimitations.Length == 0 || description.approximationLimitations.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("A qualified approximation must retain its explicit reviewed limitations."); }
            else throw new InvalidOperationException("Unreviewed native material profile.");
        }
        private void CaptureCandidate(Candidate candidate)
        {
            if (sourceFrame != null && (candidate.stage.Root.transform == sourceFrame || !candidate.stage.Root.transform.IsChildOf(sourceFrame))) throw new InvalidOperationException("Candidate is outside the explicit shared source frame.");
            candidate.rootFrame = sourceFrame != null ? SkylineOwnershipBinding.LocalTransform(candidate.stage.Root.transform, sourceFrame) : candidate.stage.Root.transform.localToWorldMatrix;
            if (!candidate.rootFrame.Equals(candidate.stage.Description.sourceFrameTransform)) throw new InvalidOperationException("Candidate metre-space placement differs from the pinned receipt.");
            candidate.filters = candidate.stage.Root.GetComponentsInChildren<MeshFilter>(true);
            candidate.meshes = candidate.filters.Select(filter => filter.sharedMesh).ToArray();
            candidate.localFrames = candidate.filters.Select(filter => SkylineOwnershipBinding.LocalTransform(filter.transform, candidate.stage.Root.transform)).ToArray();
            candidate.renderers = candidate.filters.Select(filter => filter.GetComponent<MeshRenderer>()).ToArray();
            candidate.originals = candidate.renderers.Select(renderer => comparisons.TryGetOriginalMaterials(candidate.stage.Root, renderer, out var originals) ? originals : renderer.sharedMaterials).ToArray();
        }
        private bool CandidateValid(Candidate candidate, bool verifyBuffers = false)
        {
            if (candidate.stage?.Root == null || candidate.filters == null || !StaticCandidate(candidate.stage.Root)) return false;
            if (candidate.stage.Root.activeSelf != (candidate == published)) return false;
            if (sourceFrame != null && (candidate.stage.Root.transform == sourceFrame || !candidate.stage.Root.transform.IsChildOf(sourceFrame))) return false;
            if (sourceFrame != null && (!sourceFrame.rotation.Equals(sourceFrameRotation) || !sourceFrame.lossyScale.Equals(sourceFrameScale))) return false;
            var frame = sourceFrame != null ? SkylineOwnershipBinding.LocalTransform(candidate.stage.Root.transform, sourceFrame) : candidate.stage.Root.transform.localToWorldMatrix;
            if (!frame.Equals(candidate.rootFrame)) return false;
            if (candidate.stage.Root.GetComponentsInChildren<MeshFilter>(true).Length != candidate.filters.Length) return false;
            for (int i = 0; i < candidate.filters.Length; i++)
                if (candidate.filters[i] == null || candidate.renderers[i] == null || candidate.filters[i].sharedMesh != candidate.meshes[i] || !RenderableCandidateMesh(candidate.filters[i], candidate.renderers[i]) || !ActiveBelowRoot(candidate.filters[i].transform, candidate.stage.Root.transform) || !SkylineOwnershipBinding.LocalTransform(candidate.filters[i].transform, candidate.stage.Root.transform).Equals(candidate.localFrames[i]) || !MaterialsApproved(candidate.stage.Root, candidate.renderers[i], candidate.originals[i])) return false;
            if (verifyBuffers && (GeometryFingerprint(candidate.stage.Root) != candidate.stage.Description.nativeGeometryFingerprint || MaterialFingerprint(candidate.stage.Root, comparisons) != candidate.stage.Description.nativeMaterialFingerprint)) return false;
            return true;
        }
        private static bool StaticCandidate(GameObject root) => root != null && (root.transform.parent == null || root.transform.parent.gameObject.activeInHierarchy) && root.GetComponentsInChildren<MonoBehaviour>(true).Length == 0 && root.GetComponentsInChildren<Animator>(true).Length == 0 && root.GetComponentsInChildren<Animation>(true).Length == 0 && root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0 && root.GetComponentsInChildren<LODGroup>(true).Length == 0 && root.GetComponentsInChildren<Camera>(true).Length == 0 && root.GetComponentsInChildren<Light>(true).Length == 0 && root.GetComponentsInChildren<Rigidbody>(true).Length == 0 && root.GetComponentsInChildren<Collider>(true).Length == 0 && root.GetComponentsInChildren<Renderer>(true).All(renderer => renderer is MeshRenderer && renderer.GetComponent<MeshFilter>() != null);
        private static bool RenderableCandidateMesh(MeshFilter filter, MeshRenderer renderer)
        {
            if (filter == null || renderer == null || !renderer.enabled || renderer.forceRenderingOff || renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) return false;
            var mesh = filter.sharedMesh;
            if (mesh == null || mesh.vertexCount == 0) return false;
            long triangles = 0;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                if (mesh.GetTopology(submesh) != UnityEngine.MeshTopology.Triangles) return false;
                triangles += mesh.GetIndexCount(submesh) / 3;
            }
            if (triangles == 0) return false;
            var materials = renderer.sharedMaterials;
            return materials.Length != 0 && materials.All(material => material != null && material.shader != null && material.shader.name != "Hidden/InternalErrorShader");
        }
        private static bool ActiveBelowRoot(Transform child, Transform root)
        { for (var current = child; current != null && current != root; current = current.parent) if (!current.gameObject.activeSelf) return false; return true; }
        private long CompletionNow(Candidate candidate)
        {
            if (completionClockMilliseconds != null) return completionClockMilliseconds();
            double elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - candidate.startedTimestamp) * 1000d / System.Diagnostics.Stopwatch.Frequency;
            long value = elapsed >= long.MaxValue - candidate.startedAt ? long.MaxValue : candidate.startedAt + (long)elapsed;
            return Math.Max(now, value);
        }
        private void PrepareMasks()
        {
            foreach (var mask in masks)
            {
                if (mask.variant != null) continue;
                mask.variant = UnityEngine.Object.Instantiate(mask.original); mask.variant.name = mask.original.name + " (exact source primitive mask)"; mask.variant.hideFlags = HideFlags.DontSave;
                for (int sub = 0; sub < mask.original.subMeshCount; sub++)
                {
                    var removed = new HashSet<int>(mask.bindings.SelectMany(binding => binding.Ordinals[sub]));
                    var indices = mask.original.GetTriangles(sub); var retained = new List<int>(indices.Length);
                    for (int i = 0; i < indices.Length; i += 3) if (!removed.Contains(i / 3)) { retained.Add(indices[i]); retained.Add(indices[i + 1]); retained.Add(indices[i + 2]); }
                    mask.variant.SetTriangles(retained, sub, false);
                }
                mask.variant.bounds = mask.original.bounds;
            }
        }
        private void RestoreMasks()
        {
            foreach (var mask in masks)
            {
                // Never overwrite a foreign writer's mesh pointer. LegacyValid checked
                // every pointer and the exclusive lease before entering publication.
                if (mask.filter.sharedMesh == mask.variant) mask.filter.sharedMesh = mask.original;
            }
        }
        private void ReleaseMasks()
        {
            foreach (var mask in masks) if (mask.variant != null) { DestroyPrivate(mask.variant); mask.variant = null; }
        }
        private void Release(Candidate candidate)
        {
            if (!gate.CanReleaseReplacement(candidate.token) || candidate == published) throw new InvalidOperationException("Fallback must be acknowledged before candidate release.");
            if (candidate.registered && candidate.stage?.Root != null) { comparisons.Unregister(candidate.stage.Root); candidate.registered = false; }
            if (candidate.stage != null) { candidate.stage.Release(); candidate.stage = null; }
        }
        public void Dispose()
        {
            AssertThread(); if (!CanDispose) throw new InvalidOperationException("Restore and acknowledge fallback before disposing the source loader.");
            disposed = true;
            foreach (var candidate in candidates) { candidate.cancellation.Cancel(); if (candidate.stage != null) Release(candidate); }
            foreach (var mask in masks) if (mask.variant != null) DestroyPrivate(mask.variant);
        }
        private void AssertThread()
        {
            AssertOwnerThread();
            if (disposed) throw new ObjectDisposedException(nameof(SourceReplacementLoader));
        }
        private void AssertOwnerThread()
        { if (Thread.CurrentThread.ManagedThreadId != ownerThread) throw new InvalidOperationException("Source publication and completion must run on the owning Unity thread."); }
        private static void DestroyPrivate(UnityEngine.Object value)
        { if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }

        public static byte[] ProfileBytes(SourceCandidateDescriptor descriptor) => Encoding.UTF8.GetBytes(JsonUtility.ToJson(descriptor));
        public static byte[] CanonicalLegacyEvidenceBytes(Gate.SourceIdentity source, Gate.PayloadKind kind, SourcePrimitiveBinding[] bindings)
        {
            if (source == null || bindings == null) throw new ArgumentNullException(nameof(source));
            if (kind != Gate.PayloadKind.LegacyFallback && kind != Gate.PayloadKind.LegacyBodyMapping && kind != Gate.PayloadKind.LegacyTrimMapping) throw new ArgumentException("Legacy evidence kind required.");
            var selected = bindings.Where(binding => kind == Gate.PayloadKind.LegacyFallback || (kind == Gate.PayloadKind.LegacyBodyMapping ? binding.Role == SourcePrimitiveRole.Body : binding.Role == SourcePrimitiveRole.Trim));
            var artifact = new LegacyArtifact { sourceId = source.SourceId, version = source.Version, contentHash = source.ContentHash, kind = kind.ToString(), bindings = selected.OrderBy(binding => binding.TileId, StringComparer.Ordinal).ThenBy(binding => SkylineOwnershipBinding.Path(binding.Filter.transform, binding.Root), StringComparer.Ordinal).Select(binding => new PrimitiveArtifact { role = binding.Role.ToString(), tileId = binding.TileId, bundleSignature = binding.BundleSignature, path = SkylineOwnershipBinding.Path(binding.Filter.transform, binding.Root), meshFingerprint = binding.MeshDigest, materialFingerprint = binding.MaterialDigest, localFrame = binding.LocalFrame, rootFrame = binding.RootFrame, submeshes = binding.Ordinals.Select((values, index) => new OrdinalArtifact { submesh = index, ordinals = (int[])values.Clone() }).ToArray() }).ToArray() };
            return Encoding.UTF8.GetBytes(JsonUtility.ToJson(artifact));
        }
        public static string HashBytes(byte[] data)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant(); }
        public static string MeshFingerprint(Mesh mesh)
        {
            if (mesh == null || !mesh.isReadable || mesh.blendShapeCount != 0) throw new InvalidOperationException("Readable static native mesh required for geometry evidence.");
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            {
                writer.Write(mesh.vertexCount); writer.Write(mesh.subMeshCount);
                foreach (var vertex in mesh.vertices) { RequireFinite(vertex); writer.Write(vertex.x); writer.Write(vertex.y); writer.Write(vertex.z); }
                WriteVectors(writer, mesh.normals); WriteVectors(writer, mesh.tangents);
                for (int channel = 0; channel < 8; channel++) { var uv = new List<Vector4>(); mesh.GetUVs(channel, uv); WriteVectors(writer, uv); }
                var colors = mesh.colors32; writer.Write(colors.Length); foreach (var color in colors) { writer.Write(color.r); writer.Write(color.g); writer.Write(color.b); writer.Write(color.a); }
                for (int sub = 0; sub < mesh.subMeshCount; sub++) { if (mesh.GetTopology(sub) != UnityEngine.MeshTopology.Triangles) throw new InvalidOperationException("Nontriangle native mesh rejected."); var indices = mesh.GetTriangles(sub); writer.Write(indices.Length); foreach (int index in indices) { if (index < 0 || index >= mesh.vertexCount) throw new InvalidOperationException("Native index is outside its vertex buffer."); writer.Write(index); } }
                writer.Flush(); return HashBytes(stream.ToArray());
            }
        }
        private static void WriteVectors(BinaryWriter writer, IList<Vector3> values)
        { writer.Write(values.Count); foreach (var value in values) { RequireFinite(value); writer.Write(value.x); writer.Write(value.y); writer.Write(value.z); } }
        private static void WriteVectors(BinaryWriter writer, IList<Vector4> values)
        { writer.Write(values.Count); foreach (var value in values) { if (!float.IsFinite(value.w)) throw new InvalidOperationException("Nonfinite native channel."); RequireFinite((Vector3)value); writer.Write(value.x); writer.Write(value.y); writer.Write(value.z); writer.Write(value.w); } }
        private static void RequireFinite(Vector3 value)
        { if (!float.IsFinite(value.x) || !float.IsFinite(value.y) || !float.IsFinite(value.z)) throw new InvalidOperationException("Nonfinite native geometry channel."); }
        public static string GeometryFingerprint(GameObject root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var filters = root.GetComponentsInChildren<MeshFilter>(true); if (filters.Length == 0) throw new InvalidOperationException("Candidate has no static geometry.");
            var text = new StringBuilder();
            foreach (var filter in filters.OrderBy(filter => SkylineOwnershipBinding.Path(filter.transform, root.transform), StringComparer.Ordinal))
            { text.Append(SkylineOwnershipBinding.Path(filter.transform, root.transform)).Append('|').Append(MeshFingerprint(filter.sharedMesh)).Append('|'); AppendMatrix(text, SkylineOwnershipBinding.LocalTransform(filter.transform, root.transform)); }
            return HashBytes(Encoding.UTF8.GetBytes(text.ToString()));
        }
        public static string MaterialFingerprint(GameObject root, SurfaceDetailComparison comparisons = null)
        {
            if (root == null) throw new ArgumentNullException(nameof(root)); var text = new StringBuilder();
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true).OrderBy(renderer => SkylineOwnershipBinding.Path(renderer.transform, root.transform), StringComparer.Ordinal))
            { var materials = comparisons != null && comparisons.TryGetOriginalMaterials(root, renderer, out var originals) ? originals : renderer.sharedMaterials; text.Append(SkylineOwnershipBinding.Path(renderer.transform, root.transform)).Append('|').Append(MaterialArrayFingerprint(materials)).Append('|'); }
            return HashBytes(Encoding.UTF8.GetBytes(text.ToString()));
        }
        internal static string MaterialArrayFingerprint(Material[] materials)
        {
            var text = new StringBuilder();
            foreach (var material in materials)
            {
                if (material == null || material.shader == null) throw new InvalidOperationException("Material identity cannot contain null material or shader.");
                text.Append(material.name).Append('|').Append(material.shader.name).Append('|').Append(material.renderQueue).Append('|');
                foreach (var keyword in material.shaderKeywords.OrderBy(keyword => keyword, StringComparer.Ordinal)) text.Append(keyword).Append(',');
                for (int i = 0; i < material.shader.GetPropertyCount(); i++)
                {
                    string property = material.shader.GetPropertyName(i); text.Append('|').Append(property).Append('=');
                    switch (material.shader.GetPropertyType(i))
                    {
                        case UnityEngine.Rendering.ShaderPropertyType.Color: AppendVector(text, material.GetColor(property)); break;
                        case UnityEngine.Rendering.ShaderPropertyType.Vector: AppendVector(text, material.GetVector(property)); break;
                        case UnityEngine.Rendering.ShaderPropertyType.Float:
                        case UnityEngine.Rendering.ShaderPropertyType.Range: text.Append(material.GetFloat(property).ToString("R", CultureInfo.InvariantCulture)); break;
                        case UnityEngine.Rendering.ShaderPropertyType.Int: text.Append(material.GetInteger(property)); break;
                        case UnityEngine.Rendering.ShaderPropertyType.Texture:
                            var texture = material.GetTexture(property); text.Append(texture != null ? texture.name + ":" + texture.imageContentsHash + ":" + texture.width + "x" + texture.height : "NULL");
                            AppendVector(text, material.GetTextureScale(property)); AppendVector(text, material.GetTextureOffset(property)); break;
                    }
                }
            }
            return HashBytes(Encoding.UTF8.GetBytes(text.ToString()));
        }
        private static void AppendMatrix(StringBuilder text, Matrix4x4 matrix)
        { for (int i = 0; i < 16; i++) { if (!float.IsFinite(matrix[i])) throw new InvalidOperationException("Nonfinite source transform."); text.Append(matrix[i].ToString("R", CultureInfo.InvariantCulture)).Append(','); } }
        private static void AppendVector(StringBuilder text, Vector4 vector)
        { text.Append(vector.x.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(vector.y.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(vector.z.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(vector.w.ToString("R", CultureInfo.InvariantCulture)); }
    }
}
