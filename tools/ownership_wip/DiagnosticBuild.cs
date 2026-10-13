using System;
using System.IO;
using System.Linq;
using Manhattan.Viewer;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Local diagnostic build only. Existing city bundles are loaded read-only at runtime.
public static class TemporaryLatestCityBuild
{
    public static void Build()
    {
        try
        {
            string output = Environment.GetEnvironmentVariable("MANHATTAN_LATEST_BUILD_OUTPUT");
            if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("Owned build output required");
            Directory.CreateDirectory(output);
            var catalog = AssetDatabase.LoadAssetAtPath<CityCatalog>("Assets/Resources/CityCatalog.asset");
            if (catalog == null || catalog.tiles.Length != 10038) throw new InvalidOperationException("Existing v2 city catalog unavailable");
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/ManhattanURP.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultScreenWidth = 1920; PlayerSettings.defaultScreenHeight = 1200;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.enableFrameTimingStats = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            var identity = new ViewerBuildIdentity { viewerRevision="c8f3904b17ec1cd4d3471bd59e198ce10d429957", technologyRevision="960d5de55037a3fe2739ed055c8de091b8c54aed", sourceManifestSha256="82bc35bea819762e3832210187232cafa8e16bea9fd92ecf3cf8dc2f683f8bc7", buildUtc=DateTime.UtcNow.ToString("O"), dirtyWorkingTree=true };
            File.WriteAllText("Assets/Resources/ViewerBuildIdentity.json", JsonUtility.ToJson(identity, true));
            AssetDatabase.ImportAsset("Assets/Resources/ViewerBuildIdentity.json", ImportAssetOptions.ForceSynchronousImport);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Latest public viewer with local diagnostic harness").AddComponent<ManhattanBootstrap>().catalog = catalog;
            RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>("Assets/Settings/AfternoonSky.mat");
            const string scenePath = "Assets/Scenes/TemporaryLatestCityDiagnostic.unity";
            Directory.CreateDirectory("Assets/Scenes"); EditorSceneManager.SaveScene(scene, scenePath);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{scenePath}, locationPathName=Path.Combine(output,"Player","ManhattanDiagnostic.exe"), target=BuildTarget.StandaloneWindows64, options=BuildOptions.None });
            File.WriteAllText(Path.Combine(output,"build-summary.json"), JsonUtility.ToJson(new BuildSummary { result=report.summary.result.ToString(), errors=report.summary.totalErrors, warnings=report.summary.totalWarnings, bytes=report.summary.totalSize, seconds=report.summary.totalTime.TotalSeconds, publicRevision=identity.viewerRevision, existingCatalogVersion=catalog.sourceVersion, dirtyDiagnosticBuild=true },true));
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    [Serializable] private sealed class BuildSummary { public string result, publicRevision, existingCatalogVersion; public int errors,warnings; public ulong bytes; public double seconds; public bool dirtyDiagnosticBuild; }
}
