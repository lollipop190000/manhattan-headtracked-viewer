using Manhattan.Viewer;
using UnityEngine;

namespace WebShooter.Swing
{
    public sealed class WebRopeVisual : MonoBehaviour
    {
        private WebSwingMotionInput input;
        private FloatingOrigin origin;
        private LineRenderer line;
        private Material material;
        private readonly Vector3[] points = new Vector3[32];
        private bool previouslyVisible;
        private Vector3 lastStartCity, lastEndCity;
        private float fade;
        public void Configure(WebSwingMotionInput input, FloatingOrigin origin)
        {
            this.input = input; this.origin = origin;
            line = gameObject.AddComponent<LineRenderer>();
            line.positionCount = points.Length;
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
            bool visible = input.Enabled && (input.Flying || input.Body.Attached);
            Vector3 start = camera.position + camera.right * .25f - camera.up * .2f + camera.forward * .6f;
            Vector3 end = input.Flying ? Vector3.Lerp(start, input.ShotTarget - offset, input.ShotProgress) : input.Body.Anchor - offset;
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
            float slack = visible && !input.Flying ? Mathf.Clamp(input.Body.RopeLength - Vector3.Distance(input.Body.Position, input.Body.Anchor), 0f, 8f) * .5f : .1f;
            for (int i = 0; i < points.Length; i++)
            {
                float t = i / (float)(points.Length - 1);
                points[i] = Vector3.Lerp(start, end, t) - Vector3.up * (Mathf.Sin(t * Mathf.PI) * slack);
            }
            line.widthMultiplier = .018f * (visible ? 1f : fade / .3f);
            line.SetPositions(points);
        }
        private void OnDestroy() { if (material != null) Destroy(material); }
    }
}
