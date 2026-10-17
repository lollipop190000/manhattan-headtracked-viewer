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
        public const float Radius = .3f, Skin = .02f;
        private CapsuleCollider probe;
        private readonly Collider[] overlaps = new Collider[128];
        public CitySolidRecovery CitySolids { get; set; }
        public TraversalCollision(FloatingOrigin origin, SwingPhysics body) { this.origin = origin; this.body = body; }
        private Vector3 Offset => origin != null ? origin.Offset : Vector3.zero;
        private Vector3 Bottom(Vector3 eye) => eye - Vector3.up * (body.Settings.eyeHeight - Radius);

        public Vector3 Constrain(Vector3 current, Vector3 requested) => Constrain(current, requested, true);
        public Vector3 Constrain(Vector3 current, Vector3 requested, bool synchronize)
        {
            if (synchronize) Physics.SyncTransforms();
            // Recover one shared starting pose. Recovery displacement is not
            // requested travel and must not be added a second time by the sweep.
            Vector3 delta = requested - current;
            Vector3 recovered = CitySolids != null ? CitySolids.Recover(current) : current;
            Vector3 p = ResolveOverlap(recovered - Offset);
            Vector3 cityStart = p + Offset;
            Vector3 target = CitySolids != null ? CitySolids.Constrain(cityStart, cityStart + delta) : cityStart + delta;
            Vector3 remaining = target - cityStart;
            for (int i = 0; i < 8 && remaining.sqrMagnitude > 1e-10f; i++)
            {
                if (!Physics.CapsuleCast(Bottom(p), p, Radius, remaining.normalized, out var hit,
                    remaining.magnitude + Skin, Mask, QueryTriggerInteraction.Ignore)) { p += remaining; break; }
                float travel = Mathf.Max(0f, hit.distance - Skin);
                p += remaining.normalized * Mathf.Min(travel, remaining.magnitude);
                remaining = Vector3.ProjectOnPlane(remaining - remaining.normalized * Mathf.Min(travel, remaining.magnitude), hit.normal);
                body.Contact(hit.normal, hit.collider.name);
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
                        body.Contact(normal, other.name);
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

        public Vector3 FindWall(Vector3 position, Vector3 forward, bool synchronize = true, Vector3 retainedNormal = default)
        {
            if (synchronize) Physics.SyncTransforms();
            Vector3 p = position - Offset;
            // Retention follows the physical wall, even when the view turns away.
            // Do not jump between opposing walls in a narrow alley.
            if (retainedNormal.sqrMagnitude > .5f && CastWall(p, -retainedNormal, out var retained)) return retained.normal;
            float nearest = float.PositiveInfinity; Vector3 normal = Vector3.zero;
            for (int i = 0; i < 9; i++)
            {
                Vector3 direction = i == 8 ? Vector3.ProjectOnPlane(forward, Vector3.up).normalized :
                    Quaternion.Euler(0, i * 45f, 0) * Vector3.forward;
                if (CastWall(p, direction, out var hit) && hit.distance < nearest)
                { nearest = hit.distance; normal = hit.normal; }
            }
            return normal;
        }
        private bool CastWall(Vector3 p, Vector3 direction, out RaycastHit hit) =>
            Physics.CapsuleCast(Bottom(p), p, Radius, direction, out hit, .7f, Mask, QueryTriggerInteraction.Ignore) && Mathf.Abs(hit.normal.y) < .2f;

        // Input guidance only: this never moves the body or rewrites velocity.
        // A preview keeps a requested corner cut tangent to the approaching wall
        // until the crossing opens. The ordinary swept collision remains active.
        public bool CornerThreat { get; private set; }
        public Vector3 CornerNormal { get; private set; }
        private float cornerRemaining;
        public void ResetGuidance(){CornerThreat=false;CornerNormal=Vector3.zero;cornerRemaining=0;}
        public Vector3 GuideCorner(Vector3 position, Vector3 velocity, Vector3 requested, float seconds, float clearance, float turnDegrees,float dt)
        {
            CornerThreat=false;CornerNormal=Vector3.zero;
            Vector3 horizontal=Vector3.ProjectOnPlane(velocity,Vector3.up);
            cornerRemaining=Mathf.Max(0,cornerRemaining-dt);
            if(requested.sqrMagnitude<.001f || horizontal.magnitude<12)return requested;
            if(Vector3.Angle(horizontal,requested)>15)cornerRemaining=1f;
            if(cornerRemaining==0)return requested;
            Vector3 p=position-Offset;
            if(ClearCurve(p,horizontal,requested,seconds,clearance,turnDegrees,out var normal))return requested;
            CornerThreat=true;
            CornerNormal=normal;
            Vector3 best=horizontal.normalized;float score=float.NegativeInfinity;
            for(int i=0;i<4;i++)
            {
                Vector3 candidate=i==0?Vector3.ProjectOnPlane(requested,normal):i==1?requested+normal:
                    i==2?Vector3.ProjectOnPlane(horizontal,normal):horizontal;
                if(candidate.sqrMagnitude<.001f || !ClearCurve(p,horizontal,candidate,seconds,clearance,turnDegrees,out _))continue;
                float value=Vector3.Dot(candidate.normalized,requested.normalized);
                if(value>score){score=value;best=candidate.normalized;}
            }
            return best*requested.magnitude;
        }
        private bool ClearCurve(Vector3 p,Vector3 velocity,Vector3 requested,float seconds,float clearance,float degrees,out Vector3 normal)
        {
            normal=Vector3.zero;float h=seconds/6f;float speed=velocity.magnitude;
            for(int i=0;i<6;i++)
            {
                var previousVelocity=velocity;
                velocity=Vector3.RotateTowards(velocity,requested.normalized*speed,degrees*Mathf.Deg2Rad*h,0);
                // Average the velocities over the segment. Using only the
                // already-turned end velocity understated the turning radius
                // and falsely cleared paths that the 120Hz body would hit.
                Vector3 delta=(previousVelocity+velocity)*(.5f*h);
                if(Physics.CapsuleCast(Bottom(p),p,Radius+clearance,delta.normalized,out var hit,delta.magnitude,Mask,QueryTriggerInteraction.Ignore)
                    && Mathf.Abs(hit.normal.y)<.2f){normal=hit.normal;return false;}
                // The expanded preview can already overlap a wall, and PhysX
                // sweeps do not report starting overlaps. Keep the physical
                // capsule sweep too, so crossing into the buffer cannot turn
                // an unsafe curve into a false clear result.
                if(Physics.CapsuleCast(Bottom(p),p,Radius,delta.normalized,out hit,delta.magnitude+Skin,Mask,QueryTriggerInteraction.Ignore)
                    && Mathf.Abs(hit.normal.y)<.2f){normal=hit.normal;return false;}
                p+=delta;
            }
            return true;
        }
    }
}
