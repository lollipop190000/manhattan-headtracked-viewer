using System;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
namespace Manhattan.TraversalLab.Editor
{
    public static class TraversalLabPackages
    {
        public const string Version="4.1.0";
        private static AddAndRemoveRequest request;
        private static double deadline;
        public static void Install()
        {
            Debug.Log("Installing com.unity.ml-agents@"+Version+" through UPM Client API.");
            request=Client.AddAndRemove(new[]{"com.unity.ml-agents@"+Version});deadline=EditorApplication.timeSinceStartup+600;EditorApplication.update+=Poll;
        }
        private static void Poll()
        {
            if(!request.IsCompleted && EditorApplication.timeSinceStartup<deadline)return;
            EditorApplication.update-=Poll;
            if(!request.IsCompleted || request.Status!=StatusCode.Success)
            {Debug.LogError(request.IsCompleted?request.Error.message:"UPM request timed out");EditorApplication.Exit(2);return;}
            ActivateSources();Debug.Log("Pinned ML-Agents package resolved and training sources activated.");EditorApplication.Exit(0);
        }
        [MenuItem("Tools/Traversal/Activate installed ML-Agents sources")]
        public static void ActivateSources()
        {
            var package=UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.ml-agents/package.json");
            if(package==null || package.version!=Version)throw new InvalidOperationException("Install com.unity.ml-agents@"+Version+" first.");
            string source=Path.Combine(Application.dataPath,"TraversalLab/ML~"),target=Path.Combine(Application.dataPath,"TraversalLab/MLGenerated");Directory.CreateDirectory(target);
            foreach(var path in Directory.GetFiles(source))File.Copy(path,Path.Combine(target,Path.GetFileName(path)),true);
            AssetDatabase.Refresh();
        }
    }
}
