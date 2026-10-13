using System;
using System.Collections.Generic;
using Manhattan.Viewer;
using UnityEngine;
using UnityEngine.InputSystem;
namespace WebShooter.Swing
{
    // Opt-in measurements for real player sessions, never changes input or physics.
    public sealed class TraversalDiagnostics : MonoBehaviour
    {
        private readonly List<float> frames = new List<float>(2048);
        private readonly List<float> samples = new List<float>(2048);
        private MouseWebShooter shooter;
        private ManhattanBootstrap app;
        private float elapsed, trace;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (ManhattanBootstrap.HasArgument("--traversal-diagnostics")) new GameObject("Traversal diagnostics").AddComponent<TraversalDiagnostics>();
        }
        private void LateUpdate()
        {
            if (shooter == null) shooter = FindFirstObjectByType<MouseWebShooter>();
            if (app == null) app = FindFirstObjectByType<ManhattanBootstrap>();
            var motion = shooter != null ? shooter.Motion : null;
            if (motion == null) return;
            frames.Add(Time.unscaledDeltaTime * 1000); samples.Add((float)motion.LastSampleMilliseconds);
            elapsed += Time.unscaledDeltaTime; trace += Time.unscaledDeltaTime;
            if (trace >= .5f)
            {
                trace = 0;
                Debug.Log($"TRAVERSAL_TRACE held={Mouse.current?.leftButton.isPressed} attached={motion.Body.Attached} flying={motion.Flying} paused={motion.Paused} pos={motion.Body.Position} speed={motion.Body.Velocity.magnitude:F2} rotation={app.Flight.transform.eulerAngles}");
            }
            if (elapsed < 5) return;
            frames.Sort(); samples.Sort(); int i = Mathf.Min(frames.Count-1, Mathf.FloorToInt(frames.Count*.95f));
            Debug.Log($"TRAVERSAL_PERF frames={frames.Count} frameP50Ms={frames[frames.Count/2]:F2} frameP95Ms={frames[i]:F2} motionP95Ms={samples[i]:F3} tracking={app.Display.IsTracking} headAgeMs={app.Display.ResultAgeMilliseconds:F1}");
            frames.Clear(); samples.Clear(); elapsed = 0;
        }
    }
}
