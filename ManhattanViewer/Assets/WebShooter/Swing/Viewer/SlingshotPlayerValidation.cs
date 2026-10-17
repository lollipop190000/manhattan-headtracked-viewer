using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Manhattan.Viewer;
using UnityEngine;
namespace WebShooter.Swing
{
    public sealed class SlingshotPlayerValidation : MonoBehaviour
    {
        [Serializable] private sealed class Report
        {
            public bool completed,passed,humanTuningVerified;public string error="",unityVersion;
            public List<string> checks=new List<string>();
            public List<SlingshotChecks.Metric> metrics=new List<SlingshotChecks.Metric>();
            public List<AirChainReplay.Result> cases=new List<AirChainReplay.Result>();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install(){if(ManhattanBootstrap.HasArgument("--validate-slingshot"))new GameObject("Slingshot validation").AddComponent<SlingshotPlayerValidation>();}
        private IEnumerator Start()
        {
            yield return null;var report=new Report{unityVersion=Application.unityVersion};
            TraversalTuning tuning=null;
            for(int wait=0;wait<120;wait++){tuning=FindFirstObjectByType<TraversalTuning>();if(tuning!=null)break;yield return null;}
            report.humanTuningVerified=tuning!=null && tuning.Settings.chain.enabled && tuning.Launch.enabled && Mathf.Abs(tuning.Launch.maximumSpeed-82)<.001f;
            string folder=Path.Combine(Application.persistentDataPath,"Slingshot");var args=Environment.GetCommandLineArgs();
            int index=Array.IndexOf(args,"--slingshot-report");if(index>=0 && index+1<args.Length)folder=args[index+1];Directory.CreateDirectory(folder);
            void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);report.checks.Add(name);}
            void Metric(string name,float value)=>report.metrics.Add(new SlingshotChecks.Metric{name=name,value=value});
            try{SlingshotChecks.Run(Check,Metric);SlingshotChecks.Corner(Check,Metric);SlingshotSafetyChecks.Run(Check,Metric);}
            catch(Exception e){report.error=e.ToString();}
            using var trace=new StreamWriter(Path.Combine(folder,"trace.csv"));
            trace.WriteLine("route,geometry,roadWidth,before,after,groundLaunch,seed,fps,time,cycle,x,y,z,vx,vy,vz,attached,ropeLength,anchorX,anchorY,anchorZ,contacts,resets");
            foreach(float half in new[]{15f,22f,30f})foreach(int fps in new[]{30,60,120})foreach(bool ground in new[]{false,true})
            foreach(var stage in Stages())for(int seed=0;seed<3;seed++)
            {
                using var map=new AirChainReplay.Map(half,grid:stage.z!=0,gridSpacing:stage.y==2?100:180);
                try
                {
                    var r=AirChainReplay.Run(map,stage.x,stage.y,seed,fps,f=>Write(trace,f,map.Center),turnSign:stage.w,groundLaunch:ground,zigzag:stage.z!=0,modernPilot:true);
                    report.cases.Add(r);
                }
                catch(Exception e){report.error=e.ToString();}
                yield return null;
            }
            // The catalog remains a separate footprint/PhysX fixture, not streamed triangle validation.
            var catalog=Resources.Load<CityCatalog>("CityCatalog");
            foreach(int fps in new[]{30,60,120})foreach(int sign in new[]{-1,1})foreach(int n in new[]{0,3,5,10})for(int seed=0;seed<3;seed++)
            {
                using var map=new AirChainReplay.Map(14,catalog);
                try{report.cases.Add(AirChainReplay.Run(map,n,n==0?1:n,seed,fps,f=>Write(trace,f,map.Center),turnSign:sign,modernPilot:true));}
                catch(Exception e){report.error=e.ToString();}
                yield return null;
            }
            report.completed=report.humanTuningVerified && report.error.Length==0 && report.cases.Count==1152;
            report.passed=report.completed && report.cases.All(r=>r.passed);
            File.WriteAllText(Path.Combine(folder,"validation.json"),JsonUtility.ToJson(report,true));
            var fields=typeof(AirChainReplay.Result).GetFields();using(var csv=new StreamWriter(Path.Combine(folder,"cases.csv")))
            {
                csv.WriteLine(string.Join(",",fields.Select(f=>f.Name)));
                foreach(var r in report.cases)csv.WriteLine(string.Join(",",fields.Select(f=>TraversalCsv.Quote(Convert.ToString(f.GetValue(r),CultureInfo.InvariantCulture)))));
            }
            trace.Flush();Debug.Log($"SLINGSHOT passed={report.cases.Count(r=>r.passed)}/{report.cases.Count} checks={report.checks.Count} error={report.error}");
            Application.Quit(report.passed?0:1);
        }
        public readonly struct Stage
        {
            public readonly int x,y,z,w;
            public Stage(int before,int after,int grid,int sign){x=before;y=after;z=grid;w=sign;}
        }
        public static IEnumerable<Stage> Stages()
        {
            foreach(int n in new[]{2,3,5,10})yield return new Stage(n,0,0,1);
            foreach(int sign in new[]{-1,1})
            {
                foreach(int n in new[]{0,3,5,10})yield return new Stage(n,n==0?1:n,0,sign);
                foreach(int n in new[]{0,3})foreach(int count in new[]{2,6})yield return new Stage(n,count,1,sign);
            }
        }
        private static void Write(StreamWriter writer,AirChainReplay.Frame f,Vector3 center)
        {
            var p=f.position-center;var v=f.velocity;var a=f.anchor-center;var r=f.run;
            writer.WriteLine(FormattableString.Invariant($"{r.route},{r.geometry},{r.roadWidth},{r.beforeTurn},{r.afterTurn},{r.groundLaunch},{r.seed},{r.fps},{f.time},{f.cycle},{p.x},{p.y},{p.z},{v.x},{v.y},{v.z},{f.attached},{f.ropeLength},{a.x},{a.y},{a.z},{f.contacts},{f.resets}"));
        }
    }
}
