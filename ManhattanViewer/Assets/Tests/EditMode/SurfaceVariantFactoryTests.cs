using System;
using System.Collections.Generic;
using Manhattan.Viewer;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class SurfaceVariantFactoryTests
{
    private GameObject controllerObject;
    private SurfaceDetailComparison controller;
    private readonly List<Object> objects = new List<Object>();

    [SetUp] public void SetUp()
    {
        controllerObject = new GameObject("Private surface factory test controller");
        controller = controllerObject.AddComponent<SurfaceDetailComparison>();
    }
    [TearDown] public void TearDown()
    {
        if (controllerObject != null) Object.DestroyImmediate(controllerObject);
        foreach (var value in objects) if (value != null) Object.DestroyImmediate(value);
        objects.Clear();
    }
    private Material Material(string shaderName = "Universal Render Pipeline/Lit")
    {
        var shader = Shader.Find(shaderName);
        Assert.That(shader, Is.Not.Null, "Pinned material shader must be available; this is a property/lifecycle test, not a rendered result.");
        var material = new Material(shader); objects.Add(material); return material;
    }
    private GameObject Root(params Material[] materials)
    {
        var root = new GameObject("Registered material root"); objects.Add(root);
        root.AddComponent<MeshRenderer>().sharedMaterials = materials;
        return root;
    }
    private Texture2D Texture()
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, true);
        texture.filterMode = FilterMode.Trilinear; texture.wrapMode = TextureWrapMode.Repeat;
        objects.Add(texture); return texture;
    }
    private sealed class RoughnessFactory : IFlatSurfaceVariantFactory
    {
        private readonly float smoothness;
        public int calls;
        public RoughnessFactory(float smoothness) { this.smoothness = smoothness; }
        public void ConfigureFlatVariant(Material variant) { calls++; variant.SetFloat("_Smoothness", smoothness); }
        // Value equality must not merge separately configured factory instances.
        public override bool Equals(object other) => other is RoughnessFactory;
        public override int GetHashCode() => 0;
    }
    private sealed class DeclaredNativeFactory : IFlatSurfaceVariantFactory
    {
        public void ConfigureFlatVariant(Material variant)
        {
            foreach (string map in new[] { "normalTexture", "metallicRoughnessTexture", "occlusionTexture", "emissiveTexture" })
                if (variant.HasProperty(map)) variant.SetTexture(map, null);
            variant.SetFloat("normalTexture_scale", 0);
            variant.SetFloat("metallicFactor", 0);
            variant.SetFloat("roughnessFactor", .85f);
            variant.SetVector("emissiveFactor", Vector4.zero);
            variant.DisableKeyword("_EMISSIVE"); variant.DisableKeyword("_OCCLUSION");
        }
    }
    private sealed class ThrowSecondFactory : IFlatSurfaceVariantFactory
    {
        private int calls;
        public void ConfigureFlatVariant(Material variant)
        {
            variant.SetFloat("_Metallic", 0);
            if (++calls == 2) throw new InvalidOperationException("Controlled factory failure.");
        }
    }

    [Test] public void OneSourceHasIndependentDefaultAndFactoryCachesEvenWhenFactoriesHaveValueEquality()
    {
        var source = Material(); source.SetFloat("_Smoothness", .9f);
        var first = new RoughnessFactory(.2f); var second = new RoughnessFactory(.7f);
        var defaultRoot = Root(source); var a = Root(source); var b = Root(source); var aAgain = Root(source);
        controller.Register(defaultRoot); controller.Register(a, false, first);
        controller.Register(b, false, second); controller.Register(aAgain, false, first);
        Assert.That(controller.RuntimeVariantCount, Is.EqualTo(3));
        Assert.That(first.calls, Is.EqualTo(1)); Assert.That(second.calls, Is.EqualTo(1));
        controller.EnabledDetails = false;
        var originalFlat = defaultRoot.GetComponent<Renderer>().sharedMaterial;
        var firstFlat = a.GetComponent<Renderer>().sharedMaterial;
        var secondFlat = b.GetComponent<Renderer>().sharedMaterial;
        Assert.That(originalFlat.GetFloat("_Smoothness"), Is.EqualTo(.15f));
        Assert.That(firstFlat.GetFloat("_Smoothness"), Is.EqualTo(.2f));
        Assert.That(secondFlat.GetFloat("_Smoothness"), Is.EqualTo(.7f));
        Assert.That(ReferenceEquals(firstFlat, aAgain.GetComponent<Renderer>().sharedMaterial), Is.True);
        Assert.That(ReferenceEquals(firstFlat, secondFlat), Is.False);
        Assert.That(ReferenceEquals(firstFlat, originalFlat), Is.False);
        Assert.That(source.GetFloat("_Smoothness"), Is.EqualTo(.9f));
        controller.EnabledDetails = true;
        foreach (var root in new[] { defaultRoot, a, b, aAgain }) Assert.That(ReferenceEquals(root.GetComponent<Renderer>().sharedMaterial, source), Is.True);
    }

    [Test] public void NativeFactoryRoundTripPreservesOpticalCoverageAndRestoresImportedPointersAndEmission()
    {
        var source = Material("Shader Graphs/glTF-pbrMetallicRoughness");
        var baseTexture = Texture(); var normal = Texture(); var metalRoughness = Texture(); var emission = Texture();
        source.SetVector("baseColorFactor", new Vector4(.95f, .97f, .98f, 1));
        source.SetTexture("baseColorTexture", baseTexture);
        source.SetVector("baseColorTexture_ST", new Vector4(2, 3, .1f, .2f));
        source.SetTexture("normalTexture", normal); source.SetFloat("normalTexture_scale", 1);
        source.SetTexture("metallicRoughnessTexture", metalRoughness);
        source.SetFloat("metallicFactor", .4f); source.SetFloat("roughnessFactor", .2f);
        source.SetTexture("emissiveTexture", emission); source.SetVector("emissiveFactor", new Vector4(2, 1, .5f, 0));
        source.SetFloat("transmissionFactor", 1); source.EnableKeyword("_TRANSMISSION"); source.EnableKeyword("_EMISSIVE");
        source.SetFloat("_Cull", 0); source.SetFloat("_Surface", 1); source.SetFloat("_ZWrite", 0);
        source.renderQueue = 3000;
        var root = Root(source); var renderer = root.GetComponent<Renderer>();
        var pose = new Vector3(4, 3, 2); root.transform.position = pose;
        controller.Register(root, false, new DeclaredNativeFactory());
        controller.EnabledDetails = false;
        var flat = renderer.sharedMaterial;
        Assert.That(ReferenceEquals(flat, source), Is.False);
        Assert.That(flat.GetTexture("normalTexture"), Is.Null);
        Assert.That(flat.GetTexture("metallicRoughnessTexture"), Is.Null);
        Assert.That(flat.GetTexture("emissiveTexture"), Is.Null);
        Assert.That(flat.GetFloat("normalTexture_scale"), Is.Zero);
        Assert.That(flat.GetFloat("metallicFactor"), Is.Zero);
        Assert.That(flat.GetVector("emissiveFactor"), Is.EqualTo(Vector4.zero));
        Assert.That(flat.IsKeywordEnabled("_EMISSIVE"), Is.False);
        Assert.That(flat.GetFloat("transmissionFactor"), Is.EqualTo(1));
        Assert.That(flat.IsKeywordEnabled("_TRANSMISSION"), Is.True);
        Assert.That(flat.GetVector("baseColorFactor"), Is.EqualTo(source.GetVector("baseColorFactor")));
        Assert.That(ReferenceEquals(flat.GetTexture("baseColorTexture"), baseTexture), Is.True);
        Assert.That(flat.GetVector("baseColorTexture_ST"), Is.EqualTo(source.GetVector("baseColorTexture_ST")));
        Assert.That(flat.GetFloat("_Cull"), Is.Zero); Assert.That(flat.GetFloat("_Surface"), Is.EqualTo(1));
        Assert.That(flat.GetFloat("_ZWrite"), Is.Zero); Assert.That(flat.renderQueue, Is.EqualTo(3000));
        Assert.That(root.transform.position, Is.EqualTo(pose));
        Assert.That(ReferenceEquals(source.GetTexture("normalTexture"), normal), Is.True);
        Assert.That(ReferenceEquals(source.GetTexture("metallicRoughnessTexture"), metalRoughness), Is.True);
        Assert.That(source.GetVector("emissiveFactor"), Is.EqualTo(new Vector4(2, 1, .5f, 0)));
        controller.EnabledDetails = true;
        Assert.That(ReferenceEquals(renderer.sharedMaterial, source), Is.True);
        Assert.That(ReferenceEquals(renderer.sharedMaterial.GetTexture("emissiveTexture"), emission), Is.True);
        Assert.That(renderer.sharedMaterial.IsKeywordEnabled("_EMISSIVE"), Is.True);
    }

    [Test] public void OriginalSnapshotIsACopyAndForeignMaterialPointersAreRejectedInBothComparisonStates()
    {
        var source = Material(); var root = Root(source); var renderer = root.GetComponent<Renderer>();
        var foreign = new Material(source); objects.Add(foreign);
        controller.Register(root);
        Assert.That(controller.TryGetOriginalMaterials(root, renderer, out var copy), Is.True);
        copy[0] = foreign;
        Assert.That(controller.TryGetOriginalMaterials(root, renderer, out var secondCopy), Is.True);
        Assert.That(ReferenceEquals(secondCopy[0], source), Is.True);
        Assert.That(controller.HasApprovedMaterials(root, renderer), Is.True);
        renderer.sharedMaterial = foreign;
        Assert.That(controller.HasApprovedMaterials(root, renderer), Is.False);
        controller.EnabledDetails = false;
        var flat = renderer.sharedMaterial;
        Assert.That(controller.HasApprovedMaterials(root, renderer), Is.True);
        renderer.sharedMaterial = foreign;
        Assert.That(controller.HasApprovedMaterials(root, renderer), Is.False);
        renderer.sharedMaterial = flat;
        Assert.That(controller.HasApprovedMaterials(root, renderer), Is.True);
        controller.Unregister(root);
        Assert.That(controller.HasApprovedMaterials(root, renderer), Is.False);
        Assert.That(controller.TryGetOriginalMaterials(root, renderer, out var removed), Is.False);
        Assert.That(removed, Is.Null);
    }

    [Test] public void RootsRegisteredWhileOffShareOnlyTheirFactoryAndReleaseTheLastVariantReference()
    {
        var source = Material(); var factory = new RoughnessFactory(.3f);
        controller.EnabledDetails = false;
        var first = Root(source); var second = Root(source);
        controller.Register(first, false, factory); var flat = first.GetComponent<Renderer>().sharedMaterial;
        controller.Register(second, false, factory);
        Assert.That(ReferenceEquals(flat, second.GetComponent<Renderer>().sharedMaterial), Is.True);
        Assert.That(factory.calls, Is.EqualTo(1));
        controller.Unregister(first);
        Assert.That(ReferenceEquals(first.GetComponent<Renderer>().sharedMaterial, source), Is.True);
        Assert.That(controller.RuntimeVariantCount, Is.EqualTo(1)); Assert.That(flat != null, Is.True);
        controller.Unregister(second);
        Assert.That(ReferenceEquals(second.GetComponent<Renderer>().sharedMaterial, source), Is.True);
        Assert.That(controller.RuntimeVariantCount, Is.Zero); Assert.That(flat == null, Is.True);
        Assert.That(source != null, Is.True);
    }

    [Test] public void FactoryFailureRollsBackPartiallyCreatedVariantsAndRendererSlots()
    {
        var first = Material(); var second = Material(); first.SetFloat("_Metallic", .7f); second.SetFloat("_Metallic", .8f);
        var root = Root(first, second); var renderer = root.GetComponent<Renderer>();
        controller.EnabledDetails = false;
        Assert.Throws<InvalidOperationException>(() => controller.Register(root, false, new ThrowSecondFactory()));
        Assert.That(controller.RuntimeVariantCount, Is.Zero);
        var materials = renderer.sharedMaterials;
        Assert.That(ReferenceEquals(materials[0], first), Is.True); Assert.That(ReferenceEquals(materials[1], second), Is.True);
        Assert.That(first.GetFloat("_Metallic"), Is.EqualTo(.7f)); Assert.That(second.GetFloat("_Metallic"), Is.EqualTo(.8f));
        Assert.That(controller.TryGetOriginalMaterials(root, renderer, out _), Is.False);
        controller.Register(root);
        Assert.That(controller.RuntimeVariantCount, Is.EqualTo(2));
    }

    [Test] public void CustomFactoryWithDistanceFadeRejectsBeforeAllocationOrBinding()
    {
        var source = Material(); var root = Root(source); var renderer = root.GetComponent<Renderer>();
        var factory = new RoughnessFactory(.3f);
        Assert.Throws<ArgumentException>(() => controller.Register(root, true, factory));
        Assert.That(factory.calls, Is.Zero); Assert.That(controller.RuntimeVariantCount, Is.Zero);
        Assert.That(ReferenceEquals(renderer.sharedMaterial, source), Is.True);
        Assert.That(controller.TryGetOriginalMaterials(root, renderer, out _), Is.False);
    }
}
