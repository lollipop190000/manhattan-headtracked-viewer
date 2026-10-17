using System;
using Manhattan.Viewer;
using UnityEngine;

namespace WebShooter.Swing
{
    // Opt-in, shared by EditMode and the shipping Windows validator. These use
    // the real input adapter and PhysX, isolated above streamed city geometry.
    public static class WallTraversalChecks
    {
        [Serializable] public sealed class Metric { public string name; public float value; }
        private const float BaseY = 4000f;
        private static BuildingRecord Building(float x, float width, float height) => new BuildingRecord {
            height = height, groundHeight = 0,
            cityBounds = new Bounds(new Vector3(x, height * .5f, 0), new Vector3(width, height, 100)),
            footprint = new[] { new Vector2(x-width*.5f,-50), new Vector2(x+width*.5f,-50),
                new Vector2(x+width*.5f,50), new Vector2(x-width*.5f,50) }
        };
        private static GameObject Box(float x, float width, float height)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Wall validation fixture"; go.layer = 30;
            go.transform.position = new Vector3(x, height * .5f, 0);
            go.transform.localScale = new Vector3(width, height, 100);
            return go;
        }
        public static void Run(Action<bool,string> check, Action<string,float> measure)
        {
            var rig = new GameObject("Wall input validation rig");
            var flight = rig.AddComponent<FreeFlightController>(); flight.enabled = false;
            var controls = new BufferedTraversalInput { Actions = new TraversalActions { active = true } };
            var settings = WebSwingMotionInput.TraversalSettings(); settings.features.SetFlow(true); settings.airDrag = 0;
            var motion = new WebSwingMotionInput(controls, flight, null, settings);
            void Reset(Vector3 position, Vector3 velocity)
            {
                controls.Movement = Vector2.zero; controls.LookRate = Vector2.zero;
                flight.SetPose(position, Quaternion.Euler(0,90,0)); motion.Activate(); motion.Body.Velocity = velocity;
            }
            void Advance(float dt)
            {
                var command = motion.Sample(dt); flight.SetPose(command.cityPosition, command.cityRotation);
            }
            try
            {
                foreach (float gap in new[] { .65f, .8f, 1.2f, 1.8f })
                {
                    var left = Box(-gap*.5f-5,10,BaseY+100); var right = Box(gap*.5f+5,10,BaseY+100);
                    try
                    {
                        Physics.SyncTransforms();
                        motion.Collision.CitySolids = new CitySolidRecovery(new[] {
                            Building(-gap*.5f-5,10,BaseY+100), Building(gap*.5f+5,10,BaseY+100) }, motion.Body);
                        foreach (int fps in new[] { 30,60,120 })
                        {
                            Reset(new Vector3(0,BaseY+104,0),Vector3.down*20);
                            int supports = 0;
                            for (int i=0;i<fps/2;i++) { Advance(1f/fps); if(motion.Body.Grounded) supports++; }
                            string name = $"gap{gap:F2}_{fps}fps";
                            check(supports==0 && Mathf.Abs(motion.Body.Position.y-BaseY-90.95f)<.02f &&
                                Mathf.Abs(motion.Body.Velocity.y+32)<.01f && Mathf.Abs(motion.Body.Position.x)<.001f,
                                name+" falls through empty space without roof support");
                            measure(name+"_finalY",motion.Body.Position.y-BaseY);
                            measure(name+"_verticalSpeed",motion.Body.Velocity.y); measure(name+"_supportedFrames",supports);
                        }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(left); UnityEngine.Object.DestroyImmediate(right); }
                }
                var wall = Box(5,10,BaseY+1000);
                try
                {
                    Physics.SyncTransforms();
                    motion.Collision.CitySolids = new CitySolidRecovery(new[] { Building(5,10,BaseY+1000) }, motion.Body);
                    Reset(new Vector3(.1f,BaseY+100,0),Vector3.zero);
                    var residentOnly=motion.Collision.CitySolids.Recover(motion.Body.Position);
                    var combined=motion.Collision.Constrain(motion.Body.Position,motion.Body.Position);
                    measure("overlapExtraDisplacement",Vector3.Distance(residentOnly,combined));
                    check(Vector3.Distance(residentOnly,combined)<.001f,"resident and PhysX share one overlap recovery");
                    var retreatPose=motion.Collision.Constrain(combined,combined+Vector3.left*2);
                    check(Vector3.Distance(retreatPose,combined+Vector3.left*2)<.001f,"overlap recovery allows full two metre retreat");
                    foreach (int fps in new[] { 30,60,120 })
                    {
                        Reset(new Vector3(-2,BaseY+100,0),new Vector3(70,0,20));
                        float minimumClearance = float.PositiveInfinity;
                        for(int i=0;i<fps/2;i++) { Advance(1f/fps); minimumClearance=Mathf.Min(minimumClearance,-motion.Body.Position.x); }
                        string name = $"impact70_{fps}fps";
                        check(minimumClearance>=.319f && !motion.Body.Grounded && motion.Body.State==TraversalState.Airborne,
                            name+" stops normal travel without penetration or phantom support");
                        check(Mathf.Abs(motion.Body.Velocity.z-20)<.002f,name+" preserves tangential twenty metres per second");
                        measure(name+"_minimumClearance",minimumClearance); measure(name+"_tangentSpeed",motion.Body.Velocity.z);
                        Reset(new Vector3(-2,BaseY+100,0),Vector3.right*70);
                        for(int i=0;i<fps/2;i++) Advance(1f/fps);
                        controls.Movement=Vector2.down; float impactX=motion.Body.Position.x;
                        for(int i=0;i<fps;i++) Advance(1f/fps);
                        float retreat=impactX-motion.Body.Position.x;
                        check(retreat>2.4f && motion.Body.Velocity.x< -2.8f,name+" S escapes after impact from a frontal impact");
                        measure(name+"_retreat1s",retreat);
                    }
                    Reset(new Vector3(-.321f,BaseY+100,0),Vector3.forward*20);
                    motion.Body.WallRunRequested=true;
                    motion.Body.UpdateWall(motion.Collision.FindWall(motion.Body.Position,Vector3.forward),true,1f/120);
                    check(motion.Body.State==TraversalState.WallRunning,"parallel unwebbed approach enters without facing wall");
                    for(int yaw=0;yaw<360;yaw+=15)
                    {
                        var normal=motion.Collision.FindWall(motion.Body.Position,Quaternion.Euler(0,yaw,0)*Vector3.forward,true,motion.Body.ContactNormal);
                        motion.Body.UpdateWall(normal,true,1f/120);
                        check(motion.Body.State==TraversalState.WallRunning && Vector3.Dot(normal,Vector3.left)>.99f,
                            "wall retained through view yaw "+yaw);
                    }
                    measure("retainedViewYawCount",24);
                    Reset(new Vector3(-.321f,BaseY+100,49),Vector3.forward*20); controls.Movement=Vector2.up;
                    for(int i=0;i<120;i++) Advance(1f/120);
                    check(motion.Body.State==TraversalState.Airborne && motion.Body.Position.z>55,"wall edge returns to airborne travel");

                    Reset(new Vector3(-2,BaseY+100,0),Vector3.right*70);
                    for(int i=0;i<12;i++) Advance(1f/120);
                    float x=motion.Body.Position.x; controls.Movement=Vector2.down;
                    for(int i=0;i<120;i++) Advance(1f/120);
                    measure("frontal70_retreat1s",x-motion.Body.Position.x);
                    measure("frontal70_reverseSpeed",-motion.Body.Velocity.x);
                    check(x-motion.Body.Position.x>2.4f && !motion.Body.Grounded,"frontal impact permits bounded backward escape");
                    foreach(bool disableAssist in new[]{false,true})
                    {
                        Reset(new Vector3(-.321f,BaseY+100,0),Vector3.zero);
                        settings.lowSpeedReverse=disableAssist; motion.Body.AssistStrength=disableAssist?0:1; controls.Movement=Vector2.down;
                        for(int i=0;i<120;i++) Advance(1f/120);
                        check(Mathf.Abs(motion.Body.Position.x+.321f)<.001f,disableAssist?
                            "zero assistance adds no reverse force":"reverse option off retains old brake at rest");
                    }
                    settings.lowSpeedReverse=true; motion.Body.AssistStrength=1;
                    Reset(new Vector3(-100,BaseY+100,0),Vector3.right*40); controls.Movement=Vector2.down;
                    for(int i=0;i<30;i++) Advance(1f/120);
                    check(motion.Body.Velocity.x>30 && motion.Body.Velocity.x<40,"S still brakes forty metres per second without reversal");
                    measure("fastS_speedAfterQuarterSecond",motion.Body.Velocity.x);
                }
                finally { UnityEngine.Object.DestroyImmediate(wall); }
                var a=Box(-5.6f,10,BaseY+1000);var b=Box(5.6f,10,BaseY+1000);
                try
                {
                    Physics.SyncTransforms();Reset(new Vector3(.2f,BaseY+100,0),Vector3.forward*20);
                    var nearest=motion.Collision.FindWall(motion.Body.Position,Vector3.left);
                    var retained=motion.Collision.FindWall(motion.Body.Position,Vector3.forward,true,Vector3.right);
                    check(nearest.x< -.99f,"new wall chooses closer side rather than view search order");
                    check(retained.x>.99f,"reachable current wall does not switch sides in narrow gap");
                }
                finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); }
            }
            finally { motion.Collision.Dispose(); UnityEngine.Object.DestroyImmediate(rig); }
        }
    }
}
