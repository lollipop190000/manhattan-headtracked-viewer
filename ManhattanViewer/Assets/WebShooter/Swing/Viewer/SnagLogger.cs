using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
namespace WebShooter.Swing
{
    public sealed class SnagLogger : MonoBehaviour
    {
        public SnagDetector Detector { get; } = new SnagDetector();
        public string FilePath { get; private set; }
        public int Seed { get; set; }
        public int Episode { get; set; }
        public string Condition { get; set; } = "live";
        private StreamWriter writer;
        private WebSwingMotionInput motion;
        private string session;
        private float nextFlush;
        public void Configure(string directory=null)
        {
            Close();session=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N");
            directory=directory??Path.Combine(Application.persistentDataPath,"TraversalTelemetry");Directory.CreateDirectory(directory);
            FilePath=Path.Combine(directory,"snags-"+session+".csv");
            writer=new StreamWriter(new FileStream(FilePath,FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(false));
            writer.WriteLine("session,time,x,y,z,event,state,collider,seed,episode,condition");writer.Flush();
            Detector.Detected-=Record;Detector.Detected+=Record;
        }
        private void Start(){if(writer==null)Configure();}
        public void Attach(WebSwingMotionInput source,string directory=null)
        {
            if(motion!=null)motion.PhysicsStepped-=Observe;motion=source;
            if(writer==null)Configure(directory);motion.PhysicsStepped+=Observe;
        }
        public void Observe(TraversalSample sample)=>Detector.Observe(sample);
        private void Record(SnagEvent e)
        {
            if(writer==null)return;var s=e.sample;var c=CultureInfo.InvariantCulture;
            writer.WriteLine(string.Join(",",session,s.time.ToString("F6",c),s.position.x.ToString("F6",c),s.position.y.ToString("F6",c),
                s.position.z.ToString("F6",c),e.kind.ToString().ToLowerInvariant(),s.state.ToString(),TraversalCsv.Quote(s.collider),
                Seed.ToString(c),Episode.ToString(c),TraversalCsv.Quote(Condition)));writer.Flush();
        }
        private void Update(){if(writer!=null && Time.unscaledTime>=nextFlush){writer.Flush();nextFlush=Time.unscaledTime+1;}}
        private void Close(){writer?.Dispose();writer=null;}
        private void OnDestroy(){if(motion!=null)motion.PhysicsStepped-=Observe;Detector.Detected-=Record;Close();}
    }
}
