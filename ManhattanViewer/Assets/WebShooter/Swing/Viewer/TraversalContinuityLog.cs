using System;
using System.Collections.Generic;
using System.IO;
using Manhattan.Viewer;
using UnityEngine;
namespace WebShooter.Swing
{
    // Up to240 frames in memory; disk I/O only for an anomalous pose. No historic
    // position is ever replayed into motion. Coordinates are city-space metres.
    [DefaultExecutionOrder(1200)]
    public sealed class TraversalContinuityLog : MonoBehaviour
    {
        private WebSwingMotionInput motion;private FreeFlightController flight;private FloatingOrigin origin;
        private Camera view;private Vector3 previousView;
        private struct Frame
        {
            public float time,dt,rope,shortfall,projection02,projection12;public Vector3 p,v,offset,camera;public Quaternion rotation;public TraversalState state;
            public int resets,guards,points;public string collider;
            public override string ToString()=>FormattableString.Invariant($"{time},{dt},{p.x},{p.y},{p.z},{v.x},{v.y},{v.z},{offset.x},{offset.z},{state},{resets},{guards},{rope},{shortfall},{points},{camera.x},{camera.y},{camera.z},{rotation.x},{rotation.y},{rotation.z},{rotation.w},{projection02},{projection12},{TraversalCsv.Quote(collider)}");
        }
        private readonly Queue<Frame> tail=new Queue<Frame>(256);
        private Vector3 previous;private bool sampled;private int resets,guards;private float nextReport;
        public void Configure(WebSwingMotionInput input,FreeFlightController observer,FloatingOrigin floatingOrigin)
        {motion=input;flight=observer;origin=floatingOrigin;view=Camera.main;}
        private void LateUpdate()
        {
            if(motion==null || !motion.Enabled || motion.Paused){sampled=false;return;}
            var p=flight.CityPosition;var body=motion.Body;var v=body.Velocity;var offset=origin!=null?origin.Offset:Vector3.zero;
            float dt=Mathf.Max(.0001f,Time.unscaledDeltaTime),travel=sampled?Vector3.Distance(previous,p):0;
            var camera=view!=null?view.transform.position+offset:p;
            var row=new Frame{time=Time.realtimeSinceStartup,dt=dt,p=p,v=v,offset=offset,state=body.State,resets=body.ResetSequence,guards=motion.ConstraintSnapPreventions,rope=body.RopeCorrectionMetres,shortfall=motion.MaxExternalShortfall,points=motion.Route.Points.Count,collider=body.LastCollisionName,
                camera=camera,rotation=view!=null?view.transform.rotation:Quaternion.identity,projection02=view!=null?view.projectionMatrix.m02:0,projection12=view!=null?view.projectionMatrix.m12:0};
            tail.Enqueue(row);while(tail.Count>240)tail.Dequeue();
            float cameraResidual=sampled?((camera-previousView)-(p-previous)).magnitude:0;
            bool unexpected=sampled && (travel>(v.magnitude+body.Settings.reelSpeed)*Mathf.Min(dt,.1f)+.75f || cameraResidual>.3f || body.ResetSequence!=resets || motion.ConstraintSnapPreventions!=guards);
            if(unexpected && Time.unscaledTime>=nextReport)
            {
                nextReport=Time.unscaledTime+2;
                string folder=Path.Combine(Application.persistentDataPath,"Continuity");Directory.CreateDirectory(folder);
                string file=Path.Combine(folder,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".csv");
                using(var csv=new StreamWriter(file)){csv.WriteLine("time,dt,x,y,z,vx,vy,vz,originX,originZ,state,resets,snapGuards,ropeCorrection,maxExternalShortfall,routePoints,cameraX,cameraY,cameraZ,qx,qy,qz,qw,projection02,projection12,collider");foreach(var line in tail)csv.WriteLine(line);}
                Debug.LogWarning($"TRAVERSAL_CONTINUITY travel={travel:F3} dt={dt:F4} reset={body.ResetSequence} guard={motion.ConstraintSnapPreventions} trace={file}");
            }
            previous=p;previousView=camera;sampled=true;resets=body.ResetSequence;guards=motion.ConstraintSnapPreventions;
        }
    }
}
