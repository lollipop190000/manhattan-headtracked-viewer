using UnityEngine;

namespace Manhattan.Viewer
{
    public sealed class BuildingIdentity : MonoBehaviour
    {
        public string buildingId;
        public float height;
    }

    /// <summary>Optional future game movement constraint, independent of head tracking and input sources.</summary>
    public sealed class CityCollisionConstraint : ITranslationConstraint
    {
        private readonly FloatingOrigin origin;
        public float Radius { get; set; } = .3f;
        public CityCollisionConstraint(FloatingOrigin floatingOrigin) => origin = floatingOrigin;
        public Vector3 Constrain(Vector3 currentCityPosition, Vector3 requestedCityPosition)
        {
            Vector3 delta = requestedCityPosition - currentCityPosition;
            if (delta.sqrMagnitude < .000001f) return requestedCityPosition;
            Physics.SyncTransforms();
            Vector3 local = currentCityPosition - origin.Offset;
            if (Physics.SphereCast(local, Radius, delta.normalized, out var hit, delta.magnitude + Radius, 1 << 30, QueryTriggerInteraction.Ignore))
                return currentCityPosition + delta.normalized * Mathf.Clamp(hit.distance - .02f, 0f, delta.magnitude);
            return requestedCityPosition;
        }
    }
}
