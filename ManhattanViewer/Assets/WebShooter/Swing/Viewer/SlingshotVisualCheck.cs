using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Manhattan.Viewer;
using UnityEngine;
namespace WebShooter.Swing
{
    // Explicit rendered QA, driven through the same action adapter. No camera,
    // physics pose or tracker override is installed during ordinary gameplay.
    public sealed class SlingshotVisualCheck : MonoBehaviour
    {
        [Serializable] private sealed class Report
        {
            public bool passed;public string error="";public int hands,lines,attachments,contacts,floorTouches,resets;
            public float chargeDisplacement,launchSpeed,peakShake,settledShake,maxFrameTravel,maxMotionMilliseconds,frameP95Milliseconds;
            public float historyAtRest,historyMoving;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install(){if(ManhattanBootstrap.HasArgument("--slingshot-visual-check"))new GameObject("Slingshot rendered QA").AddComponent<SlingshotVisualCheck>();}
        private IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(12);
            var report=new Report();var app=FindFirstObjectByType<ManhattanBootstrap>();var shooter=FindFirstObjectByType<MouseWebShooter>();
            if(app==null || shooter==null || shooter.Motion==null){Application.Quit(2);yield break;}
            string folder=Path.Combine(Application.persistentDataPath,"SlingshotVisual");var args=Environment.GetCommandLineArgs();
            int index=Array.IndexOf(args,"--visual-output");if(index>=0 && index+1<args.Length)folder=args[index+1];Directory.CreateDirectory(folder);
            var motion=shooter.Motion;var hands=shooter.GetComponent<TraversalHands>();var webs=shooter.GetComponent<DualWebLaunchVisual>();
            var history=shooter.GetComponent<TraversalTemporalHistory>();report.historyAtRest=history.CurrentWeight;
            shooter.enabled=false;app.Flight.enabled=false;app.KeyboardInput.Capture(true);
            var controls=new BufferedTraversalInput();motion.InputOverride=controls;motion.CanSimulate=()=>true;motion.Paused=false;
            Vector3 start=app.Flight.CityPosition;start.y=motion.Body.FloorY;
            float yaw=app.Flight.transform.eulerAngles.y;bool found=false;
            for(int attempt=0;attempt<24;attempt++)
            {
                app.Flight.SetPose(start,Quaternion.Euler(0,yaw+attempt*15,0));motion.Activate();Physics.SyncTransforms();
                if(motion.Launch.Begin(start,app.Flight.transform.forward,app.Origin.Offset,TraversalCollision.Mask)){found=true;break;}
                yield return null;
            }
            if(!found){report.error="No bilateral city launch at the initial ground pose";Save();Application.Quit(3);yield break;}
            var timings=new List<float>();
            motion.WebAttached+=_=>report.attachments++;
            controls.Actions=new TraversalActions{active=true,launchHeld=true,aim=new Ray(start-app.Origin.Offset,app.Flight.transform.forward)};
            for(int frame=0;frame<96;frame++)
            {
                Drive();yield return new WaitForEndOfFrame();
                report.peakShake=Mathf.Max(report.peakShake,motion.Launch.FeedbackAmplitude);
                if(frame==6)Capture("01-dual-webs.png");
                if(frame==55)Capture("02-clench.png");
                if(frame==90)
                {
                    report.hands=hands.VisibleHands;report.lines=webs.VisibleLines;report.settledShake=motion.Launch.FeedbackAmplitude;
                    report.chargeDisplacement=Vector3.Distance(start,motion.Body.Position);Capture("03-ready.png");
                }
            }
            int contactsBefore=motion.Body.ContactSequence,floorBefore=motion.Body.FloorContactSequence,resetsBefore=motion.Body.ResetSequence;
            controls.Actions=new TraversalActions{active=true,launchReleased=true,aim=new Ray(motion.Body.Position-app.Origin.Offset,app.Flight.transform.forward)};
            Drive();report.launchSpeed=motion.Launch.LastLaunchVelocity.magnitude;yield return new WaitForEndOfFrame();Capture("04-release.png");
            controls.Actions=new TraversalActions{active=true};
            for(int frame=0;frame<18;frame++){Drive();yield return new WaitForEndOfFrame();}
            Capture("05-airborne.png");
            Vector3 target=motion.Launch.Left;
            controls.Actions=new TraversalActions{active=true,firePressed=true,fireHeld=true,aim=new Ray(motion.Body.Position-app.Origin.Offset,target-motion.Body.Position),viewportX=.2f};
            controls.Movement=Vector2.up;
            for(int frame=0;frame<30;frame++){Drive();yield return new WaitForEndOfFrame();if(frame==8)Capture("06-next-web.png");}
            report.historyMoving=history.CurrentWeight;
            controls.Clear();controls.Actions=new TraversalActions{active=true};for(int frame=0;frame<15;frame++){Drive();yield return new WaitForEndOfFrame();}
            Capture("07-released-flight.png");
            report.contacts=motion.Body.ContactSequence-contactsBefore;report.floorTouches=motion.Body.FloorContactSequence-floorBefore;report.resets=motion.Body.ResetSequence-resetsBefore;
            timings.Sort();report.frameP95Milliseconds=timings[Mathf.Min(timings.Count-1,Mathf.FloorToInt(timings.Count*.95f))];
            report.passed=report.hands==2 && report.lines==2 && report.chargeDisplacement<.02f && report.launchSpeed>70 &&
                report.launchSpeed<=82.01f && report.settledShake<report.peakShake*.3f && report.attachments>0 &&
                report.maxFrameTravel<3 && !motion.Body.Grounded && report.historyMoving<.56f && report.contacts==0 && report.floorTouches==0 && report.resets==0;
            Save();Application.Quit(report.passed?0:1);
            void Drive()
            {
                var previous=motion.Body.Position;var c=motion.Sample(1f/60);
                app.Flight.transform.SetPositionAndRotation(c.cityPosition-app.Origin.Offset,c.cityRotation);
                report.maxFrameTravel=Mathf.Max(report.maxFrameTravel,Vector3.Distance(previous,c.cityPosition));
                report.maxMotionMilliseconds=Mathf.Max(report.maxMotionMilliseconds,(float)motion.LastSampleMilliseconds);
                timings.Add(Time.unscaledDeltaTime*1000);
            }
            void Capture(string name){var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(folder,name),image.EncodeToPNG());Destroy(image);}
            void Save()=>File.WriteAllText(Path.Combine(folder,"visual-validation.json"),JsonUtility.ToJson(report,true));
        }
    }
}
