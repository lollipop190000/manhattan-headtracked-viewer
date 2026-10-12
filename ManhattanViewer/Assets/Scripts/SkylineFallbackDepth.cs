using System;
using System.Collections.Generic;
using UnityEngine;

namespace Manhattan.Viewer
{
    /// <summary>Private depth-priority variants for the legacy opaque Skyline hull.
    /// Triangle ownership still removes fully covered faces; this resolves the
    /// residual coplanar pixels when only part of a split wall is resident.</summary>
    public sealed class SkylineFallbackDepth : IDisposable
    {
        public const string ShaderName = "Manhattan/SkylineFallbackLit";
        private sealed class Variant { public Material source, material; public int references; }
        private sealed class Binding { public Renderer renderer; public Material[] original; public Variant[] variants; }
        private readonly Dictionary<Material, Variant> variants = new Dictionary<Material, Variant>();
        private readonly Dictionary<GameObject, List<Binding>> roots = new Dictionary<GameObject, List<Binding>>();
        public int RuntimeVariantCount => variants.Count;
        private static readonly string[] Maps = { "_BaseMap", "_MainTex", "_BumpMap", "_MetallicGlossMap", "_SpecGlossMap", "_OcclusionMap", "_ParallaxMap", "_DetailMask", "_DetailAlbedoMap", "_DetailNormalMap", "_EmissionMap" };

        private static bool Eligible(Material material)
        {
            if (material == null || material.name != "Skyline" || material.shader == null || material.shader.name != "Universal Render Pipeline/Lit") return false;
            if (material.GetFloat("_Surface") != 0 || material.GetFloat("_AlphaClip") != 0 || material.GetFloat("_Cull") != 2 || material.GetFloat("_ZWrite") != 1) return false;
            foreach (string keyword in new[] { "_ALPHATEST_ON", "_SURFACE_TYPE_TRANSPARENT", "_ALPHAPREMULTIPLY_ON", "_ALPHAMODULATE_ON" })
                if (material.IsKeywordEnabled(keyword)) return false;
            foreach (string map in Maps) if (material.HasProperty(map) && material.GetTexture(map) != null) return false;
            return true;
        }

        // Register after ownership captures imported provenance, before SurfaceDetailComparison.
        public void Register(CityTileRecord tile, GameObject root)
        {
            if (tile == null || tile.layer != CityLayer.Skyline || root == null || roots.ContainsKey(root)) return;
            var bindings = new List<Binding>();
            try
            {
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    var original = renderer.sharedMaterials;
                    if (mesh == null || original.Length > mesh.subMeshCount) continue;
                    var binding = new Binding { renderer = renderer, original = original, variants = new Variant[original.Length] };
                    bindings.Add(binding);
                    var selected = (Material[])original.Clone();
                    for (int i = 0; i < original.Length; i++)
                    {
                        var source = original[i]; if (!Eligible(source)) continue;
                        if (!variants.TryGetValue(source, out var variant))
                        {
                            var shader = Resources.Load<Shader>("SkylineFallbackLit");
                            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Skyline fallback shader is unavailable");
                            var material = new Material(source) { shader = shader, name = source.name, hideFlags = HideFlags.DontSave };
                            material.CopyPropertiesFromMaterial(source);
                            variant = new Variant { source = source, material = material };
                            variants.Add(source, variant);
                        }
                        variant.references++; binding.variants[i] = variant; selected[i] = variant.material;
                    }
                    renderer.sharedMaterials = selected;
                }
                roots.Add(root, bindings);
            }
            catch { foreach (var binding in bindings) Release(binding); throw; }
        }

        // SurfaceDetailComparison must unregister first, restoring these variants.
        public void Unregister(GameObject root)
        {
            if (!roots.TryGetValue(root, out var bindings)) return;
            foreach (var binding in bindings) Release(binding);
            roots.Remove(root);
        }
        private void Release(Binding binding)
        {
            if (binding.renderer != null) binding.renderer.sharedMaterials = binding.original;
            foreach (var variant in binding.variants)
            {
                if (variant == null || --variant.references != 0) continue;
                variants.Remove(variant.source);
                if (Application.isPlaying) UnityEngine.Object.Destroy(variant.material);
                else UnityEngine.Object.DestroyImmediate(variant.material);
            }
        }
        public void Dispose() { foreach (var root in new List<GameObject>(roots.Keys)) Unregister(root); }
    }
}
