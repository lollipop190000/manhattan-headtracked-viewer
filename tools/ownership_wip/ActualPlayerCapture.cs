using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HeadTracked.Display;
using Manhattan.Viewer;
using UnityEngine;

// Private capture harness only. Captures the actual normally rendered Player backbuffer.
// No imported asset, material, mesh, lighting, streaming or rendering policy changes.
public sealed class TemporaryCurrentViewCapture : MonoBehaviour
{
    private sealed class PoseInput : IMotionInputSource
    {
        public Vector3 position; public Quaternion rotation;
        public MotionCommand Sample(float dt)=>new MotionCommand {hasAbsolutePose=true,cityPosition=position,cityRotation=rotation};
    }
    [Serializable] private sealed class Sample
    {
        public string kind,file;public int index,unityFrame,loaded,pending,failures;
        public double realtime,routeElapsed,readbackWriteSeconds;public Vector3 city,euler,origin;
        public long maskedSkylineTriangles;public bool ownershipPending;
    }
    [Serializable] private sealed class Report
    {
        public string publishedBaseRevision="c8f3904b17ec1cd4d3471bd59e198ce10d429957";
        public string candidateRuntimeSourceSha256="b99f29aea6e80386bb2d3baf09ca22a043c6f16a491eddd59362f40b9ca27710";
        public string frozenSourceManifestSha256="58083ba937f310d0ef6eb85d4dc836e30ed93a372e374262d836b690741f1ad7";
        public string sourceVersion,pipeline="URP17.3.0",unity,capture="Actual visible Player backbuffer via ScreenCapture.CaptureScreenshotIntoRenderTexture; no extra camera render request";
        public string cameraPath="Street fixed (1190.60266,1.304856,3515.447998), Euler(317.97827,343.10513,0). Roof fixed(1225,150,3585), Euler(35,208.5,0). Continuous eastward roof path x1225->1875 at50m/s over13s, y150,z3585 and same rotation; normal streaming/origin shifts remain enabled.";
        public string limitation="Capture GPU readback, raw frame disk writes and still PNG encoding perturb pacing. This is visual evidence, not performance acceptance. Original performance gates remain failed. No webcam, desktop, generated imagery, asset/model/PBR/renderer changes or claimed reproduction of original aerial flicker. UI canvases hidden for capture only; current physical frame remains enabled. No published-source A/B claim.";
        public int width,height,webcamSources;public bool tracking=false,streamerEnabled,taa;
        public float requestedCaptureFps=15,requestedClipSeconds=13;public double captureSpanSeconds,observedCaptureFps;
        public Sample[] samples;
    }
    private ManhattanBootstrap app;private PoseInput input;private string output;
    private RenderTexture target;private Texture2D image;private byte[] raw,header;
    private readonly List<Sample> samples=new List<Sample>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Attach()
    {
        if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MANHATTAN_CURRENT_CAPTURE")))return;
        var a=UnityEngine.Object.FindFirstObjectByType<ManhattanBootstrap>();if(a!=null)a.gameObject.AddComponent<TemporaryCurrentViewCapture>();
    }
    private IEnumerator Start()
    {
        app=GetComponent<ManhattanBootstrap>();output=Environment.GetEnvironmentVariable("MANHATTAN_CURRENT_CAPTURE");Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"frames"));
        if(UnityEngine.Object.FindObjectsByType<PythonBridgeSource>(FindObjectsSortMode.None).Length!=0)throw new InvalidOperationException("Webcam source must be absent");
        app.Display.TrackingEnabled=false;app.KeyboardInput.InputEnabled=false;
        foreach(var canvas in app.GetComponentsInChildren<Canvas>(true))canvas.enabled=false;
        input=new PoseInput {position=new Vector3(1190.60266f,1.304856f,3515.447998f),rotation=Quaternion.Euler(317.97827f,343.10513f,0)};
        app.Flight.InputSource=input;app.Flight.SetPose(input.position,input.rotation);
        if(!app.Rendering.targetCamera.enabled||app.Rendering.targetCamera.targetTexture!=null)throw new InvalidOperationException("Normal Player camera required");
        yield return Ready();
        if(Screen.width!=1920||Screen.height!=1200)throw new InvalidOperationException("Capture must be actual1920x1200");
        target=new RenderTexture(Screen.width,Screen.height,0,RenderTextureFormat.ARGB32);target.Create();
        image=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);raw=new byte[Screen.width*Screen.height*3];header=Encoding.ASCII.GetBytes("P6\n"+Screen.width+" "+Screen.height+"\n255\n");
        yield return Still("street.png");
        input.position=new Vector3(1225,150,3585);input.rotation=Quaternion.Euler(35,208.5f,0);app.Flight.SetPose(input.position,input.rotation);app.Rendering.ResetHistory();
        yield return Ready();yield return Still("roof.png");
        Vector3 start=input.position;double began=Time.realtimeSinceStartupAsDouble,next=0;int index=0;
        while(true)
        {
            double elapsed=Time.realtimeSinceStartupAsDouble-began;input.position=start+Vector3.right*(float)Math.Min(650,elapsed*50);
            yield return new WaitForEndOfFrame();elapsed=Time.realtimeSinceStartupAsDouble-began;
            if(elapsed>=next)
            {
                string file="frames/frame-"+index.ToString("D5")+".ppm";var s=Describe("motion",file,index,elapsed);double before=Time.realtimeSinceStartupAsDouble;ReadBack();
                using(var stream=new FileStream(Path.Combine(output,file),FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536))
                {stream.Write(header,0,header.Length);int row=Screen.width*3;for(int y=Screen.height-1;y>=0;y--)stream.Write(raw,y*row,row);}
                s.readbackWriteSeconds=Time.realtimeSinceStartupAsDouble-before;samples.Add(s);index++;next=elapsed+1.0/15;
            }
            if(elapsed>=13)break;
        }
        yield return Still("route-end.png");
        var first=samples.Find(s=>s.kind=="motion");Sample last=null;int count=0;foreach(var s in samples)if(s.kind=="motion"){last=s;count++;}
        var report=new Report {sourceVersion=app.catalog.sourceVersion,unity=Application.unityVersion,width=Screen.width,height=Screen.height,webcamSources=UnityEngine.Object.FindObjectsByType<PythonBridgeSource>(FindObjectsSortMode.None).Length,streamerEnabled=app.Streamer.enabled,taa=app.Preferences.temporalAA,captureSpanSeconds=last.realtime-first.realtime,observedCaptureFps=(count-1)/(last.realtime-first.realtime),samples=samples.ToArray()};
        File.WriteAllText(Path.Combine(output,"capture-report.json"),JsonUtility.ToJson(report,true));target.Release();Destroy(target);Destroy(image);yield return null;Application.Quit();
    }
    private IEnumerator Ready()
    {
        int stable=0,last=-1;double until=Time.realtimeSinceStartupAsDouble+75;
        while(stable<90&&Time.realtimeSinceStartupAsDouble<until){yield return null;if(app.Streamer.PendingLoads==0&&!app.Streamer.OwnershipWorkPending&&app.Streamer.LoadedTiles==last)stable++;else stable=0;last=app.Streamer.LoadedTiles;}
        if(stable<90||app.Streamer.Failures!=0)throw new InvalidOperationException("Capture residency did not stabilize");
    }
    private void ReadBack()
    {
        ScreenCapture.CaptureScreenshotIntoRenderTexture(target);var previous=RenderTexture.active;
        try {RenderTexture.active=target;image.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0,false);image.Apply(false,false);image.GetRawTextureData<byte>().CopyTo(raw);}finally {RenderTexture.active=previous;}
    }
    private IEnumerator Still(string file)
    {
        yield return new WaitForEndOfFrame();var s=Describe("still",file,-1,0);double before=Time.realtimeSinceStartupAsDouble;ReadBack();File.WriteAllBytes(Path.Combine(output,file),image.EncodeToPNG());ScreenCapture.CaptureScreenshot(Path.Combine(output,"native-"+file));s.readbackWriteSeconds=Time.realtimeSinceStartupAsDouble-before;samples.Add(s);yield return new WaitForEndOfFrame();yield return new WaitForEndOfFrame();
    }
    private Sample Describe(string kind,string file,int index,double elapsed)=>new Sample {kind=kind,file=file,index=index,unityFrame=Time.frameCount,realtime=Time.realtimeSinceStartupAsDouble,routeElapsed=elapsed,city=app.Flight.CityPosition,euler=app.Flight.transform.eulerAngles,origin=app.Origin.Offset,loaded=app.Streamer.LoadedTiles,pending=app.Streamer.PendingLoads,failures=app.Streamer.Failures,maskedSkylineTriangles=app.Streamer.MaskedSkylineTriangles,ownershipPending=app.Streamer.OwnershipWorkPending};
}
