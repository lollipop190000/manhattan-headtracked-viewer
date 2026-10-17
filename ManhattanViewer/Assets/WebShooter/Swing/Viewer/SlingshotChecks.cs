using System;
using Manhattan.Viewer;
using UnityEngine;
namespace WebShooter.Swing
{
    public static class SlingshotChecks
    {
        [Serializable] public sealed class Metric { public string name;public float value; }
        public static void Run(Action<bool,string> check,Action<string,float> metric)
        {
            using var map=new AirChainReplay.Map(22);
            var rig=new GameObject("Launch checks");var flight=rig.AddComponent<FreeFlightController>();flight.enabled=false;
            var input=new BufferedTraversalInput();var motion=new WebSwingMotionInput(input,flight,null,TraversalTuning.Defaults());
            var start=map.Center+new Vector3(0,motion.Body.FloorY,-500);
            void Frame(float dt){var c=motion.Sample(dt);flight.SetPose(c.cityPosition,c.cityRotation);}
            try
            {
                foreach(int fps in new[]{30,60,120})foreach(float hold in new[]{.2f,.7f,1.5f,4f})
                {
                    input.Clear();flight.SetPose(start,Quaternion.identity);motion.Activate();
                    input.Actions=new TraversalActions{active=true,launchPressed=true,launchHeld=true,aim=new Ray(start,Vector3.forward)};
                    for(int i=0;i<Mathf.CeilToInt(hold*fps);i++)Frame(1f/fps);
                    string id=$"{fps}fps_{hold:F1}s";
                    check(motion.Launch.Charging,id+" both actual side surfaces attached");
                    metric(id+"_chargeDisplacement",Vector3.Distance(start,motion.Body.Position));
                    check(Vector3.Distance(start,motion.Body.Position)<.002f,id+" no charge teleport or powered lift");
                    input.Actions=new TraversalActions{active=true,launchReleased=true,aim=new Ray(motion.Body.Position,Vector3.forward)};
                    Frame(1f/fps);float launchSpeed=motion.Launch.LastLaunchVelocity.magnitude;
                    check(motion.Launch.Launches>0 && launchSpeed<=82.001f && launchSpeed>=24,id+" bounded release velocity");
                    int contacts=motion.Body.ContactSequence;float maxStep=0,minClear=1000;
                    Vector3 previous=motion.Body.Position;
                    for(int i=0;i<fps;i++)
                    {
                        Frame(1f/fps);maxStep=Mathf.Max(maxStep,Vector3.Distance(previous,motion.Body.Position));previous=motion.Body.Position;
                        minClear=Mathf.Min(minClear,22-Mathf.Abs(previous.x-map.Center.x)-.3f);
                    }
                    check(motion.Body.ContactSequence==contacts && !motion.Body.Grounded,id+" clear airborne launch");
                    check(motion.Body.ResetSequence>0 && maxStep<90f/fps+.05f,id+" continuous poses at launch speed");
                    metric(id+"_releaseSpeed",launchSpeed);metric(id+"_height1s",motion.Body.Position.y-start.y);metric(id+"_maxFrameTravel",maxStep);metric(id+"_wallClearance",minClear);
                }
                input.Clear();flight.SetPose(start,Quaternion.identity);motion.Activate();
                input.Actions=new TraversalActions{active=true,launchPressed=true,launchHeld=true,aim=new Ray(start,Vector3.forward)};
                Frame(.1f);input.Actions=new TraversalActions{active=false};Frame(.1f);
                check(!motion.Launch.Charging,"capture loss cancels stored launch without firing");
                // Existing fast airborne momentum must not be reset by charging/release.
                flight.SetPose(start+Vector3.up*80,Quaternion.identity);motion.Activate();motion.Body.Velocity=Vector3.forward*100;
                var launch=new DualWebLaunch();check(launch.Begin(motion.Body.Position,Vector3.forward,Vector3.zero,TraversalCollision.Mask),"airborne launch finds both anchors");
                launch.Tick(1.5f,motion.Body.Position,Vector3.zero,TraversalCollision.Mask);
                check(launch.Release(motion.Body,Vector3.forward,Vector3.zero,TraversalCollision.Mask),"fast airborne charge releases");
                check(motion.Body.Velocity.magnitude>=99.99f,"existing faster momentum preserved");
                metric("fastIncoming_releaseSpeed",motion.Body.Velocity.magnitude);
            }
            finally{motion.Collision.Dispose();UnityEngine.Object.DestroyImmediate(rig);}
        }
        public static void Corner(Action<bool,string> check,Action<string,float> metric)
        {
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.layer=30;
            try
            {
                wall.transform.position=Vector3.up*3000;wall.transform.localScale=new Vector3(12,40,6);wall.transform.rotation=Quaternion.Euler(0,37,0);
                Physics.SyncTransforms();var box=wall.GetComponent<BoxCollider>();
                var start=wall.transform.TransformPoint(new Vector3(-1,0,0));var end=wall.transform.TransformPoint(new Vector3(1,0,0));
                var route=new WebRopeRoute();route.Begin(end);check(route.Update(start,Vector3.zero,TraversalCollision.Mask),"rotated facade route found");
                float gap=0;
                for(int i=0;i<route.Points.Count-1;i++)gap=Mathf.Max(gap,Vector3.Distance(route.Points[i],box.ClosestPoint(route.Points[i])));
                metric("rotatedCorner_maxSurfaceGap",gap);
                check(gap<.03f,"all rotated wrap knots touch real facade edges");
                check(route.Points.Count>1,"corner actually exercised");
                box.enabled=false;var mesh=wall.AddComponent<MeshCollider>();mesh.sharedMesh=wall.GetComponent<MeshFilter>().sharedMesh;
                Physics.SyncTransforms();route.Begin(end);bool backfaces=Physics.queriesHitBackfaces;
                check(route.Update(start,Vector3.zero,TraversalCollision.Mask),"rotated triangle mesh route found");
                check(Physics.queriesHitBackfaces==backfaces,"rope query restores the global backface setting");
                gap=0;
                for(int i=0;i<route.Points.Count-1;i++)
                {
                    var local=wall.transform.InverseTransformPoint(route.Points[i]);
                    var closest=new Vector3(Mathf.Clamp(local.x,-.5f,.5f),Mathf.Clamp(local.y,-.5f,.5f),Mathf.Clamp(local.z,-.5f,.5f));
                    gap=Mathf.Max(gap,Vector3.Distance(route.Points[i],wall.transform.TransformPoint(closest)));
                }
                metric("rotatedMeshCorner_maxSurfaceGap",gap);check(gap<.03f && route.Points.Count>1,"triangle mesh knots stay on physical edges");
                var previous=start;float interior=0;
                foreach(var point in route.Points)
                {
                    for(int i=0;i<=64;i++)
                    {
                        var q=wall.transform.InverseTransformPoint(Vector3.Lerp(previous,point,i/64f));
                        float depth=Mathf.Min((.5f-Mathf.Abs(q.x))*12,(.5f-Mathf.Abs(q.y))*40,(.5f-Mathf.Abs(q.z))*6);
                        interior=Mathf.Max(interior,depth);
                    }
                    previous=point;
                }
                metric("rotatedMeshCorner_maxInteriorDepth",interior);check(interior<.005f,"mesh route segments remain outside the independent box volume");
            }
            finally{UnityEngine.Object.DestroyImmediate(wall);}
        }
    }
}
