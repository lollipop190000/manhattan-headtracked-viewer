using Manhattan.Viewer;
using UnityEngine;

namespace WebShooter.Swing
{
    [DefaultExecutionOrder(1000)] // Follow the current frame head pose, never the previous frame.
    public sealed class WebRopeVisual : MonoBehaviour
    {
        private WebSwingMotionInput input;
        private FloatingOrigin origin;
        private TraversalHands hands;
        private LineRenderer line;
        private Material material;
        private readonly Vector3[] points = new Vector3[32];
        private bool previouslyVisible;
        private Vector3 lastStartCity, lastEndCity;
        private float fade;
        public void Configure(WebSwingMotionInput input, FloatingOrigin origin, TraversalHands hands = null)
        {
            this.input = input; this.origin = origin; this.hands = hands;
            line = gameObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.widthMultiplier = .018f;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var template = Resources.Load<Material>("WindowFrameMaterial");
            material = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", new Color(.95f, .95f, .95f));
            line.sharedMaterial = material;
            line.enabled = false;
        }
        private void LateUpdate()
        {
            if (input == null || Camera.main == null) return;
            var camera = Camera.main.transform;
            Vector3 offset = origin != null ? origin.Offset : Vector3.zero;
            bool visible = input.Enabled && ((input.Flying && input.ShotAge >= WebSwingMotionInput.GestureSeconds) || input.Body.Attached);
            Vector3 start = hands != null ? hands.WebOrigin : camera.position + camera.right * .25f - camera.up * .2f + camera.forward * .6f;
            Vector3 end = input.Flying ? Vector3.Lerp(start, input.ShotTarget - offset, input.ShotProgress) : input.ShotTarget - offset;
            if (visible) { lastStartCity = start + offset; lastEndCity = end + offset; fade = 0f; }
            else
            {
                if (previouslyVisible) fade = .3f;
                if (!input.Paused) fade = Mathf.Max(0f, fade - Time.deltaTime);
                start = lastStartCity - offset - Vector3.up * (1f - fade / .3f);
                end = lastEndCity - offset;
            }
            previouslyVisible = visible;
            line.enabled = visible || fade > 0f;
            if (!line.enabled) return;
            // Taut visual path. Manual payout affects the physical constraint, not a dangling sine wave.
            int count = 2;
            points[0] = start; points[1] = end;
            if (visible && input.Body.Attached && input.Route.Points.Count > 0)
            {
                count = input.Route.Points.Count + 1;
                for (int i = 0; i < input.Route.Points.Count; i++) points[i + 1] = input.Route.Points[i] - offset;
            }
            line.positionCount = count;
            for (int i = 0; i < count; i++) line.SetPosition(i, points[i]);
            line.widthMultiplier = .018f * (visible ? 1f : fade / .3f);

        }
        private void OnDestroy() { if (material != null) Destroy(material); }
    }
}
