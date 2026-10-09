using System;
using UnityEngine;

namespace Manhattan.Viewer
{
    public enum CityLayer { Skyline, Core, Ground, Near, Vegetation, Collision }

    [Serializable]
    public sealed class CityTileRecord
    {
        public string id;
        public string address;
        public CityLayer layer;
        public Bounds cityBounds;
        public Vector3 cityOrigin;
        public long residentBytes;
        public int triangles;
        [Tooltip("Skyline fallback is hidden only after this many visible Core replacements exist. Zero keeps it visible.")]
        public int requiredReplacementCount;
        [Tooltip("Skyline IDs hidden while this complete replacement is visible. Leave empty for additive detail.")]
        public string[] replaces = Array.Empty<string>();
    }

    [Serializable]
    public sealed class BuildingRecord
    {
        public string id;
        public Vector2[] footprint;
        public float groundHeight;
        public float height;
        public Bounds cityBounds;
    }

    [CreateAssetMenu(menuName = "Manhattan/City Catalog")]
    public sealed class CityCatalog : ScriptableObject
    {
        public string sourceVersion = "manhattan-v1";
        public Vector3 initialEyePosition = new Vector3(1225f, 1.85f, 3585f);
        public Vector3 initialEulerAngles = new Vector3(0f, -151.5f, 0f);
        public CityTileRecord[] tiles = Array.Empty<CityTileRecord>();
        public BuildingRecord[] buildings = Array.Empty<BuildingRecord>();
    }
}
