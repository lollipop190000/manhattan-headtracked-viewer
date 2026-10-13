using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeadTracked.Display;
using Manhattan.Viewer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class CitySunlightTests
{
    private readonly List<Object> owned = new List<Object>();
    private T Own<T>(T item) where T : Object { owned.Add(item); return item; }
    [UnityTearDown] public IEnumerator Cleanup()
    {
        foreach (var item in owned) if (item != null) Object.Destroy(item);
        owned.Clear(); yield return null; yield return null;
    }

    [UnityTest] public IEnumerator CameraAndComparisonRoundTripsDoNotMoveSunOrChangeLighting()
    {
        var host = Own(new GameObject("lighting comparison fixture"));
        var camera = new GameObject("observer").AddComponent<Camera>(); camera.transform.SetParent(host.transform); camera.enabled = false;
        var profile = host.AddComponent<AfternoonRenderProfile>(); profile.Configure(camera);
        var sun = profile.Sunlight; sun.Configure();
        var plane = new GameObject("screen").transform; plane.SetParent(host.transform);
        var display = camera.gameObject.AddComponent<HeadTrackedDisplay>(); display.Configure(plane, null);
        var frame = host.AddComponent<ScreenWindowFrame>(); frame.Configure(display);
        var surfaces = host.AddComponent<SurfaceDetailComparison>();
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.SetParent(host.transform); cube.transform.position = new Vector3(50, 20, 60);
        var material = Own(new Material(Shader.Find("Universal Render Pipeline/Lit"))); cube.GetComponent<Renderer>().sharedMaterial = material; surfaces.Register(cube);
        var before = sun.Sun.transform.rotation; var ambient = RenderSettings.ambientSkyColor;
        var pipeline = GraphicsSettings.currentRenderPipeline;
        for (int state=0;state<8;state++)
        {
            camera.transform.SetPositionAndRotation(new Vector3(state*10, 2, state*20), Quaternion.Euler(state*7,state*32,0));
            display.TrackingEnabled=(state&1)!=0; frame.EffectEnabled=(state&2)!=0; surfaces.EnabledDetails=(state&4)!=0;
            yield return null; yield return null;
            Assert.That(Quaternion.Angle(before,sun.Sun.transform.rotation), Is.LessThan(.001f));
            Assert.That(sun.Moving, Is.False); Assert.That(sun.Position, Is.EqualTo(CitySunlight.DefaultPosition));
            Assert.That(RenderSettings.ambientSkyColor, Is.EqualTo(ambient)); Assert.That(GraphicsSettings.currentRenderPipeline, Is.SameAs(pipeline));
            Assert.That(cube.transform.position, Is.EqualTo(new Vector3(50,20,60)));
        }
        Assert.That(host.GetComponentsInChildren<Light>().Length, Is.EqualTo(1), "Configure must be idempotent.");
        Assert.That(sun.Sun.transform.IsChildOf(camera.transform), Is.False);
        foreach(var r in plane.GetComponentsInChildren<Renderer>()) Assert.That(r.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
        sun.SetMoving(true);
        for(int state=0;state<8;state++)
        {
            float beforePosition=sun.Position;
            display.TrackingEnabled=(state&1)!=0;frame.EffectEnabled=(state&2)!=0;surfaces.EnabledDetails=(state&4)!=0;
            yield return null;yield return null;
            Assert.That(sun.Moving,Is.True);Assert.That(sun.Position,Is.GreaterThan(beforePosition),"Comparison controls must not stop or reset an explicitly started sun.");
        }
    }

    [Test] public void MovementPauseScrubAndEndAreExplicitAndBounded()
    {
        var sun = Own(new GameObject("sun controls")).AddComponent<CitySunlight>(); sun.Configure();
        sun.Advance(30); Assert.That(sun.Position, Is.EqualTo(.6f));
        sun.SetMoving(true); sun.Advance(30); Assert.That(sun.Position, Is.EqualTo(.7f).Within(.0001));
        sun.SetMoving(false); sun.Advance(30); Assert.That(sun.Position, Is.EqualTo(.7f).Within(.0001));
        var paused = sun.Sun.transform.rotation;
        sun.SetPosition(float.NaN); sun.SetPosition(float.PositiveInfinity); Assert.That(sun.Sun.transform.rotation, Is.EqualTo(paused));
        sun.SetMoving(true); sun.Advance(float.NaN); sun.Advance(-1); Assert.That(sun.Position, Is.EqualTo(.7f).Within(.0001));
        sun.Advance(1000); Assert.That(sun.Position, Is.EqualTo(1)); Assert.That(sun.Moving, Is.False);
        sun.SetMoving(true); Assert.That(sun.Moving, Is.False, "No sudden wrap to an earlier sun.");
        sun.ResetAfternoon(); Assert.That(sun.Moving, Is.False); Assert.That(sun.Position, Is.EqualTo(.6f));
        Assert.That(Quaternion.Angle(sun.Sun.transform.rotation,Quaternion.Euler(28,225,0)), Is.LessThan(.001));
    }

    [UnityTest] public IEnumerator DestroyRestoresPreviousEnvironmentAndPipeline()
    {
        var oldSun = RenderSettings.sun; var oldMode=RenderSettings.ambientMode; var oldSky=RenderSettings.ambientSkyColor;
        var oldFog=RenderSettings.fog; var oldDensity=RenderSettings.fogDensity; var oldPipeline=QualitySettings.renderPipeline;
        var host=Own(new GameObject("owned lighting lifetime"));var camera=host.AddComponent<Camera>();camera.enabled=false;
        host.AddComponent<AfternoonRenderProfile>().Configure(camera);
        Object.Destroy(host); yield return null; yield return null;
        Assert.That(RenderSettings.sun,Is.SameAs(oldSun)); Assert.That(RenderSettings.ambientMode,Is.EqualTo(oldMode));
        Assert.That(RenderSettings.ambientSkyColor,Is.EqualTo(oldSky)); Assert.That(RenderSettings.fog,Is.EqualTo(oldFog));
        Assert.That(RenderSettings.fogDensity,Is.EqualTo(oldDensity)); Assert.That(QualitySettings.renderPipeline,Is.SameAs(oldPipeline));
    }

    [UnityTest] public IEnumerator SettingsControlsDoNotPersistOrChangeSceneAndRestartPaused()
    {
        string previous=Environment.GetEnvironmentVariable("HEADTRACKED_CALIBRATION_DIRECTORY");
        string folder=Path.Combine(Application.temporaryCachePath,"sunlight-"+Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("HEADTRACKED_CALIBRATION_DIRECTORY",folder);
        try
        {
            for(int run=0;run<2;run++)
            {
                var frameTemplate=Resources.Load<Material>("WindowFrameMaterial");
                string templateKeywords=string.Join(";",frameTemplate.shaderKeywords.OrderBy(k=>k));
                float templateReceive=frameTemplate.GetFloat("_ReceiveShadows");
                var templateColor=frameTemplate.GetColor("_BaseColor");
                var host=Own(new GameObject("sunlight UI"));host.SetActive(false);
                var app=host.AddComponent<ManhattanBootstrap>();app.catalog=Own(ScriptableObject.CreateInstance<CityCatalog>());
                host.SetActive(true); yield return null;
                var sun=app.Rendering.Sunlight; Assert.That(sun.Moving,Is.False);Assert.That(sun.Position,Is.EqualTo(.6f));
                Assert.That(app.Frame.FrameMaterial.GetFloat("_ReceiveShadows"),Is.Zero);
                Assert.That(app.Frame.FrameMaterial.IsKeywordEnabled("_RECEIVE_SHADOWS_OFF"),Is.True);
                Assert.That(app.Frame.FrameMaterial,Is.Not.SameAs(frameTemplate));
                Assert.That(frameTemplate.IsKeywordEnabled("_RECEIVE_SHADOWS_OFF"),Is.True,"The build template must retain the local shader variant.");
                Assert.That(string.Join(";",frameTemplate.shaderKeywords.OrderBy(k=>k)),Is.EqualTo(templateKeywords),"Runtime must not mutate the authored template.");
                Assert.That(frameTemplate.GetFloat("_ReceiveShadows"),Is.EqualTo(templateReceive));
                Assert.That(frameTemplate.GetColor("_BaseColor"),Is.EqualTo(templateColor));
                var city=app.Flight.CityPosition;var calibration=JsonUtility.ToJson(app.Display.Calibration);var preferences=JsonUtility.ToJson(app.Preferences);
                var buttons=host.GetComponentsInChildren<UnityEngine.UI.Button>(true);
                buttons.Single(b=>b.GetComponentInChildren<UnityEngine.UI.Text>().text=="Move sun slowly").onClick.Invoke();
                Assert.That(sun.Moving,Is.True);
                var slider=host.GetComponentsInChildren<UnityEngine.UI.Slider>(true).Single(s=>s.name=="Sun Position");
                slider.value=.8f; Assert.That(sun.Moving,Is.False);Assert.That(sun.Position,Is.EqualTo(.8f));
                buttons.Single(b=>b.GetComponentInChildren<UnityEngine.UI.Text>().text=="Reset afternoon light").onClick.Invoke();
                Assert.That(sun.Position,Is.EqualTo(.6f)); Assert.That(app.Flight.CityPosition,Is.EqualTo(city));
                Assert.That(JsonUtility.ToJson(app.Display.Calibration),Is.EqualTo(calibration)); Assert.That(JsonUtility.ToJson(app.Preferences),Is.EqualTo(preferences));
                Assert.That(Directory.Exists(folder)&&Directory.EnumerateFiles(folder).Any(),Is.False);
                sun.SetPosition(.9f);sun.SetMoving(true);Object.Destroy(host);yield return null;yield return null;
            }
        }
        finally { Environment.SetEnvironmentVariable("HEADTRACKED_CALIBRATION_DIRECTORY",previous); }
    }

    [UnityTest] public IEnumerator RenderedShadowStaysOnWorldReceiverWhenCameraMovesAndFollowsSun()
    {
        if(Application.isBatchMode||SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null) Assert.Ignore("Requires GPU Game View.");
        var host=Own(new GameObject("world shadow fixture"));
        var camera=new GameObject("shadow camera").AddComponent<Camera>();camera.transform.SetParent(host.transform);
        camera.orthographic=true;camera.orthographicSize=22;camera.nearClipPlane=.3f;camera.farClipPlane=500;camera.cullingMask=1<<29;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        var profile=host.AddComponent<AfternoonRenderProfile>();profile.Configure(camera);profile.SetTemporalAA(false);
        camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
        profile.Sunlight.Sun.cullingMask=1<<29;
        var material=Own(new Material(Shader.Find("Universal Render Pipeline/Lit")));material.SetColor("_BaseColor",Color.white);material.SetFloat("_Smoothness",0);material.SetFloat("_SpecularHighlights",0);
        var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.transform.SetParent(host.transform);ground.layer=29;
        ground.transform.position=new Vector3(0,-.5f,0);ground.transform.localScale=new Vector3(60,1,60);ground.GetComponent<Renderer>().sharedMaterial=material;
        var block=GameObject.CreatePrimitive(PrimitiveType.Cube);block.transform.SetParent(host.transform);block.layer=29;
        block.transform.position=new Vector3(0,5,0);block.transform.localScale=new Vector3(3,10,3);block.GetComponent<Renderer>().sharedMaterial=material;
        var rt=Own(new RenderTexture(384,384,32));rt.Create();camera.targetTexture=rt;
        var pixels=Own(new Texture2D(384,384,TextureFormat.RGB24,false));var previous=RenderTexture.active;
        try
        {
            var shadowPoint=new Vector3(-6.65f,0,-6.65f);var litPoint=new Vector3(6.65f,0,-6.65f);
            float firstShadow=0;
            foreach(var pos in new[]{new Vector3(0,35,-35),new Vector3(8,38,-34)})
            {
                camera.transform.position=pos;camera.transform.LookAt(Vector3.zero);
                for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();
                RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,384,384),0,0);pixels.Apply();
                float shadow=Sample(pixels,camera,shadowPoint),lit=Sample(pixels,camera,litPoint);
                Assert.That(shadow,Is.LessThan(lit*.8f),"The same world point must stay shadowed when only the camera moves.");
                if(firstShadow==0)firstShadow=shadow;else Assert.That(shadow,Is.EqualTo(firstShadow).Within(.06f));
            }
            profile.Sunlight.SetPosition(0);
            for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();
            RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,384,384),0,0);pixels.Apply();
            Assert.That(Sample(pixels,camera,shadowPoint),Is.GreaterThan(firstShadow*1.3f),"Moving sunlight must release the previous receiver from shadow.");
            Assert.That(Sample(pixels,camera,new Vector3(0,0,-3.9f)),Is.LessThan(Sample(pixels,camera,litPoint)*.8f),"Shadow must arrive at the new sun-projected location.");
        }
        finally{RenderTexture.active=previous;camera.targetTexture=null;rt.Release();}
    }
    private static float Sample(Texture2D pixels,Camera camera,Vector3 world)
    {
        var p=camera.WorldToScreenPoint(world);float sum=0;
        for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)sum+=pixels.GetPixel(Mathf.RoundToInt(p.x)+x,Mathf.RoundToInt(p.y)+y).grayscale;
        return sum/9;
    }
}
