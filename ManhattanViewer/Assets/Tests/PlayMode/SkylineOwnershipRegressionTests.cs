using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Manhattan.Viewer;
using NUnit.Framework;
using UnityEngine;
using Object=UnityEngine.Object;

public sealed class SkylineOwnershipRegressionTests
{
    private SkylineGeometryOwnership ownership;
    private readonly List<Object> objects=new List<Object>();
    private GameObject sky,core;
    private Material material;
    private Mesh original;
    private readonly Vector3[] positions={new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(0,1,0),new Vector3(10,0,0),new Vector3(11,0,0),new Vector3(10,1,0)};
    [SetUp]public void Setup()
    {
        ownership=new SkylineGeometryOwnership();
        material=new Material(Shader.Find("Universal Render Pipeline/Lit"));objects.Add(material);
        material.name="Skyline";
        material.SetFloat("_Surface",0);material.SetFloat("_AlphaClip",0);material.SetFloat("_Cull",2);material.SetFloat("_ZWrite",1);
    }
    private GameObject Root(string name,int[] indices,Material mat=null)
    {
        var root=new GameObject(name);objects.Add(root);
        var mesh=new Mesh {name=name+" source",vertices=positions,normals=new[]{Vector3.forward,Vector3.forward,Vector3.forward,Vector3.forward,Vector3.forward,Vector3.forward},uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.zero,Vector2.right,Vector2.up},triangles=indices};objects.Add(mesh);
        root.AddComponent<MeshFilter>().sharedMesh=mesh;root.AddComponent<MeshRenderer>().sharedMaterial=mat==null?material:mat;
        return root;
    }
    private void CreatePair(bool opposite=false,Vector3 origin=default,Material fallbackMaterial=null)
    {
        sky=Root("sky",new[]{0,1,2,3,4,5},fallbackMaterial);original=sky.GetComponent<MeshFilter>().sharedMesh;
        core=Root("core",opposite?new[]{0,2,1}:new[]{0,1,2});sky.transform.position=core.transform.position=origin;
        var skyTile=new CityTileRecord {id="sky",layer=CityLayer.Skyline,requiredReplacementCount=2,cityOrigin=origin};
        var coreTile=new CityTileRecord {id="core",layer=CityLayer.Core,replaces=new[]{"sky"},cityOrigin=origin};
        OwnershipTestData.Configure(ownership,new[]{skyTile,coreTile},new[]{sky,core});
        Assert.That(ownership.Register(skyTile,sky),Is.True);
        Assert.That(ownership.Register(coreTile,core),Is.True);
    }
    private void Drain()
    {
        for(int i=0;i<1000&&ownership.Busy;i++)ownership.Tick(1000);
        Assert.That(ownership.Busy,Is.False,"Finite synthetic geometry/cohorts must finish.");
        if(sky!=null)sky.SetActive(ownership.CanShowSkyline(sky));
    }
    [Test]public void PartialReplacementMasksOnlyDemonstratedOwnerAndPreservesSourceAndPbrData()
    {
        CreatePair();ownership.SetDesired(core,true);Drain();
        var masked=sky.GetComponent<MeshFilter>().sharedMesh;
        Assert.That(core.activeSelf,Is.True);Assert.That(sky.activeSelf,Is.True);
        Assert.That(masked.triangles,Is.EqualTo(new[]{3,4,5}));Assert.That(original.triangles,Is.EqualTo(new[]{0,1,2,3,4,5}));
        Assert.That(masked.vertices,Is.EqualTo(original.vertices));Assert.That(masked.normals,Is.EqualTo(original.normals));Assert.That(masked.uv,Is.EqualTo(original.uv));Assert.That(masked.bounds,Is.EqualTo(original.bounds));
        Assert.That(sky.GetComponent<MeshRenderer>().sharedMaterial,Is.SameAs(material));Assert.That(ownership.MaskedTriangles,Is.EqualTo(1));
    }
    [Test]public void CoreRemovalPublishesRestoredFallbackBeforeReleaseCanProceed()
    {
        CreatePair();ownership.SetDesired(core,true);Drain();ownership.SetDesired(core,false);
        Assert.That(ownership.CanRelease(core),Is.False);ownership.Tick(0);
        Assert.That(core.activeSelf,Is.True);Assert.That(sky.GetComponent<MeshFilter>().sharedMesh.triangles.Length,Is.EqualTo(3));
        Drain();Assert.That(core.activeSelf,Is.False);Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));Assert.That(ownership.CanRelease(core),Is.True);
        ownership.Unregister(core);Assert.That(original.triangles.Length,Is.EqualTo(6));
    }
    [Test]public void CancelBeforePreparedPublicationRetainsFullFallback()
    {
        CreatePair();ownership.SetDesired(core,true);ownership.Tick(.00001f);ownership.SetDesired(core,false);Drain();
        Assert.That(core.activeSelf,Is.False);Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));Assert.That(ownership.MaskedTriangles,Is.Zero);
    }
    [Test]public void LateFallbackIsIndexedAgainstAlreadyActiveCoreBeforePresentation()
    {
        core=Root("core",new[]{0,1,2});sky=Root("sky",new[]{0,1,2,3,4,5});original=sky.GetComponent<MeshFilter>().sharedMesh;
        var skyTile=new CityTileRecord {id="sky",layer=CityLayer.Skyline,requiredReplacementCount=2};
        var coreTile=new CityTileRecord {id="core",layer=CityLayer.Core,replaces=new[]{"sky"}};
        OwnershipTestData.Configure(ownership,new[]{skyTile,coreTile},new[]{sky,core});
        ownership.Register(coreTile,core);ownership.SetDesired(core,true);Drain();Assert.That(core.activeSelf,Is.True);
        ownership.Register(skyTile,sky);
        Assert.That(sky.activeSelf,Is.False);Assert.That(core.activeSelf,Is.True);Assert.That(ownership.CanShowSkyline(sky),Is.False);
        Drain();Assert.That(sky.activeSelf,Is.True);Assert.That(core.activeSelf,Is.True);Assert.That(sky.GetComponent<MeshFilter>().sharedMesh.triangles,Is.EqualTo(new[]{3,4,5}));
    }
    [Test]public void OppositeWindingDoesNotProvePixelCoverage()
    {
        CreatePair(true);ownership.SetDesired(core,true);Drain();Assert.That(ownership.MaskedTriangles,Is.Zero);Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));
    }
    [Test]public void CanceledCapturedCoreCannotOverlapLateFallbackBeforeNextPublication()
    {
        CreatePair();ownership.SetDesired(core,true);
        // Deterministically stop the resumable preparation after its active cohort is captured,
        // while the first private mesh allocation yields before pointer publication.
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var entries=(IDictionary)typeof(SkylineGeometryOwnership).GetField("entries",flags).GetValue(ownership);
        var snapshot=(IList)typeof(SkylineGeometryOwnership).GetField("snapshot",flags).GetValue(ownership);
        foreach(var entry in entries.Values)snapshot.Add(entry);
        var prepared=(IEnumerator)typeof(SkylineGeometryOwnership).GetMethod("Reconcile",flags).Invoke(ownership,null);
        typeof(SkylineGeometryOwnership).GetField("work",flags).SetValue(ownership,prepared);
        for(int i=0;i<1000&&ownership.RuntimeMeshCount==0;i++)Assert.That(prepared.MoveNext(),Is.True);
        Assert.That(ownership.RuntimeMeshCount,Is.EqualTo(1));Assert.That(core.activeSelf,Is.False);
        ownership.SetDesired(core,false);
        var late=Root("late sky",new[]{0,1,2,3,4,5});
        // The same Core was captured before cancellation and also owns this late fallback.
        var coreEntry=entries[core];var tile=(CityTileRecord)coreEntry.GetType().GetField("tile").GetValue(coreEntry);tile.replaces=new[]{"sky","late"};
        ownership.Register(new CityTileRecord {id="late",layer=CityLayer.Skyline,requiredReplacementCount=2},late);
        Assert.That(late.activeSelf,Is.False);Assert.That(ownership.CanShowSkyline(late),Is.False);
        while(prepared.MoveNext())Assert.That(core.activeSelf&&ownership.CanShowSkyline(late),Is.False);
        Assert.That(core.activeSelf,Is.True,"The previously captured cohort can publish before its cancellation cohort.");
        Assert.That(ownership.CanShowSkyline(late),Is.False,"The late fallback must remain gated through that stale publication.");
        Drain();Assert.That(core.activeSelf,Is.False);Assert.That(ownership.CanShowSkyline(late),Is.True);
        Assert.That(late.GetComponent<MeshFilter>().sharedMesh.triangles.Length,Is.EqualTo(6));
    }
    [TestCase("_AlphaClip",1)]
    [TestCase("_Surface",1)]
    [TestCase("_ZWrite",0)]
    [TestCase("_Cull",1)]
    public void NonCoveringCoreMaterialKeepsFullFallback(string property,float value)
    {
        CreatePair();var special=new Material(material);objects.Add(special);special.SetFloat(property,value);core.GetComponent<MeshRenderer>().sharedMaterial=special;
        ownership.SetDesired(core,true);Drain();Assert.That(ownership.MaskedTriangles,Is.Zero);Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));
    }
    [Test]public void FloatingOriginChangesDoNotInvalidateMetreSpaceOwnersOrMutateImportedData()
    {
        CreatePair(false,new Vector3(12500,0,9000));ownership.SetDesired(core,true);Drain();
        sky.transform.position-=new Vector3(12000,0,9000);core.transform.position-=new Vector3(12000,0,9000);
        ownership.SetDesired(core,false);Drain();ownership.SetDesired(core,true);Drain();
        Assert.That(ownership.MaskedTriangles,Is.EqualTo(1));Assert.That(sky.GetComponent<MeshFilter>().sharedMesh.triangles,Is.EqualTo(new[]{3,4,5}));Assert.That(original.triangles.Length,Is.EqualTo(6));Assert.That(ownership.RuntimeMeshCount,Is.EqualTo(1));
    }
    [TestCase(true)]
    [TestCase(false)]
    public void TwoSidedFallbackKeepsCoverageThatDefaultCoreCannotDemonstrate(bool twoSidedFaces)
    {
        CreatePair();
        if(twoSidedFaces){var special=new Material(material){name="Skyline"};objects.Add(special);special.SetFloat("_Cull",0);sky.GetComponent<MeshRenderer>().sharedMaterial=special;}
        else sky.GetComponent<MeshRenderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.TwoSided;
        ownership.SetDesired(core,true);Drain();Assert.That(ownership.MaskedTriangles,Is.Zero);Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));
    }
    [TestCase("_ALPHATEST_ON")]
    [TestCase("_SURFACE_TYPE_TRANSPARENT")]
    [TestCase("_ALPHAPREMULTIPLY_ON")]
    [TestCase("_ALPHAMODULATE_ON")]
    public void NonCoveringShaderKeywordKeepsFallbackEvenIfScalarPropertiesLookOpaque(string keyword)
    {
        CreatePair();var special=new Material(material);objects.Add(special);special.EnableKeyword(keyword);core.GetComponent<MeshRenderer>().sharedMaterial=special;
        ownership.SetDesired(core,true);Drain();Assert.That(ownership.MaskedTriangles,Is.Zero);Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));
    }
    [TestCase(UnityEngine.Rendering.ShadowCastingMode.Off)]
    [TestCase(UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)]
    public void NonCoveringCoreRendererKeepsFullFallback(UnityEngine.Rendering.ShadowCastingMode mode)
    {
        CreatePair();core.GetComponent<MeshRenderer>().shadowCastingMode=mode;
        ownership.SetDesired(core,true);Drain();Assert.That(ownership.MaskedTriangles,Is.Zero);Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));
    }
    [Test]public void OpaqueCoreCanOwnNonShadowCastingFallbackWithoutRemovingAnyFallbackShadow()
    {
        CreatePair();sky.GetComponent<MeshRenderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        ownership.SetDesired(core,true);Drain();Assert.That(ownership.MaskedTriangles,Is.EqualTo(1));Assert.That(sky.GetComponent<MeshFilter>().sharedMesh.triangles,Is.EqualTo(new[]{3,4,5}));
        ownership.SetDesired(core,false);Drain();Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));Assert.That(sky.GetComponent<MeshRenderer>().shadowCastingMode,Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.Off));
    }
    [Test]public void IntentionalOpaqueFallbackMaterialIsNotRemovedByCoordinateCoincidence()
    {
        var overlay=new Material(material){name="Intentional opaque overlay"};objects.Add(overlay);CreatePair(false,default,overlay);
        ownership.SetDesired(core,true);Drain();Assert.That(ownership.MaskedTriangles,Is.Zero);Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));
    }
    [Test]public void TexturedFallbackIsPreservedEvenIfNamedLikeLegacyHull()
    {
        var textured=new Material(material){name="Skyline"};objects.Add(textured);var texture=new Texture2D(2,2);objects.Add(texture);textured.SetTexture("_BaseMap",texture);CreatePair(false,default,textured);
        ownership.SetDesired(core,true);Drain();Assert.That(ownership.MaskedTriangles,Is.Zero);Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));
    }
    [Test]public void SurfaceOffCannotEraseImportedTexturedFallbackProvenance()
    {
        var textured=new Material(material){name="Skyline"};objects.Add(textured);var texture=new Texture2D(2,2);objects.Add(texture);textured.SetTexture("_BaseMap",texture);
        CreatePair(false,default,textured);
        var surfaces=sky.AddComponent<SurfaceDetailComparison>();surfaces.EnabledDetails=false;surfaces.Register(sky);
        Assert.That(sky.GetComponent<MeshRenderer>().sharedMaterial.name,Is.EqualTo("Skyline (surface off)"));Assert.That(sky.GetComponent<MeshRenderer>().sharedMaterial.GetTexture("_BaseMap"),Is.Null);
        ownership.SetDesired(core,true);Drain();Assert.That(ownership.MaskedTriangles,Is.Zero);Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));
        surfaces.EnabledDetails=true;ownership.SetDesired(core,false);Drain();ownership.SetDesired(core,true);Drain();Assert.That(ownership.MaskedTriangles,Is.Zero);
        surfaces.EnabledDetails=false;ownership.SetDesired(core,false);Drain();ownership.SetDesired(core,true);Drain();Assert.That(ownership.MaskedTriangles,Is.Zero);surfaces.Unregister(sky);
    }
    [TestCase(false)]
    [TestCase(true)]
    public void ExtraMaterialOverlayOnLastSubmeshSurvivesSurfaceComparison(bool surfaceOff)
    {
        var overlay=new Material(material){name="Intentional overlay"};objects.Add(overlay);
        sky=Root("sky",new[]{0,1,2,3,4,5});original=sky.GetComponent<MeshFilter>().sharedMesh;
        sky.GetComponent<MeshRenderer>().sharedMaterials=new[]{material,overlay};
        core=Root("core",new[]{0,1,2});
        var skyTile=new CityTileRecord {id="sky",layer=CityLayer.Skyline,requiredReplacementCount=2};
        var coreTile=new CityTileRecord {id="core",layer=CityLayer.Core,replaces=new[]{"sky"}};
        OwnershipTestData.Configure(ownership,new[]{skyTile,coreTile},new[]{sky,core});
        ownership.Register(skyTile,sky);ownership.Register(coreTile,core);
        var surfaces=sky.AddComponent<SurfaceDetailComparison>();surfaces.EnabledDetails=!surfaceOff;surfaces.Register(sky);
        ownership.SetDesired(core,true);Drain();Assert.That(ownership.MaskedTriangles,Is.Zero);
        Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));Assert.That(original.triangles.Length,Is.EqualTo(6));
        surfaces.EnabledDetails=surfaceOff;ownership.SetDesired(core,false);Drain();ownership.SetDesired(core,true);Drain();
        Assert.That(ownership.MaskedTriangles,Is.Zero);Assert.That(sky.GetComponent<MeshRenderer>().sharedMaterials.Length,Is.EqualTo(2));
        surfaces.Unregister(sky);
    }
    [Test]public void MissingOrFailedCoreCannotRemoveUnownedFallbackAndZeroBudgetRetainsInitialSkyline()
    {
        CreatePair();ownership.Tick(0);Assert.That(ownership.CanShowSkyline(sky),Is.True);Assert.That(core.activeSelf,Is.False);Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));
        ownership.Unregister(core);Drain();Assert.That(ownership.MaskedTriangles,Is.Zero);Assert.That(sky.GetComponent<MeshFilter>().sharedMesh.triangles.Length,Is.EqualTo(6));
    }
    [Test]public void DisposingOwnershipRestoresOriginalMeshAndReleasesOnlyOwnedVariants()
    {
        CreatePair();ownership.SetDesired(core,true);Drain();Assert.That(ownership.RuntimeMeshCount,Is.EqualTo(1));ownership.Dispose();
        Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));Assert.That(ownership.RuntimeMeshCount,Is.Zero);Assert.That(material,Is.Not.Null);Assert.That(original.triangles.Length,Is.EqualTo(6));
    }
    [TearDown]public void Cleanup(){ownership?.Dispose();foreach(var o in objects)if(o!=null)Object.Destroy(o);objects.Clear();}
}
