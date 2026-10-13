using Manhattan.Viewer;
using UnityEngine;

namespace WebShooter.Swing
{
    /// <summary>Sweeps an upright body, then slides along contacts. All public positions are city coordinates.</summary>
    public sealed class TraversalCollision : ITranslationConstraint, System.IDisposable
    {
        private readonly FloatingOrigin origin;
        private readonly SwingPhysics body;
        public const int Mask = 1 << 30;
        private const float Radius = .3f, Skin = .02f;
        private CapsuleCollider probe;
        private readonly Collider[] overlaps = new Collider[128];
        public CitySolidRecovery CitySolids { get; set; }
        public TraversalCollision(FloatingOrigin origin, SwingPhysics body) { this.origin = origin; this.body = body; }
        private Vector3 Offset => origin != null ? origin.Offset : Vector3.zero;
        private Vector3 Bottom(Vector3 eye) => eye - Vector3.up * (body.Settings.eyeHeight - Radius);

        public Vector3 Constrain(Vector3 current, Vector3 requested)
        {
            Physics.SyncTransforms();
            if (CitySolids != null)
            {
                var recovered = CitySolids.Constrain(current, current);
                requested = CitySolids.Constrain(recovered, recovered + requested - current);
                current = recovered;
            }
            Vector3 p = current - Offset, remaining = requested - current;
            p = ResolveOverlap(p);
            for (int i = 0; i < 8 && remaining.sqrMagnitude > 1e-10f; i++)
            {
                if (!Physics.CapsuleCast(Bottom(p), p, Radius, remaining.normalized, out var hit,
                    remaining.magnitude + Skin, Mask, QueryTriggerInteraction.Ignore)) { p += remaining; break; }
                float travel = Mathf.Max(0f, hit.distance - Skin);
                p += remaining.normalized * Mathf.Min(travel, remaining.magnitude);
                remaining = Vector3.ProjectOnPlane(remaining - remaining.normalized * Mathf.Min(travel, remaining.magnitude), hit.normal);
                body.Contact(hit.normal);
            }
            return ResolveOverlap(p) + Offset;
        }

        // Casts alone do not report colliders already containing the body (streaming,
        // teleports and mode changes). A trigger query shape resolves those overlaps.
        private Vector3 ResolveOverlap(Vector3 p)
        {
            if (probe == null)
            {
                var go = new GameObject("Traversal overlap query") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
                probe = go.AddComponent<CapsuleCollider>();
                probe.isTrigger = true;
                go.transform.position = Vector3.down * 100000f;
                probe.radius = Radius;
                probe.height = body.Settings.eyeHeight + Radius;
                probe.center = Vector3.down * ((body.Settings.eyeHeight - Radius) * .5f);
                Physics.SyncTransforms();
            }
            for (int pass = 0; pass < 12; pass++)
            {
                int count = Physics.OverlapCapsuleNonAlloc(Bottom(p), p, Radius, overlaps, Mask, QueryTriggerInteraction.Ignore);
                bool moved = false;
                for (int i = 0; i < count; i++)
                {
                    var other = overlaps[i];
                    if (Physics.ComputePenetration(probe, p, Quaternion.identity, other, other.transform.position,
                        other.transform.rotation, out var normal, out float depth) && depth > 0f)
                    {
                        p += normal * (depth + Skin);
                        body.Contact(normal);
                        moved = true;
                    }
                }
                if (!moved) break;
            }
            return p;
        }

        public void Dispose()
        {
            if (probe == null) return;
            if (Application.isPlaying) Object.Destroy(probe.gameObject); else Object.DestroyImmediate(probe.gameObject);
            probe = null;
        }

        public Vector3 FindWall(Vector3 position, Vector3 forward)
        {
            Physics.SyncTransforms();
            Vector3 p = position - Offset;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            for (int i = 0; i < 3; i++)
            {
                Vector3 direction = i == 0 ? forward : i == 1 ? right : -right;
                // Include the 0.8m resident visual clearance around city footprints.
                if (Physics.CapsuleCast(Bottom(p), p, Radius, direction, out var hit, .7f, Mask, QueryTriggerInteraction.Ignore)
                    && Mathf.Abs(hit.normal.y) < .2f) return hit.normal;
            }
            return Vector3.zero;
        }
    }
}
