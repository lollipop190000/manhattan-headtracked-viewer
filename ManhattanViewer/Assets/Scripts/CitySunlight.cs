using UnityEngine;
using UnityEngine.Rendering;

namespace Manhattan.Viewer
{
    /// <summary>Session-only afternoon preview. World-space sunlight never follows the observer.</summary>
    public sealed class CitySunlight : MonoBehaviour
    {
        public const float DefaultPosition = .6f;
        public const float PreviewDurationSeconds = 300f;
        public Light Sun { get; private set; }
        public float Position { get; private set; } = DefaultPosition;
        public bool Moving { get; private set; }
        public bool AtEnd => Position >= 1f;
        private EnvironmentSnapshot previous;
        private bool configured;

        public void Configure()
        {
            if (configured) return;
            previous = new EnvironmentSnapshot(); configured = true;
            var sunObject = new GameObject("Afternoon Sun");
            sunObject.transform.SetParent(transform, false);
            Sun = sunObject.AddComponent<Light>(); Sun.type = LightType.Directional;
            Sun.shadows = LightShadows.Soft; Sun.shadowBias = .15f; Sun.shadowNormalBias = .3f;
            RenderSettings.sun = Sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            // A little more sky fill preserves readable shaded facades without another light.
            RenderSettings.ambientSkyColor = new Color(.58f, .68f, .80f);
            RenderSettings.ambientEquatorColor = new Color(.48f, .51f, .56f);
            RenderSettings.ambientGroundColor = new Color(.28f, .27f, .27f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(.62f, .69f, .75f); RenderSettings.fogDensity = .00007f;
            ResetAfternoon();
        }

        public void SetMoving(bool value) => Moving = configured && value && !AtEnd;
        public void SetPosition(float value)
        {
            if (!float.IsFinite(value)) return;
            Position = Mathf.Clamp01(value);
            if (AtEnd) Moving = false;
            ApplySun();
        }
        public void ResetAfternoon() { Moving = false; SetPosition(DefaultPosition); }
        public void Advance(float seconds)
        {
            if (Moving && float.IsFinite(seconds) && seconds > 0)
                SetPosition(Position + seconds / PreviewDurationSeconds);
        }
        private void Update() => Advance(Time.unscaledDeltaTime);
        private void LateUpdate() => ApplySun();
        private void ApplySun()
        {
            if (Sun == null) return;
            // An illustrative daylight arc, not an astronomical date/time simulation.
            // The original 28 / 225 degree afternoon direction is retained at 60%.
            Sun.transform.rotation = Quaternion.Euler(Mathf.Lerp(52f, 12f, Position), Mathf.Lerp(180f, 255f, Position), 0);
            Sun.color = Color.Lerp(new Color(1f, .96f, .87f), new Color(1f, .83f, .68f), Position);
            Sun.intensity = Mathf.Lerp(1.9f, 1.65f, Position);
        }
        private void OnDestroy()
        {
            if (configured && RenderSettings.sun == Sun) previous.Restore();
            if (Sun != null) Destroy(Sun.gameObject);
        }

        private sealed class EnvironmentSnapshot
        {
            private readonly Light sun = RenderSettings.sun;
            private readonly AmbientMode ambient = RenderSettings.ambientMode;
            private readonly Color sky = RenderSettings.ambientSkyColor, equator = RenderSettings.ambientEquatorColor,
                ground = RenderSettings.ambientGroundColor, fogColor = RenderSettings.fogColor;
            private readonly bool fog = RenderSettings.fog;
            private readonly FogMode fogMode = RenderSettings.fogMode;
            private readonly float fogDensity = RenderSettings.fogDensity;
            public void Restore()
            {
                RenderSettings.sun = sun; RenderSettings.ambientMode = ambient;
                RenderSettings.ambientSkyColor = sky; RenderSettings.ambientEquatorColor = equator; RenderSettings.ambientGroundColor = ground;
                RenderSettings.fog = fog; RenderSettings.fogMode = fogMode; RenderSettings.fogColor = fogColor; RenderSettings.fogDensity = fogDensity;
            }
        }
    }
}
