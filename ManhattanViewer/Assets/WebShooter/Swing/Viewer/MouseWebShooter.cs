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
        private TraversalHands hands;
        private float handSide = .7f;
        private IMotionInputSource previousInput;
        private ITranslationConstraint previousConstraint;
        private int lastFireFrame = -1;
        private float nextHandSearch;
        public WebSwingMotionInput Motion => swing;
        private void OnEnable() { Application.wantsToQuit += LogQuit; }
        private void OnDisable() { Application.wantsToQuit -= LogQuit; swing?.Release(); }
        private void OnApplicationFocus(bool focused) { if (!focused) swing?.Release(); }
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
            if (hand == null && Time.unscaledTime >= nextHandSearch)
            {
                nextHandSearch = Time.unscaledTime + 2f;
                hand = FindFirstObjectByType<HandAimController>();
                if (hand != null) hand.Fired += Fire;
            }
            bool active = app.KeyboardInput.InputEnabled && app.KeyboardInput.Captured;
            active &= Application.isFocused;
            swing.Paused = !active;
            var keys = Keyboard.current;
            var mouse = Mouse.current;
            bool mousePress = active && capturedLastFrame && swing.Enabled && mouse != null && mouse.leftButton.wasPressedThisFrame;
            if (active && mouse != null && Mathf.Abs(mouse.delta.ReadValue().x) > .5f)
                handSide = mouse.delta.ReadValue().x < 0 ? .3f : .7f;
            if (mousePress && hands != null) hands.Select(Cursor.lockState == CursorLockMode.Locked ? handSide : mouse.position.ReadValue().x / Screen.width);
            bool hadMouseRope = swing.MouseOwnsRope;
            Ray aim = Camera.main != null ? Camera.main.ViewportPointToRay(new Vector3(.5f, .5f, 0f)) : default;
            bool mouseHit = swing.HandleMouseHold(active && swing.Enabled, mousePress && Camera.main != null,
                mouse != null && mouse.leftButton.isPressed, aim);
            if (mousePress) { lastFireFrame = Time.frameCount; Debug.Log($"TRAVERSAL_MOUSE_PRESS hit={mouseHit} position={swing.Body.Position} target={swing.ShotTarget}"); }
            if (hadMouseRope && !swing.MouseOwnsRope) Debug.Log($"TRAVERSAL_MOUSE_RELEASE attached={swing.Body.Attached} flying={swing.Flying} velocity={swing.Body.Velocity}");
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
                    if (keys != null && keys.leftBracketKey.wasPressedThisFrame) swing.Body.AssistStrength = Mathf.Max(0f, swing.Body.AssistStrength - .25f);
                    if (keys != null && keys.rightBracketKey.wasPressedThisFrame) swing.Body.AssistStrength = Mathf.Min(1f, swing.Body.AssistStrength + .25f);
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
            swing.CanSimulate = () => app.KeyboardInput.InputEnabled && app.KeyboardInput.Captured && Application.isFocused;
            app.Flight.InputSource = swing;
            app.Flight.TranslationConstraint = new FlightCollisionGate(swing);
            app.Streamer.CollisionProxiesEnabled = true;
            hands = gameObject.AddComponent<TraversalHands>(); hands.Configure(swing, app.Display);
            gameObject.AddComponent<WebRopeVisual>().Configure(swing, app.Origin, hands);
        }
        private void Fire(Ray ray)
        {
            if (swing == null || !swing.Enabled || !app.KeyboardInput.InputEnabled || !app.KeyboardInput.Captured || lastFireFrame == Time.frameCount) return;
            lastFireFrame = Time.frameCount;
            if (hands != null && Camera.main != null) hands.Select(Camera.main.WorldToViewportPoint(ray.GetPoint(10f)).x);
            bool hit = swing.TryFire(ray);
            Debug.Log($"TRAVERSAL_FIRE hit={hit} position={swing.Body.Position} target={swing.ShotTarget}");
        }
        private void OnGUI()
        {
            if (swing == null) return;
            if (!app.KeyboardInput.Captured)
            {
                GUI.Box(new Rect(12, Screen.height - 60, 620, 48), "Click to capture. Aim HIGH, then HOLD left mouse to swing. Release to detach.\nStable view by default. E: reel in | Q: reel out | R: flight");
                return;
            }
            GUI.Label(new Rect(Screen.width * .5f - 8, Screen.height * .5f - 12, 20, 24), "+");
            GUI.Label(new Rect(12, Screen.height - 52, 1200, 24), "Space: jump | HOLD LMB: attach | Let go: release | W: boost | E/Q: reel in/out | R: flight");
            GUI.Label(new Rect(12, Screen.height - 28, 800, 24), swing.Enabled ? $"{swing.Body.State} | {swing.Body.Velocity.magnitude:0.0} m/s | Assist {swing.Body.AssistStrength:0.00} ([ / ])" : "Free flight (wall collision ON)");
        }
        private void OnDestroy()
        {
            if (hand != null) hand.Fired -= Fire;
            if (app != null && app.Flight != null && app.Flight.InputSource == swing)
            { app.Flight.InputSource = previousInput; app.Flight.TranslationConstraint = previousConstraint; }
            swing?.Collision.Dispose();
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
