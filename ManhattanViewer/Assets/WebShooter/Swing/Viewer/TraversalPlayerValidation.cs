using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Manhattan.Viewer;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace WebShooter.Swing
{
    /// <summary>Opt-in executable regression check. Never runs in normal play.</summary>
    public sealed class TraversalPlayerValidation : MonoBehaviour
    {
        [Serializable] private sealed class Report
        {
            public bool passed; public string error; public string unityVersion;
            public float reconnectAttachSeconds,reconnectSpeedBefore,reconnectSpeedAfterFire;
            public float airFullTurnDegrees,airQuarterTurnDegrees,anchorGeometricSpeed,anchorMomentumSpeed,mouseReeledMetres;
            public List<string> checks = new List<string>();
        }
        private readonly Report report = new Report();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (ManhattanBootstrap.HasArgument("--validate-traversal")) new GameObject("Traversal validation").AddComponent<TraversalPlayerValidation>();
        }
        private void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            report.checks.Add(name);
        }
        private IEnumerator Start()
        {
            yield return null;
            var rig = new GameObject("Validation rig");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                report.unityVersion = Application.unityVersion;
                var liveApp = FindFirstObjectByType<ManhattanBootstrap>();
                Check(liveApp != null && liveApp.Rendering.AntialiasingName == (liveApp.Preferences.temporalAA ? "TemporalAntiAliasing" : "None"),
                    "traversal preserves saved antialiasing preference");
                // Isolated above the city; real PhysX queries and the shipping movement adapter.
                var flight = rig.AddComponent<FreeFlightController>(); flight.enabled = false;
                var start = new Vector3(0, 2000, 0);
                flight.SetPose(start, Quaternion.identity);
                wall.layer = 30; wall.transform.position = start + Vector3.forward * 20;
                wall.transform.localScale = new Vector3(100, 100, 1);
                Physics.SyncTransforms();
                var input = new WebSwingMotionInput(null, flight, null);
                input.Activate();
                input.Body.Velocity = Vector3.right * 15;
                Check(input.TryFire(new Ray(start, Vector3.forward)), "direct hit accepted");
                Check(input.Flying && !input.Body.Attached, "no tension before arrival");
                for (int i = 0; i < 120; i++)
                {
                    var command = input.Sample(1f / 120);
                    flight.transform.position = command.cityPosition;
                }
                Check(input.Body.Attached, "swing remains attached");
                Check(Vector3.Distance(input.Body.Position, input.Body.Anchor) <= input.Body.RopeLength + .01f, "rope constraint");
                Vector3 velocity = input.Body.Velocity;
                input.Release(); Check(input.Body.Velocity == velocity, "release preserves velocity");
                Check(input.TryFire(new Ray(input.Body.Position, input.Body.Anchor - input.Body.Position)), "second swing attaches to aimed building");
                for (int i = 0; i < 30; i++) { var command = input.Sample(1f / 120); flight.transform.position = command.cityPosition; }
                Check(input.Body.Attached, "continuous swing arrival");
                input.Release();
                flight.SetPose(start, Quaternion.identity); input.Activate();
                var holdRay = new Ray(start, Vector3.forward);
                Check(input.HandleMouseHold(true, true, true, holdRay), "mouse hold press starts shot");
                for (int i = 0; i < 40; i++)
                {
                    input.HandleMouseHold(true, false, true, holdRay);
                    var command = input.Sample(1f / 120); flight.transform.position = command.cityPosition;
                }
                Check(input.Body.Attached, "held mouse keeps rope attached after arrival");
                velocity = input.Body.Velocity;
                input.HandleMouseHold(true, false, false, holdRay);
                Check(!input.Body.Attached && !input.Flying && input.Body.Velocity == velocity, "mouse release detaches without impulse");
                input.HandleMouseHold(true, true, true, holdRay);
                input.HandleMouseHold(false, false, true, holdRay);
                Check(!input.Body.Attached && !input.Flying, "focus loss cancels travelling shot");
                input.Body.Reset(start); input.Body.Velocity = Vector3.forward * 70;
                Vector3 stopped = input.Collision.Constrain(start, start + Vector3.forward * 40);
                Check(stopped.z < 19.3f, "high speed wall sweep");
                input.Body.Reset(start); input.Body.Attach(start + Vector3.up * 40);
                input.Body.Velocity = new Vector3(12, 0, 30);
                var deflected = input.Collision.Constrain(start, start + new Vector3(12, 0, 40));
                Check(deflected.z < 19.3f && !input.Body.Attached && input.Body.Velocity.z < -10,
                    "swing wall collision detaches and reflects outward");
                Check(input.Body.Velocity.x > 11 && input.Body.WallDeflections == 1,
                    "glancing collision preserves tangential speed without repeated bounce");
                var taut = new SwingPhysics(new SwingSettings { gravity = 0, airDrag = 0, attachmentTakeupSeconds = .75f });
                taut.Reset(start); taut.Velocity = Vector3.up * 10; taut.Attach(start + Vector3.up * 40);
                for (int i = 0; i < 60; i++) taut.Step(1f / 120);
                Check(Mathf.Abs(taut.RopeLength - Vector3.Distance(taut.Position, taut.Anchor)) < .01f,
                    "initial attachment stays taut during inward motion");
                Check(Mathf.Abs(taut.Position.y - start.y - 5) < .02f && taut.Velocity == Vector3.up * 10,
                    "initial takeup adds no impulse or position jump");
                taut.Step(.1f, default, -1);
                Check(taut.RopeLength > Vector3.Distance(taut.Position, taut.Anchor) + 3,
                    "manual payout overrides initial takeup");
                input.Body.Reset(start);
                input.Enabled = false;
                var gate = new MouseWebShooter.FlightCollisionGate(input);
                Check(gate.Constrain(start, start + Vector3.forward * 100).z < 19.3f, "ghost mode blocks wall");
                var embedded = start + Vector3.forward * 20;
                var recovered = gate.Constrain(embedded, embedded);
                Check(Mathf.Abs(recovered.z - 20) >= .8f, "stationary overlap recovery");
                Check(Vector3.Distance(gate.Constrain(stopped, stopped + Vector3.back * 2), stopped + Vector3.back * 2) < .01f, "retreat from wall without sticking");
                // Use the same non-convex mesh type as city collision tiles.
                DestroyImmediate(wall.GetComponent<BoxCollider>());
                wall.AddComponent<MeshCollider>().sharedMesh = wall.GetComponent<MeshFilter>().sharedMesh;
                Physics.SyncTransforms();
                Check(gate.Constrain(start, start + Vector3.forward * 100).z < 19.3f, "city mesh wall sweep");
                input.Collision.CitySolids = new CitySolidRecovery(new[] { new BuildingRecord {
                    height = 2100, groundHeight = 0, cityBounds = new Bounds(new Vector3(0, 1050, 20), new Vector3(100, 2100, 1)),
                    footprint = new[] { new Vector2(-50, 19.5f), new Vector2(50, 19.5f), new Vector2(50, 20.5f), new Vector2(-50, 20.5f) }
                } }, input.Body);
                recovered = gate.Constrain(embedded, embedded);
                Check(Mathf.Abs(recovered.z - 20) >= .8f, "city mesh interior recovery using resident footprints");
                wall.GetComponent<MeshCollider>().enabled = false;
                Check(gate.Constrain(start, start + Vector3.forward * 100).z < 19.3f, "unloaded collision tile still blocks ghost movement");
                wall.GetComponent<MeshCollider>().enabled = true;
                input.Enabled = true;
                input.Body.Reset(stopped); input.Body.Velocity = Vector3.right * 12;
                bool remainedOutside = true;
                for (int i = 0; i < 120; i++)
                {
                    input.Body.UpdateWall(input.Collision.FindWall(input.Body.Position, Vector3.right), true, 1f / 120);
                    var before = input.Body.Position; input.Body.Step(1f / 120, new Vector3(1, 0, 1));
                    input.Body.Position = input.Collision.Constrain(before, input.Body.Position);
                    remainedOutside &= input.Body.Position.z < 19.3f;
                }
                Check(remainedOutside, "all 120 wall run frames outside mesh");
                Check(input.Body.State == TraversalState.WallRunning, "sustained wall run against real mesh");
                input.Body.Reset(start); input.Body.Velocity = Vector3.right * 12;
                input.Body.UpdateWall(Vector3.back, true, .01f);
                Check(input.Body.State == TraversalState.WallRunning, "wall run entry");
                input.Body.Jump(); Check(input.Body.State == TraversalState.Airborne && input.Body.Velocity.z < 0, "wall jump exits outward");
                input.Body.Reset(start); input.Body.Velocity = Vector3.right * 12;
                for (int i = 0; i < 400; i++) input.Body.UpdateWall(Vector3.back, true, .01f);
                Check(input.Body.State == TraversalState.Airborne, "wall run timeout");
                input.Collision.CitySolids = null;
                wall.transform.position = new Vector3(0, 2000, 0); wall.transform.localScale = new Vector3(100, 2, 100);
                flight.SetPose(new Vector3(0, 2003, 0), Quaternion.identity); input.Activate();
                input.Body.Velocity = new Vector3(40, -10, 0);
                for (int i = 0; i < 120; i++) { var c = input.Sample(1f / 120); flight.SetPose(c.cityPosition, c.cityRotation); }
                Check(input.Body.Grounded && input.Body.Velocity.magnitude < .1f && input.Body.Position.x < 4f,
                    "rooftop friction stops 40mps landing within four metres");
                input.JumpRequested = true; input.Sample(1f / 120);
                Check(input.Body.Velocity.y > 19f, "rooftop can jump after stopping");
                wall.transform.position = new Vector3(30, 2040, 0); wall.transform.localScale = Vector3.one;
                flight.SetPose(start, Quaternion.identity); input.Activate(); Physics.SyncTransforms();
                Check(input.TryFire(new Ray(start, wall.transform.position - start)), "passive pendulum target hit");
                for (int i = 0; i < 60; i++) { var c = input.Sample(1f / 120); flight.SetPose(c.cityPosition, c.cityRotation); }
                float passiveLength = input.Body.RopeLength;
                float passiveEnergy = .5f * input.Body.Velocity.sqrMagnitude + input.Body.Settings.gravity * input.Body.Position.y;
                bool conserved = input.Body.Attached;
                for (int i = 0; i < 1200; i++)
                {
                    var c = input.Sample(1f / 120); flight.SetPose(c.cityPosition, c.cityRotation);
                    conserved &= input.Body.Attached && Mathf.Abs(input.Body.RopeLength - passiveLength) < .01f &&
                        .5f * input.Body.Velocity.sqrMagnitude + input.Body.Settings.gravity * input.Body.Position.y <= passiveEnergy + 3f;
                }
                Check(conserved, "holding web for ten seconds adds no energy or automatic reeling");
                input.Body.Position = start; input.Paused = true; input.Sample(.1f);
                Check(input.Body.Position == start, "paused physics");
                input.Collision.Dispose();
                // Exercise the shipping reconnect input path with real colliders, independently of the agent policy.
                wall.transform.position=start+new Vector3(0,25,100);wall.transform.localScale=new Vector3(10,20,1);
                flight.SetPose(start,Quaternion.identity);Physics.SyncTransforms();
                var relaySettings=WebSwingMotionInput.TraversalSettings();relaySettings.gravity=0;relaySettings.airDrag=0;
                var relay=new WebSwingMotionInput(null,flight,null,relaySettings);relay.Activate();relay.ReconnectAssist.enabled=true;
                relay.Body.Attach(start+Vector3.up*50);relay.Release();relay.Body.Velocity=Vector3.right*40;
                Check(!relay.HandleMouseHold(true,true,true,new Ray(start,Vector3.right)) && relay.ReconnectPending,"reconnect buffers a missed press");
                for(int i=0;i<6;i++){var c=relay.Sample(1f/120);flight.SetPose(c.cityPosition,c.cityRotation);}
                report.reconnectSpeedBefore=relay.Body.Velocity.magnitude;
                Check(relay.HandleMouseHold(true,false,true,new Ray(relay.Body.Position,wall.transform.position-relay.Body.Position)),"reconnect accepts fresh aim without another click");
                report.reconnectSpeedAfterFire=relay.Body.Velocity.magnitude;
                Check(Mathf.Abs(report.reconnectSpeedBefore-report.reconnectSpeedAfterFire)<.0001f,"reconnect preserves incoming velocity");
                for(int i=0;i<5;i++){var c=relay.Sample(1f/120);flight.SetPose(c.cityPosition,c.cityRotation);}
                report.reconnectAttachSeconds=relay.ShotAge;
                Check(relay.Body.Attached && report.reconnectAttachSeconds<=.05f,"reconnect physically attaches within fifty milliseconds");
                velocity=relay.Body.Velocity;relay.HandleMouseHold(true,false,false,default);
                Check(!relay.Body.Attached && relay.Body.Velocity==velocity,"reconnected web still releases on mouse up without impulse");
                relay.HandleMouseHold(true,true,true,new Ray(relay.Body.Position,Vector3.right));
                relay.HandleMouseHold(false,false,true,default);
                Check(!relay.HandleMouseHold(true,false,true,new Ray(relay.Body.Position,wall.transform.position-relay.Body.Position)) && !relay.Flying,
                    "focus loss cancels buffered reconnect without replaying an old ray");
                relay.Collision.Dispose();
                ValidateContinuity();
                report.passed = true;
            }
            catch (Exception e) { report.error = e.ToString(); Debug.LogException(e); }
            finally
            {
                Destroy(rig); Destroy(wall);
                string output = Path.Combine(Application.persistentDataPath, "traversal-validation.json");
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--traversal-report") output = args[i + 1];
                File.WriteAllText(output, JsonUtility.ToJson(report, true));
            }
            Application.Quit(report.passed ? 0 : 1);
        }
        private void ValidateContinuity()
        {
            SwingPhysics NewBody()
            {
                var s=WebSwingMotionInput.TraversalSettings();s.features.SetFlow(true);s.gravity=0;s.airDrag=0;
                var b=new SwingPhysics(s);b.Reset(Vector3.up*4000);return b;
            }
            var wallBody=NewBody();wallBody.WallRunRequested=true;wallBody.Velocity=Vector3.left*20;wallBody.Contact(Vector3.right);
            wallBody.Jump();wallBody.Velocity=Vector3.left*20;wallBody.Contact(Vector3.right);
            Check(wallBody.State==TraversalState.Airborne,"contact respects wall jump cooldown");
            for(int i=0;i<37;i++){wallBody.Velocity=Vector3.left*20;wallBody.UpdateWall(Vector3.right,true,1f/120);}
            Check(wallBody.State==TraversalState.WallRunning,"wall entry resumes after cooldown");
            wallBody.Velocity=Vector3.forward*20;wallBody.Attach(wallBody.Position+new Vector3(0,50,50));
            wallBody.UpdateWall(Vector3.right,true,1f/120);
            Check(wallBody.Attached && wallBody.State==TraversalState.Swinging && wallBody.Velocity==Vector3.forward*20,
                "new web exits wall run without losing velocity or immediate reentry");
            wallBody.Release();wallBody.UpdateWall(Vector3.zero,true,.4f);wallBody.Velocity=Vector3.left*20;wallBody.Contact(Vector3.right);
            for(int i=0;i<250;i++){wallBody.Velocity=Vector3.left*20;wallBody.UpdateWall(Vector3.right,true,1f/120);wallBody.Velocity=Vector3.left*20;wallBody.Contact(Vector3.right);}
            Check(wallBody.State==TraversalState.Airborne,"repeated contacts cannot bypass wall run exhaustion");
            float Turn(float assist)
            {
                var b=NewBody();b.AssistStrength=assist;b.Velocity=Vector3.forward*40;
                for(int i=0;i<120;i++)b.Step(1f/120,Vector3.right);
                Check(Mathf.Abs(b.Velocity.magnitude-40)<.002f,"air turning preserves forty metres per second at assist "+assist);
                return Mathf.Atan2(b.Velocity.x,b.Velocity.z)*Mathf.Rad2Deg;
            }
            report.airFullTurnDegrees=Turn(1);report.airQuarterTurnDegrees=Turn(.25f);
            Check(Mathf.Abs(report.airFullTurnDegrees-22.75f)<.01f && Mathf.Abs(report.airQuarterTurnDegrees-report.airFullTurnDegrees*.25f)<.01f,
                "air turn rate scales with assistance and weaker air ratio");
            var near=GameObject.CreatePrimitive(PrimitiveType.Cube);var wide=GameObject.CreatePrimitive(PrimitiveType.Cube);var rig=new GameObject("Continuity input fixture");
            WebSwingMotionInput motion=null;Mouse mouse=null;Keyboard keyboard=null;
            try
            {
                var start=Vector3.up*4000;var ray=new Ray(start,new Vector3(0,1,.25f));
                near.layer=wide.layer=30;near.transform.position=start+new Vector3(0,60,17);near.transform.localScale=new Vector3(8,1,2);
                wide.transform.position=start+new Vector3(0,60,27);wide.transform.localScale=new Vector3(12,1,6);Physics.SyncTransforms();
                Check(!Physics.Raycast(ray,250,1<<30),"anchor comparison begins from an actual aim miss");
                var flight=rig.AddComponent<FreeFlightController>();flight.enabled=false;var s=NewBody().Settings;
                motion=new WebSwingMotionInput(null,flight,null,s);motion.ReconnectAssist.enabled=true;
                float AttachSpeed(float weight)
                {
                    flight.SetPose(start,Quaternion.identity);motion.Activate();motion.Body.Attach(start+Vector3.up*50);motion.Release();motion.Body.Velocity=Vector3.down*40;
                    motion.ReconnectAssist.momentumWeight=weight;Check(motion.TryFire(ray),"real cone hit at momentum weight "+weight);
                    Check(Vector3.Angle(ray.direction,motion.ShotTarget-start)<=12.01f,"assisted anchor stays within twelve degrees at weight "+weight);
                    for(int i=0;i<5;i++){var c=motion.Sample(1f/120);flight.SetPose(c.cityPosition,c.cityRotation);}
                    Check(motion.Body.Attached && motion.ShotAge<=.05f,"physical reconnect stays below fifty milliseconds at weight "+weight);
                    return motion.Body.Velocity.magnitude;
                }
                report.anchorGeometricSpeed=AttachSpeed(0);report.anchorMomentumSpeed=AttachSpeed(1.5f);
                Check(report.anchorMomentumSpeed>report.anchorGeometricSpeed*1.2f && report.anchorMomentumSpeed<=40.001f,
                    "momentum-aware assisted anchor retains more speed without adding energy");
                motion.Release();flight.SetPose(start,Quaternion.identity);motion.Activate();
                mouse=InputSystem.AddDevice<Mouse>();keyboard=InputSystem.AddDevice<Keyboard>();InputSystem.QueueStateEvent(keyboard,new KeyboardState());
                var human=new HumanTraversalInput(null,()=>true);motion.InputOverride=human;
                InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Left));InputSystem.Update();human.ReadActions();InputSystem.Update();
                Check(motion.HandleMouseHold(true,true,true,new Ray(start,near.transform.position-start)),"mouse reel fixture attaches through the real shot and route");
                for(int i=0;i<60;i++){var c=motion.Sample(1f/120);flight.SetPose(c.cityPosition,c.cityRotation);}
                Check(motion.Body.Attached,"mouse reel fixture reaches attachment");
                InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Left).WithButton(MouseButton.Right));InputSystem.Update();var actions=human.ReadActions();
                Check(actions.reel==1 && !actions.release,"real right mouse hold maps to reel instead of release");
                float length=motion.Body.RopeLength;
                for(int i=0;i<30;i++){var c=motion.Sample(1f/120);flight.SetPose(c.cityPosition,c.cityRotation);}
                report.mouseReeledMetres=length-motion.Body.RopeLength;
                Check(motion.Body.Attached && Mathf.Abs(report.mouseReeledMetres-6)<.002f,"held right mouse reels six metres in a quarter second and stays attached");
                InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Left));InputSystem.Update();
                var command=motion.Sample(1f/120);flight.SetPose(command.cityPosition,command.cityRotation);
                Check(motion.Body.Attached && motion.ReelInput==0,"right mouse up stops reeling without detaching");
                InputSystem.QueueStateEvent(mouse,new MouseState());InputSystem.Update();
                command=motion.Sample(1f/120);flight.SetPose(command.cityPosition,command.cityRotation);
                Check(!motion.Flying && !motion.Body.Attached,"left mouse up still releases after right mouse remap");
            }
            finally{if(mouse!=null)InputSystem.RemoveDevice(mouse);if(keyboard!=null)InputSystem.RemoveDevice(keyboard);motion?.Collision.Dispose();DestroyImmediate(near);DestroyImmediate(wide);DestroyImmediate(rig);}
        }
    }
}
