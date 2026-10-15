using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace Manhattan.TraversalLab.Editor
{
    public static class TraversalLabBuild
    {
        public const string ScenePath="Assets/TraversalLab/Scenes/TraversalLab.unity";
        [MenuItem("Tools/Traversal/Create test scene")]
        public static void CreateScene()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("Traversal Lab").AddComponent<TraversalLabBootstrap>();EditorSceneManager.SaveScene(scene,ScenePath);
        }
        public static void BuildAgent()=>Build(true);
        public static void BuildScripted()=>Build(false);
        private static void Build(bool agent)
        {
            try
            {
                if(agent && Type.GetType("Manhattan.TraversalLab.TraversalAgentLauncher, Manhattan.Traversal.ML")==null)
                    throw new InvalidOperationException("Run the pinned package installer, then restart this build.");
                CreateScene();
                string path=Environment.GetEnvironmentVariable("MANHATTAN_LAB_BUILD");
                if(string.IsNullOrEmpty(path))path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../outputs/TraversalLabPlayer/TraversalLab.exe"));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var settings=AddressableAssetSettingsDefaultObject.Settings;
                var previous=settings!=null?settings.BuildAddressablesWithPlayerBuild:default;
                UnityEditor.Build.Reporting.BuildReport result;
                try
                {
                    // The isolated lab uses primitive geometry, not city bundles.
                    // Avoid the Addressables player hook rebuilding all city tiles.
                    if(settings!=null)settings.BuildAddressablesWithPlayerBuild=AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
                    result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=path,
                        target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
                }
                finally
                {
                    if(settings!=null)
                    {settings.BuildAddressablesWithPlayerBuild=previous;EditorUtility.SetDirty(settings);AssetDatabase.SaveAssetIfDirty(settings);}
                }
                if(result.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException(result.summary.result.ToString());
                Debug.Log("TRAVERSAL_LAB_BUILD bytes="+result.summary.totalSize+" path="+path);EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(2);}
        }
    }
}
