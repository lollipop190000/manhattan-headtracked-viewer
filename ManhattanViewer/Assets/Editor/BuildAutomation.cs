using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Manhattan.Viewer;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.SceneManagement;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Manhattan.Editor
{
    public static class BuildAutomation
    {
        private static AddAndRemoveRequest request;
        private static double deadline;
        public static void InstallPackages()
        {
            string revision = Environment.GetEnvironmentVariable("MANHATTAN_TECHNOLOGY_REVISION");
            if (string.IsNullOrEmpty(revision) || revision.Length != 40) throw new ArgumentException("Set MANHATTAN_TECHNOLOGY_REVISION to the tested 40-character Git commit.");
            string package = "https://github.com/lollipop190000/head-tracked-display.git?path=/Packages/com.headtracked.display#" + revision;
            request = Client.AddAndRemove(new[] { package, "com.unity.cloud.gltfast@6.20.0", "com.unity.addressables@2.7.6", "com.unity.inputsystem@1.20.0", "com.unity.render-pipelines.universal@17.3.0", "com.unity.test-framework@1.6.0", "com.unity.ugui@2.0.0", "com.unity.nuget.newtonsoft-json@3.2.2" });
            deadline = EditorApplication.timeSinceStartup + 600;
            EditorApplication.update += Poll;
        }
        private static void Poll()
        {
            if (!request.IsCompleted && EditorApplication.timeSinceStartup < deadline) return;
            EditorApplication.update -= Poll;
            bool ok = request.IsCompleted && request.Status == StatusCode.Success;
            if (ok) Debug.Log("MANHATTAN_PACKAGES " + string.Join(", ", request.Result.Select(p => p.name + "@" + p.version)));
            else Debug.LogError(request.Error?.message ?? "Package installation timed out");
            EditorApplication.Exit(ok ? 0 : 1);
        }

        [MenuItem("Manhattan/Create viewer scene and render settings")]
        public static void ConfigureProject()
        {
            // Remove only the untouched blank-template tutorial and sample scene.
            AssetDatabase.DeleteAsset("Assets/Readme.asset");
            AssetDatabase.DeleteAsset("Assets/TutorialInfo");
            AssetDatabase.DeleteAsset("Assets/Scenes/SampleScene.unity");
            PlayerSettings.companyName = "HeadTrackedDisplay"; PlayerSettings.productName = "ManhattanViewer";
            PlayerSettings.bundleVersion = "0.1.0"; PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultScreenWidth = 1920; PlayerSettings.defaultScreenHeight = 1200;
            PlayerSettings.fullScreenMode = FullScreenMode.ExclusiveFullScreen; PlayerSettings.runInBackground = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            var player = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var input = player.FindProperty("activeInputHandler"); if (input != null) input.intValue = 1;
            player.ApplyModifiedPropertiesWithoutUndo();
            Directory.CreateDirectory("Assets/Settings"); Directory.CreateDirectory("Assets/Scenes"); AssetDatabase.Refresh();
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/ManhattanRenderer.asset");
            if (renderer == null) { renderer = ScriptableObject.CreateInstance<UniversalRendererData>(); AssetDatabase.CreateAsset(renderer, "Assets/Settings/ManhattanRenderer.asset"); }
            var rendererProperties = new SerializedObject(renderer);
            var post = rendererProperties.FindProperty("postProcessData"); if(post!=null)post.objectReferenceValue=AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            rendererProperties.ApplyModifiedPropertiesWithoutUndo();
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/ManhattanURP.asset");
            if (pipeline == null) { pipeline = UniversalRenderPipelineAsset.Create(renderer); AssetDatabase.CreateAsset(pipeline, "Assets/Settings/ManhattanURP.asset"); }
            pipeline.msaaSampleCount = 1; pipeline.renderScale = 1; pipeline.supportsHDR = true;
            pipeline.shadowDistance = 150; pipeline.shadowCascadeCount = 4; pipeline.cascade4Split = new Vector3(.08f,.22f,.50f);
            var props = new SerializedObject(pipeline);
            SetBool(props,"m_SoftShadowsSupported",true);SetBool(props,"m_UseSRPBatcher",true);SetBool(props,"m_EnableLODCrossFade",true);
            var resolution=props.FindProperty("m_MainLightShadowmapResolution");if(resolution!=null)resolution.intValue=2048;
            props.ApplyModifiedPropertiesWithoutUndo();
            GraphicsSettings.defaultRenderPipeline = pipeline;
            for(int i=0;i<QualitySettings.names.Length;i++){QualitySettings.SetQualityLevel(i);QualitySettings.renderPipeline=pipeline;QualitySettings.vSyncCount=1;}
            QualitySettings.SetQualityLevel(0);
            var ao = renderer.rendererFeatures.Find(f=>f!=null&&f.GetType().Name=="ScreenSpaceAmbientOcclusion");
            if(ao==null){var type=typeof(UniversalRendererData).Assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion");if(type!=null){ao=(ScriptableRendererFeature)ScriptableObject.CreateInstance(type);ao.name="Restrained contact shading";AssetDatabase.AddObjectToAsset(ao,renderer);renderer.rendererFeatures.Add(ao);}}
            if(ao!=null){var data=new SerializedObject(ao);SetBool(data,"m_Settings.Downsample",true);var radius=data.FindProperty("m_Settings.Radius");if(radius!=null)radius.floatValue=.4f;var intensity=data.FindProperty("m_Settings.Intensity");if(intensity!=null)intensity.floatValue=.45f;data.ApplyModifiedPropertiesWithoutUndo();}
            renderer.SetDirty();EditorUtility.SetDirty(renderer);EditorUtility.SetDirty(pipeline);
            var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Settings/AfternoonSky.mat");
            if(sky==null){sky=new Material(Shader.Find("Skybox/Procedural"));AssetDatabase.CreateAsset(sky,"Assets/Settings/AfternoonSky.mat");}
            sky.SetColor("_SkyTint",new Color(.45f,.58f,.75f));sky.SetColor("_GroundColor",new Color(.29f,.26f,.22f));sky.SetFloat("_AtmosphereThickness",1.1f);sky.SetFloat("_Exposure",1.1f);EditorUtility.SetDirty(sky);
            // Keep the startup shader available before any streamed city bundle is loaded.
            Directory.CreateDirectory("Assets/Resources");
            var windowFrame = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/WindowFrameMaterial.mat");
            if(windowFrame==null){windowFrame=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(windowFrame,"Assets/Resources/WindowFrameMaterial.mat");}
            windowFrame.SetColor("_BaseColor",new Color(.13f,.16f,.19f));windowFrame.SetFloat("_Metallic",.4f);windowFrame.SetFloat("_Smoothness",.65f);EditorUtility.SetDirty(windowFrame);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = sky; RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            var go = new GameObject("Manhattan Viewer"); var bootstrap = go.AddComponent<ManhattanBootstrap>();
            bootstrap.catalog = AssetDatabase.LoadAssetAtPath<CityCatalog>("Assets/Resources/CityCatalog.asset");
            bootstrap.windowFrameMaterial = windowFrame;
            ConfigureCityDetailMaterials(bootstrap.catalog);
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/ManhattanViewer.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/ManhattanViewer.unity", true) };
            AssetDatabase.SaveAssets();
        }
        private static void SetBool(SerializedObject obj,string name,bool value){var p=obj.FindProperty(name);if(p!=null)p.boolValue=value;}

        private static void ConfigureCityDetailMaterials(CityCatalog catalog)
        {
            const string shaderName = "Manhattan/CityDetailLit";
            const string materialPath = "Assets/Resources/CityDetailMaterial.mat";
            const string variantsPath = "Assets/Resources/CityDetailVariants";
            Shader shader = Shader.Find(shaderName);
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("City detail shader is missing or has compile errors: " + shaderName);
            var template = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (template == null) { template = new Material(shader); AssetDatabase.CreateAsset(template, materialPath); }
            template.shader = shader; template.SetFloat(SurfaceDetailComparison.DistanceFadeProperty, 1f); EditorUtility.SetDirty(template);
            // Runtime shader switches need the same local keyword variants as the
            // imported detail materials. Keep both comparison states in the build
            // without making Resources reference all native city textures/meshes.
            Directory.CreateDirectory(variantsPath); AssetDatabase.Refresh();
            var sources = new Dictionary<string, (Material source, string[] keywords)>(StringComparer.Ordinal);
            string[] flattened = { "_NORMALMAP", "_PARALLAXMAP", "_DETAIL_MULX2", "_DETAIL_SCALED", "_METALLICSPECGLOSSMAP", "_OCCLUSIONMAP", "_EMISSION" };
            if (catalog != null)
            {
                string[] paths = catalog.tiles.Where(t => t.layer == CityLayer.Near).Select(t => "Assets/Generated/City/Tiles/" + t.id + ".prefab").ToArray();
                foreach (string dependency in AssetDatabase.GetDependencies(paths, true))
                {
                    if (!dependency.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) continue;
                    Material source = AssetDatabase.LoadAssetAtPath<Material>(dependency); if (source == null) continue;
                    foreach (string[] keywords in new[] { source.shaderKeywords, source.shaderKeywords.Where(k => !flattened.Contains(k)).ToArray() })
                    {
                        string[] sorted = keywords.OrderBy(k => k, StringComparer.Ordinal).ToArray(); string key = string.Join("|", sorted);
                        if (!sources.ContainsKey(key)) sources.Add(key, (source, sorted));
                    }
                }
            }
            var retained = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in sources)
            {
                string digest; using (var hash = SHA256.Create()) digest = BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(pair.Key))).Replace("-", "").Substring(0, 16).ToLowerInvariant();
                string path = variantsPath + "/Keywords_" + digest + ".mat"; retained.Add(path);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
                material.shader = shader; material.CopyPropertiesFromMaterial(pair.Value.source);
                foreach (string property in material.GetTexturePropertyNames()) material.SetTexture(property, property.Contains("Bump") || property.Contains("Normal") ? Texture2D.normalTexture : Texture2D.whiteTexture);
                material.shaderKeywords = pair.Value.keywords; material.SetFloat(SurfaceDetailComparison.DistanceFadeProperty, 1f); EditorUtility.SetDirty(material);
            }
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { variantsPath }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid); if (!retained.Contains(path)) AssetDatabase.DeleteAsset(path);
            }
            foreach (string dependency in AssetDatabase.GetDependencies(retained.Append(materialPath).ToArray(), true))
                if (dependency.StartsWith("Assets/Generated/City/", StringComparison.Ordinal) || dependency.StartsWith("Assets/CitySource/", StringComparison.Ordinal))
                    throw new InvalidDataException("City detail shader retention must not force city data into Resources: " + dependency);
            Debug.Log("CITY_DETAIL_SHADER_VARIANTS " + sources.Count + " keyword combinations retained without native city dependencies.");
        }

        public static void BuildWindows()
        {
            if(AssetDatabase.LoadAssetAtPath<CityCatalog>("Assets/Resources/CityCatalog.asset")==null)throw new InvalidOperationException("Run CityBake.BuildAll first.");
            WriteBuildIdentity();
            ConfigureProject();
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult addressableResult);
            if(!string.IsNullOrEmpty(addressableResult.Error))throw new InvalidOperationException(addressableResult.Error);
            // Content was built above; prevent the player preprocessor from building it a second time.
            UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
            string output=Environment.GetEnvironmentVariable("MANHATTAN_BUILD_PATH");
            if(string.IsNullOrEmpty(output))output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Builds/ManhattanViewer/ManhattanViewer.exe"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Player build failed: "+report.summary.result);
            Debug.Log("MANHATTAN_WINDOWS_BUILD "+output+" bytes="+report.summary.totalSize);
        }

        private static void WriteBuildIdentity()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            string revision = ReadGit(root, "rev-parse HEAD");
            bool dirty = !string.IsNullOrWhiteSpace(ReadGit(root, "status --porcelain --untracked-files=no"));
            var technology = JObject.Parse(File.ReadAllText(Path.Combine(root, "tools/technology-version.json")));
            string sourcePath = Path.Combine(root, "assets/source-manifest.json");
            string sourceHash;
            using (var sha = SHA256.Create()) sourceHash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(sourcePath))).Replace("-", "").ToLowerInvariant();
            var identity = new JObject
            {
                ["viewerRevision"] = string.IsNullOrEmpty(revision) ? "uncommitted" : revision,
                ["technologyRevision"] = (string)technology["revision"],
                ["sourceManifestSha256"] = sourceHash,
                ["buildUtc"] = DateTime.UtcNow.ToString("O"),
                ["dirtyWorkingTree"] = dirty
            };
            Directory.CreateDirectory("Assets/Resources");
            File.WriteAllText("Assets/Resources/ViewerBuildIdentity.json", identity.ToString());
            AssetDatabase.ImportAsset("Assets/Resources/ViewerBuildIdentity.json", ImportAssetOptions.ForceSynchronousImport);
        }

        private static string ReadGit(string root, string arguments)
        {
            var start = new System.Diagnostics.ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var process = System.Diagnostics.Process.Start(start))
            {
                string output = process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == 0 ? output.Trim() : string.Empty;
            }
        }
    }
}
