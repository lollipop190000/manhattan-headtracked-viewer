using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Manhattan.Viewer
{
    // Run after FreeFlightController (-300) and FloatingOrigin (-200), before rendering.
    [DefaultExecutionOrder(-50)]
    public sealed class CityStreamer : MonoBehaviour
    {
        public CityCatalog catalog;
        public FreeFlightController flight;
        public FloatingOrigin origin;
        public SurfaceDetailComparison surfaces;
        [Min(1)] public long residencyBudgetBytes = 1536L * 1024 * 1024;
        public float workBudgetMilliseconds = 2f;
        public bool CollisionProxiesEnabled { get; set; }
        public int LoadedTiles { get; private set; }
        public int PendingLoads { get; private set; }
        public long EstimatedResidentBytes { get; private set; }
        public float FrameWorkMilliseconds { get; private set; }
        public int Failures { get; private set; }
        public long CatalogEvaluations { get; private set; }

        public struct NearDiagnosticState
        {
            public string sourceVersion, nearestLoadedTile;
            public int loadedTiles, activeTiles, activeRenderers, visibleRendererFlags;
            public float nearestLoadedBoundsDistance, minimumOpacity, maximumOpacity;
            public bool surfaceDetailsEnabled, opacityAvailable;
        }

        /// <summary>On-demand, read-only state of instantiated Near residents. Flags are not pixel visibility proof.</summary>
        public NearDiagnosticState GetNearDiagnostics()
        {
            var result = new NearDiagnosticState
            {
                sourceVersion = catalog != null ? catalog.sourceVersion : "unavailable",
                nearestLoadedBoundsDistance = float.PositiveInfinity,
                minimumOpacity = -1f, maximumOpacity = -1f,
                surfaceDetailsEnabled = surfaces != null && surfaces.EnabledDetails
            };
            Vector3 position = flight != null ? flight.CityPosition : Vector3.zero;
            foreach (var r in residents)
            {
                if (r.instance == null || r.tile.layer != CityLayer.Near) continue;
                result.loadedTiles++;
                if (r.instance.activeInHierarchy) result.activeTiles++;
                if (flight == null) continue;
                float distance = DistanceToTile(r.tile.cityBounds, position);
                if (distance < result.nearestLoadedBoundsDistance)
                {
                    result.nearestLoadedBoundsDistance = distance;
                    result.nearestLoadedTile = r.tile.id;
                }
            }
            if (surfaces != null)
            {
                surfaces.GetNearFadeRange(out result.minimumOpacity, out result.maximumOpacity,
                    out result.activeRenderers, out result.visibleRendererFlags);
                result.opacityAvailable = result.activeRenderers > 0;
            }
            return result;
        }

        private sealed class Residency
        {
            public CityTileRecord tile;
            public AsyncOperationHandle<GameObject> handle;
            public bool hasHandle, loading, wanted, visible, releaseQueued;
            public GameObject instance;
            public float retryAt, distance, priority;
            public float appliedNearOpacity = float.NaN;
            public Renderer[] renderers;
            public int residentIndex = -1;
        }
        private readonly List<Residency> all = new List<Residency>();
        private readonly List<Residency> residents = new List<Residency>();
        private readonly List<Residency> requests = new List<Residency>();
        private readonly Queue<Residency> deferredReleases = new Queue<Residency>();
        private readonly Dictionary<string, int> replacementCounts = new Dictionary<string, int>();
        private readonly Stopwatch workTimer = new Stopwatch();
        private readonly SkylineGeometryOwnership ownership = new SkylineGeometryOwnership();
        /// <summary>Notify after a resident tile root's relation, renderer/material or mesh pointer changes.
        /// Pass null to invalidate every group. Shared mesh vertex/index contents remain immutable.
        /// Call before the next streaming tick; this does not grant support for mutable shared geometry.</summary>
        public void InvalidateOwnership(GameObject tileRoot=null)=>ownership.Invalidate(tileRoot);
        public bool OwnershipWorkPending => ownership.Busy;
        public long MaskedSkylineTriangles => ownership.MaskedTriangles;
        public int OwnershipRuntimeMeshCount => ownership.RuntimeMeshCount;
        public int OwnershipUnsupportedMeshes => ownership.UnsupportedMeshes;
        public long OwnershipPublications => ownership.Publications;
        public double OwnershipTickMilliseconds => ownership.LastTickMilliseconds;
        public double OwnershipMaximumTickMilliseconds => ownership.MaximumTickMilliseconds;
        public double OwnershipMaximumValidationMilliseconds => ownership.MaximumSourceValidationMilliseconds;
        public double OwnershipMaximumMeshReadMilliseconds => ownership.MaximumMeshReadMilliseconds;
        public double OwnershipMaximumMeshCloneMilliseconds => ownership.MaximumMeshCloneMilliseconds;
        public double OwnershipMaximumUploadMilliseconds => ownership.MaximumIndexUploadMilliseconds;
        public long OwnershipDecodedMappingBytes => ownership.DecodedMappingBytes;
        public long OwnershipStagingArrayBytes => ownership.StagingArrayBytes;
        public double OwnershipMaximumPublicationMilliseconds => ownership.MaximumPublicationMilliseconds;
        public double OwnershipMaximumRevalidationMilliseconds => ownership.MaximumRevalidationMilliseconds;
        public double OwnershipMaximumCleanupMilliseconds => ownership.MaximumCleanupMilliseconds;
        private float nextEvaluation;
        private bool initialized;

        private readonly struct EvaluationRadii
        {
            public readonly float visible, visibleExit, load, release, loadRejectSquared, releaseRejectSquared;
            public EvaluationRadii(CityLayer layer)
            {
                visible = VisibleRadius(layer); visibleExit = VisibleExitRadius(layer);
                load = LoadRadius(layer); release = ReleaseRadius(layer);
                // Keep the old rounded sqrt comparison near every finite boundary. The 1cm
                // rejection pad exceeds float rounding at the largest policy radius (1200m).
                // Infinity stays Infinity, so Skyline always uses the original distance path.
                loadRejectSquared = (load + .01f) * (load + .01f);
                releaseRejectSquared = (release + .01f) * (release + .01f);
            }
        }
        private static readonly EvaluationRadii[] evaluationRadii =
        {
            new EvaluationRadii(CityLayer.Skyline), new EvaluationRadii(CityLayer.Core),
            new EvaluationRadii(CityLayer.Ground), new EvaluationRadii(CityLayer.Near),
            new EvaluationRadii(CityLayer.Vegetation), new EvaluationRadii(CityLayer.Collision)
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float DistanceSquaredToTile(Vector3 minimum, Vector3 maximum, Vector3 position)
        {
            float x = position.x < minimum.x ? minimum.x - position.x : position.x > maximum.x ? position.x - maximum.x : 0f;
            float y = position.y < minimum.y ? minimum.y - position.y : position.y > maximum.y ? position.y - maximum.y : 0f;
            float z = position.z < minimum.z ? minimum.z - position.z : position.z > maximum.z ? position.z - maximum.z : 0f;
            return x * x + y * y + z * z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EvaluateTile(Residency r, Vector3 position, Vector3 predicted, bool collisionEnabled)
        {
            CityLayer layer = r.tile.layer;
            ref readonly EvaluationRadii radii = ref evaluationRadii[(uint)layer < evaluationRadii.Length ? (int)layer : 1];
            // Read live bounds once per evaluation; do not cache mutable catalog geometry/layers.
            Bounds bounds = r.tile.cityBounds;
            Vector3 minimum = bounds.min, maximum = bounds.max;
            float currentSquared = DistanceSquaredToTile(minimum, maximum, position);
            float predictedSquared = DistanceSquaredToTile(minimum, maximum, predicted);
            float rejectSquared = r.hasHandle ? radii.releaseRejectSquared : radii.loadRejectSquared;
            if (currentSquared > rejectSquared && predictedSquared > rejectSquared)
            {
                // Both distances are beyond load/release and every visible exit radius.
                // Unrequested distances/priorities are unused; resident Near feedback recomputes
                // its distance each frame. NaN comparisons fall through to the original path.
                r.distance = r.priority = float.PositiveInfinity;
                r.wanted = r.visible = false;
                return;
            }
            r.distance = Mathf.Sqrt(currentSquared);
            float predictedDistance = Mathf.Sqrt(predictedSquared);
            float nearest = Mathf.Min(r.distance, predictedDistance);
            bool allowed = layer != CityLayer.Collision || collisionEnabled;
            r.wanted = allowed && nearest <= (r.hasHandle ? radii.release : radii.load);
            r.visible = allowed && r.distance <= (r.visible ? radii.visibleExit : radii.visible);
            r.priority = layer == CityLayer.Skyline ? -100000f : nearest;
            if (layer == CityLayer.Core || layer == CityLayer.Ground) r.priority -= 2000f;
        }

        public void Initialize()
        {
            if (initialized || catalog == null) return;
            initialized = true;
            if(surfaces!=null)surfaces.DetailsChanged+=OnSurfaceDetailsChanged;
            var sidecar=Resources.Load<TextAsset>("CityOwnership");
            if(sidecar!=null)
            {
                try {
                    var digest=Resources.Load<TextAsset>("CityOwnershipHash");
                    if(digest==null||!SkylineOwnershipBinding.VerifyPayload(sidecar.text,digest.text))throw new InvalidOperationException("Sidecar payload integrity mismatch");
                    ownership.Configure(JsonUtility.FromJson<SkylineOwnershipData>(sidecar.text),catalog.sourceVersion,null,catalog);
                }
                catch(Exception error){UnityEngine.Debug.LogWarning("Ownership sidecar invalid; full fallback preserved: "+error.Message);}
            }
            foreach (var tile in catalog.tiles)
                if (tile != null && !string.IsNullOrEmpty(tile.address)) all.Add(new Residency { tile = tile });
            if (origin != null) origin.Rebased += OnRebased;
        }

        public static float VisibleRadius(CityLayer layer)
        {
            switch (layer)
            {
                case CityLayer.Skyline: return float.PositiveInfinity;
                case CityLayer.Near: return 180f;
                case CityLayer.Vegetation: return 250f;
                case CityLayer.Collision: return 180f;
                default: return 800f;
            }
        }
        public static float LoadRadius(CityLayer layer)
        {
            switch (layer)
            {
                case CityLayer.Skyline: return float.PositiveInfinity;
                case CityLayer.Near: return 240f;
                case CityLayer.Vegetation: return 350f;
                case CityLayer.Collision: return 250f;
                default: return 1000f;
            }
        }
        public static float VisibleExitRadius(CityLayer layer) =>
            Mathf.Min(VisibleRadius(layer) + 20f, (VisibleRadius(layer) + LoadRadius(layer)) * .5f);
        public static bool IsVisible(CityLayer layer, float distance, bool wasVisible) =>
            distance <= (wasVisible ? VisibleExitRadius(layer) : VisibleRadius(layer));
        public static float NearOpacity(float distance) => 1f - Mathf.InverseLerp(120f, 180f, distance);
        public static float ReleaseRadius(CityLayer layer)
        {
            switch (layer)
            {
                case CityLayer.Skyline: return float.PositiveInfinity;
                case CityLayer.Near: return 320f;
                case CityLayer.Vegetation: return 450f;
                case CityLayer.Collision: return 350f;
                default: return 1200f;
            }
        }
        public static float DistanceToTile(Bounds bounds, Vector3 cityPosition)
        {
            // Bounds.ClosestPoint crosses into native code. Clamp each axis here instead;
            // retain distances in metres so request ordering and fade/radius boundaries stay unchanged.
            Vector3 minimum = bounds.min, maximum = bounds.max;
            float x = cityPosition.x < minimum.x ? minimum.x - cityPosition.x : cityPosition.x > maximum.x ? cityPosition.x - maximum.x : 0f;
            float y = cityPosition.y < minimum.y ? minimum.y - cityPosition.y : cityPosition.y > maximum.y ? cityPosition.y - maximum.y : 0f;
            float z = cityPosition.z < minimum.z ? minimum.z - cityPosition.z : cityPosition.z > maximum.z ? cityPosition.z - maximum.z : 0f;
            return Mathf.Sqrt(x * x + y * y + z * z);
        }
        public static bool CanReplace(CityTileRecord fallback, int visibleCoreReplacementCount) =>
            fallback != null && fallback.layer == CityLayer.Skyline && fallback.requiredReplacementCount > 0 &&
            visibleCoreReplacementCount >= fallback.requiredReplacementCount;

        private void Update()
        {
            Initialize();
            if (flight == null) return;
            workTimer.Restart();
            if (Time.unscaledTime >= nextEvaluation)
            {
                Evaluate(); nextEvaluation = Time.unscaledTime + .15f;
            }
            ProcessDeferredRelease();
            // Only one prefab is instantiated per frame; the baker enforces bounded chunk sizes.
            foreach (var r in residents)
            {
                if (!r.loading || !r.handle.IsDone) continue;
                r.loading = false; PendingLoads--;
                if (r.handle.Status != AsyncOperationStatus.Succeeded)
                {
                    Failures++; r.retryAt = Time.unscaledTime + 5f;
                    UnityEngine.Debug.LogWarning("City tile failed: " + r.tile.id + ". Keeping skyline fallback.");
                    QueueRelease(r); continue;
                }
                if (!r.wanted) { QueueRelease(r); continue; }
                if (workTimer.Elapsed.TotalMilliseconds >= workBudgetMilliseconds) { r.loading = true; PendingLoads++; break; }
                r.instance = Instantiate(r.handle.Result, transform);
                r.appliedNearOpacity = float.NaN;
                r.instance.name = r.tile.id;
                r.instance.transform.position = r.tile.cityOrigin - (origin != null ? origin.Offset : Vector3.zero);
                r.instance.transform.rotation = Quaternion.identity;
                r.instance.transform.localScale = Vector3.one;
                if (r.tile.layer == CityLayer.Collision) SetLayer(r.instance.transform, 30);
                r.renderers = r.instance.GetComponentsInChildren<Renderer>(true);
                ownership.Register(r.tile, r.instance, r.handle.Result);
                if (surfaces != null) surfaces.Register(r.instance, r.tile.layer == CityLayer.Near);
                LoadedTiles++;
                UpdateVisibility();
                break;
            }
            int ongoing = PendingLoads;
            foreach (var r in requests)
            {
                if (ongoing >= 2 || workTimer.Elapsed.TotalMilliseconds >= workBudgetMilliseconds) break;
                if (r.hasHandle || !r.wanted || r.retryAt > Time.unscaledTime) continue;
                long cost = Math.Max(1L, r.tile.residentBytes);
                if (r.tile.layer != CityLayer.Skyline && EstimatedResidentBytes + cost > residencyBudgetBytes) continue;
                r.handle = Addressables.LoadAssetAsync<GameObject>(r.tile.address);
                r.hasHandle = r.loading = true;
                r.residentIndex = residents.Count; residents.Add(r);
                EstimatedResidentBytes += cost; PendingLoads++; ongoing++;
            }
            UpdateNearFeedback();
            long previousPublication = ownership.Publications;
            // Leave headroom for an indivisible ownership step and the visibility update
            // after publication. Spending the entire remaining budget before that work
            // consistently exceeded the 2ms scheduling target. Existing coverage stays
            // published while a cohort waits; mesh uploads/publication remain atomic.
            ownership.Tick(Mathf.Max(0f, workBudgetMilliseconds * .7f - (float)workTimer.Elapsed.TotalMilliseconds));
            if (ownership.Publications != previousPublication) UpdateVisibility();
            FrameWorkMilliseconds = (float)workTimer.Elapsed.TotalMilliseconds;
        }

        private void Evaluate()
        {
            CatalogEvaluations++;
            Vector3 position = flight.CityPosition;
            Vector3 predicted = position + flight.Velocity * 2f;
            requests.Clear();
            foreach (var r in all)
            {
                EvaluateTile(r, position, predicted, CollisionProxiesEnabled);
                if (!r.wanted && r.hasHandle && !r.loading) QueueRelease(r);
                // A completed load can become useful again before its queued release runs.
                // Put it back through the existing one-instance-per-frame completion path.
                if (r.wanted && r.hasHandle && !r.loading && r.instance == null && r.handle.Status == AsyncOperationStatus.Succeeded)
                {
                    r.loading = true; PendingLoads++;
                }
                if (r.wanted && !r.hasHandle) requests.Add(r);
            }
            requests.Sort((a, b) => a.priority.CompareTo(b.priority));
            UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            replacementCounts.Clear();
            foreach (var r in residents)
            {
                if (r.instance == null || r.tile.layer != CityLayer.Core || r.tile.replaces == null) continue;
                bool represented = ownership.Manages(r.instance) ? r.instance.activeSelf : r.visible;
                if (!represented) continue;
                foreach (string id in r.tile.replaces)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    replacementCounts.TryGetValue(id, out int count); replacementCounts[id] = count + 1;
                }
            }
            foreach (var r in residents)
            {
                if (r.instance == null) continue;
                if (r.tile.layer == CityLayer.Core && ownership.Manages(r.instance))
                {
                    ownership.SetDesired(r.instance, r.visible);
                    continue;
                }
                replacementCounts.TryGetValue(r.tile.id, out int visibleCoreReplacementCount);
                bool managedSkyline = r.tile.layer == CityLayer.Skyline && ownership.Manages(r.instance);
                bool active = r.visible && (managedSkyline || !CanReplace(r.tile, visibleCoreReplacementCount)) &&
                    (r.tile.layer != CityLayer.Skyline || ownership.CanShowSkyline(r.instance));
                if (r.instance.activeSelf != active) r.instance.SetActive(active);
            }
        }

        private void UpdateNearFeedback()
        {
            // Only instantiated Near residents need frame-rate distance feedback. Catalogue scans,
            // priorities and load/release decisions retain their existing 0.15 second cadence.
            Vector3 position = flight.CityPosition;
            foreach (var r in residents)
            {
                if (r.instance == null || r.tile.layer != CityLayer.Near) continue;
                r.distance = DistanceToTile(r.tile.cityBounds, position);
                r.visible = IsVisible(CityLayer.Near, r.distance, r.visible);
                if (r.instance.activeSelf != r.visible) r.instance.SetActive(r.visible);
                // Keep zero opacity through the exit band; re-entry cannot reveal stale full detail.
                float opacity = NearOpacity(r.distance);
                if (surfaces != null && r.appliedNearOpacity != opacity)
                {
                    surfaces.SetOpacity(r.instance, opacity);
                    r.appliedNearOpacity = opacity;
                }
            }
        }

        private void OnRebased(Vector3 shift)
        {
            InvalidateOwnership();
            foreach (var r in residents) if (r.instance != null) r.instance.transform.position = r.tile.cityOrigin - origin.Offset;
        }
        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform child in t) SetLayer(child, layer);
        }
        private void QueueRelease(Residency r)
        {
            if (r.releaseQueued) return;
            r.releaseQueued = true; deferredReleases.Enqueue(r);
        }
        private void ProcessDeferredRelease()
        {
            while (deferredReleases.Count > 0 && workTimer.Elapsed.TotalMilliseconds < workBudgetMilliseconds)
            {
                var r = deferredReleases.Dequeue(); r.releaseQueued = false;
                if (!r.hasHandle || r.loading) continue;
                if (r.wanted && r.handle.Status == AsyncOperationStatus.Succeeded) continue;
                if (r.instance != null && !ownership.CanRelease(r.instance))
                {
                    ownership.SetDesired(r.instance, false);
                    QueueRelease(r);
                    break;
                }
                Release(r);
                break; // Spread tile destruction and Addressables releases across frames.
            }
        }
        private void Release(Residency r)
        {
            if (r.instance != null)
            {
                ownership.Unregister(r.instance);
                if (surfaces != null) surfaces.Unregister(r.instance);
                Destroy(r.instance); r.instance = null; LoadedTiles--;
            }
            if (r.hasHandle)
            {
                Addressables.Release(r.handle);
                EstimatedResidentBytes -= Math.Max(1L, r.tile.residentBytes);
                int lastIndex = residents.Count - 1;
                var moved = residents[lastIndex];
                residents[r.residentIndex] = moved; moved.residentIndex = r.residentIndex;
                residents.RemoveAt(lastIndex); r.residentIndex = -1;
            }
            r.hasHandle = r.loading = r.releaseQueued = false; r.renderers = null;
        }
        private void OnSurfaceDetailsChanged()=>InvalidateOwnership();
        private void OnDestroy()
        {
            if(surfaces!=null)surfaces.DetailsChanged-=OnSurfaceDetailsChanged;
            ownership.Dispose();
            if (origin != null) origin.Rebased -= OnRebased;
            while (residents.Count > 0) Release(residents[residents.Count - 1]);
            deferredReleases.Clear();
        }
    }
}
