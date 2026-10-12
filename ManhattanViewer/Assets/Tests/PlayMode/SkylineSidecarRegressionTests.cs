using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Manhattan.Viewer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

public sealed class SkylineSidecarRegressionTests
{
    private readonly List<Object> objects=new List<Object>();
    private SkylineGeometryOwnership ownership;private GameObject sky,core,source;private Mesh original;
    private CityTileRecord skyTile,coreTile;private SkylineOwnershipData data;
    private GameObject Root(string name,int[] indices,Material material)
    {
        var root=new GameObject(name);objects.Add(root);
        var mesh=new Mesh {name=name+" source",vertices=new[]{Vector3.zero,Vector3.right,Vector3.up,Vector3.right*10,Vector3.right*11,Vector3.right*10+Vector3.up},triangles=indices};objects.Add(mesh);
        root.AddComponent<MeshFilter>().sharedMesh=mesh;root.AddComponent<MeshRenderer>().sharedMaterial=material;return root;
    }
    [SetUp]public void Setup()
    {
        ownership=new SkylineGeometryOwnership();var material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="Skyline"};objects.Add(material);
        material.SetFloat("_Surface",0);material.SetFloat("_AlphaClip",0);material.SetFloat("_Cull",2);material.SetFloat("_ZWrite",1);
        sky=Root("sky",new[]{0,1,2,3,4,5},material);source=Root("core",new[]{0,1,2},material);core=Object.Instantiate(source);objects.Add(core);
        original=sky.GetComponent<MeshFilter>().sharedMesh;
        skyTile=new CityTileRecord {id="sky",address="sky-address",layer=CityLayer.Skyline,requiredReplacementCount=2};
        coreTile=new CityTileRecord {id="core",address="core-address",layer=CityLayer.Core,replaces=new[]{"sky"}};
        data=OwnershipTestData.Compile(new[]{skyTile,coreTile},new[]{sky,core});
    }
    private void Register(Func<string,string> signature=null)
    {
        ownership.Configure(data,"fixture",signature??(_=>"fixture-content"));
        ownership.Register(skyTile,sky);ownership.Register(coreTile,core,source);ownership.SetDesired(core,true);
    }
    private void Drain(){for(int i=0;i<1000&&ownership.Busy;i++)ownership.Tick(1000);Assert.That(ownership.Busy,Is.False);}
    [TestCase(false)]
    [TestCase(true)]
    public void SubMillimetreBoundaryMatchRetiresOnlyCoveredWallAndRestoresBeforeDeparture(bool surfaceOff)
    {
        var fallbackVertices = new[] {
            new Vector3(1018.458984375f,.22f,3234.323974609375f),
            new Vector3(1018.458984375f,182.8800048828125f,3234.323974609375f),
            new Vector3(989.177978515625f,182.8800048828125f,3250.080078125f),
            Vector3.right*10,Vector3.right*11,Vector3.right*10+Vector3.up};
        var replacementVertices = (Vector3[])fallbackVertices.Clone();
        replacementVertices[0]=new Vector3(1018.4586791992188f,.22f,3234.323486328125f);
        replacementVertices[1]=new Vector3(1018.4586791992188f,182.8800048828125f,3234.323486328125f);
        replacementVertices[2]=new Vector3(989.17822265625f,182.8800048828125f,3250.080078125f);
        original.vertices=fallbackVertices;source.GetComponent<MeshFilter>().sharedMesh.vertices=replacementVertices;
        var sourceMesh=source.GetComponent<MeshFilter>().sharedMesh;
        var sourceIndices=sourceMesh.triangles;var originalIndices=original.triangles;
        var originalMaterials=sky.GetComponent<MeshRenderer>().sharedMaterials;
        data=OwnershipTestData.Compile(new[]{skyTile,coreTile},new[]{sky,core});
        ownership.Configure(data,"fixture",_=>"fixture-content");
        ownership.Register(skyTile,sky);ownership.Register(coreTile,core,source);
        var surfaces=sky.AddComponent<SurfaceDetailComparison>();surfaces.EnabledDetails=!surfaceOff;surfaces.Register(sky);
        ownership.SetDesired(core,true);Drain();
        Assert.That(ownership.MaskedTriangles,Is.EqualTo(1),"The exact covered wall remains eligible across a 1mm bucket boundary.");
        Assert.That(sky.GetComponent<MeshFilter>().sharedMesh.triangles,Is.EqualTo(new[]{3,4,5}),"The unrelated fallback stays present.");
        Assert.That(core.activeSelf,Is.True);Assert.That(core.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(sourceMesh));
        ownership.SetDesired(core,false);Assert.That(ownership.CanRelease(core),Is.False);Drain();
        Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));Assert.That(core.activeSelf,Is.False);
        Assert.That(ownership.CanRelease(core),Is.True);
        ownership.SetDesired(core,true);Drain();Assert.That(ownership.MaskedTriangles,Is.EqualTo(1));
        ownership.SetDesired(core,false);Drain();surfaces.Unregister(sky);
        Assert.That(original.vertices,Is.EqualTo(fallbackVertices));Assert.That(original.triangles,Is.EqualTo(originalIndices));
        Assert.That(sourceMesh.vertices,Is.EqualTo(replacementVertices));Assert.That(sourceMesh.triangles,Is.EqualTo(sourceIndices));
        Assert.That(sky.GetComponent<MeshRenderer>().sharedMaterials,Is.EqualTo(originalMaterials));
    }
    [TestCase(1,true)]
    [TestCase(2,false)]
    public void CompleteReplacementRetiresSameQuadWithDifferentDiagonalOnlyForExactCatalogCohort(int required,bool complete)
    {
        var quad=new[]{Vector3.zero,Vector3.right,Vector3.right+Vector3.up,Vector3.up};
        original.Clear();original.vertices=quad;original.triangles=new[]{0,1,2,0,2,3};
        var detailed=source.GetComponent<MeshFilter>().sharedMesh;detailed.Clear();detailed.vertices=quad;detailed.triangles=new[]{0,1,3,1,2,3};
        skyTile.requiredReplacementCount=required;var catalog=ScriptableObject.CreateInstance<CityCatalog>();objects.Add(catalog);catalog.sourceVersion="fixture";catalog.tiles=new[]{skyTile,coreTile};
        data=OwnershipTestData.Compile(catalog.tiles,new[]{sky,core});Assert.That(data.groups[0].parts,Is.Empty,"Different diagonals share no complete triangle");
        ownership.Configure(data,"fixture",_=>"fixture-content",catalog);ownership.Register(skyTile,sky);ownership.Register(coreTile,core,source);ownership.SetDesired(core,true);Drain();
        Assert.That(ownership.MaskedTriangles,Is.EqualTo(complete?2:0));Assert.That(sky.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0),Is.EqualTo(complete?0:6));
        Assert.That(original.triangles,Is.EqualTo(new[]{0,1,2,0,2,3}));Assert.That(core.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(detailed));
        ownership.SetDesired(core,false);Drain();Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));Assert.That(core.activeSelf,Is.False);
    }
    [TestCase("mesh")]
    [TestCase("material")]
    [TestCase("relation")]
    public void CompleteCohortMutationBeforePublicationRestoresFullFallback(string mutation)
    {
        skyTile.requiredReplacementCount=1;var catalog=ScriptableObject.CreateInstance<CityCatalog>();objects.Add(catalog);catalog.sourceVersion="fixture";catalog.tiles=new[]{skyTile,coreTile};
        ownership.Configure(data,"fixture",_=>"fixture-content",catalog);ownership.Register(skyTile,sky);ownership.Register(coreTile,core,source);ownership.SetDesired(core,true);
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;var entries=(IDictionary)typeof(SkylineGeometryOwnership).GetField("entries",flags).GetValue(ownership);var snapshot=(IList)typeof(SkylineGeometryOwnership).GetField("snapshot",flags).GetValue(ownership);foreach(var entry in entries.Values)snapshot.Add(entry);
        var work=(IEnumerator)typeof(SkylineGeometryOwnership).GetMethod("Reconcile",flags).Invoke(ownership,null);typeof(SkylineGeometryOwnership).GetField("work",flags).SetValue(ownership,work);
        for(int i=0;i<1000&&ownership.RuntimeMeshCount==0;i++)Assert.That(work.MoveNext(),Is.True);Assert.That(ownership.RuntimeMeshCount,Is.EqualTo(1));
        if(mutation=="mesh")core.GetComponent<MeshFilter>().sharedMesh=null;
        else if(mutation=="material"){var material=new Material(source.GetComponent<MeshRenderer>().sharedMaterial);objects.Add(material);material.SetFloat("_AlphaClip",1);core.GetComponent<MeshRenderer>().sharedMaterial=material;}
        else coreTile.replaces=Array.Empty<string>();
        LogAssert.Expect(LogType.Warning,new Regex("Core coverage changed during preparation|Complete replacement cohort changed during preparation"));Drain();
        Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));Assert.That(ownership.MaskedTriangles,Is.Zero);Assert.That(ownership.RuntimeMeshCount,Is.Zero);
    }
    [TestCase(false)]
    [TestCase(true)]
    public void CompleteCatalogCohortRetiresUnmatchedHullAndRestoresItBeforeOwnerDeparture(bool surfaceOff)
    {
        var otherSource=Root("other core",new[]{3,4,5},source.GetComponent<MeshRenderer>().sharedMaterial);
        var vertices=otherSource.GetComponent<MeshFilter>().sharedMesh.vertices;for(int i=3;i<6;i++)vertices[i]+=Vector3.forward*.0012f;
        otherSource.GetComponent<MeshFilter>().sharedMesh.vertices=vertices;
        var other=Object.Instantiate(otherSource);objects.Add(other);
        var otherTile=new CityTileRecord {id="other",address="other-address",layer=CityLayer.Core,replaces=new[]{"sky"}};
        var catalog=ScriptableObject.CreateInstance<CityCatalog>();objects.Add(catalog);catalog.sourceVersion="fixture";catalog.tiles=new[]{skyTile,coreTile,otherTile};
        data=OwnershipTestData.Compile(catalog.tiles,new[]{sky,core,other});
        // Only the first primitive coincides exactly. Complete catalog replacement is a
        // distinct contract; do not widen spatial tolerance or discard material intent.
        ownership.Configure(data,"fixture",_=>"fixture-content",catalog);
        ownership.Register(skyTile,sky);ownership.Register(coreTile,core,source);ownership.Register(otherTile,other,otherSource);
        var surfaces=sky.AddComponent<SurfaceDetailComparison>();surfaces.EnabledDetails=!surfaceOff;surfaces.Register(sky);
        ownership.SetDesired(core,true);Drain();Assert.That(ownership.MaskedTriangles,Is.EqualTo(1));
        ownership.SetDesired(other,true);Drain();Assert.That(ownership.MaskedTriangles,Is.EqualTo(2));Assert.That(sky.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0),Is.Zero);
        Assert.That(core.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(source.GetComponent<MeshFilter>().sharedMesh));
        ownership.SetDesired(other,false);Assert.That(ownership.CanRelease(other),Is.False);Drain();
        Assert.That(other.activeSelf,Is.False);Assert.That(ownership.CanRelease(other),Is.True);Assert.That(ownership.MaskedTriangles,Is.EqualTo(1));
        Assert.That(sky.GetComponent<MeshFilter>().sharedMesh.triangles,Is.EqualTo(new[]{3,4,5}));Assert.That(original.triangles.Length,Is.EqualTo(6));
        surfaces.Unregister(sky);
    }
    [TestCase("textured")]
    [TestCase("intentional")]
    [TestCase("overlay")]
    public void CompleteCohortPreservesImportedSurfaceIntent(string kind)
    {
        var otherSource=Root("other core",new[]{3,4,5},source.GetComponent<MeshRenderer>().sharedMaterial);var other=Object.Instantiate(otherSource);objects.Add(other);
        var otherTile=new CityTileRecord {id="other",address="other-address",layer=CityLayer.Core,replaces=new[]{"sky"}};
        var special=new Material(sky.GetComponent<MeshRenderer>().sharedMaterial);objects.Add(special);
        if(kind=="textured"){var texture=new Texture2D(2,2);objects.Add(texture);special.SetTexture("_BaseMap",texture);}
        if(kind=="intentional")special.name="Intentional facade";
        if(kind=="overlay")sky.GetComponent<MeshRenderer>().sharedMaterials=new[]{sky.GetComponent<MeshRenderer>().sharedMaterial,special};else sky.GetComponent<MeshRenderer>().sharedMaterial=special;
        var catalog=ScriptableObject.CreateInstance<CityCatalog>();objects.Add(catalog);catalog.sourceVersion="fixture";catalog.tiles=new[]{skyTile,coreTile,otherTile};
        data=OwnershipTestData.Compile(catalog.tiles,new[]{sky,core,other});ownership.Configure(data,"fixture",_=>"fixture-content",catalog);
        ownership.Register(skyTile,sky);ownership.Register(coreTile,core,source);ownership.Register(otherTile,other,otherSource);ownership.SetDesired(core,true);ownership.SetDesired(other,true);Drain();
        Assert.That(ownership.MaskedTriangles,Is.Zero);Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));
    }
    [Test]public void PreviousQuantizedRulePreservesFallbackUntilAnExplicitSidecarRebuild()
    {
        data.ruleVersion="legacy-flat-hull-v4-extra-material-safe";
        Register();Drain();
        Assert.That(ownership.SidecarBindings,Is.Zero);
        Assert.That(ownership.MaskedTriangles,Is.Zero);
        Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));
        Assert.That(original.triangles,Is.EqualTo(new[]{0,1,2,3,4,5}));
    }
    [Test]public void MissingSidecarPreservesFullFallback()
    {
        ownership.Register(skyTile,sky);ownership.Register(coreTile,core,source);ownership.SetDesired(core,true);Drain();
        Assert.That(core.activeSelf,Is.True);Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));Assert.That(ownership.MaskedTriangles,Is.Zero);
    }
    [Test]public void BundleHashMismatchCannotMaskFallback()
    {
        Register(address=>address==coreTile.address?"different-content":"fixture-content");Drain();
        Assert.That(ownership.MaskedTriangles,Is.Zero);Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));
    }
    [Test]public void SourceVersionMismatchCannotMaskFallback()
    {
        data.sourceVersion="other-city";Register();Drain();Assert.That(ownership.MaskedTriangles,Is.Zero);Assert.That(ownership.SidecarBindings,Is.Zero);
    }
    [Test]public void SameNameAndCountsReplacementMeshCannotClaimSourceIdentity()
    {
        var replacement=Object.Instantiate(core.GetComponent<MeshFilter>().sharedMesh);objects.Add(replacement);replacement.name=core.GetComponent<MeshFilter>().sharedMesh.name;
        var vertices=replacement.vertices;for(int i=0;i<vertices.Length;i++)vertices[i]+=Vector3.forward*5;replacement.vertices=vertices;
        core.GetComponent<MeshFilter>().sharedMesh=replacement;Register();Drain();Assert.That(ownership.MaskedTriangles,Is.Zero);Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));
    }
    [Test]public void SourceReplacementDuringPreparationRevokesCohortAndReleasesVariants()
    {
        Register();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var entries=(IDictionary)typeof(SkylineGeometryOwnership).GetField("entries",flags).GetValue(ownership);
        var snapshot=(IList)typeof(SkylineGeometryOwnership).GetField("snapshot",flags).GetValue(ownership);foreach(var entry in entries.Values)snapshot.Add(entry);
        var work=(IEnumerator)typeof(SkylineGeometryOwnership).GetMethod("Reconcile",flags).Invoke(ownership,null);typeof(SkylineGeometryOwnership).GetField("work",flags).SetValue(ownership,work);
        for(int i=0;i<1000&&ownership.RuntimeMeshCount==0;i++)Assert.That(work.MoveNext(),Is.True);
        Assert.That(ownership.RuntimeMeshCount,Is.EqualTo(1));var replacement=Object.Instantiate(source.GetComponent<MeshFilter>().sharedMesh);objects.Add(replacement);replacement.name=source.GetComponent<MeshFilter>().sharedMesh.name;
        core.GetComponent<MeshFilter>().sharedMesh=replacement;
        LogAssert.Expect(LogType.Warning,new Regex("Core coverage changed during preparation"));Drain();
        Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));Assert.That(ownership.RuntimeMeshCount,Is.Zero);Assert.That(ownership.StagingArrayBytes,Is.Zero);Assert.That(ownership.DecodedMappingBytes,Is.Zero);
    }
    [TestCase(false)]
    [TestCase(true)]
    public void MissingCoreMeshDuringPreparationCannotPublishMask(bool removeFilter)
    {
        Register();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var entries=(IDictionary)typeof(SkylineGeometryOwnership).GetField("entries",flags).GetValue(ownership);
        var snapshot=(IList)typeof(SkylineGeometryOwnership).GetField("snapshot",flags).GetValue(ownership);foreach(var entry in entries.Values)snapshot.Add(entry);
        var work=(IEnumerator)typeof(SkylineGeometryOwnership).GetMethod("Reconcile",flags).Invoke(ownership,null);typeof(SkylineGeometryOwnership).GetField("work",flags).SetValue(ownership,work);
        for(int i=0;i<1000&&ownership.RuntimeMeshCount==0;i++)Assert.That(work.MoveNext(),Is.True);
        Assert.That(ownership.RuntimeMeshCount,Is.EqualTo(1));
        if(removeFilter)Object.DestroyImmediate(core.GetComponent<MeshFilter>());else core.GetComponent<MeshFilter>().sharedMesh=null;
        LogAssert.Expect(LogType.Warning,new Regex("Core coverage changed during preparation"));Drain();
        Assert.That(ownership.MaskedTriangles,Is.Zero);Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));Assert.That(ownership.RuntimeMeshCount,Is.Zero);Assert.That(ownership.StagingArrayBytes,Is.Zero);
    }
    [Test]public void FailureAfterLateSkyAndCoreCancellationPublishesFallbackInSameStreamerTick()
    {
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var host=new GameObject("failed late Skyline host");objects.Add(host);
        var flight=host.AddComponent<FreeFlightController>();flight.enabled=false;
        var streamer=host.AddComponent<CityStreamer>();streamer.enabled=false;streamer.flight=flight;streamer.workBudgetMilliseconds=1000;
        typeof(CityStreamer).GetField("initialized",flags).SetValue(streamer,true);
        typeof(CityStreamer).GetField("nextEvaluation",flags).SetValue(streamer,float.PositiveInfinity);
        ownership.Dispose();ownership=(SkylineGeometryOwnership)typeof(CityStreamer).GetField("ownership",flags).GetValue(streamer);
        ownership.Configure(data,"fixture",_=>"fixture-content");ownership.Register(coreTile,core,source);ownership.SetDesired(core,true);ownership.Register(skyTile,sky);
        Assert.That(sky.activeSelf,Is.False,"Late fallback starts gated until a coherent publication");
        var residents=(IList)typeof(CityStreamer).GetField("residents",flags).GetValue(streamer);
        try
        {
        var recordType=typeof(CityStreamer).GetNestedType("Residency",BindingFlags.NonPublic);
        foreach(var pair in new[]{Tuple.Create(skyTile,sky,true),Tuple.Create(coreTile,core,false)})
        {
            var record=Activator.CreateInstance(recordType,true);recordType.GetField("tile").SetValue(record,pair.Item1);recordType.GetField("instance").SetValue(record,pair.Item2);recordType.GetField("visible").SetValue(record,pair.Item3);residents.Add(record);
        }
        var entries=(IDictionary)typeof(SkylineGeometryOwnership).GetField("entries",flags).GetValue(ownership);
        var snapshot=(IList)typeof(SkylineGeometryOwnership).GetField("snapshot",flags).GetValue(ownership);foreach(var entry in entries.Values)snapshot.Add(entry);
        var work=(IEnumerator)typeof(SkylineGeometryOwnership).GetMethod("Reconcile",flags).Invoke(ownership,null);typeof(SkylineGeometryOwnership).GetField("work",flags).SetValue(ownership,work);
        for(int i=0;i<1000&&ownership.RuntimeMeshCount==0;i++)Assert.That(work.MoveNext(),Is.True);
        Assert.That(ownership.RuntimeMeshCount,Is.EqualTo(1));ownership.SetDesired(core,false);core.GetComponent<MeshFilter>().sharedMesh=null;
        LogAssert.Expect(LogType.Warning,new Regex("Core coverage changed during preparation"));
        typeof(CityStreamer).GetMethod("Update",flags).Invoke(streamer,null);
        Assert.That(core.activeSelf,Is.False);Assert.That(sky.activeSelf,Is.True);Assert.That(ownership.Publications,Is.EqualTo(1));
        Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));Assert.That(ownership.RuntimeMeshCount,Is.Zero);Assert.That(ownership.StagingArrayBytes,Is.Zero);
        }
        finally {residents.Clear();} // Synthetic records own no Addressables handles.
    }
    [TestCase(false)]
    [TestCase(true)]
    public void NonIdentityTileRootCannotUseLegacyCorrespondence(bool rotate)
    {
        if(rotate){source.transform.rotation=Quaternion.Euler(0,90,0);core.transform.rotation=source.transform.rotation;}
        else{source.transform.localScale=Vector3.one*2;core.transform.localScale=source.transform.localScale;}
        Register();Drain();Assert.That(ownership.MaskedTriangles,Is.Zero);Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));
    }
    [TestCase(true)]
    [TestCase(false)]
    public void SmallRootChangeDuringPreparationCannotPublishMask(bool rotate)
    {
        Register();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var entries=(IDictionary)typeof(SkylineGeometryOwnership).GetField("entries",flags).GetValue(ownership);
        var snapshot=(IList)typeof(SkylineGeometryOwnership).GetField("snapshot",flags).GetValue(ownership);foreach(var entry in entries.Values)snapshot.Add(entry);
        var work=(IEnumerator)typeof(SkylineGeometryOwnership).GetMethod("Reconcile",flags).Invoke(ownership,null);typeof(SkylineGeometryOwnership).GetField("work",flags).SetValue(ownership,work);
        for(int i=0;i<1000&&ownership.RuntimeMeshCount==0;i++)Assert.That(work.MoveNext(),Is.True);
        if(rotate)core.transform.rotation=Quaternion.Euler(0,.1f,0);else core.transform.localScale=new Vector3(1.000001f,1,1);
        LogAssert.Expect(LogType.Warning,new Regex("Core coverage changed during preparation"));Drain();
        Assert.That(ownership.MaskedTriangles,Is.Zero);Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));Assert.That(ownership.RuntimeMeshCount,Is.Zero);
    }
    [Test]public void PayloadIntegrityRejectsMissingOrModifiedContent()
    {
        Assert.That(SkylineOwnershipBinding.VerifyPayload("abc","ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"),Is.True);
        Assert.That(SkylineOwnershipBinding.VerifyPayload("abd","ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"),Is.False);
        Assert.That(SkylineOwnershipBinding.VerifyPayload("abc",null),Is.False);
    }
    [TestCase(false)]
    [TestCase(true)]
    public void DestroyedBoundRendererCannotPublishMaskEvenWithReplacement(bool replace)
    {
        Register();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var entries=(IDictionary)typeof(SkylineGeometryOwnership).GetField("entries",flags).GetValue(ownership);
        var snapshot=(IList)typeof(SkylineGeometryOwnership).GetField("snapshot",flags).GetValue(ownership);foreach(var entry in entries.Values)snapshot.Add(entry);
        var work=(IEnumerator)typeof(SkylineGeometryOwnership).GetMethod("Reconcile",flags).Invoke(ownership,null);typeof(SkylineGeometryOwnership).GetField("work",flags).SetValue(ownership,work);
        for(int i=0;i<1000&&ownership.RuntimeMeshCount==0;i++)Assert.That(work.MoveNext(),Is.True);
        var renderer=core.GetComponent<MeshRenderer>();var material=renderer.sharedMaterial;Object.DestroyImmediate(renderer);
        if(replace)core.AddComponent<MeshRenderer>().sharedMaterial=material;
        LogAssert.Expect(LogType.Warning,new Regex("Core coverage changed during preparation"));Drain();
        Assert.That(ownership.MaskedTriangles,Is.Zero);Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));Assert.That(ownership.RuntimeMeshCount,Is.Zero);
    }
    [Test]public void RefreshedMaterialListDetectsReplacementAfterPriorOpaquePublication()
    {
        Register();Drain();Assert.That(ownership.MaskedTriangles,Is.EqualTo(1));
        var transparent=new Material(core.GetComponent<MeshRenderer>().sharedMaterial);objects.Add(transparent);transparent.SetFloat("_Surface",1);
        core.GetComponent<MeshRenderer>().sharedMaterials=new[]{transparent};ownership.SetDesired(core,false);Drain();ownership.SetDesired(core,true);Drain();
        Assert.That(ownership.MaskedTriangles,Is.Zero);Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));
    }
    [Test]public void RefreshedMaterialListPreservesSurfaceOffOnTransitions()
    {
        Register();var surfaces=core.AddComponent<SurfaceDetailComparison>();surfaces.Register(core);
        surfaces.EnabledDetails=false;Drain();Assert.That(ownership.MaskedTriangles,Is.EqualTo(1));
        ownership.SetDesired(core,false);Drain();surfaces.EnabledDetails=true;ownership.SetDesired(core,true);Drain();
        Assert.That(ownership.MaskedTriangles,Is.EqualTo(1));Assert.That(original.triangles.Length,Is.EqualTo(6));surfaces.Unregister(core);
    }
    [Test]public void LastOwnerDepartureReleasesGroupStagingAndPrivateMeshes()
    {
        Register();Drain();Assert.That(ownership.MaskedTriangles,Is.EqualTo(1));Assert.That(ownership.StagingArrayBytes,Is.GreaterThan(0));
        ownership.SetDesired(core,false);Drain();Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));Assert.That(ownership.RuntimeMeshCount,Is.Zero);Assert.That(ownership.StagingArrayBytes,Is.Zero);
        ownership.SetDesired(core,true);Drain();Assert.That(ownership.MaskedTriangles,Is.EqualTo(1));Assert.That(ownership.RuntimeMeshCount,Is.EqualTo(1));
    }
    [Test]public void MalformedOrdinalAfterPriorPublicationRestoresAllAndReleasesPreparedGroups()
    {
        Register();Drain();Assert.That(ownership.RuntimeMeshCount,Is.EqualTo(1));ownership.SetDesired(core,false);Drain();
        data.groups[0].parts[0].owners[0].ordinals=new[]{999};ownership.SetDesired(core,true);
        LogAssert.Expect(LogType.Warning,new Regex("Ownership ordinal outside original mesh"));Drain();
        Assert.That(sky.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(original));Assert.That(ownership.RuntimeMeshCount,Is.Zero);Assert.That(ownership.StagingArrayBytes,Is.Zero);
    }
    [Test]public void MultipleOwnersRestoreOnlyAfterFinalOwnerAndKeepSourceIndices()
    {
        var second=Object.Instantiate(source);objects.Add(second);var secondTile=new CityTileRecord {id="second",address="second-address",layer=CityLayer.Core,replaces=new[]{"sky"}};
        data=OwnershipTestData.Compile(new[]{skyTile,coreTile,secondTile},new[]{sky,core,second});Register();ownership.Register(secondTile,second,source);ownership.SetDesired(second,true);Drain();
        Assert.That(ownership.MaskedTriangles,Is.EqualTo(1));ownership.SetDesired(core,false);Drain();Assert.That(ownership.MaskedTriangles,Is.EqualTo(1));
        ownership.SetDesired(second,false);Drain();Assert.That(ownership.MaskedTriangles,Is.Zero);Assert.That(ownership.RuntimeMeshCount,Is.Zero);Assert.That(original.triangles.Length,Is.EqualTo(6));
    }
    [TearDown]public void Cleanup(){ownership?.Dispose();foreach(var obj in objects)if(obj!=null)Object.Destroy(obj);objects.Clear();}
}
