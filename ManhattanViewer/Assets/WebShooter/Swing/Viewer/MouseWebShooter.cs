using Manhattan.Viewer;
using WebShooter.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WebShooter.Swing
{
    [DefaultExecutionOrder(-400)]
    public sealed class MouseWebShooter : MonoBehaviour
    {
        private ManhattanBootstrap app;
        private WebSwingMotionInput swing;
        private HandAimController hand;
        private bool capturedLastFrame;
        private IMotionInputSource previousInput;
        private ITranslationConstraint previousConstraint;
        private int lastFireFrame = -1;
        private readonly TrackedLeanSteering trackedLean = new TrackedLeanSteering();
        private bool headAssist = true;
        private void OnEnable() { Application.wantsToQuit += LogQuit; }
        private void OnDisable() { Application.wantsToQuit -= LogQuit; }
        private bool LogQuit()
        {
            Debug.Log($"TRAVERSAL_QUIT_REQUEST state={swing?.Body.State} position={swing?.Body.Position} velocity={swing?.Body.Velocity}");
            return true;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!ManhattanBootstrap.HasArgument("-runTests") && !ManhattanBootstrap.HasArgument("--benchmark")) new GameObject("Web Traversal").AddComponent<MouseWebShooter>();
        }
        private void Update()
        {
            if (swing == null) { TryConnect(); return; }
            app.Streamer.CollisionProxiesEnabled = true;
            // Rapid travel and near-wall views should not retain a long TAA history.
            app.Rendering?.SetTraversalStability(true);
            if (hand == null)
            {
                hand = FindFirstObjectByType<HandAimController>();
                if (hand != null) hand.Fired += Fire;
            }
            bool active = app.KeyboardInput.InputEnabled && app.KeyboardInput.Captured;
            swing.Paused = !active;
            var keys = Keyboard.current;
            var mouse = Mouse.current;
            if (active && capturedLastFrame && keys != null && keys.hKey.wasPressedThisFrame) headAssist = !headAssist;
            var display = app.Display;
            bool fresh = active && swing.Enabled && headAssist && display != null && display.TrackingEnabled &&
                display.IsTracking && display.HasFreshEyeEstimate && display.Confidence >= .6f && display.ResultAgeMilliseconds < 200f;
            swing.TrackedSteering = trackedLean.Sample(display != null ? display.EyePositionMeters.x : 0f, fresh, Time.unscaledDeltaTime);
            if (active && capturedLastFrame)
            {
                if (keys != null && keys.rKey.wasPressedThisFrame)
                {
                    swing.Release();
                    if (swing.Enabled) swing.Enabled = false; else swing.Activate();
                }
                if (swing.Enabled)
                {
                    swing.ReelInput = (keys != null && keys.eKey.isPressed ? 1f : 0f) - (keys != null && keys.qKey.isPressed ? 1f : 0f);
                    if (keys != null && keys.vKey.wasPressedThisFrame) swing.AutoFollow = !swing.AutoFollow;
                    if (keys != null && keys.leftBracketKey.wasPressedThisFrame) swing.Body.AssistStrength = Mathf.Max(0f, swing.Body.AssistStrength - .25f);
                    if (keys != null && keys.rightBracketKey.wasPressedThisFrame) swing.Body.AssistStrength = Mathf.Min(1f, swing.Body.AssistStrength + .25f);
                    if (mouse != null && mouse.leftButton.wasPressedThisFrame && Camera.main != null)
                        Fire(Camera.main.ViewportPointToRay(new Vector3(.5f, .5f, 0f)));
                    if (mouse != null && mouse.rightButton.wasPressedThisFrame) swing.Release();
                    if (keys != null && keys.spaceKey.wasPressedThisFrame) swing.JumpRequested = true;
                }
            }
            capturedLastFrame = active;
        }
        private void TryConnect()
        {
            app = FindFirstObjectByType<ManhattanBootstrap>();
            if (app == null || app.Flight == null || app.Origin == null || app.Streamer == null || app.KeyboardInput == null) return;
            previousInput = app.Flight.InputSource;
            previousConstraint = app.Flight.TranslationConstraint;
            swing = new WebSwingMotionInput(previousInput, app.Flight, app.Origin) { Paused = true };
            if (app.catalog != null) swing.Collision.CitySolids = new CitySolidRecovery(app.catalog.buildings, swing.Body);
            swing.CanSimulate = () => app.KeyboardInput.InputEnabled && app.KeyboardInput.Captured;
            app.Flight.InputSource = swing;
            app.Flight.TranslationConstraint = new FlightCollisionGate(swing);
            app.Streamer.CollisionProxiesEnabled = true;
            gameObject.AddComponent<WebRopeVisual>().Configure(swing, app.Origin);
        }
        private void Fire(Ray ray)
        {
            if (swing == null || !swing.Enabled || !app.KeyboardInput.InputEnabled || !app.KeyboardInput.Captured || lastFireFrame == Time.frameCount) return;
            lastFireFrame = Time.frameCount;
            bool hit = swing.TryFire(ray);
            Debug.Log($"TRAVERSAL_FIRE hit={hit} position={swing.Body.Position} target={swing.ShotTarget}");
        }
        private void OnGUI()
        {
            if (swing == null) return;
            if (!app.KeyboardInput.Captured)
            {
                GUI.Box(new Rect(12, Screen.height - 60, 620, 48), "Click to play. Aim at a HIGH building, then click again to launch.\nAutomatic jump / swing assist / view follow. E: reel in | Q: reel out | R: flight");
                return;
            }
            GUI.Label(new Rect(Screen.width * .5f - 8, Screen.height * .5f - 12, 20, 24), "+");
            GUI.Label(new Rect(12, Screen.height - 52, 1100, 24), "Aim high + LMB: launch | RMB: release | W: boost | E/Q: reel in/out | R: flight | V: auto view | H: head assist");
            GUI.Label(new Rect(12, Screen.height - 28, 800, 24), swing.Enabled ? $"Head {(headAssist ? "ON" : "OFF")} | {swing.Body.State} | {swing.Body.Velocity.magnitude:0.0} m/s | Assist {swing.Body.AssistStrength:0.00} ([ / ]) | Auto view {(swing.AutoFollow ? "ON" : "OFF")}" : "Free flight (wall collision ON)");
        }
        private void OnDestroy()
        {
            if (hand != null) hand.Fired -= Fire;
            if (app != null && app.Flight != null && app.Flight.InputSource == swing)
            { app.Flight.InputSource = previousInput; app.Flight.TranslationConstraint = previousConstraint; }
            swing?.Collision.Dispose();
            if (app != null) app.Rendering?.SetTraversalStability(false);
        }

        // Traversal already sweeps every substep. Free flight uses the same body shape
        // and collision recovery, but retains its existing no-gravity controls.
        public sealed class FlightCollisionGate : ITranslationConstraint
        {
            private readonly WebSwingMotionInput input;
            public FlightCollisionGate(WebSwingMotionInput input) { this.input = input; }
            public Vector3 Constrain(Vector3 current, Vector3 requested) =>
                input.Enabled || input.Paused ? requested : input.Collision.Constrain(current,
                    new Vector3(requested.x, Mathf.Max(input.Body.FloorY, requested.y), requested.z));
        }
    }
}
