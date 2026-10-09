using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Manhattan.Viewer
{
    public sealed class AfternoonRenderProfile : MonoBehaviour
    {
        public Camera targetCamera;
        private UniversalAdditionalCameraData cameraData;
        private UniversalRenderPipelineAsset runtimePipeline;
        private RenderPipelineAsset previousQualityPipeline;
        private VolumeProfile runtimeVolume;
        public void Configure(Camera camera)
        {
            targetCamera = camera;
            camera.allowHDR = true; camera.allowMSAA = false; camera.allowDynamicResolution = false;
            cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            SetTemporalAA(true);
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline != null)
            {
                previousQualityPipeline = QualitySettings.renderPipeline;
                runtimePipeline = Instantiate(pipeline); runtimePipeline.name = "Manhattan Runtime Render Profile";
                runtimePipeline.hideFlags = HideFlags.DontSave;
                runtimePipeline.msaaSampleCount = 1; runtimePipeline.renderScale = 1f;
                runtimePipeline.shadowCascadeCount = 4; runtimePipeline.shadowDistance = 150f;
                QualitySettings.renderPipeline = runtimePipeline;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.47f, .58f, .73f);
            RenderSettings.ambientEquatorColor = new Color(.36f, .39f, .43f);
            RenderSettings.ambientGroundColor = new Color(.18f, .16f, .15f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(.62f, .69f, .75f); RenderSettings.fogDensity = .00007f;
            var sunObject = new GameObject("Late Afternoon Sun");
            sunObject.transform.SetParent(transform, false);
            sunObject.transform.rotation = Quaternion.Euler(28f, 225f, 0f);
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional; sun.color = new Color(1f, .81f, .62f); sun.intensity = 2f;
            sun.shadows = LightShadows.Soft; sun.shadowBias = .025f; sun.shadowNormalBias = .2f;
            RenderSettings.sun = sun;
            var volume = new GameObject("Fixed Afternoon Grade").AddComponent<Volume>();
            volume.transform.SetParent(transform, false);
            volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            runtimeVolume = profile;
            var tone = profile.Add<Tonemapping>(); tone.mode.Override(TonemappingMode.ACES);
            var color = profile.Add<ColorAdjustments>(); color.postExposure.Override(0f); color.contrast.Override(7f); color.saturation.Override(3f);
            volume.sharedProfile = profile;
        }
        public void SetTemporalAA(bool value)
        {
            if (cameraData == null) return;
            var desired = value ? AntialiasingMode.TemporalAntiAliasing : AntialiasingMode.None;
            if (cameraData.antialiasing == desired) return;
            cameraData.antialiasing = desired;
            ref var taa = ref cameraData.taaSettings;
            taa.quality = TemporalAAQuality.Medium; taa.baseBlendFactor = .75f; taa.jitterScale = .75f;
            taa.mipBias = 0f; taa.varianceClampScale = .9f; taa.contrastAdaptiveSharpening = 0f;
            ResetHistory();
        }
        public void ResetHistory() { if (cameraData != null) cameraData.resetHistory = true; }
        private void OnDestroy()
        {
            if (runtimePipeline != null)
            {
                if (QualitySettings.renderPipeline == runtimePipeline) QualitySettings.renderPipeline = previousQualityPipeline;
                Destroy(runtimePipeline);
            }
            if (runtimeVolume != null) Destroy(runtimeVolume);
        }
    }
}
