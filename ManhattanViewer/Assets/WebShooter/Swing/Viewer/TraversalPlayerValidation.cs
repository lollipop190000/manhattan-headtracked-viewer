using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Manhattan.Viewer;
using UnityEngine;

namespace WebShooter.Swing
{
    /// <summary>Opt-in executable regression check. Never runs in normal play.</summary>
    public sealed class TraversalPlayerValidation : MonoBehaviour
    {
        [Serializable] private sealed class Report { public bool passed; public string error; public string unityVersion; public List<string> checks = new List<string>(); }
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
                var lean = new TrackedLeanSteering(); lean.Sample(0, true, 1f / 60);
                float steering = 0;
                for (int i = 0; i < 60; i++) steering = lean.Sample(.1f, true, 1f / 60);
                Check(steering > .9f && lean.Sample(.1f, false, 1f / 60) == 0,
                    "tracked lean builds gradually and stale tracking cancels immediately");
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
                input.Body.Position = start; input.Paused = true; input.Sample(.1f);
                Check(input.Body.Position == start, "paused physics");
                input.Collision.Dispose();
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
    }
}
