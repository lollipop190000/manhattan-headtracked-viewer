using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Manhattan.Viewer;
using UnityEngine;
using WebShooter.Swing;
namespace Manhattan.TraversalLab
{
    public sealed class PacingMeasurement : MonoBehaviour
    {
        [Serializable] private sealed class Case
        {
            public string name,outcome;
            public Vector3 anchor;
            public Vector3 initialVelocity;
            public float ratio,attachSeconds=-1,initialDrop,bottomSpeed=-1,earlyConstraintCorrection,releaseSpeedBefore,releaseSpeedAfter,riseAfterRelease;
        }
        [Serializable] private sealed class Report {public List<Case> cases=new List<Case>();}
        private IEnumerator Start()
        {
            var report=new Report();
            foreach(string label in new[]{"baseline","pacing-slack-1.08","pacing-short-0.90"})
            {
                var result=new Case{name=label,ratio=label=="pacing-short-0.90"?.9f:1.08f};
                var rig=new GameObject("Pacing measurement rig");var flight=rig.AddComponent<FreeFlightController>();flight.enabled=false;
                flight.SetPose(Vector3.up*100,Quaternion.identity);
                var settings=WebSwingMotionInput.TraversalSettings();settings.features.SetFlow(true);settings.features.SetPacing(label!="baseline");settings.features.initialLengthRatio=result.ratio;
                // Hold the actual anchor fixed for the speed/height/latency comparison.
                // Cone selection is a separate aiming experiment, not a confound here.
                settings.features.anchorAssistStrength=0;
                var motion=new WebSwingMotionInput(null,flight,null,settings);motion.Activate();
                result.initialVelocity=new Vector3(0,-1,1).normalized*30;motion.Body.Velocity=result.initialVelocity;
                // An elevated anchor block leaves the measured pendulum plane free.
                // A full-height wall blocks the far half-arc; sideways velocity instead
                // creates an orbit whose apex need not reach the release phase.
                var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Controlled elevated anchor";wall.layer=30;
                wall.transform.position=new Vector3(0,150,50);wall.transform.localScale=new Vector3(4,2,1);
                Physics.SyncTransforms();motion.WebAttached+=_=>result.attachSeconds=motion.ShotAge;
                if(!motion.TryFire(new Ray(flight.CityPosition,new Vector3(0,50,50)))){result.outcome="ray_missed";report.cases.Add(result);Destroy(rig);Destroy(wall);yield return null;continue;}
                result.anchor=motion.ShotTarget;
                float lowest=100,releaseY=0,peak=0,releaseAge=0;bool bottom=false,released=false;float previousY=0;
                for(int frame=0;frame<50*120;frame++)
                {
                    var c=motion.Sample(1f/120);flight.SetPose(c.cityPosition,c.cityRotation);lowest=Mathf.Min(lowest,c.cityPosition.y);
                    if(result.attachSeconds>=0 && motion.ShotAge<result.attachSeconds+.1f)result.earlyConstraintCorrection=Mathf.Max(result.earlyConstraintCorrection,motion.Body.RopeCorrectionMetres);
                    if(motion.Body.Attached && previousY<0 && motion.Body.Velocity.y>=0 && !bottom)
                    {bottom=true;result.bottomSpeed=motion.Body.Velocity.magnitude;result.initialDrop=100-lowest;}
                    previousY=motion.Body.Velocity.y;
                    if(bottom && !released && motion.Body.Attached && motion.Body.Velocity.y>3 && Vector3.Dot((motion.Body.Position-motion.Body.Anchor).normalized,Vector3.down)<.65f)
                    {
                        result.releaseSpeedBefore=motion.Body.Velocity.magnitude;motion.Release(true);result.releaseSpeedAfter=motion.Body.Velocity.magnitude;
                        released=true;releaseY=peak=motion.Body.Position.y;
                    }
                    if(released){releaseAge+=1f/120;peak=Mathf.Max(peak,motion.Body.Position.y);if(releaseAge>=3){result.riseAfterRelease=peak-releaseY;result.outcome="complete";break;}}
                    else if(result.attachSeconds>=0 && !motion.Body.Attached){result.outcome="rope_lost_before_release";break;}
                    if(frame%240==0)yield return null;
                }
                if(string.IsNullOrEmpty(result.outcome))result.outcome="upper_arc_not_reached";
                report.cases.Add(result);Debug.Log("PACING_RESULT "+JsonUtility.ToJson(result));
                motion.Collision.Dispose();rig.SetActive(false);wall.SetActive(false);Destroy(rig);Destroy(wall);yield return null;
            }
            string output=LabArguments.Get("--pacing-report",Path.Combine(Application.persistentDataPath,"pacing-measurement.json"));Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            File.WriteAllText(output,JsonUtility.ToJson(report,true));Application.Quit(report.cases.TrueForAll(c=>c.outcome=="complete")?0:2);
        }
    }
}
