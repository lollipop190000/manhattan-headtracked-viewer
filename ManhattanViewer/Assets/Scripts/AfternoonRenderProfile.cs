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
        public CitySunlight Sunlight { get; private set; }
        public static void ConfigureShadows(UniversalRenderPipelineAsset pipeline)
        {
            // Retain dense near cascades while moving the final fade away from nearby buildings.
            pipeline.shadowCascadeCount = 4; pipeline.shadowDistance = 800f;
            pipeline.mainLightShadowmapResolution = 4096;
            pipeline.cascade4Split = new Vector3(.035f, .13f, .4f);
            pipeline.cascadeBorder = .35f;
        }
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
                ConfigureShadows(runtimePipeline);
                QualitySettings.renderPipeline = runtimePipeline;
            }
            Sunlight = gameObject.AddComponent<CitySunlight>(); Sunlight.Configure();
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
