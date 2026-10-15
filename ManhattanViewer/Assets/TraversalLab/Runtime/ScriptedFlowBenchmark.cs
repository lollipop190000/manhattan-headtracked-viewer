using System;
using System.Collections;
using System.IO;
using System.Text;
using Manhattan.Viewer;
using UnityEngine;
using WebShooter.Swing;
namespace Manhattan.TraversalLab
{
    public sealed class ScriptedFlowBenchmark : MonoBehaviour
    {
        private IEnumerator Start()
        {
            string folder=LabArguments.Get("--telemetry-dir",Path.Combine(Application.persistentDataPath,"TraversalTelemetry"));Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,"episodes-scripted-"+Guid.NewGuid().ToString("N")+".csv");
            using(var writer=new StreamWriter(path,false,new UTF8Encoding(false)))
            {
                writer.WriteLine(EpisodeMetrics.Header);
                int count=Math.Max(1,LabArguments.Int("--map-count",8));
                for(int mapIndex=0;mapIndex<count;mapIndex++)foreach(bool enabled in new[]{false,true})
                {
                    int seed=LabArguments.Int("--map-seed",1000)+mapIndex;
                    using(var map=new SeededTraversalMap(seed,false))
                    {
                        var rig=new GameObject("Scripted traversal rig");var flight=rig.AddComponent<FreeFlightController>();flight.enabled=false;
                        flight.SetPose(new Vector3(-7,1.85f,-21),Quaternion.identity);
                        var controls=new BufferedTraversalInput();var settings=WebSwingMotionInput.TraversalSettings();settings.features.SetFlow(enabled);
                        var motion=new WebSwingMotionInput(controls,flight,null,settings){AutomaticCruiseAssist=true};motion.Activate();
                        var logger=rig.AddComponent<SnagLogger>();logger.Attach(motion,folder);logger.Seed=seed;logger.Episode=mapIndex;logger.Condition=enabled?"on":"off";
                        var metrics=new EpisodeMetrics();motion.PhysicsStepped+=metrics.Observe;
                        motion.InputFireRequested+=_=>metrics.Shots++;motion.WebAttached+=_=>metrics.Attachments++;
                        for(int frame=0;frame<32*120;frame++)
                        {
                            float t=frame/120f;bool held=t>=.35f && t<2.3f;
                            controls.Movement=Vector2.up;
                            controls.LookRate=new Vector2(t>=7&&t<8 || t>=15&&t<16 || t>=23&&t<24?90:0,0);
                            var building=map.Layout.Buildings[4];Vector3 target=building.center+Vector3.up*building.size.y*.35f;
                            controls.Actions=new TraversalActions{active=true,fireHeld=held,firePressed=frame==42,jump=frame==20,
                                reel=t>=1&&t<1.5f?1:0,aim=new Ray(motion.Body.Position,target-motion.Body.Position),viewportX=.75f};
                            var c=motion.Sample(1f/120);flight.SetPose(c.cityPosition,c.cityRotation);metrics.MaxWrapPoints=Math.Max(metrics.MaxWrapPoints,motion.Route.Points.Count);
                            if(Mathf.Abs(c.cityPosition.x)>34 || Mathf.Abs(c.cityPosition.z)>34)break;
                            if(frame%240==0)yield return null;
                        }
                        // This is an input replay, not a navigation or trained-policy success claim.
                        writer.WriteLine(metrics.Row(seed,mapIndex,enabled?"on":"off",false,logger.Detector.Snags,logger.Detector.Stucks));writer.Flush();
                        Debug.Log($"SCRIPTED_FLOW seed={seed} condition={enabled} seconds={metrics.Duration:F3} snags={logger.Detector.Snags} stucks={logger.Detector.Stucks} speed={metrics.MeanSpeed:F3}");
                        motion.Collision.Dispose();rig.SetActive(false);Destroy(rig);
                    }
                    yield return null;
                }
            }
            Application.Quit(0);
        }
    }
}
