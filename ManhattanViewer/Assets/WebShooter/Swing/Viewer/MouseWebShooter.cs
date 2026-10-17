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
        private ViewerSettingsUI viewerUI;
        private bool lastHelpMode,hasHelpMode;
        public WebSwingMotionInput Motion => swing;
        private void OnEnable() { Application.wantsToQuit += LogQuit; }
        private void OnDisable() { Application.wantsToQuit -= LogQuit; swing?.Launch.Cancel();swing?.Release(); }
        private void OnApplicationFocus(bool focused) { if (!focused) {swing?.Launch.Cancel();swing?.Release();} }
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
            if(viewerUI!=null && (!hasHelpMode || lastHelpMode!=swing.Enabled))
            {
                viewerUI.SetMovementHint(swing.Enabled?"WASD steer · Q hold/release launch · RMB/E reel · Z pay out · R flight":"WASD fly · Q/E descend/rise · Mouse look · R swing · Esc settings");
                hasHelpMode=true;lastHelpMode=swing.Enabled;
            }
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
            viewerUI=app.GetComponent<ViewerSettingsUI>();
            previousConstraint = app.Flight.TranslationConstraint;
            var tuning=gameObject.AddComponent<TraversalTuning>();
            var human=new HumanTraversalInput(previousInput,()=>app.KeyboardInput.InputEnabled && app.KeyboardInput.Captured && Application.isFocused);
            swing = new WebSwingMotionInput(human, app.Flight, app.Origin,tuning.Settings) { Paused = true, AutomaticCruiseAssist = true };
            swing.ReconnectAssist=tuning.Reconnect;
            swing.Launch.Settings=tuning.Launch;
            var features=swing.Body.Settings.features;
            Debug.Log($"TRAVERSAL_FEATURES steering={features.preserveSteeringMomentum} glancing={features.preserveGlancingContact} transitions={features.preserveTransitionMomentum} wrap={features.resetLengthAtWrap} pacing={features.attachmentDrop} fastAttach={features.fixedAttachmentDelay} reconnect={swing.ReconnectAssist.enabled} chain={swing.Body.Settings.chain.enabled}");
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
            gameObject.AddComponent<DualWebLaunchVisual>().Configure(swing,app.Origin,hands,Camera.main);
            gameObject.AddComponent<TraversalTemporalHistory>().Configure(swing,Camera.main);
            gameObject.AddComponent<TraversalContinuityLog>().Configure(swing,app.Flight,app.Origin);
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
                GUI.Box(new Rect(12, Screen.height - 60, 980, 48), "Click to capture. HOLD Q then release: two-hand launch. HOLD left mouse: web swing.\nSpace: jump | W: boost | Let go LMB: detach | RMB / E: reel | Z: pay out | R: flight");
                return;
            }
            GUI.Label(new Rect(Screen.width * .5f - 8, Screen.height * .5f - 12, 20, 24), "+");
            GUI.Label(new Rect(12, Screen.height - 52, 1400, 24), "HOLD Q / release: launch | Space: jump | HOLD LMB: attach | LMB up: detach | W: boost | RMB / E: reel | Z: pay out | R: flight");
            GUI.Label(new Rect(12, Screen.height - 28, 800, 24), swing.Enabled ? $"{swing.Body.State} | {swing.Body.Velocity.magnitude:0.0} m/s | Assist {swing.Body.AssistStrength:0.00} ([ / ])" : "Free flight (wall collision ON)");
            if(swing.Enabled && swing.Launch.Status.Length>0)GUI.Label(new Rect(Screen.width*.5f-180,Screen.height*.67f,500,26),
                swing.Launch.Charging?$"{swing.Launch.Status}  {swing.Launch.Charge*100:0}%":swing.Launch.Status);
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
