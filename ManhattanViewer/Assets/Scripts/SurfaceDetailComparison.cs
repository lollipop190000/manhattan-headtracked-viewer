using System;
using System.Collections.Generic;
using UnityEngine;

namespace Manhattan.Viewer
{
    /// <summary>Imported materials remain untouched. Runtime variants can be restored for every resident or new tile.</summary>
    public sealed class SurfaceDetailComparison : MonoBehaviour
    {
        public const string DistanceFadeProperty = "_CityDetailOpacity";
        private sealed class Binding
        {
            public Renderer renderer;
            public Material[] original, detailed, flat;
            public bool fades;
            public Variant[] variants;
        }
        private sealed class Variant
        {
            public Material source, detailed, flat;
            public int references;
            public bool fades;
        }
        private readonly Dictionary<GameObject, List<Binding>> tiles = new Dictionary<GameObject, List<Binding>>();
        private readonly Dictionary<Material, Variant> regularVariants = new Dictionary<Material, Variant>();
        private readonly Dictionary<Material, Variant> fadeVariants = new Dictionary<Material, Variant>();
        public int RuntimeVariantCount => regularVariants.Count + fadeVariants.Count;
        public Material[] OriginalMaterialsSnapshot()
        {
            var materials = new HashSet<Material>();
            foreach (var bindings in tiles.Values) foreach (var binding in bindings)
                foreach (var material in binding.original) if (material != null) materials.Add(material);
            var result = new Material[materials.Count]; materials.CopyTo(result); return result;
        }
        public void GetNearFadeRange(out float minimum, out float maximum, out int renderers, out int visibleRenderers)
        {
            minimum = 1f; maximum = 0f; renderers = visibleRenderers = 0;
            if (block == null) block = new MaterialPropertyBlock();
            foreach (var bindings in tiles.Values) foreach (var binding in bindings)
            {
                if (!binding.fades || binding.renderer == null || !binding.renderer.enabled || !binding.renderer.gameObject.activeInHierarchy) continue;
                binding.renderer.GetPropertyBlock(block);
                float opacity = block.GetFloat(DistanceFade);
                minimum = Mathf.Min(minimum, opacity); maximum = Mathf.Max(maximum, opacity); renderers++;
                if (binding.renderer.isVisible) visibleRenderers++;
            }
            if (renderers == 0) minimum = maximum = -1f;
        }
        private MaterialPropertyBlock block;
        private static readonly int DistanceFade = Shader.PropertyToID(DistanceFadeProperty);
        private Shader distanceFadeShader;
        private bool enabledDetails = true;
        private void Awake() => block = new MaterialPropertyBlock();
        public bool EnabledDetails
        {
            get => enabledDetails;
            set
            {
                if (enabledDetails == value) return;
                enabledDetails = value;
                foreach (var bindings in tiles.Values) foreach (var binding in bindings) Apply(binding);
            }
        }

