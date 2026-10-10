using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using HeadTracked.Display;
using Manhattan.Viewer;
using UnityEngine;
using UnityEngine.Rendering;

// Optional owned-Player media capture. No camera rerender, interpolation or timing claims.
public sealed class TemporaryAsyncViewCapture:MonoBehaviour
{
    private sealed class Pose:IMotionInputSource {public Vector3 position;public Quaternion rotation;public MotionCommand Sample(float dt)=>new MotionCommand {hasAbsolutePose=true,cityPosition=position,cityRotation=rotation};}
    [Serializable]private sealed class Sample {public string kind,file;public int index,unityFrame,loaded,pending,failures;public double realtime,routeElapsed,requestIssueMs,readbackLatencyMs,copyMs,backgroundWriteMs;public Vector3 city,euler,origin;public long maskedSkylineTriangles;public bool ownershipPending;}
    [Serializable]private sealed class Report {public string candidateRuntimeSourceSha256,publishedBaseRevision="633c6fc024f34ced9c1c102160bcbc90f111caa9",sourceVersion,unity,pipeline="URP17.3.0",capture="Normal visible Unity Player backbuffer; asynchronous GPU readback and background raw writes; native still screenshots";public string limitation="Visual evidence only. No frame interpolation, trimming, fast playback, webcam, desktop, model or renderer changes. Capture overhead is not performance acceptance. UI canvases hidden only for capture; current physical frame and streaming remain enabled.";public int width,height,webcamSources,readbackErrors,schedulingSkipped;public float requestedCaptureFps=30,requestedClipSeconds=16;public double captureSpanSeconds,observedCaptureFps;public Sample[] samples;}
    private ManhattanBootstrap app;private Pose input;private string output;private readonly List<Sample> samples=new List<Sample>();private readonly List<Task> writes=new List<Task>();private readonly List<RenderTexture> targets=new List<RenderTexture>();private readonly List<bool> busy=new List<bool>();private byte[] header;private int errors,skipped,inflight;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]private static void Attach(){if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MANHATTAN_ASYNC_CAPTURE")))return;var a=UnityEngine.Object.FindFirstObjectByType<ManhattanBootstrap>();if(a!=null)a.gameObject.AddComponent<TemporaryAsyncViewCapture>();}
    private IEnumerator Start()
    {
        app=GetComponent<ManhattanBootstrap>();output=Environment.GetEnvironmentVariable("MANHATTAN_ASYNC_CAPTURE");Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"frames"));if(!SystemInfo.supportsAsyncGPUReadback)throw new Exception("Async GPU readback unavailable");if(UnityEngine.Object.FindObjectsByType<PythonBridgeSource>(FindObjectsSortMode.None).Length!=0)throw new Exception("Webcam source forbidden");app.Display.TrackingEnabled=false;app.KeyboardInput.InputEnabled=false;foreach(var c in app.GetComponentsInChildren<Canvas>(true))c.enabled=false;
        input=new Pose {position=new Vector3(1190.60266f,1.304856f,3515.447998f),rotation=Quaternion.Euler(317.97827f,343.10513f,0)};app.Flight.InputSource=input;app.Flight.SetPose(input.position,input.rotation);yield return Ready();if(Screen.width!=1920||Screen.height!=1200)throw new Exception("Native1920x1200 required");
        for(int i=0;i<4;i++){var t=new RenderTexture(Screen.width,Screen.height,0,RenderTextureFormat.ARGB32);t.Create();targets.Add(t);busy.Add(false);}header=Encoding.ASCII.GetBytes("P6\n1920 1200\n255\n");
        yield return Still("native-street.png");input.position=new Vector3(1225,150,3585);input.rotation=Quaternion.Euler(35,208.5f,0);app.Flight.SetPose(input.position,input.rotation);app.Rendering.ResetHistory();yield return Ready();yield return Still("native-roof.png");
        yield return new WaitForEndOfFrame();Request("anchor","anchor.ppm",-1,0);ScreenCapture.CaptureScreenshot(Path.Combine(output,"native-anchor.png"));while(inflight>0)yield return null;
        Vector3 start=input.position;double began=Time.realtimeSinceStartupAsDouble,next=0;int index=0;
        while(true)
        {
            double elapsed=Time.realtimeSinceStartupAsDouble-began;double distance=elapsed<=8?elapsed*50:Math.Max(0,(16-elapsed)*50);input.position=start+Vector3.right*(float)distance;yield return new WaitForEndOfFrame();elapsed=Time.realtimeSinceStartupAsDouble-began;
            if(elapsed>=next){if(Request("motion","frames/frame-"+index.ToString("D5")+".ppm",index,elapsed)){index++;next=index/30.0;}else skipped++;}
            if(elapsed>=16)break;
        }
        input.position=start;app.Flight.SetPose(input.position,input.rotation);yield return Ready();yield return Still("native-route-end.png");while(inflight>0||writes.Exists(t=>!t.IsCompleted))yield return null;foreach(var t in writes)if(t.IsFaulted)throw t.Exception;if(errors>0)throw new Exception("GPU readback errors: "+errors);
        var motion=samples.FindAll(s=>s.kind=="motion");var report=new Report {candidateRuntimeSourceSha256=Environment.GetEnvironmentVariable("MANHATTAN_CAPTURE_RUNTIME_SHA"),sourceVersion=app.catalog.sourceVersion,unity=Application.unityVersion,width=Screen.width,height=Screen.height,webcamSources=0,readbackErrors=errors,schedulingSkipped=skipped,captureSpanSeconds=motion[motion.Count-1].realtime-motion[0].realtime,observedCaptureFps=(motion.Count-1)/(motion[motion.Count-1].realtime-motion[0].realtime),samples=samples.ToArray()};File.WriteAllText(Path.Combine(output,"capture-report.json"),JsonUtility.ToJson(report,true));foreach(var t in targets){t.Release();Destroy(t);}Application.Quit();
    }
    private IEnumerator Ready(){int stable=0,last=-1;double until=Time.realtimeSinceStartupAsDouble+75;while(stable<90&&Time.realtimeSinceStartupAsDouble<until){yield return null;if(app.Streamer.PendingLoads==0&&!app.Streamer.OwnershipWorkPending&&last==app.Streamer.LoadedTiles)stable++;else stable=0;last=app.Streamer.LoadedTiles;}if(stable<90||app.Streamer.Failures!=0)throw new Exception("Residency not ready");}
    private IEnumerator Still(string name){yield return new WaitForEndOfFrame();samples.Add(Describe("still",name,-1,0));ScreenCapture.CaptureScreenshot(Path.Combine(output,name));yield return new WaitForEndOfFrame();yield return new WaitForEndOfFrame();}
    private Sample Describe(string kind,string file,int index,double elapsed)=>new Sample {kind=kind,file=file,index=index,unityFrame=Time.frameCount,realtime=Time.realtimeSinceStartupAsDouble,routeElapsed=elapsed,city=app.Flight.CityPosition,euler=app.Flight.transform.eulerAngles,origin=app.Origin.Offset,loaded=app.Streamer.LoadedTiles,pending=app.Streamer.PendingLoads,failures=app.Streamer.Failures,maskedSkylineTriangles=app.Streamer.MaskedSkylineTriangles,ownershipPending=app.Streamer.OwnershipWorkPending};
    private bool Request(string kind,string file,int index,double elapsed)
    {
        int slot=busy.FindIndex(b=>!b);if(slot<0)return false;busy[slot]=true;inflight++;var s=Describe(kind,file,index,elapsed);samples.Add(s);var watch=System.Diagnostics.Stopwatch.StartNew();ScreenCapture.CaptureScreenshotIntoRenderTexture(targets[slot]);
        AsyncGPUReadback.Request(targets[slot],0,TextureFormat.RGB24,request=>
        {
            s.readbackLatencyMs=(Time.realtimeSinceStartupAsDouble-s.realtime)*1000;busy[slot]=false;inflight--;if(request.hasError){errors++;return;}var copy=System.Diagnostics.Stopwatch.StartNew();var bytes=request.GetData<byte>().ToArray();s.copyMs=copy.Elapsed.TotalMilliseconds;string path=Path.Combine(output,file);writes.Add(Task.Run(()=>{var w=System.Diagnostics.Stopwatch.StartNew();using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536)){stream.Write(header,0,header.Length);stream.Write(bytes,0,bytes.Length);}s.backgroundWriteMs=w.Elapsed.TotalMilliseconds;}));
        });s.requestIssueMs=watch.Elapsed.TotalMilliseconds;return true;
    }
}
