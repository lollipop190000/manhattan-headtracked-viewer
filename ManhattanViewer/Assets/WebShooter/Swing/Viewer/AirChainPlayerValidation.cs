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
    // Explicit opt-in replay. Does not run or change pose in normal gameplay.
    public sealed class AirChainPlayerValidation : MonoBehaviour
    {
        [Serializable] private sealed class Report
        {
            public bool completed,controlledRegressionPassed,allRoutesPassed;
            public bool humanTuningVerified;
            public string error="",unityVersion;
            public int controlledOn,controlledOnPassed,controlledOff,controlledOffPassed,cityOn,cityOnPassed,cityOff,cityOffPassed;
            public List<AirChainReplay.Result> cases=new List<AirChainReplay.Result>();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if(ManhattanBootstrap.HasArgument("--validate-airchain"))
                new GameObject("Air chain validation").AddComponent<AirChainPlayerValidation>();
        }
        private IEnumerator Start()
        {
            yield return null;
            var report=new Report{unityVersion=Application.unityVersion};
            TraversalTuning tuning=null;
            for(int wait=0;wait<120;wait++){tuning=FindFirstObjectByType<TraversalTuning>();if(tuning!=null)break;yield return null;}
            report.humanTuningVerified=tuning!=null && tuning.Settings.chain.enabled==!ManhattanBootstrap.HasArgument("--chain-off");
            string folder=Path.Combine(Application.persistentDataPath,"AirChain",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            var args=Environment.GetCommandLineArgs();
            int index=Array.IndexOf(args,"--airchain-report");
            if(index>=0 && index+1<args.Length)folder=args[index+1];
            Directory.CreateDirectory(folder);
            using var trace=new StreamWriter(Path.Combine(folder,"trace.csv"));
            using var failureTrace=new StreamWriter(Path.Combine(folder,"failure-trace.csv"));
            const string header="geometry,assisted,before,after,seed,fps,time,cycle,x,y,z,vx,vy,vz,attached,reel,contacts,floorTouches,resets,state,ropeLength,anchorX,anchorY,anchorZ,normalX,normalY,normalZ,collider";
            trace.WriteLine(header);failureTrace.WriteLine(header);
            foreach(bool city in new[]{false,true})
            foreach(float half in city?new[]{14f}:new[]{15f,22f,30f})
            {
                var catalog=city?Resources.Load<CityCatalog>("CityCatalog"):null;
                using var map=new AirChainReplay.Map(half,catalog);
                foreach(int fps in new[]{30,60,120})foreach(var stage in AirChainReplay.Stages())
                foreach(bool assist in new[]{false,true})for(int seed=0;seed<(city?3:5);seed++)
                {
                    // Keep complete representative traces, including failures.
                    bool capture=fps==60 && seed==0 && (city || half==15) && stage.x==10;
                    var tail=new Queue<AirChainReplay.Frame>();
                    AirChainReplay.Result result=null;
                    try
                    {
                        result=AirChainReplay.Run(map,stage.x,stage.y,seed,fps,frame=>
                        {
                            if(capture)WriteFrame(trace,frame,map.Center);
                            tail.Enqueue(frame);if(tail.Count>fps*2)tail.Dequeue();
                        },assist);
                    }
                    catch(Exception e){report.error=e.ToString();}
                    if(result!=null)
                    {
                        report.cases.Add(result);
                        if(!result.passed)foreach(var frame in tail)WriteFrame(failureTrace,frame,map.Center);
                        if(report.cases.Count%100==0)Debug.Log($"AIRCHAIN_PROGRESS cases={report.cases.Count}/2160");
                    }
                    yield return null;
                }
            }
            foreach(var r in report.cases)
            {
                bool city=r.geometry!="controlled cross";
                if(city && r.assisted){report.cityOn++;if(r.passed)report.cityOnPassed++;}
                else if(city){report.cityOff++;if(r.passed)report.cityOffPassed++;}
                else if(r.assisted){report.controlledOn++;if(r.passed)report.controlledOnPassed++;}
                else{report.controlledOff++;if(r.passed)report.controlledOffPassed++;}
            }
            // Catalog diagnostics are deliberately separate from the controlled regression gate.
            report.completed=report.humanTuningVerified && report.error.Length==0 && report.controlledOn==900 && report.controlledOff==900 && report.cityOn==180 && report.cityOff==180;
            report.controlledRegressionPassed=report.completed && report.controlledOnPassed==900;
            report.allRoutesPassed=report.controlledRegressionPassed && report.cityOnPassed==180;
            File.WriteAllText(Path.Combine(folder,"validation.json"),JsonUtility.ToJson(report,true));
            var fields=typeof(AirChainReplay.Result).GetFields();
            using(var csv=new StreamWriter(Path.Combine(folder,"cases.csv")))
            {
                csv.WriteLine(string.Join(",",fields.Select(f=>f.Name)));
                foreach(var r in report.cases)csv.WriteLine(string.Join(",",fields.Select(f=>
                    "\""+Convert.ToString(f.GetValue(r),CultureInfo.InvariantCulture).Replace("\"","\"\"")+"\"")));
            }
            trace.Flush();failureTrace.Flush();
            Debug.Log($"AIRCHAIN controlled ON {report.controlledOnPassed}/{report.controlledOn}, OFF {report.controlledOffPassed}/{report.controlledOff}; catalog ON {report.cityOnPassed}/{report.cityOn}, OFF {report.cityOffPassed}/{report.cityOff}");
            Application.Quit(report.allRoutesPassed?0:1);
        }
        private static void WriteFrame(StreamWriter writer,AirChainReplay.Frame frame,Vector3 center)
        {
            var r=frame.run;var p=frame.position-center;var v=frame.velocity;var a=frame.anchor-center;var n=frame.normal;
            string collider="\""+(frame.collider??"").Replace("\"","\"\"")+"\"";
            writer.WriteLine(FormattableString.Invariant($"{r.geometry},{r.assisted},{r.beforeTurn},{r.afterTurn},{r.seed},{r.fps},{frame.time},{frame.cycle},{p.x},{p.y},{p.z},{v.x},{v.y},{v.z},{frame.attached},{frame.reel},{frame.contacts},{frame.floorTouches},{frame.resets},{frame.state},{frame.ropeLength},{a.x},{a.y},{a.z},{n.x},{n.y},{n.z},{collider}"));
        }
    }
}