        public void Register(GameObject root, bool fades = false)
        {
            if (root == null || tiles.ContainsKey(root)) return;
            var bindings = new List<Binding>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var binding = new Binding { renderer = renderer, original = renderer.sharedMaterials, fades = fades };
                binding.detailed = new Material[binding.original.Length]; binding.flat = new Material[binding.original.Length];
                binding.variants = new Variant[binding.original.Length];
                for (int i = 0; i < binding.original.Length; i++)
                {
                    var source = binding.original[i];
                    if (source == null) continue;
                    var cache = fades ? fadeVariants : regularVariants;
                    if (!cache.TryGetValue(source, out var variants))
                    {
                        var fadeShader = fades ? GetDistanceFadeShader() : null;
                        var detailed = fades ? new Material(source) : source;
                        if (fades)
                        {
                            // The template is a strong build reference; matching Lit properties retain PBR maps, UV transforms and alpha state.
                            detailed.shader = fadeShader; detailed.CopyPropertiesFromMaterial(source);
                            detailed.SetFloat(DistanceFade, 1f); detailed.name += " (distance fade)"; detailed.hideFlags = HideFlags.DontSave;
                        }
                        var flat = new Material(detailed) { name = source.name + " (surface off)", hideFlags = HideFlags.DontSave };
                        foreach (string keyword in new[] { "_NORMALMAP", "_PARALLAXMAP", "_DETAIL_MULX2", "_DETAIL_SCALED", "_METALLICSPECGLOSSMAP", "_OCCLUSIONMAP", "_EMISSION" }) flat.DisableKeyword(keyword);
                        foreach (string map in new[] { "_BaseMap", "_MainTex", "_BumpMap", "_MetallicGlossMap", "_SpecGlossMap", "_OcclusionMap", "_ParallaxMap", "_DetailMask", "_DetailAlbedoMap", "_DetailNormalMap", "_EmissionMap" })
                        {
                            bool coverage = (map == "_BaseMap" || map == "_MainTex") &&
                                ((flat.HasProperty("_AlphaClip") && flat.GetFloat("_AlphaClip") > .5f) ||
                                (flat.HasProperty("_Surface") && flat.GetFloat("_Surface") > .5f));
                            if (!coverage && flat.HasProperty(map)) flat.SetTexture(map, null);
                        }
                        if (flat.HasProperty("_BumpScale")) flat.SetFloat("_BumpScale", 0f);
                        if (flat.HasProperty("_Metallic")) flat.SetFloat("_Metallic", 0f);
                        if (flat.HasProperty("_Smoothness")) flat.SetFloat("_Smoothness", .15f);
                        if (flat.HasProperty("_SpecColor")) flat.SetColor("_SpecColor", new Color(.04f, .04f, .04f));
                        if (flat.HasProperty("_EmissionColor")) flat.SetColor("_EmissionColor", Color.black);
                        variants = new Variant { source = source, detailed = detailed, flat = flat, fades = fades }; cache.Add(source, variants);
                    }
                    variants.references++;
                    binding.variants[i] = variants;
                    binding.detailed[i] = variants.detailed; binding.flat[i] = variants.flat;
                }
                bindings.Add(binding); Apply(binding);
            }
            tiles.Add(root, bindings);
            if (fades) SetOpacity(root, 1f);
        }
        private void Apply(Binding binding)
        {
            if (binding.renderer != null) binding.renderer.sharedMaterials = enabledDetails ? binding.detailed : binding.flat;
        }
        private Shader GetDistanceFadeShader()
        {
            if (distanceFadeShader != null) return distanceFadeShader;
            var template = Resources.Load<Material>("CityDetailMaterial");
            if (template == null || template.shader == null || !template.HasProperty(DistanceFade))
                throw new InvalidOperationException("City detail material is missing or does not support " + DistanceFadeProperty + ". Recreate viewer scene and render settings before building.");
            distanceFadeShader = template.shader; return distanceFadeShader;
        }
        public void SetOpacity(GameObject root, float opacity)
        {
            if (!tiles.TryGetValue(root, out var bindings)) return;
            if (block == null) block = new MaterialPropertyBlock();
            foreach (var b in bindings)
            {
                if (!b.fades || b.renderer == null) continue;
                b.renderer.GetPropertyBlock(block);
                block.SetFloat(DistanceFade, Mathf.Clamp01(opacity));
                b.renderer.SetPropertyBlock(block);
            }
        }
        public void Unregister(GameObject root)
        {
            if (!tiles.TryGetValue(root, out var bindings)) return;
            foreach (var b in bindings)
            {
                if (b.renderer != null) b.renderer.sharedMaterials = b.original;
                foreach (var variant in b.variants)
                {
                    if (variant == null || --variant.references != 0) continue;
                    (variant.fades ? fadeVariants : regularVariants).Remove(variant.source);
                    if (variant.fades) Destroy(variant.detailed);
                    Destroy(variant.flat);
                }
            }
            tiles.Remove(root);
        }
        private void OnDestroy()
        {
            foreach (var root in new List<GameObject>(tiles.Keys)) Unregister(root);
            tiles.Clear(); regularVariants.Clear(); fadeVariants.Clear();
        }
    }
}
