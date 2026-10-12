using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Manhattan.Viewer;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace Manhattan.Editor
{
    /// <summary>Explicit offline build step. Compile correspondence from the exact built assets,
    /// never from a possibly newer working mesh. Imported asset files remain unchanged.</summary>
    public static class SkylineOwnershipBake
    {
        private static bool generating;
        private static bool initializedForOwnership;
        // Execute with an owned batch Editor without -quit. The editor must continue
        // updating while catalog/asset loads and previous bundle unloads complete.
        public static async void FromEnvironment()
        {
            int exitCode=1;
            try {await FromEnvironmentAsync();exitCode=0;}
            catch(Exception error){Debug.LogException(error);}
            finally {if(Application.isBatchMode)EditorApplication.Exit(exitCode);}
        }
        public static async Task FromEnvironmentAsync()
        {
            string catalogPath=Environment.GetEnvironmentVariable("MANHATTAN_OWNERSHIP_CATALOG_PATH");
            if(string.IsNullOrEmpty(catalogPath))throw new ArgumentException("Set MANHATTAN_OWNERSHIP_CATALOG_PATH to the built Addressables catalog.bin/json.");
            string skylineId=Environment.GetEnvironmentVariable("MANHATTAN_OWNERSHIP_VALIDATE_SKYLINE_ID");
            if(!string.IsNullOrEmpty(skylineId))await ValidateBuiltGroupAsync(catalogPath,skylineId,Environment.GetEnvironmentVariable("MANHATTAN_OWNERSHIP_VALIDATION_OUTPUT"));
            else await GenerateAsync(catalogPath);
        }
        public static async Task FromBuildSettingsAsync(string settingsPath)
        {
            string folder=Path.GetDirectoryName(Path.GetFullPath(settingsPath));
            string[] catalogs=Directory.GetFiles(folder,"catalog*",SearchOption.TopDirectoryOnly)
                .Where(p=>p.EndsWith(".bin",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".json",StringComparison.OrdinalIgnoreCase)).ToArray();
            if(catalogs.Length!=1)throw new InvalidOperationException("Expected one exact built Addressables catalog beside "+settingsPath);
            await GenerateAsync(catalogs[0]);
        }
        public static async Task ValidateBuiltGroupAsync(string catalogPath,string skylineId,string outputDirectory)
        {
            if(string.IsNullOrEmpty(skylineId)||string.IsNullOrEmpty(outputDirectory))throw new ArgumentException("Validation requires a Skyline id and a separate output directory.");
            string output=Path.GetFullPath(outputDirectory),assets=Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(output.StartsWith(assets,StringComparison.OrdinalIgnoreCase)||string.Equals(output.TrimEnd(Path.DirectorySeparatorChar),assets.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Validation output must be outside Assets; it must not replace the full city sidecar.");
            await GenerateAsync(catalogPath,output,skylineId,false);
        }
        private static async Task GenerateAsync(string catalogPath,string outputDirectory="Assets/Resources",string skylineId=null,bool import=true)
        {
            if(generating)throw new InvalidOperationException("Ownership generation is already running.");
            generating=true;
            Func<IResourceLocation,string> previousTransform=null;
            var previousProviders=new List<IResourceProvider>();
            var previousLocators=new List<IResourceLocator>();
            bool capturedState=false;
            var load=default(AsyncOperationHandle<IResourceLocator>);
            var records=new Dictionary<string,SkylineOwnershipTile>(StringComparer.Ordinal);
            var groups=new List<SkylineOwnershipGroup>();
            try
            {
                var city=AssetDatabase.LoadAssetAtPath<CityCatalog>("Assets/Resources/CityCatalog.asset");
                if(city==null)throw new InvalidOperationException("Existing city catalog required; ownership generation does not bake or migrate assets.");
                catalogPath=Path.GetFullPath(catalogPath);string root=Path.GetDirectoryName(catalogPath).Replace('\\','/');
                // First-use initialization belongs to the shared Editor context. Let it
                // settle before borrowing registrations; do not reverse its private state.
                await EnsureInitializationAsync();
                previousTransform=Addressables.InternalIdTransformFunc;
                foreach(var previousLocator in Addressables.ResourceLocators)previousLocators.Add(previousLocator);
                foreach(var previousProvider in Addressables.ResourceManager.ResourceProviders)previousProviders.Add(previousProvider);
                capturedState=true;
                foreach(var provider in new IResourceProvider[]{new AssetBundleProvider(),new BundledAssetProvider()})
                    if(!Addressables.ResourceManager.ResourceProviders.Any(p=>p.ProviderId==provider.ProviderId))Addressables.ResourceManager.ResourceProviders.Add(provider);
                string runtime=Addressables.RuntimePath.Replace('\\','/').TrimEnd('/');
                Addressables.InternalIdTransformFunc=location=>
                {
                    string id=location.InternalId.Replace('\\','/');
                    return id.StartsWith(runtime+"/",StringComparison.OrdinalIgnoreCase)?root+id.Substring(runtime.Length):previousTransform!=null?previousTransform(location):location.InternalId;
                };
                load=Addressables.LoadContentCatalogAsync(catalogPath,false);
                var locator=await load.Task;if(load.Status!=AsyncOperationStatus.Succeeded||locator==null)throw new InvalidOperationException("Built catalog load failed");
                var skies=city.tiles.Where(t=>t.layer==CityLayer.Skyline&&t.requiredReplacementCount>0&&(skylineId==null||t.id==skylineId)).OrderBy(t=>t.id,StringComparer.Ordinal).ToArray();
                if(skies.Length==0)throw new InvalidOperationException("No matching Skyline group in existing city catalog");
                foreach(var sky in skies)
                {
                    var handles=new List<AsyncOperationHandle<GameObject>>();var instances=new List<GameObject>();
                    try
                    {
                        var related=city.tiles.Where(t=>t.layer==CityLayer.Core&&t.replaces!=null&&Array.IndexOf(t.replaces,sky.id)>=0).Prepend(sky).ToArray();
                        using(var compiler=new OfflineOwnershipGroupCompiler())
                        {
                            foreach(var tile in related)
                            {
                                if(!locator.Locate(tile.address,typeof(GameObject),out IList<IResourceLocation> locations)||locations.Count!=1)throw new InvalidOperationException("Ambiguous/missing built prefab: "+tile.address);
                                var asset=Addressables.LoadAssetAsync<GameObject>(locations[0]);handles.Add(asset);var prefab=await asset.Task;
                                if(asset.Status!=AsyncOperationStatus.Succeeded||prefab==null)throw new InvalidOperationException("Built prefab load failed: "+tile.id);
                                if(!prefab.transform.rotation.Equals(Quaternion.identity)||!prefab.transform.lossyScale.Equals(Vector3.one))throw new InvalidOperationException("Legacy ownership requires axis-aligned metre roots: "+tile.id);
                                var instance=UnityEngine.Object.Instantiate(prefab);instance.hideFlags=HideFlags.HideAndDontSave;instances.Add(instance);
                                string signature=SkylineOwnershipBinding.BundleSignature(tile.address,new[]{locator});
                                if(string.IsNullOrEmpty(signature))throw new InvalidOperationException("Built asset content hashes missing: "+tile.id);
                                records[tile.id]=SkylineOwnershipBinding.Describe(tile,instance,signature);compiler.Register(tile,instance);
                            }
                            compiler.Tick(1000000);if(compiler.Busy)throw new InvalidOperationException("Offline ownership compiler unfinished");
                            groups.Add(compiler.ExportGroup(sky.id));
                        }
                    }
                    finally
                    {
                        try {foreach(var instance in instances)if(instance!=null)UnityEngine.Object.DestroyImmediate(instance);}
                        finally {foreach(var handle in handles)if(handle.IsValid())Addressables.Release(handle);}
                    }
                    // Even cached loads need to yield between released groups. No recursive
                    // resource-manager pumping or main-thread blocking wait is permitted.
                    await Task.Yield();
                    if(groups.Count%10==0)Debug.Log("OWNERSHIP_OFFLINE_GROUPS "+groups.Count+"/"+skies.Length);
                }
                var data=new SkylineOwnershipData {sourceVersion=city.sourceVersion,tiles=records.Values.OrderBy(t=>t.id,StringComparer.Ordinal).ToArray(),groups=groups.ToArray()};
                string json=JsonUtility.ToJson(data,false);string digest;
                using(var sha=SHA256.Create())digest=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-","").ToLowerInvariant();
                PublishOutput(outputDirectory,json,digest,import);
                Debug.Log("OWNERSHIP_OFFLINE_READY groups="+groups.Count+" bytes="+Encoding.UTF8.GetByteCount(json)+" sha256="+digest);
            }
            finally
            {
                try
                {
                    // Loading one catalog also registers a DynamicResourceLocator and
                    // its declared providers. Restore all references introduced by this
                    // exclusive offline scope, including registrations on a failed load.
                    // No unrelated Addressables requests may run concurrently with it.
                    try
                    {
                        if(capturedState)foreach(var locator in Addressables.ResourceLocators.ToArray())
                            if(!previousLocators.Any(previous=>ReferenceEquals(previous,locator)))Addressables.RemoveResourceLocator(locator);
                    }
                    finally {if(load.IsValid())Addressables.Release(load);}
                }
                finally
                {
                    try
                    {
                        if(capturedState)Addressables.InternalIdTransformFunc=previousTransform;
                        if(capturedState)foreach(var provider in Addressables.ResourceManager.ResourceProviders.ToArray())
                            if(!previousProviders.Any(previous=>ReferenceEquals(previous,provider)))Addressables.ResourceManager.ResourceProviders.Remove(provider);
                    }
                    finally {generating=false;}
                }
            }
        }
        private static async Task EnsureInitializationAsync()
        {
            if(initializedForOwnership)return;
            var initialization=Addressables.InitializeAsync(false);
            try
            {
                await initialization.Task;
                // An Editor's default catalog may be unavailable. The explicit built
                // catalog still has to succeed and prove every requested asset identity.
                initializedForOwnership=true;
            }
            finally {if(initialization.IsValid())Addressables.Release(initialization);}
        }
        private static void PublishOutput(string outputDirectory,string json,string digest,bool import)
        {
            // No output is touched until every group succeeds. Stage both files before
            // publication and restore their exact previous bytes if publication/import fails.
            Directory.CreateDirectory(outputDirectory);
            string dataPath=Path.Combine(outputDirectory,"CityOwnership.json"),hashPath=Path.Combine(outputDirectory,"CityOwnershipHash.txt");
            byte[] previousData=File.Exists(dataPath)?File.ReadAllBytes(dataPath):null;
            byte[] previousHash=File.Exists(hashPath)?File.ReadAllBytes(hashPath):null;
            string suffix="."+Guid.NewGuid().ToString("N")+".tmp",dataTemp=dataPath+suffix,hashTemp=hashPath+suffix;
            bool dataPublished=false,hashPublished=false;
            try
            {
                File.WriteAllText(dataTemp,json,new UTF8Encoding(false));File.WriteAllText(hashTemp,digest,new UTF8Encoding(false));
                ReplaceStaged(dataTemp,dataPath);dataPublished=true;
                ReplaceStaged(hashTemp,hashPath);hashPublished=true;
                if(import)
                {
                    AssetDatabase.ImportAsset(dataPath.Replace('\\','/'),ImportAssetOptions.ForceSynchronousImport);
                    AssetDatabase.ImportAsset(hashPath.Replace('\\','/'),ImportAssetOptions.ForceSynchronousImport);
                }
            }
            catch(Exception publicationError)
            {
                try
                {
                    if(dataPublished)RestoreFile(dataPath,previousData);
                    if(hashPublished)RestoreFile(hashPath,previousHash);
                    if(import)
                    {
                        if(dataPublished)AssetDatabase.ImportAsset(dataPath.Replace('\\','/'),ImportAssetOptions.ForceSynchronousImport);
                        if(hashPublished)AssetDatabase.ImportAsset(hashPath.Replace('\\','/'),ImportAssetOptions.ForceSynchronousImport);
                    }
                }
                catch(Exception restoreError){throw new AggregateException("Ownership publication and output restoration failed.",publicationError,restoreError);}
                throw;
            }
            finally
            {
                DeleteTemporary(dataTemp);DeleteTemporary(hashTemp);
            }
        }
        private static void ReplaceStaged(string temporary,string destination)
        {
            if(File.Exists(destination))File.Replace(temporary,destination,null);else File.Move(temporary,destination);
        }
        private static void RestoreFile(string path,byte[] previous)
        {
            if(previous==null)File.Delete(path);else File.WriteAllBytes(path,previous);
        }
        private static void DeleteTemporary(string path)
        {
            try {if(File.Exists(path))File.Delete(path);}
            catch(Exception error){Debug.LogWarning("Could not remove ownership staging file: "+error.Message);}
        }
    }
}
