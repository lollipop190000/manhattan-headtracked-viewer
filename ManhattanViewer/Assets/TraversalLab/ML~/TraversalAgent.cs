using System;
using System.IO;
using System.Text;
using Manhattan.Viewer;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using WebShooter.Swing;
namespace Manhattan.TraversalLab
{
    [DefaultExecutionOrder(20)]
    public sealed class TraversalAgent : Agent
    {
        private FreeFlightController flight;
        private WebSwingMotionInput motion;
        private readonly BufferedTraversalInput controls=new BufferedTraversalInput();
        private SeededTraversalMap map;
        private SnagLogger logger;
        private EpisodeMetrics metrics;
        private StreamWriter episodeLog;
        private Vector3 goal;
        private float previousDistance,initialDistance;
        private int episodeOrdinal,seed,lastFireChoice;
        private bool lastJump,heldFire,leftHand;
        private string condition;
        private Transform rays;
        public override void Initialize()
        {
            MaxStep=0;flight=gameObject.AddComponent<FreeFlightController>();flight.enabled=false;
            var settings=WebSwingMotionInput.TraversalSettings();motion=new WebSwingMotionInput(controls,flight,null,settings){AutomaticCruiseAssist=true};
            string folder=LabArguments.Get("--telemetry-dir",Path.Combine(Application.persistentDataPath,"TraversalTelemetry"));Directory.CreateDirectory(folder);
            logger=gameObject.AddComponent<SnagLogger>();logger.Attach(motion,folder);
            episodeLog=new StreamWriter(Path.Combine(folder,"episodes-"+Guid.NewGuid().ToString("N")+".csv"),false,new UTF8Encoding(false)){AutoFlush=true};episodeLog.WriteLine(EpisodeMetrics.Header);
            rays=transform.Find("Horizontal building rays");
            motion.PhysicsStepped+=s=>{metrics?.Observe(s);if(metrics!=null)metrics.MaxWrapPoints=Math.Max(metrics.MaxWrapPoints,motion.Route.Points.Count);};
            motion.InputFireRequested+=_=>{if(metrics!=null)metrics.Shots++;};motion.WebAttached+=_=>{if(metrics!=null)metrics.Attachments++;};
        }
        public override void OnEpisodeBegin()
        {
            if(motion==null)return;map?.Dispose();controls.Clear();heldFire=lastJump=false;lastFireChoice=0;
            bool evaluation=LabArguments.Has("--evaluation");int perMap=Math.Max(1,LabArguments.Int("--episodes-per-map",8));
            int baseSeed=LabArguments.Int("--map-seed",1000);
            seed=evaluation?baseSeed+episodeOrdinal/perMap:baseSeed+episodeOrdinal+Math.Max(0,LabArguments.Int("--mlagents-port",5005)-5005)*100000;
            map=new SeededTraversalMap(seed,!LabArguments.Has("-nographics"));
            map.Layout.PickEndpoints(evaluation?episodeOrdinal%perMap:episodeOrdinal,out var start,out goal);
            var direction=Vector3.ProjectOnPlane(goal-start,Vector3.up);flight.SetPose(start,Quaternion.LookRotation(direction.sqrMagnitude>.01f?direction:Vector3.forward));motion.Activate();
            string mode=LabArguments.Get("--flow","mixed");bool enabled=mode=="on" || (mode=="mixed" && episodeOrdinal%2==0);
            motion.Body.Settings.features.SetFlow(enabled);condition=enabled?"on":"off";
            logger.Seed=seed;logger.Episode=episodeOrdinal;logger.Condition=condition;logger.Detector.Reset();metrics=new EpisodeMetrics();
            initialDistance=previousDistance=Vector3.Distance(start,goal);
        }
        public override void CollectObservations(VectorSensor sensor)
        {
            var b=motion.Body;Vector3 delta=goal-b.Position;
            sensor.AddObservation(transform.InverseTransformDirection(b.Velocity)/95f);
            sensor.AddObservation(transform.InverseTransformDirection(delta.normalized));sensor.AddObservation(Mathf.Clamp01(delta.magnitude/100f));
            for(int hand=0;hand<2;hand++)
            {
                bool selected=(hand==0)==leftHand;sensor.AddObservation(selected && b.Attached);sensor.AddObservation(selected && motion.Flying);
                sensor.AddObservation(selected && b.Attached?b.RopeLength/250f:0);
            }
            int state=(int)(b.Grounded?TraversalState.Grounded:b.State);sensor.AddOneHotObservation(state,5);
            sensor.AddObservation(b.Grounded);sensor.AddObservation(Mathf.Clamp01(b.TensionAcceleration/100f));
            sensor.AddObservation(Physics.Raycast(b.Position,transform.forward,out var hit,250,1<<30)?hit.distance/250f:1f);
            sensor.AddObservation(b.Attached?transform.InverseTransformDirection((b.Anchor-b.Position).normalized):Vector3.zero);
            sensor.AddObservation(Mathf.DeltaAngle(0,transform.eulerAngles.x)/90f);
            sensor.AddObservation(lastJump);sensor.AddObservation(lastFireChoice/2f);sensor.AddObservation(controls.Actions.reel);
        }
        public override void OnActionReceived(ActionBuffers actions)
        {
            var c=actions.ContinuousActions;var d=actions.DiscreteActions;
            controls.Movement=new Vector2(c[0],c[1]);controls.LookRate=new Vector2(c[2]*120,c[3]*90);
            int choice=d[0];bool fire=choice!=0;bool pressed=fire && (!heldFire || choice==2 && lastFireChoice!=2);
            bool jump=d[2]==1;leftHand=d[3]==0;
            controls.Actions=new TraversalActions{active=true,fireHeld=fire,firePressed=pressed,jump=jump&&!lastJump,
                reel=d[1]==1?1:d[1]==2?-1:0,aim=new Ray(motion.Body.Position,transform.forward),viewportX=leftHand?.25f:.75f};
            heldFire=fire;lastFireChoice=choice;lastJump=jump;
        }
        private void FixedUpdate()
        {
            if(motion==null || map==null)return;
            if(!Academy.Instance.IsCommunicatorOn){Debug.LogError("A trainer or evaluation client must be connected; heuristic runs are not PPO results.");Application.Quit(3);return;}
            controls.Actions.aim=new Ray(motion.Body.Position,transform.forward);
            var command=motion.Sample(Time.fixedDeltaTime);flight.SetPose(command.cityPosition,command.cityRotation);
            if(rays!=null)rays.rotation=Quaternion.Euler(0,transform.eulerAngles.y,0);
            float distance=Vector3.Distance(motion.Body.Position,goal);
            // Event counts are deliberately absent from the reward.
            AddReward((previousDistance-distance)/Mathf.Max(1,initialDistance)-.002f*Time.fixedDeltaTime);previousDistance=distance;
            bool success=distance<2f;
            bool lost=Mathf.Abs(command.cityPosition.x)>34 || Mathf.Abs(command.cityPosition.z)>34 || command.cityPosition.y < -5;
            bool timeout=metrics.Duration>=LabArguments.Float("--episode-seconds",45);
            if(success || lost || timeout)
            {
                if(success)AddReward(2);else if(lost)AddReward(-1);
                episodeLog.WriteLine(metrics.Row(seed,episodeOrdinal,condition,success,logger.Detector.Snags,logger.Detector.Stucks));
                var stats=Academy.Instance.StatsRecorder;
                stats.Add("Traversal/SuccessRate",success?1:0);stats.Add("Traversal/EpisodeSeconds",metrics.Duration);
                stats.Add("Traversal/MeanSpeed",metrics.MeanSpeed);if(success)stats.Add("Traversal/GoalSeconds",metrics.Duration);
                episodeOrdinal++;if(timeout)EpisodeInterrupted();else EndEpisode();
            }
        }
        private void OnDestroy(){map?.Dispose();episodeLog?.Dispose();motion?.Collision.Dispose();}
    }
}
