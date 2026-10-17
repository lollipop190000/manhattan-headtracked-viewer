using System.Collections.Generic;
using Manhattan.Viewer;
using UnityEngine;

namespace WebShooter.Swing
{
    // Resident footprint geometry closes the gap while collision tiles stream in.
    // It also detects points fully inside a non-convex mesh, which PhysX overlap
    // queries do not reliably report. Coordinates here are always city coordinates.
    public sealed class CitySolidRecovery
    {
        private const float Cell = 100f;
        // Match the swept capsule, not a wider visual exclusion zone: two such
        // zones used to overlap across walkable alleys and manufacture roof support.
        private const float Radius = TraversalCollision.Radius + TraversalCollision.Skin;
        private readonly Dictionary<Vector2Int, List<BuildingRecord>> cells = new Dictionary<Vector2Int, List<BuildingRecord>>();
        private readonly SwingPhysics body;
        private CitySolidRecovery(Dictionary<Vector2Int,List<BuildingRecord>> index,SwingPhysics target){cells=index;body=target;}
        public CitySolidRecovery ForBody(SwingPhysics target)=>new CitySolidRecovery(cells,target);
        public CitySolidRecovery(BuildingRecord[] buildings, SwingPhysics body)
        {
            this.body = body;
            foreach (var b in buildings)
            {
                if (b.footprint == null || b.footprint.Length < 3) continue;
                var min = Key(b.cityBounds.min - Vector3.one * Radius);
                var max = Key(b.cityBounds.max + Vector3.one * Radius);
                for (int x = min.x; x <= max.x; x++) for (int z = min.y; z <= max.y; z++)
                {
                    var key = new Vector2Int(x, z);
                    if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<BuildingRecord>();
                    list.Add(b);
                }
            }
        }
        private static Vector2Int Key(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));
        public Vector3 Constrain(Vector3 start, Vector3 target)
        {
            var p = Recover(start);
            var delta = target - start;
            // Less than the body's radius: even a thin footprint cannot be crossed
            // between samples. Each correction cancels motion into that surface.
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / .15f));
            var step = delta / steps;
            for (int i = 0; i < steps; i++)
            {
                var requested = p + step;
                p = Recover(requested);
                var correction = p - requested;
                if (correction.sqrMagnitude > 1e-10f && Vector3.Dot(step, correction) < 0f)
                    step = Vector3.ProjectOnPlane(step, correction.normalized);
            }
            return p;
        }
        public Vector3 Recover(Vector3 p)
        {
            for (int pass = 0; pass < 8; pass++)
            {
                if (!cells.TryGetValue(Key(p), out var list)) break;
                bool corrected = false;
                foreach (var b in list)
                {
                    // Reject distant footprints before walking their polygon edges.
                    var bounds = b.cityBounds;
                    if (p.x < bounds.min.x - Radius || p.x > bounds.max.x + Radius ||
                        p.z < bounds.min.z - Radius || p.z > bounds.max.z + Radius) continue;
                    float roofEscape = b.height + body.Settings.eyeHeight + .02f - p.y;
                    if (roofEscape <= 0f || p.y + Radius <= b.groundHeight) continue;
                    var q = new Vector2(p.x, p.z); bool inside = false;
                    float distance2 = float.PositiveInfinity; Vector2 closest = default, edge = default;
                    var ring = b.footprint;
                    for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                    {
                        Vector2 a = ring[j], c = ring[i], e = c - a;
                        if ((a.y > q.y) != (c.y > q.y) && q.x < (c.x - a.x) * (q.y - a.y) / (c.y - a.y) + a.x) inside = !inside;
                        var nearest = a + e * Mathf.Clamp01(Vector2.Dot(q - a, e) / Mathf.Max(e.sqrMagnitude, 1e-12f));
                        float d = (q - nearest).sqrMagnitude;
                        if (d < distance2) { distance2 = d; closest = nearest; edge = e; }
                    }
                    if (!inside && distance2 >= Radius * Radius) continue;
                    float distance = Mathf.Sqrt(distance2);
                    float horizontalEscape = inside ? distance + Radius : Radius - distance;
                    Vector3 normal;
                    // Only the actual footprint can support the feet. Proximity
                    // to an outside edge must never become a landing in empty air.
                    if (inside && roofEscape < horizontalEscape) { normal = Vector3.up; p.y += roofEscape; }
                    else
                    {
                        Vector2 outward = distance > .00001f ? (inside ? closest - q : q - closest) / distance : new Vector2(edge.y, -edge.x).normalized;
                        normal = new Vector3(outward.x, 0, outward.y); p += normal * (horizontalEscape + .001f);
                    }
                    body.Contact(normal,"City solid "+b.id); corrected = true;
                }
                if (!corrected) break;
            }
            return p;
        }
    }
}
