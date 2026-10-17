using System;
using UnityEngine;

namespace Manhattan.Viewer
{
    [Serializable]
    public sealed class CityBuildRegion
    {
        public float centerX = 1225f, centerZ = 3585f;
        public float radius = 450f, armLength = 1800f, halfWidth = 150f;
        public float yaw;

        public void Validate()
        {
            foreach (float value in new[] { centerX, centerZ, radius, armLength, halfWidth, yaw })
                if (!float.IsFinite(value)) throw new ArgumentException("Region dimensions must be finite.");
            if (radius <= 0 || halfWidth <= 0 || armLength < radius) throw new ArgumentException("Region requires positive radius/width and arms at least as long as the radius.");
        }
        public bool Intersects(Bounds bounds)
        {
            Vector3 min = bounds.min, max = bounds.max;
            float dx = Mathf.Max(min.x - centerX, 0f, centerX - max.x);
            float dz = Mathf.Max(min.z - centerZ, 0f, centerZ - max.z);
            if (dx * dx + dz * dz <= radius * radius) return true;
            return IntersectsArm(bounds,armLength,halfWidth) || IntersectsArm(bounds,halfWidth,armLength);
        }
        private bool IntersectsArm(Bounds bounds,float halfX,float halfZ)
        {
            // 2D AABB/OBB separating axes. Retain touching tiles and tall buildings.
            float angle=Mathf.Repeat(yaw,360)*Mathf.Deg2Rad;
            float c=Mathf.Cos(angle),s=Mathf.Sin(angle),ac=Mathf.Abs(c),asine=Mathf.Abs(s);
            float x=bounds.center.x-centerX,z=bounds.center.z-centerZ;
            float ex=bounds.extents.x,ez=bounds.extents.z;
            return Mathf.Abs(c*x-s*z)<=halfX+ac*ex+asine*ez &&
                Mathf.Abs(s*x+c*z)<=halfZ+asine*ex+ac*ez &&
                Mathf.Abs(x)<=ex+ac*halfX+asine*halfZ &&
                Mathf.Abs(z)<=ez+asine*halfX+ac*halfZ;
        }
    }
}
