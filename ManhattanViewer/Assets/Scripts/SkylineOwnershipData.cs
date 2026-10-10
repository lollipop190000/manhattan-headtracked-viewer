using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace Manhattan.Viewer
{
    [Serializable] public sealed class SkylineOwnershipData
    {
        public int schema = 2;
        public string ruleVersion = "legacy-flat-hull-v4-extra-material-safe";
        public string sourceVersion;
        public SkylineOwnershipTile[] tiles = Array.Empty<SkylineOwnershipTile>();
        public SkylineOwnershipGroup[] groups = Array.Empty<SkylineOwnershipGroup>();
    }
    [Serializable] public sealed class SkylineOwnershipTile
    {
        public string id, address, bundleSignature;
        public Vector3 cityOrigin;
        public SkylineOwnershipMesh[] meshes = Array.Empty<SkylineOwnershipMesh>();
    }
    [Serializable] public sealed class SkylineOwnershipMesh
    {
        public string path, name;
        public int vertices;
        public int[] indexCounts;
        public Matrix4x4 localTransform;
    }
    [Serializable] public sealed class SkylineOwnershipGroup
    {
        public string skylineId;
        public SkylineOwnershipPart[] parts = Array.Empty<SkylineOwnershipPart>();
    }
    [Serializable] public sealed class SkylineOwnershipPart
    {
        public string path;
        public SkylineOwnershipOwner[] owners = Array.Empty<SkylineOwnershipOwner>();
    }
    [Serializable] public sealed class SkylineOwnershipOwner
    {
        public string coreId;
        // Flat triangle ordinals in original submesh order, preserving winding and all channels.
        public int[] ordinals;
    }
    public static class SkylineOwnershipBinding
    {
        // Hash the dependency content IDs, never the city's geometry buffers at runtime.
        // Locators without built bundle hashes cannot establish source identity.
        public static string BundleSignature(string address)=>BundleSignature(address,null);
        public static string BundleSignature(string address,IEnumerable<IResourceLocator> locators)
        {
            var variants = new HashSet<string>(StringComparer.Ordinal);
            foreach (var locator in locators??Addressables.ResourceLocators)
            {
                if (!locator.Locate(address, typeof(GameObject), out var locations)) continue;
                foreach (var location in locations)
                {
                    var content = new SortedSet<string>(StringComparer.Ordinal);
                    var visited = new HashSet<IResourceLocation>();
                    if (!Collect(location, visited, content) || content.Count == 0) return null;
                    content.Add("asset|" + location.ProviderId + "|" + location.ResourceType.FullName + "|" + location.InternalId);
                    variants.Add(string.Join("\n", content));
                }
            }
            if (variants.Count != 1) return null; // Ambiguous locators fail closed for masking.
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(variants.First()))).Replace("-", "").ToLowerInvariant();
        }
        public static bool VerifyPayload(string json,string expected)
        {
            if(string.IsNullOrEmpty(json)||string.IsNullOrEmpty(expected))return false;
            using(var sha=SHA256.Create())
                return string.Equals(BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-", ""),expected.Trim(),StringComparison.OrdinalIgnoreCase);
        }
        private static bool Collect(IResourceLocation location, HashSet<IResourceLocation> visited, SortedSet<string> content)
        {
            if (!visited.Add(location)) return true;
            if (location.Data is AssetBundleRequestOptions bundle)
            {
                if (string.IsNullOrEmpty(bundle.BundleName) || string.IsNullOrEmpty(bundle.Hash)) return false;
                content.Add(bundle.BundleName + "|" + bundle.Hash);
            }
            if (location.HasDependencies)
                foreach (var dependency in location.Dependencies)
                    if (!Collect(dependency, visited, content)) return false;
            return true;
        }
        public static string Path(Transform child, Transform root)
        {
            var indices = new List<int>();
            while (child != root)
            {
                if (child == null) throw new ArgumentException("Mesh outside tile root");
                indices.Add(child.GetSiblingIndex()); child = child.parent;
            }
            indices.Reverse(); return string.Join("/", indices);
        }
        public static Matrix4x4 LocalTransform(Transform child, Transform root)
        {
            var matrix = Matrix4x4.identity;
            while (child != root)
            {
                if (child == null) throw new ArgumentException("Mesh outside tile root");
                matrix = Matrix4x4.TRS(child.localPosition, child.localRotation, child.localScale) * matrix; child = child.parent;
            }
            return matrix;
        }
        public static SkylineOwnershipTile Describe(CityTileRecord tile, GameObject root, string signature)
        {
            return new SkylineOwnershipTile { id=tile.id, address=tile.address, bundleSignature=signature, cityOrigin=tile.cityOrigin,
                meshes=root.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh!=null).Select(f=>new SkylineOwnershipMesh {
                    path=Path(f.transform,root.transform),name=f.sharedMesh.name,vertices=f.sharedMesh.vertexCount,
                    indexCounts=Enumerable.Range(0,f.sharedMesh.subMeshCount).Select(s=>checked((int)f.sharedMesh.GetIndexCount(s))).ToArray(),
                    localTransform=LocalTransform(f.transform,root.transform)}).ToArray() };
        }
    }
}
