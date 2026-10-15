using System;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
namespace Manhattan.TraversalLab.Editor
{
    [InitializeOnLoad]
    public static class TraversalLabPackages
    {
        public const string Version="4.1.0";
        private const string PendingKey="TraversalLab.PackageInstallPending",DeadlineKey="TraversalLab.PackageInstallDeadline";
        private static AddAndRemoveRequest request;
        static TraversalLabPackages()
        {if(SessionState.GetBool(PendingKey,false))EditorApplication.delayCall+=Subscribe;}
        public static void Install()
        {
            Debug.Log("Installing com.unity.ml-agents@"+Version+" through UPM Client API.");
            SessionState.SetBool(PendingKey,true);SessionState.SetString(DeadlineKey,DateTime.UtcNow.AddMinutes(10).Ticks.ToString());
            try{request=Client.AddAndRemove(new[]{"com.unity.ml-agents@"+Version});Subscribe();}
            catch(Exception e){Finish(false,e.Message);}
        }
        private static void Subscribe(){EditorApplication.update-=Poll;EditorApplication.update+=Poll;}
        private static void Poll()
        {
            if(long.TryParse(SessionState.GetString(DeadlineKey,"0"),out long deadline) && DateTime.UtcNow.Ticks>deadline)
            {Finish(false,"UPM request or package registration timed out");return;}
            if(request!=null)
            {
                if(!request.IsCompleted)return;
                if(request.Status!=StatusCode.Success){Finish(false,request.Error?.message??"UPM request failed");return;}
            }
            // UPM can complete before AssetDatabase registers the package; a domain
            // reload can also replace this static instance while importing it.
            if(EditorApplication.isCompiling || EditorApplication.isUpdating || InstalledVersion()!=Version)return;
            try{ActivateSources();Finish(true,"Pinned ML-Agents package resolved and training sources activated.");}
            catch(Exception e){Finish(false,e.Message);}
        }
        private static string InstalledVersion()=>Array.Find(UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages(),p=>p.name=="com.unity.ml-agents")?.version;
        private static void Finish(bool success,string message)
        {
            EditorApplication.update-=Poll;SessionState.SetBool(PendingKey,false);
            if(success)Debug.Log(message);else Debug.LogError(message);EditorApplication.Exit(success?0:2);
        }
        [MenuItem("Tools/Traversal/Activate installed ML-Agents sources")]
        public static void ActivateSources()
        {
            if(InstalledVersion()!=Version)throw new InvalidOperationException("Install com.unity.ml-agents@"+Version+" first.");
            string source=Path.Combine(Application.dataPath,"TraversalLab/ML~"),target=Path.Combine(Application.dataPath,"TraversalLab/MLGenerated");Directory.CreateDirectory(target);
            foreach(var path in Directory.GetFiles(source))File.Copy(path,Path.Combine(target,Path.GetFileName(path)),true);
            // Clear the resume flag before Refresh can trigger a domain reload.
            SessionState.SetBool(PendingKey,false);AssetDatabase.Refresh();
        }
    }
}
