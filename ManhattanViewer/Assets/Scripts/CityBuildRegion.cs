using System;
using UnityEngine;

namespace Manhattan.Viewer
{
    [Serializable]
    public sealed class CityBuildRegion
    {
        public float centerX = 1225f, centerZ = 3585f;
        public float radius = 450f, armLength = 1800f, halfWidth = 150f;

        public void Validate()
        {
            foreach (float value in new[] { centerX, centerZ, radius, armLength, halfWidth })
                if (!float.IsFinite(value)) throw new ArgumentException("Region dimensions must be finite.");
            if (radius <= 0 || halfWidth <= 0 || armLength < radius) throw new ArgumentException("Region requires positive radius/width and arms at least as long as the radius.");
        }
        public bool Intersects(Bounds bounds)
        {
            Vector3 min = bounds.min, max = bounds.max;
            float dx = Mathf.Max(min.x - centerX, 0f, centerX - max.x);
            float dz = Mathf.Max(min.z - centerZ, 0f, centerZ - max.z);
            if (dx * dx + dz * dz <= radius * radius) return true;
            return (min.x <= centerX + armLength && max.x >= centerX - armLength && min.z <= centerZ + halfWidth && max.z >= centerZ - halfWidth)
                || (min.x <= centerX + halfWidth && max.x >= centerX - halfWidth && min.z <= centerZ + armLength && max.z >= centerZ - armLength);
        }
    }
}
