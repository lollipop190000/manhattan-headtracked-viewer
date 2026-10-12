using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Manhattan.Viewer
{
    /// <summary>Configures an already private Surface OFF clone. Do not mutate imported assets.
    /// Keep alpha coverage, optics and rendering state independent of surface detail.
    /// A factory must retain the same configuration while its registered roots remain resident.</summary>
    public interface IFlatSurfaceVariantFactory
    {
        void ConfigureFlatVariant(Material privateVariant);
    }

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
            public float appliedOpacity = float.NaN;
        }
        private sealed class Variant
        {
            public Material source, detailed, flat;
            public VariantKey key;
            public int references;
            public bool fades;
        }
        private readonly struct VariantKey : IEquatable<VariantKey>
        {
            public readonly Material source;
            public readonly IFlatSurfaceVariantFactory factory;
            public VariantKey(Material source, IFlatSurfaceVariantFactory factory) { this.source = source; this.factory = factory; }
            public bool Equals(VariantKey other) => EqualityComparer<Material>.Default.Equals(source, other.source) && ReferenceEquals(factory, other.factory);
            public override bool Equals(object obj) => obj is VariantKey other && Equals(other);
            public override int GetHashCode() => unchecked(source.GetHashCode() * 397 ^ (factory == null ? 0 : RuntimeHelpers.GetHashCode(factory)));
        }
        private readonly Dictionary<GameObject, List<Binding>> tiles = new Dictionary<GameObject, List<Binding>>();
        private readonly Dictionary<VariantKey, Variant> regularVariants = new Dictionary<VariantKey, Variant>();
        private readonly Dictionary<VariantKey, Variant> fadeVariants = new Dictionary<VariantKey, Variant>();
        public event Action DetailsChanged;
        public int RuntimeVariantCount => regularVariants.Count + fadeVariants.Count;
        public long OpacityPropertyWrites { get; private set; }
        public long OpacityTileVisits { get; private set; }
        public Material[] OriginalMaterialsSnapshot()
        {
            var materials = new HashSet<Material>();
            foreach (var bindings in tiles.Values) foreach (var binding in bindings)
                foreach (var material in binding.original) if (material != null) materials.Add(material);
            var result = new Material[materials.Count]; materials.CopyTo(result); return result;
        }
        /// <summary>Returns a copy of the imported slots for this registered renderer.
        /// The caller cannot replace this controller's original slot array.</summary>
        public bool TryGetOriginalMaterials(GameObject root, Renderer renderer, out Material[] copy)
        {
            copy = null;
            if (root == null || renderer == null || !tiles.TryGetValue(root, out var bindings)) return false;
            foreach (var binding in bindings)
                if (ReferenceEquals(binding.renderer, renderer))
                { copy = (Material[])binding.original.Clone(); return true; }
            return false;
        }
        /// <summary>Accepts only this controller's exact currently selected material slots.
        /// A foreign material with matching properties is still an unapproved pointer.</summary>
        public bool HasApprovedMaterials(GameObject root, Renderer renderer)
        {
            if (root == null || renderer == null || !tiles.TryGetValue(root, out var bindings)) return false;
            foreach (var binding in bindings)
                if (ReferenceEquals(binding.renderer, renderer))
                {
                    var current = renderer.sharedMaterials;
                    var approved = enabledDetails ? binding.detailed : binding.flat;
                    if (current.Length != approved.Length) return false;
                    for (int index = 0; index < current.Length; index++)
                        if (!ReferenceEquals(current[index], approved[index])) return false;
                    return true;
                }
            return false;
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
                DetailsChanged?.Invoke();
            }
        }

        public void Register(GameObject root, bool fades = false, IFlatSurfaceVariantFactory factory = null)
        {
            if (factory != null && fades) throw new ArgumentException("Custom flat surface factories require the non-fading material path.", nameof(factory));
            if (root == null || tiles.ContainsKey(root)) return;
            var bindings = new List<Binding>();
            try
            {
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    var binding = new Binding { renderer = renderer, original = renderer.sharedMaterials, fades = fades };
                    binding.detailed = new Material[binding.original.Length]; binding.flat = new Material[binding.original.Length];
                    binding.variants = new Variant[binding.original.Length];
                    bindings.Add(binding);
                    for (int i = 0; i < binding.original.Length; i++)
                    {
                        var source = binding.original[i];
                        if (source == null) continue;
                        var cache = fades ? fadeVariants : regularVariants;
                        var key = new VariantKey(source, factory);
                        if (!cache.TryGetValue(key, out var variants))
                        {
                            var fadeShader = fades ? GetDistanceFadeShader() : null;
                            var detailed = fades ? new Material(source) : source;
                            Material flat = null;
                            try
                            {
                                if (fades)
                                {
                                    // The template is a strong build reference; matching Lit properties retain PBR maps, UV transforms and alpha state.
                                    detailed.shader = fadeShader; detailed.CopyPropertiesFromMaterial(source);
                                    detailed.SetFloat(DistanceFade, 1f); detailed.name += " (distance fade)"; detailed.hideFlags = HideFlags.DontSave;
                                }
                                flat = new Material(detailed) { name = source.name + " (surface off)", hideFlags = HideFlags.DontSave };
                                if (factory == null) ConfigureDefaultFlatVariant(flat);
                                else factory.ConfigureFlatVariant(flat);
                                variants = new Variant { source = source, detailed = detailed, flat = flat, key = key, fades = fades };
                                cache.Add(key, variants);
                            }
                            catch
                            {
                                ReleasePrivateMaterial(flat);
                                if (fades) ReleasePrivateMaterial(detailed);
                                throw;
                            }
                        }
                        variants.references++;
                        binding.variants[i] = variants;
                        binding.detailed[i] = variants.detailed; binding.flat[i] = variants.flat;
                    }
                    Apply(binding);
                }
                tiles.Add(root, bindings);
                if (fades) SetOpacity(root, 1f);
            }
            catch
            {
                foreach (var binding in bindings) ReleaseBinding(binding);
                tiles.Remove(root);
                throw;
            }
        }
        private static void ConfigureDefaultFlatVariant(Material flat)
        {
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
            OpacityTileVisits++;
            opacity = Mathf.Clamp01(opacity);
            if (block == null) block = new MaterialPropertyBlock();
            foreach (var b in bindings)
            {
                if (!b.fades || b.renderer == null || b.appliedOpacity == opacity) continue;
                b.renderer.GetPropertyBlock(block);
                block.SetFloat(DistanceFade, opacity);
                b.renderer.SetPropertyBlock(block);
                OpacityPropertyWrites++;
                b.appliedOpacity = opacity;
            }
        }
        public void Unregister(GameObject root)
        {
            if (!tiles.TryGetValue(root, out var bindings)) return;
            foreach (var binding in bindings) ReleaseBinding(binding);
            tiles.Remove(root);
        }
        private void ReleaseBinding(Binding binding)
        {
            if (binding.renderer != null) binding.renderer.sharedMaterials = binding.original;
            foreach (var variant in binding.variants)
            {
                if (variant == null || --variant.references != 0) continue;
                (variant.fades ? fadeVariants : regularVariants).Remove(variant.key);
                if (variant.fades) ReleasePrivateMaterial(variant.detailed);
                ReleasePrivateMaterial(variant.flat);
            }
        }
        private static void ReleasePrivateMaterial(Material material)
        {
            if (material == null) return;
            if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
        }
        private void OnDestroy()
        {
            foreach (var root in new List<GameObject>(tiles.Keys)) Unregister(root);
            tiles.Clear(); regularVariants.Clear(); fadeVariants.Clear();
        }
    }
}
