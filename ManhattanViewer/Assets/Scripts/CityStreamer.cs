using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Manhattan.Viewer
{
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

        private sealed class Residency
        {
            public CityTileRecord tile;
            public AsyncOperationHandle<GameObject> handle;
            public bool hasHandle, loading, wanted, visible, releaseQueued;
            public GameObject instance;
            public float retryAt, distance, priority;
            public Renderer[] renderers;
        }
        private readonly List<Residency> all = new List<Residency>();
        private readonly List<Residency> requests = new List<Residency>();
        private readonly Queue<Residency> deferredReleases = new Queue<Residency>();
        private readonly Dictionary<string, int> replacementCounts = new Dictionary<string, int>();
        private readonly Stopwatch workTimer = new Stopwatch();
        private float nextEvaluation;
        private bool initialized;

        public void Initialize()
        {
            if (initialized || catalog == null) return;
            initialized = true;
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
            foreach (var r in all)
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
                r.instance.name = r.tile.id;
                r.instance.transform.position = r.tile.cityOrigin - (origin != null ? origin.Offset : Vector3.zero);
                r.instance.transform.rotation = Quaternion.identity;
                r.instance.transform.localScale = Vector3.one;
                if (r.tile.layer == CityLayer.Collision) SetLayer(r.instance.transform, 30);
                r.renderers = r.instance.GetComponentsInChildren<Renderer>(true);
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
                EstimatedResidentBytes += cost; PendingLoads++; ongoing++;
            }
            FrameWorkMilliseconds = (float)workTimer.Elapsed.TotalMilliseconds;
        }

        private void Evaluate()
        {
            Vector3 position = flight.CityPosition;
            Vector3 predicted = position + flight.Velocity * 2f;
            requests.Clear();
            foreach (var r in all)
            {
                r.distance = DistanceToTile(r.tile.cityBounds, position);
                float predictedDistance = DistanceToTile(r.tile.cityBounds, predicted);
                bool allowed = r.tile.layer != CityLayer.Collision || CollisionProxiesEnabled;
                float threshold = r.hasHandle ? ReleaseRadius(r.tile.layer) : LoadRadius(r.tile.layer);
                r.wanted = allowed && Mathf.Min(r.distance, predictedDistance) <= threshold;
                r.visible = allowed && r.distance <= VisibleRadius(r.tile.layer);
                r.priority = r.tile.layer == CityLayer.Skyline ? -100000f : Mathf.Min(r.distance, predictedDistance);
                if (r.tile.layer == CityLayer.Core || r.tile.layer == CityLayer.Ground) r.priority -= 2000f;
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
            foreach (var r in all)
            {
                if (r.instance == null || !r.visible || r.tile.layer != CityLayer.Core || r.tile.replaces == null) continue;
                foreach (string id in r.tile.replaces)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    replacementCounts.TryGetValue(id, out int count); replacementCounts[id] = count + 1;
                }
            }
            foreach (var r in all)
            {
                if (r.instance == null) continue;
                replacementCounts.TryGetValue(r.tile.id, out int visibleCoreReplacementCount);
                bool active = r.visible && !CanReplace(r.tile, visibleCoreReplacementCount);
                if (r.instance.activeSelf != active) r.instance.SetActive(active);
                if (active && r.tile.layer == CityLayer.Near && surfaces != null)
                    surfaces.SetOpacity(r.instance, 1f - Mathf.InverseLerp(120f, 180f, r.distance));
            }
        }

        private void OnRebased(Vector3 shift)
        {
            foreach (var r in all) if (r.instance != null) r.instance.transform.position = r.tile.cityOrigin - origin.Offset;
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
                Release(r);
                break; // Spread tile destruction and Addressables releases across frames.
            }
        }
        private void Release(Residency r)
        {
            if (r.instance != null)
            {
                if (surfaces != null) surfaces.Unregister(r.instance);
                Destroy(r.instance); r.instance = null; LoadedTiles--;
            }
            if (r.hasHandle)
            {
                Addressables.Release(r.handle);
                EstimatedResidentBytes -= Math.Max(1L, r.tile.residentBytes);
            }
            r.hasHandle = r.loading = r.releaseQueued = false; r.renderers = null;
        }
        private void OnDestroy()
        {
            if (origin != null) origin.Rebased -= OnRebased;
            foreach (var r in all) Release(r);
            deferredReleases.Clear();
        }
    }
}
