using System;
using System.Collections;
using System.IO;
using Manhattan.Viewer;
using UnityEngine;
namespace WebShooter.Swing
{
    // Explicit visual QA only. Exercises the shipping adapter, then exits; never enabled during ordinary play.
    public sealed class TraversalVisualCheck : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install(){if(ManhattanBootstrap.HasArgument("--traversal-visual-check")) new GameObject("Traversal visual QA").AddComponent<TraversalVisualCheck>();}
        private IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(12);
            var app=FindFirstObjectByType<ManhattanBootstrap>();var shooter=FindFirstObjectByType<MouseWebShooter>();
            if(app==null || shooter==null || shooter.Motion==null){Application.Quit(2);yield break;}
            float readyDeadline=Time.realtimeSinceStartup+60f;
            while(app.Streamer.LoadedTiles==0 && Time.realtimeSinceStartup<readyDeadline) yield return null;
            var motion=shooter.Motion;var hands=shooter.GetComponent<TraversalHands>();shooter.enabled=false;app.Flight.enabled=false;
            motion.CanSimulate=()=>true;motion.Paused=false;
            string folder=Application.persistentDataPath;var args=Environment.GetCommandLineArgs();
            for(int i=0;i+1<args.Length;i++)if(args[i]=="--visual-output")folder=args[i+1];
            Directory.CreateDirectory(folder);
            var initialPosition=app.Flight.CityPosition;var initialRotation=app.Flight.transform.rotation;
            yield return new WaitForEndOfFrame();
            var idle=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(folder,"idle.png"),idle.EncodeToPNG());Destroy(idle);
            for(int side=0;side<2;side++)
            {
                motion.Release();app.Flight.SetPose(initialPosition,initialRotation);motion.Activate();hands.Select(side==0?.2f:.8f);
                bool hit=false;
                float deadline=Time.realtimeSinceStartup+30f;
                while(!hit && Time.realtimeSinceStartup<deadline)
                {
                    Physics.SyncTransforms();
                    for(int y=8;y>=3&&!hit;y--)for(int x=1;x<10&&!hit;x++)
                        hit=motion.HandleMouseHold(true,true,true,Camera.main.ViewportPointToRay(new Vector3(x*.1f,y*.1f,0)));
                    if(!hit) yield return new WaitForSecondsRealtime(.25f);
                }
                if(!hit){Debug.LogError("VISUAL_QA no city target");Application.Quit(3);yield break;}
                for(int frame=0;frame<90;frame++)
                {
                    var command=motion.Sample(1f/60);app.Flight.SetPose(command.cityPosition,command.cityRotation);
                    yield return new WaitForEndOfFrame();
                    if(frame==5 || frame==30)
                    {
                        string name=(side==0?"left":"right")+(frame==5?"-gesture":"-grip")+".png";
                        var shot=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(folder,name),shot.EncodeToPNG());Destroy(shot);
                    }
                }
                Debug.Log($"VISUAL_QA side={side} attached={motion.Body.Attached} speed={motion.Body.Velocity.magnitude:F2} handCount={hands.VisibleHands} wristZ={hands.SelectedWristDepth:F3} corners={motion.Route.Points.Count-1} slack={motion.Body.RopeLength-Vector3.Distance(motion.Body.Position,motion.Body.Anchor):F4}");
                motion.HandleMouseHold(true,false,false,default);
                for(int i=0;i<30;i++){var c=motion.Sample(1f/60);app.Flight.SetPose(c.cityPosition,c.cityRotation);yield return null;}
            }
            yield return new WaitForEndOfFrame();
            var released=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(folder,"released.png"),released.EncodeToPNG());Destroy(released);
            Debug.Log($"VISUAL_QA released handCount={hands.VisibleHands} speed={motion.Body.Velocity.magnitude:F2}");
            Application.Quit(0);
        }
    }
}
