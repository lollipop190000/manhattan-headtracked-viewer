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
        private TraversalHands hands;
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
            if (!ManhattanBootstrap.HasArgument("-runTests") && !ManhattanBootstrap.HasArgument("--benchmark") &&
                !ManhattanBootstrap.HasArgument("--traversal-agent") && !ManhattanBootstrap.HasArgument("--scripted-flow") &&
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!="TraversalLab") new GameObject("Web Traversal").AddComponent<MouseWebShooter>();
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
        }
        private void TryConnect()
        {
            app = FindFirstObjectByType<ManhattanBootstrap>();
            if (app == null || app.Flight == null || app.Origin == null || app.Streamer == null || app.KeyboardInput == null) return;
            previousInput = app.Flight.InputSource;
            previousConstraint = app.Flight.TranslationConstraint;
            var tuning=gameObject.AddComponent<TraversalTuning>();
            var human=new HumanTraversalInput(previousInput,()=>app.KeyboardInput.InputEnabled && app.KeyboardInput.Captured && Application.isFocused);
            swing = new WebSwingMotionInput(human, app.Flight, app.Origin,tuning.Settings) { Paused = true, AutomaticCruiseAssist = true };
            swing.ReconnectAssist=tuning.Reconnect;
            var features=swing.Body.Settings.features;
            Debug.Log($"TRAVERSAL_FEATURES steering={features.preserveSteeringMomentum} glancing={features.preserveGlancingContact} transitions={features.preserveTransitionMomentum} wrap={features.resetLengthAtWrap} pacing={features.attachmentDrop} fastAttach={features.fixedAttachmentDelay} reconnect={swing.ReconnectAssist.enabled}");
            app.Flight.gameObject.AddComponent<SnagLogger>().Attach(swing);
            if (app.catalog != null) swing.Collision.CitySolids = new CitySolidRecovery(app.catalog.buildings, swing.Body);
            swing.CanSimulate = () => app.KeyboardInput.InputEnabled && app.KeyboardInput.Captured && Application.isFocused;
            app.Flight.InputSource = swing;
            app.Flight.TranslationConstraint = new FlightCollisionGate(swing);
            app.Streamer.CollisionProxiesEnabled = true;
            hands = gameObject.AddComponent<TraversalHands>(); hands.Configure(swing, app.Display);
            swing.InputFireRequested+=x=>{hands.Select(x);lastFireFrame=Time.frameCount;};
            gameObject.AddComponent<WebRopeVisual>().Configure(swing, app.Origin, hands);
            gameObject.AddComponent<TraversalSpeedEffects>().Configure(swing,Camera.main);
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
                GUI.Box(new Rect(12, Screen.height - 60, 740, 48), "Click to capture. Space: jump. Aim HIGH and HOLD left mouse to attach.\nW: boost | Let go LMB: release | HOLD RMB / E: reel in | Q: reel out | R: flight");
                return;
            }
            GUI.Label(new Rect(Screen.width * .5f - 8, Screen.height * .5f - 12, 20, 24), "+");
            GUI.Label(new Rect(12, Screen.height - 52, 1200, 24), "Space: jump | HOLD LMB: attach | Let go LMB: release | W: boost | HOLD RMB / E: reel in | Q: reel out | R: flight");
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
