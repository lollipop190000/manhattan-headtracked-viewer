using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
        public static void FromEnvironment()
        {
            string catalogPath=Environment.GetEnvironmentVariable("MANHATTAN_OWNERSHIP_CATALOG_PATH");
            if(string.IsNullOrEmpty(catalogPath))throw new ArgumentException("Set MANHATTAN_OWNERSHIP_CATALOG_PATH to the built Addressables catalog.bin/json.");
            string skylineId=Environment.GetEnvironmentVariable("MANHATTAN_OWNERSHIP_VALIDATE_SKYLINE_ID");
            if(!string.IsNullOrEmpty(skylineId))ValidateBuiltGroup(catalogPath,skylineId,Environment.GetEnvironmentVariable("MANHATTAN_OWNERSHIP_VALIDATION_OUTPUT"));
            else Generate(catalogPath);
        }
        public static void FromBuildSettings(string settingsPath)
        {
            string folder=Path.GetDirectoryName(Path.GetFullPath(settingsPath));
            string[] catalogs=Directory.GetFiles(folder,"catalog*",SearchOption.TopDirectoryOnly)
                .Where(p=>p.EndsWith(".bin",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".json",StringComparison.OrdinalIgnoreCase)).ToArray();
            if(catalogs.Length!=1)throw new InvalidOperationException("Expected one exact built Addressables catalog beside "+settingsPath);
            Generate(catalogs[0]);
        }
        public static void ValidateBuiltGroup(string catalogPath,string skylineId,string outputDirectory)
        {
            if(string.IsNullOrEmpty(skylineId)||string.IsNullOrEmpty(outputDirectory))throw new ArgumentException("Validation requires a Skyline id and a separate output directory.");
            string output=Path.GetFullPath(outputDirectory),assets=Path.GetFullPath("Assets").TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(output.StartsWith(assets,StringComparison.OrdinalIgnoreCase)||output.TrimEnd(Path.DirectorySeparatorChar)==assets.TrimEnd(Path.DirectorySeparatorChar))throw new ArgumentException("Validation output must be outside Assets; it must not replace the full city sidecar.");
            Generate(catalogPath,output,skylineId,false);
        }
        private static void Generate(string catalogPath,string outputDirectory="Assets/Resources",string skylineId=null,bool import=true)
        {
            var city=AssetDatabase.LoadAssetAtPath<CityCatalog>("Assets/Resources/CityCatalog.asset");
            if(city==null)throw new InvalidOperationException("Existing city catalog required; ownership generation does not bake or migrate assets.");
            catalogPath=Path.GetFullPath(catalogPath);string root=Path.GetDirectoryName(catalogPath).Replace('\\','/');
            var previousTransform=Addressables.InternalIdTransformFunc;
            var addedProviders=new List<IResourceProvider>();
            var previousLocators=new HashSet<IResourceLocator>(Addressables.ResourceLocators);
            foreach(var provider in new IResourceProvider[]{new AssetBundleProvider(),new BundledAssetProvider()})
                if(!Addressables.ResourceManager.ResourceProviders.Any(p=>p.ProviderId==provider.ProviderId)){Addressables.ResourceManager.ResourceProviders.Add(provider);addedProviders.Add(provider);}
            string runtime=Addressables.RuntimePath.Replace('\\','/').TrimEnd('/');
            Addressables.InternalIdTransformFunc=location=>
            {
                string id=location.InternalId.Replace('\\','/');
                return id.StartsWith(runtime+"/",StringComparison.OrdinalIgnoreCase)?root+id.Substring(runtime.Length):previousTransform!=null?previousTransform(location):location.InternalId;
            };
            var load=default(AsyncOperationHandle<IResourceLocator>);
            var records=new Dictionary<string,SkylineOwnershipTile>(StringComparer.Ordinal);
            var groups=new List<SkylineOwnershipGroup>();
            try
            {
                load=Addressables.LoadContentCatalogAsync(catalogPath,false);
                var locator=load.WaitForCompletion();if(load.Status!=AsyncOperationStatus.Succeeded||locator==null)throw new InvalidOperationException("Built catalog load failed");
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
                                var asset=Addressables.LoadAssetAsync<GameObject>(locations[0]);handles.Add(asset);var prefab=asset.WaitForCompletion();
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
                        foreach(var instance in instances)UnityEngine.Object.DestroyImmediate(instance);
                        foreach(var handle in handles)if(handle.IsValid())Addressables.Release(handle);
                    }
                    if(groups.Count%10==0)Debug.Log("OWNERSHIP_OFFLINE_GROUPS "+groups.Count+"/"+skies.Length);
                }
                var data=new SkylineOwnershipData {sourceVersion=city.sourceVersion,tiles=records.Values.OrderBy(t=>t.id,StringComparer.Ordinal).ToArray(),groups=groups.ToArray()};
                string json=JsonUtility.ToJson(data,false);string digest;
                using(var sha=SHA256.Create())digest=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-","").ToLowerInvariant();
                Directory.CreateDirectory(outputDirectory);
                string dataPath=Path.Combine(outputDirectory,"CityOwnership.json"),hashPath=Path.Combine(outputDirectory,"CityOwnershipHash.txt");
                File.WriteAllText(dataPath,json,new UTF8Encoding(false));
                File.WriteAllText(hashPath,digest,new UTF8Encoding(false));
                if(import)
                {
                    AssetDatabase.ImportAsset(dataPath,ImportAssetOptions.ForceSynchronousImport);
                    AssetDatabase.ImportAsset(hashPath,ImportAssetOptions.ForceSynchronousImport);
                }
                Debug.Log("OWNERSHIP_OFFLINE_READY groups="+groups.Count+" bytes="+Encoding.UTF8.GetByteCount(json)+" sha256="+digest);
            }
            finally
            {
                if(load.IsValid()){if(load.Status==AsyncOperationStatus.Succeeded&&load.Result!=null&&!previousLocators.Contains(load.Result))Addressables.RemoveResourceLocator(load.Result);Addressables.Release(load);}
                Addressables.InternalIdTransformFunc=previousTransform;
                foreach(var provider in addedProviders)Addressables.ResourceManager.ResourceProviders.Remove(provider);
            }
        }
    }
}
