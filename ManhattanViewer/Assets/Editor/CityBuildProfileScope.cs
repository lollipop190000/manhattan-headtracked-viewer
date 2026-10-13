using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Manhattan.Viewer;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Manhattan.Editor
{
    /// <summary>Temporarily filters generated build inputs; durable byte backups recover interrupted builds.</summary>
    public sealed class CityBuildProfileScope : IDisposable
    {
        private readonly string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private readonly string backup;
        private readonly string journal;
        private bool active;
        public string Profile { get; }
        public CityBuildProfileScope()
        {
            backup = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../work/city-profile-backup"));
            journal = Path.Combine(backup, "manifest.json");
            Recover();
            Profile = Environment.GetEnvironmentVariable("MANHATTAN_CITY_PROFILE") ?? "Full";
            if (Profile != "Full" && Profile != "Development") throw new ArgumentException("Unknown city build profile: " + Profile);
            if (Profile == "Full") return;
            Directory.CreateDirectory(backup);
            var paths = Directory.GetFiles(Path.Combine(Application.dataPath, "AddressableAssetsData"), "*.asset", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(project, p)).ToList();
            paths.Add("Assets/Resources/CityCatalog.asset");
            foreach (string name in new[] { "CityOwnership.json", "CityOwnershipHash.txt", "CityBuildProfile.json" })
            { paths.Add("Assets/Resources/" + name); paths.Add("Assets/Resources/" + name + ".meta"); }
            var records = new JArray();
            foreach (string relative in paths)
            {
                string source = Path.Combine(project, relative), copy = Path.Combine(backup, relative);
                bool exists = File.Exists(source);
                if (exists) { Directory.CreateDirectory(Path.GetDirectoryName(copy)); File.Copy(source, copy, true); }
                records.Add(new JObject { ["path"] = relative, ["existed"] = exists });
            }
            File.WriteAllText(journal, records.ToString());
            active = true;
        }

        public void Apply(CityCatalog catalog, AddressableAssetSettings settings)
        {
            if (Profile == "Full")
            {
                WriteProfile(new JObject { ["profile"] = "Full", ["selectedTileCount"] = catalog.tiles.Length });
                return;
            }
            string regionPath = Environment.GetEnvironmentVariable("MANHATTAN_CITY_REGION");
            if (string.IsNullOrEmpty(regionPath)) regionPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../../tools/development-region.json"));
            var region = JsonUtility.FromJson<CityBuildRegion>(File.ReadAllText(regionPath));
            if (region == null) throw new ArgumentException("Missing development region.");
            region.Validate();
            var original = catalog.tiles;
            var selected = new HashSet<string>(original.Where(t => region.Intersects(t.cityBounds)).Select(t => t.id));
            // A selected Core can reference a neighbouring fallback tile. Retain that dependency only.
            foreach (var tile in original.Where(t => selected.Contains(t.id) && t.layer == CityLayer.Core))
                foreach (string fallback in tile.replaces ?? Array.Empty<string>()) selected.Add(fallback);
            var tiles = original.Where(t => selected.Contains(t.id)).ToArray();
            if (tiles.Length == 0 || !tiles.Any(t => t.layer == CityLayer.Collision)) throw new InvalidOperationException("Region must include city and collision tiles.");
            foreach (var sky in tiles.Where(t => t.layer == CityLayer.Skyline))
                sky.requiredReplacementCount = tiles.Count(t => t.layer == CityLayer.Core && (t.replaces ?? Array.Empty<string>()).Contains(sky.id));
            catalog.tiles = tiles;
            catalog.buildings = catalog.buildings.Where(b => region.Intersects(b.cityBounds)).ToArray();
            catalog.initialEyePosition = new Vector3(region.centerX, catalog.initialEyePosition.y, region.centerZ);
            EditorUtility.SetDirty(catalog);
            string[] prefabPaths = tiles.Select(t => "Assets/Generated/City/Tiles/" + t.id + ".prefab").ToArray();
            if (prefabPaths.Any(p => AssetDatabase.LoadAssetAtPath<GameObject>(p) == null)) throw new InvalidOperationException("A selected tile prefab is missing.");
            var dependencies = new HashSet<string>(AssetDatabase.GetDependencies(prefabPaths, true), StringComparer.Ordinal);
            int removed = 0;
            foreach (var group in settings.groups.Where(g => g != null && g.Name.StartsWith("City ", StringComparison.Ordinal)))
                foreach (var entry in group.entries.ToArray())
                    if (!dependencies.Contains(entry.AssetPath)) { settings.RemoveAssetEntry(entry.guid); removed++; }
            AssetDatabase.SaveAssets();
            var report = new JObject
            {
                ["profile"] = Profile, ["region"] = JObject.Parse(JsonUtility.ToJson(region)),
                ["fullTileCount"] = original.Length, ["selectedTileCount"] = tiles.Length,
                ["selectedTriangles"] = tiles.Sum(t => (long)t.triangles), ["removedAddressableEntries"] = removed,
                ["layers"] = JObject.FromObject(tiles.GroupBy(t => t.layer.ToString()).ToDictionary(g => g.Key, g => g.Count())),
                ["tiles"] = new JArray(tiles.Select(t => new JObject { ["id"] = t.id, ["layer"] = t.layer.ToString(), ["centerX"] = t.cityBounds.center.x, ["centerZ"] = t.cityBounds.center.z, ["sizeX"] = t.cityBounds.size.x, ["sizeZ"] = t.cityBounds.size.z }))
            };
            WriteProfile(report);
            string output = Environment.GetEnvironmentVariable("MANHATTAN_BUILD_PATH");
            if (!string.IsNullOrEmpty(output)) { Directory.CreateDirectory(Path.GetDirectoryName(output)); File.WriteAllText(Path.Combine(Path.GetDirectoryName(output), "city-build-profile.json"), report.ToString()); }
            Debug.Log($"CITY_DEVELOPMENT_REGION tiles={tiles.Length}/{original.Length} removedEntries={removed}");
        }
        private static void WriteProfile(JObject profile)
        {
            File.WriteAllText("Assets/Resources/CityBuildProfile.json", profile.ToString());
            AssetDatabase.ImportAsset("Assets/Resources/CityBuildProfile.json", ImportAssetOptions.ForceSynchronousImport);
        }
        private void Recover()
        {
            if (!File.Exists(journal)) return;
            var records = JArray.Parse(File.ReadAllText(journal));
            foreach (JObject record in records)
            {
                string relative = (string)record["path"], target = Path.GetFullPath(Path.Combine(project, relative));
                if (!target.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid backup path.");
                if ((bool)record["existed"]) File.Copy(Path.Combine(backup, relative), target, true);
                else if (File.Exists(target)) File.Delete(target);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            File.Delete(journal);
            Debug.Log("CITY_BUILD_PROFILE_RESTORED full source catalog and settings");
        }
        public void Dispose()
        {
            if (!active) return;
            AssetDatabase.SaveAssets();
            Recover();
            active = false;
        }
    }
}
