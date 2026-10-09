using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Manhattan.Viewer;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Manhattan.Editor
{
    /// <summary>Deterministic editor-only GLB to independent, meter-scale native tile conversion.</summary>
    public static class CityBake
    {
        private const string Output = "Assets/Generated/City";
        private const int MaxVertices = 60000;
        private static readonly List<CityTileRecord> Records = new List<CityTileRecord>();
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static readonly Dictionary<string, Texture> Textures = new Dictionary<string, Texture>();
        // Instance IDs are useful only inside one imported model's lifetime.
        private static readonly Dictionary<int, string> ImportedMaterialKeys = new Dictionary<int, string>();
        private static readonly Dictionary<int, string> ImportedTextureKeys = new Dictionary<int, string>();
        private sealed class Registration
        {
            public string address, groupName;
            public bool separate;
        }
        private static readonly Dictionary<string, Registration> PendingRegistrations = new Dictionary<string, Registration>();
        private static readonly Dictionary<int, string> GeneratedAssetPaths = new Dictionary<int, string>();
        private static readonly Dictionary<string, Object> PendingNativeAssets = new Dictionary<string, Object>();
        private sealed class PrefabPart
        {
            public string meshPath, materialPath;
        }
        private sealed class PendingPrefab
        {
            public string id;
            public CityLayer layer;
            public readonly List<PrefabPart> parts = new List<PrefabPart>();
        }
        private static readonly List<PendingPrefab> PendingPrefabs = new List<PendingPrefab>();
        private static readonly string[] LandmarkNames = { "Empire State", "Chrysler", "One WTC", "One Vanderbilt", "432 Park", "111 West 57th" };
        private static readonly HashSet<string> LandmarkBuildingIds = new HashSet<string>(StringComparer.Ordinal);
        private static AddressableAssetSettings settings;
        private static int meshSerial;
        public static Vector3 SourceToCity(float east, float north, float up) => new Vector3(-east, up, -north);

        [MenuItem("Manhattan/Bake full city")]
        public static void BuildAll()
        {
            string source = Environment.GetEnvironmentVariable("MANHATTAN_SOURCE_ROOT");
            if (string.IsNullOrEmpty(source)) source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../3d_modeling"));
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException("Prepare the city assets listed in assets/source-manifest.json first: " + source);
            Directory.CreateDirectory(Output + "/Meshes"); Directory.CreateDirectory(Output + "/Materials");
            Directory.CreateDirectory(Output + "/Textures"); Directory.CreateDirectory(Output + "/Tiles");
            Directory.CreateDirectory("Assets/Resources"); Directory.CreateDirectory("Assets/CitySource");
            AssetDatabase.Refresh();
            settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null) throw new InvalidOperationException("Addressables settings unavailable");
            settings.BuildRemoteCatalog = false;
            ResetBakeState();
            BuildingRecord[] buildings;
            AssetDatabase.StartAssetEditing();
            try { buildings = BakeBuildings(source); }
            finally { AssetDatabase.StopAssetEditing(); }
            FlushRegistrations();
            string[] files = Directory.GetFiles(source, "*.glb", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            string filter = Environment.GetEnvironmentVariable("MANHATTAN_BAKE_FILTER");
            foreach (string file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrEmpty(filter) && name.StartsWith("Near_") && !name.Contains(filter)) continue;
                CityLayer layer = name.StartsWith("Near_") ? CityLayer.Near : name.Contains("Vegetation") ? CityLayer.Vegetation : name.Contains("Streets") ? CityLayer.Ground : CityLayer.Core;
                string importedPath = "Assets/CitySource/" + Path.GetFileName(file);
                ImportSourceModel(file, importedPath);
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(importedPath);
                if (asset == null) throw new InvalidDataException("glTFast import failed: " + importedPath);
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                ImportedMaterialKeys.Clear(); ImportedTextureKeys.Clear();
                AssetDatabase.StartAssetEditing();
                try { BakeModel(instance, layer, name); }
                finally
                {
                    try { AssetDatabase.StopAssetEditing(); }
                    finally { Object.DestroyImmediate(instance); }
                }
                FlushRegistrations();
                Debug.Log("CITY_BAKE " + name + " catalog=" + Records.Count);
                EditorUtility.UnloadUnusedAssetsImmediate();
            }
            AssignReplacementCoverage(Records);
            var catalog = AssetDatabase.LoadAssetAtPath<CityCatalog>("Assets/Resources/CityCatalog.asset");
            if (catalog == null) { catalog = ScriptableObject.CreateInstance<CityCatalog>(); AssetDatabase.CreateAsset(catalog, "Assets/Resources/CityCatalog.asset"); }
            catalog.tiles = Records.OrderBy(r => r.id, StringComparer.Ordinal).ToArray();
            catalog.buildings = buildings;
            string sourceManifest = Path.GetFullPath(Path.Combine(Application.dataPath, "../../assets/source-manifest.json"));
            catalog.sourceVersion = (string)JObject.Parse(File.ReadAllText(sourceManifest))["sourceVersion"];
            catalog.initialEyePosition = SourceToCity(-1225f, -3585f, 1.85f);
            catalog.initialEulerAngles = Quaternion.LookRotation(SourceToCity(-1180f, -3502f, 20f) - catalog.initialEyePosition).eulerAngles;
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
            PruneAddressables();
            ValidateDependencies();
            WriteBakeReport(catalog);
            Debug.Log("CITY_BAKE_COMPLETE " + catalog.tiles.Length + " tiles; " + buildings.Length + " buildings");
        }

        private static void WriteBakeReport(CityCatalog catalog)
        {
            string report = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Builds/city-bake.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(report));
            File.WriteAllText(report, new JObject { ["sourceVersion"] = catalog.sourceVersion, ["tileCount"] = catalog.tiles.Length,
                ["triangles"] = catalog.tiles.Sum(t => (long)t.triangles), ["buildingCount"] = catalog.buildings.Length,
                ["tiles"] = JArray.FromObject(catalog.tiles.Select(t => new {
                    t.id, t.address, layer = t.layer.ToString(),
                    cityBounds = new { center = Coordinate(t.cityBounds.center), size = Coordinate(t.cityBounds.size) },
                    cityOrigin = Coordinate(t.cityOrigin), t.residentBytes, t.triangles, t.replaces, t.requiredReplacementCount })) }.ToString());
        }

        [MenuItem("Manhattan/Refresh landmarks, Core and Ground")]
        public static void RefreshLandmarkSkyline()
        {
            var catalog=AssetDatabase.LoadAssetAtPath<CityCatalog>("Assets/Resources/CityCatalog.asset");
            if(catalog==null||catalog.tiles==null||catalog.tiles.Length==0)throw new InvalidDataException("Bake a complete city before refreshing its base layers.");
            string source=Environment.GetEnvironmentVariable("MANHATTAN_SOURCE_ROOT");
            if(string.IsNullOrEmpty(source))source=Path.GetFullPath(Path.Combine(Application.dataPath,"../../3d_modeling"));
            string manifest=Path.GetFullPath(Path.Combine(Application.dataPath,"../../assets/source-manifest.json"));
            string sourceVersion=(string)JObject.Parse(File.ReadAllText(manifest))["sourceVersion"];
            if(sourceVersion!=catalog.sourceVersion)throw new InvalidDataException("Partial refresh requires the catalog's source version. Run a full bake after a source version change.");
            var preserved=catalog.tiles.Where(r=>r.layer==CityLayer.Near||r.layer==CityLayer.Vegetation).ToArray();
            settings=AddressableAssetSettingsDefaultObject.GetSettings(true);
            if(settings==null)throw new InvalidOperationException("Addressables settings unavailable.");
            ResetBakeState();Records.AddRange(preserved);
            BuildingRecord[] buildings;
            AssetDatabase.StartAssetEditing();
            try{buildings=BakeBuildings(source);}
            finally{AssetDatabase.StopAssetEditing();}
            FlushRegistrations();
            foreach(string name in new[]{"Manhattan_Core","Manhattan_Streets_Parks"})
            {
                string file=Path.Combine(source,"Manhattan_Unity_Core_and_Ground",name+".glb");
                if(!File.Exists(file))throw new FileNotFoundException("Missing canonical base GLB.",file);
                string path="Assets/CitySource/"+name+".glb";
                ImportSourceModel(file,path);
                var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if(asset==null)throw new InvalidDataException("Base GLB import failed: "+path);
                GameObject instance=(GameObject)PrefabUtility.InstantiatePrefab(asset);
                ImportedMaterialKeys.Clear();ImportedTextureKeys.Clear();
                AssetDatabase.StartAssetEditing();
                try{BakeModel(instance,name=="Manhattan_Core"?CityLayer.Core:CityLayer.Ground,name);}
                finally
                {
                    try{AssetDatabase.StopAssetEditing();}
                    finally{Object.DestroyImmediate(instance);}
                }
                FlushRegistrations();EditorUtility.UnloadUnusedAssetsImmediate();
            }
            if(Records.Select(r=>r.id).Distinct(StringComparer.Ordinal).Count()!=Records.Count)throw new InvalidDataException("Duplicate tile ID after base refresh.");
            if(Records.Count(r=>r.id.StartsWith("Skyline_Landmark_",StringComparison.Ordinal))!=LandmarkNames.Length)
                throw new InvalidDataException("Expected six independent landmark fallback records.");
            AssignReplacementCoverage(Records);
            // Explicit unused-asset reclamation does not keep method-local asset
            // references alive. Reload the persistent catalog after the import loop.
            catalog=AssetDatabase.LoadAssetAtPath<CityCatalog>("Assets/Resources/CityCatalog.asset");
            if(catalog==null)throw new InvalidDataException("City catalog disappeared during base refresh.");
            catalog.tiles=Records.OrderBy(r=>r.id,StringComparer.Ordinal).ToArray();catalog.buildings=buildings;
            EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();PruneAddressables();ValidateDependencies();WriteBakeReport(catalog);
            Debug.Log("CITY_LANDMARK_REFRESH_COMPLETE: six landmark silhouettes; "+preserved.Length+" Near/Vegetation records preserved; "+catalog.tiles.Length+" total tiles.");
        }

        private sealed class Piece
        {
            public readonly List<Vector3> positions = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<Vector4> tangents = new List<Vector4>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> indices = new List<int>();
            public readonly Dictionary<long, int> vertexMap = new Dictionary<long, int>();
            public Material material;
            public int Add(long key, Vector3 position, Vector3 normal, Vector4 tangent, Vector2 tex)
            {
                if (vertexMap.TryGetValue(key, out int index)) return index;
                index = positions.Count; vertexMap.Add(key, index); positions.Add(position); normals.Add(normal); tangents.Add(tangent); uv.Add(tex); return index;
            }
        }

        private sealed class Tile
        {
            public string id; public Vector3 origin; public CityLayer layer;
            public readonly Dictionary<string, List<Piece>> pieces = new Dictionary<string, List<Piece>>();
            public Piece Get(Material material)
            {
                string key = GeneratedPath(material);
                if (!pieces.TryGetValue(key, out var list)) { list = new List<Piece>(); pieces.Add(key, list); }
                if (list.Count == 0 || list[list.Count - 1].positions.Count > MaxVertices - 3) list.Add(new Piece { material = material });
                return list[list.Count - 1];
            }
        }

        private static void BakeModel(GameObject root, CityLayer layer, string sourceName)
        {
            int size = layer == CityLayer.Near || layer == CityLayer.Vegetation ? 125 : 500;
            var tiles = new Dictionary<string, Tile>();
            var landmarks = new Dictionary<string, Tile>();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                // Keep one ocean surface. The source box and resident skyline water
                // otherwise have exactly coplanar top faces at Y=-14.
                if (layer == CityLayer.Core && filter.gameObject.name.StartsWith("Water / Water / Hudson & East River", StringComparison.Ordinal)) continue;
                CityLayer meshLayer = layer == CityLayer.Core &&
                    (filter.gameObject.name.StartsWith("Landscape /", StringComparison.Ordinal) ||
                     filter.gameObject.name.StartsWith("Water /", StringComparison.Ordinal)) ? CityLayer.Ground : layer;
                var mesh = filter.sharedMesh; var renderer = filter.GetComponent<MeshRenderer>();
                if (mesh == null || renderer == null) continue;
                int serial = ++meshSerial;
                Matrix4x4 matrix = filter.transform.localToWorldMatrix;
                Matrix4x4 normalMatrix = matrix.inverse.transpose;
                bool flip = matrix.determinant < 0;
                Vector3[] v = mesh.vertices, n = mesh.normals; Vector4[] t = mesh.tangents; Vector2[] u = mesh.uv;
                Vector3[] world = v.Select(matrix.MultiplyPoint3x4).ToArray();
                string landmarkName=layer==CityLayer.Core?LandmarkNameForSourceObject(filter.gameObject.name):null;
                Tile landmark=null;
                if(landmarkName!=null)
                {
                    string id="Skyline_Landmark_"+Safe(sourceName)+"_"+Safe(landmarkName).Replace(' ','_');
                    if(!landmarks.TryGetValue(id,out landmark))
                    {
                        Vector3 center=renderer.bounds.center;
                        landmark=new Tile{id=id,layer=CityLayer.Skyline,origin=new Vector3(Mathf.FloorToInt(center.x/500)*500,0,Mathf.FloorToInt(center.z/500)*500)};
                        landmarks.Add(id,landmark);
                    }
                }
                Material[] mats = renderer.sharedMaterials;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    Material material = BakeMaterial(sub < mats.Length ? mats[sub] : null);
                    int[] indices = mesh.GetTriangles(sub);
                    for (int k = 0; k < indices.Length; k += 3)
                    {
                        int a = indices[k], b = indices[k + 1], c = indices[k + 2];
                        Vector3 mid = (world[a] + world[b] + world[c]) / 3f;
                        int x = Mathf.FloorToInt(mid.x / size), z = Mathf.FloorToInt(mid.z / size);
                        string id = meshLayer + "_" + sourceName + "_" + x + "_" + z;
                        if (!tiles.TryGetValue(id, out Tile tile)) { tile = new Tile { id = id, layer = meshLayer, origin = new Vector3(x * size, 0, z * size) }; tiles.Add(id, tile); }
                        Piece piece = tile.Get(material);
                        Piece landmarkPiece=landmark?.Get(material);
                        for (int corner = 0; corner < 3; corner++)
                        {
                            int index = corner == 0 ? a : corner == 1 ? (flip ? c : b) : (flip ? b : c);
                            Vector3 normal = n.Length == v.Length ? normalMatrix.MultiplyVector(n[index]).normalized : Vector3.up;
                            Vector4 tangent = t.Length == v.Length ? t[index] : new Vector4(1, 0, 0, 1);
                            Vector3 tangentDirection = matrix.MultiplyVector(new Vector3(tangent.x, tangent.y, tangent.z)).normalized;
                            tangent = new Vector4(tangentDirection.x, tangentDirection.y, tangentDirection.z, tangent.w * (flip ? -1 : 1));
                            piece.indices.Add(piece.Add(((long)serial << 32) | (uint)index, world[index] - tile.origin, normal, tangent, u.Length == v.Length ? u[index] : Vector2.zero));
                            if(landmarkPiece!=null)landmarkPiece.indices.Add(landmarkPiece.Add(((long)serial<<32)|(uint)index,world[index]-landmark.origin,normal,tangent,u.Length==v.Length?u[index]:Vector2.zero));
                        }
                    }
                }
            }
            foreach (Tile tile in tiles.Values.OrderBy(t => t.id, StringComparer.Ordinal)) SaveTile(tile);
            foreach(Tile landmark in landmarks.Values.OrderBy(t=>t.id,StringComparer.Ordinal))SaveTile(landmark);
        }

        public static string LandmarkNameForSourceObject(string objectName)
        {
            if(string.IsNullOrEmpty(objectName))return null;
            foreach(string name in LandmarkNames)
                if(objectName.StartsWith("Landmark "+name+" /",StringComparison.Ordinal))return name;
            return null;
        }

        private static BuildingRecord[] BakeBuildings(string source,bool includeResidentGround=true)
        {
            string file = Path.Combine(source, "Manhattan_Source_and_Materials/data/buildings_metric.json");
            JArray rows = JArray.Parse(File.ReadAllText(file));
            var result = new List<BuildingRecord>(); var tiles = new Dictionary<string, Tile>();
            var landmarkCollisions=new Dictionary<string,Tile>();
            LandmarkBuildingIds.Clear();
            var candidates=JObject.Parse(File.ReadAllText(Path.Combine(source,"Manhattan_Source_and_Materials/data/landmark_candidates.json")));
            foreach(string name in LandmarkNames)
            {
                string id=(string)candidates[name]?[0]?["doitt_id"];
                if(string.IsNullOrEmpty(id))throw new InvalidDataException("Missing source landmark candidate: "+name);
                LandmarkBuildingIds.Add(id);
            }
            Material material = SimpleMaterial("Skyline", new Color(.46f, .43f, .39f));
            int serial = 0;
            foreach (JObject row in rows)
            {
                var ring = (JArray)row["rings"][0];
                Vector2[] polygon = ring.Select(p => new Vector2(-(float)p[0], -(float)p[1])).ToArray();
                if (polygon.Length > 1 && polygon[0] == polygon[polygon.Length - 1]) polygon = polygon.Take(polygon.Length - 1).ToArray();
                if (polygon.Length < 3) continue;
                float signedArea=0;for(int i=0;i<polygon.Length;i++){var a=polygon[i];var b=polygon[(i+1)%polygon.Length];signedArea+=a.x*b.y-b.x*a.y;}if(signedArea<0)Array.Reverse(polygon);
                float height = (float)row["height_m"];
                if (!float.IsFinite(height) || height <= .22f) throw new InvalidDataException("Invalid height for building " + row["id"]);
                Bounds bounds = new Bounds(new Vector3(polygon[0].x, height * .5f, polygon[0].y), Vector3.zero);
                foreach (Vector2 p in polygon) { bounds.Encapsulate(new Vector3(p.x, .22f, p.y)); bounds.Encapsulate(new Vector3(p.x, height, p.y)); }
                result.Add(new BuildingRecord { id = (string)row["id"], footprint = polygon, height = height, groundHeight = .22f, cityBounds = bounds });
                int x = Mathf.FloorToInt(bounds.center.x / 500), z = Mathf.FloorToInt(bounds.center.z / 500);
                string id = "Skyline_" + x + "_" + z;
                string buildingId=(string)row["id"];
                int partSeparator=buildingId.LastIndexOf('_');
                bool landmark=LandmarkBuildingIds.Contains(partSeparator>=0?buildingId.Substring(0,partSeparator):buildingId);
                var target=landmark?landmarkCollisions:tiles;
                if (!target.TryGetValue(id, out Tile tile)) { tile = new Tile { id = id, layer = landmark?CityLayer.Collision:CityLayer.Skyline, origin = new Vector3(x * 500, 0, z * 500) }; target.Add(id, tile); }
                ++serial;
                for (int j = 0; j < polygon.Length; j++)
                {
                    Vector2 a = polygon[j], b = polygon[(j + 1) % polygon.Length];
                    Vector3 p0 = new Vector3(a.x, .22f, a.y), p1 = new Vector3(b.x, .22f, b.y), p2 = new Vector3(b.x, height, b.y), p3 = new Vector3(a.x, height, a.y);
                    AddTriangle(tile, material, p0, p2, p1); AddTriangle(tile, material, p0, p3, p2);
                }
                foreach (int[] tri in Triangulate(polygon)) AddTriangle(tile, material, new Vector3(polygon[tri[0]].x, height, polygon[tri[0]].y), new Vector3(polygon[tri[2]].x, height, polygon[tri[2]].y), new Vector3(polygon[tri[1]].x, height, polygon[tri[1]].y));
            }
            foreach (Tile tile in tiles.Values.OrderBy(t => t.id))
            {
                SaveTile(tile);
                var collision = new Tile { id = tile.id.Replace("Skyline", "Collision"), layer = CityLayer.Collision, origin = tile.origin };
                foreach (var pair in tile.pieces) collision.pieces.Add(pair.Key, new List<Piece>(pair.Value));
                if(landmarkCollisions.TryGetValue(tile.id,out Tile extra))
                    foreach(var pair in extra.pieces)
                    {
                        if(!collision.pieces.TryGetValue(pair.Key,out var pieces)){pieces=new List<Piece>();collision.pieces.Add(pair.Key,pieces);}
                        pieces.AddRange(pair.Value);
                    }
                SaveTile(collision);
            }
            foreach(var pair in landmarkCollisions.Where(p=>!tiles.ContainsKey(p.Key)))
            {
                pair.Value.id=pair.Key.Replace("Skyline","Collision");SaveTile(pair.Value);
            }
            if(!includeResidentGround)return result.ToArray();
            JObject island = JObject.Parse(File.ReadAllText(Path.Combine(source, "Manhattan_Source_and_Materials/data/main_island_metric.json")));
            var points = new List<Vector2>();
            foreach (JArray p in island["coordinates"][0]) { Vector2 v = new Vector2(-(float)p[0], -(float)p[1]); if (points.Count == 0 || Vector2.Distance(points[points.Count - 1], v) > 30f) points.Add(v); }
            if (points.Count > 1 && Vector2.Distance(points[0], points[points.Count - 1]) < 30f) points.RemoveAt(points.Count - 1);
            float islandArea=0;for(int i=0;i<points.Count;i++)islandArea+=Cross(points[i],points[(i+1)%points.Count]);if(islandArea<0)points.Reverse();
            Tile ground = new Tile { id = "Skyline_Ground", layer = CityLayer.Skyline, origin = Vector3.zero };
            var groundMat = SimpleMaterial("Far ground", new Color(.30f, .32f, .28f));
            foreach (int[] tri in Triangulate(points.ToArray())) AddTriangle(ground, groundMat, new Vector3(points[tri[0]].x, -.03f, points[tri[0]].y), new Vector3(points[tri[2]].x, -.03f, points[tri[2]].y), new Vector3(points[tri[1]].x, -.03f, points[tri[1]].y));
            SaveTile(ground);
            Tile water = new Tile { id = "Skyline_Water", layer = CityLayer.Skyline, origin = Vector3.zero };
            var waterMat = SimpleMaterial("Far water", new Color(.055f, .12f, .17f)); waterMat.SetFloat("_Smoothness", .72f);
            AddTriangle(water, waterMat, new Vector3(-30000,-14,-30000), new Vector3(30000,-14,30000), new Vector3(30000,-14,-30000));
            AddTriangle(water, waterMat, new Vector3(-30000,-14,-30000), new Vector3(-30000,-14,30000), new Vector3(30000,-14,30000)); SaveTile(water);
            return result.ToArray();
        }

        private static void AddTriangle(Tile tile, Material material, Vector3 a, Vector3 b, Vector3 c)
        {
            Piece piece = tile.Get(material); Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            AddProxyVertex(piece, tile.origin, normal, a);
            AddProxyVertex(piece, tile.origin, normal, b);
            AddProxyVertex(piece, tile.origin, normal, c);
        }

        private static void AddProxyVertex(Piece piece, Vector3 origin, Vector3 normal, Vector3 position)
        {
            int index = piece.positions.Count;
            piece.positions.Add(position - origin); piece.normals.Add(normal);
            piece.tangents.Add(new Vector4(1, 0, 0, 1));
            piece.uv.Add(new Vector2(position.x * .05f, position.z * .05f)); piece.indices.Add(index);
        }

        private static object Coordinate(Vector3 value) => new { value.x, value.y, value.z };

        /// <summary>
        /// A fallback batch can contain buildings crossing its logical cell. Detailed
        /// triangles are assigned independently by centroid, so all intersecting Core
        /// cells must be visible before that entire fallback can be hidden.
        /// </summary>
        public static void AssignReplacementCoverage(IReadOnlyList<CityTileRecord> records)
        {
            var skyline = records.Where(r => r.layer == CityLayer.Skyline &&
                r.id != "Skyline_Ground" && r.id != "Skyline_Water").ToArray();
            foreach (var record in records)
            {
                record.replaces = Array.Empty<string>();
                if (record.layer == CityLayer.Skyline) record.requiredReplacementCount = 0;
            }
            foreach (var core in records.Where(r => r.layer == CityLayer.Core))
            {
                // Include actual mesh bounds as well as the centroid's logical cell:
                // long roof triangles can extend past either neighboring cell edge.
                float minX = Mathf.Min(core.cityOrigin.x, core.cityBounds.min.x);
                float maxX = Mathf.Max(core.cityOrigin.x + 500f, core.cityBounds.max.x);
                float minZ = Mathf.Min(core.cityOrigin.z, core.cityBounds.min.z);
                float maxZ = Mathf.Max(core.cityOrigin.z + 500f, core.cityBounds.max.z);
                var covered = new List<string>();
                foreach (var fallback in skyline)
                {
                    Bounds bounds = fallback.cityBounds;
                    if (bounds.min.x >= maxX || bounds.max.x <= minX ||
                        bounds.min.z >= maxZ || bounds.max.z <= minZ) continue;
                    covered.Add(fallback.id);
                    fallback.requiredReplacementCount++;
                }
                core.replaces = covered.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            }
        }

        public static IEnumerable<int[]> Triangulate(Vector2[] points)
        {
            var ids = Enumerable.Range(0, points.Length).ToList(); float area = 0;
            for (int i=0;i<points.Length;i++) { Vector2 a=points[i],b=points[(i+1)%points.Length]; area += a.x*b.y-b.x*a.y; }
            if (area < 0) ids.Reverse();
            int guard = points.Length * points.Length;
            while (ids.Count > 2 && guard-- > 0)
            {
                bool found=false;
                for(int i=0;i<ids.Count;i++)
                {
                    int a=ids[(i+ids.Count-1)%ids.Count],b=ids[i],c=ids[(i+1)%ids.Count];
                    if(Cross(points[b]-points[a],points[c]-points[b]) <= .00001f) continue;
                    bool contains=false;
                    foreach(int p in ids) if(p!=a&&p!=b&&p!=c&&Cross(points[b]-points[a],points[p]-points[a])>=0&&Cross(points[c]-points[b],points[p]-points[b])>=0&&Cross(points[a]-points[c],points[p]-points[c])>=0){contains=true;break;}
                    if(contains)continue; yield return new[]{a,b,c}; ids.RemoveAt(i);found=true;break;
                }
                if(!found) break;
            }
        }
        private static float Cross(Vector2 a, Vector2 b) => a.x*b.y-a.y*b.x;

        private static void SaveTile(Tile tile)
        {
            int triangles=0; long bytes=0; Bounds bounds=default; bool hasBounds=false; int part=0;
            var pending = new PendingPrefab { id = tile.id, layer = tile.layer };
            foreach(var piece in tile.pieces.Values.SelectMany(p=>p))
            {
                var mesh=new Mesh { name=tile.id+"_"+part++, indexFormat=IndexFormat.UInt16 };
                mesh.SetVertices(piece.positions);mesh.SetNormals(piece.normals);mesh.SetTangents(piece.tangents);mesh.SetUVs(0,piece.uv);mesh.SetTriangles(piece.indices,0);mesh.RecalculateBounds();
                string path=Output+"/Meshes/"+mesh.name+".asset"; mesh=SaveAsset(mesh,path);
                pending.parts.Add(new PrefabPart { meshPath = path, materialPath = GeneratedPath(piece.material) });
                Bounds local=mesh.bounds;local.center+=tile.origin;if(!hasBounds){bounds=local;hasBounds=true;}else bounds.Encapsulate(local);
                triangles+=piece.indices.Count/3;bytes+=piece.positions.Count*48L+piece.indices.Count*2L;
            }
            PendingPrefabs.Add(pending);
            Records.Add(new CityTileRecord{id=tile.id,address=tile.id,layer=tile.layer,cityOrigin=tile.origin,cityBounds=bounds,triangles=triangles,residentBytes=bytes,
                replaces=Array.Empty<string>()});
        }

        private static Material SimpleMaterial(string name, Color color)
        {
            string key = Digest("simple|" + name + "|" + ColorKey(color));
            if(Materials.TryGetValue(key,out Material m))return m;
            m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",.25f);m.enableInstancing=true;
            string path=Output+"/Materials/material_"+key+".mat";m=SaveAsset(m,path);Materials.Add(key,m);Register(path,"material_"+key,"City shared",false);return m;
        }
        private static Material BakeMaterial(Material source)
        {
            if(source==null)return SimpleMaterial("Fallback",Color.gray);
            string key = MaterialKey(source);
            if(Materials.TryGetValue(key,out Material cached))return cached;
            Material m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=source.name,enableInstancing=true};
            Color color=source.HasProperty("baseColorFactor")?source.GetColor("baseColorFactor"):source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):Color.white;m.SetColor("_BaseColor",color);
            float rough=source.HasProperty("roughnessFactor")?source.GetFloat("roughnessFactor"):.7f;float metallic=source.HasProperty("metallicFactor")?source.GetFloat("metallicFactor"):0;
            m.SetFloat("_Smoothness",1-rough);m.SetFloat("_Metallic",metallic);
            CopyTexture(source,m,"baseColorTexture","_BaseMap");CopyTexture(source,m,"normalTexture","_BumpMap");CopyTexture(source,m,"occlusionTexture","_OcclusionMap");
            if(m.GetTexture("_BumpMap")!=null){m.EnableKeyword("_NORMALMAP");m.SetFloat("_BumpScale",source.HasProperty("normalTexture_scale")?source.GetFloat("normalTexture_scale"):1);}
            if(m.GetTexture("_OcclusionMap")!=null){m.EnableKeyword("_OCCLUSIONMAP");m.SetFloat("_OcclusionStrength",source.HasProperty("occlusionTexture_strength")?source.GetFloat("occlusionTexture_strength"):1);}
            Texture packed=source.HasProperty("metallicRoughnessTexture")?source.GetTexture("metallicRoughnessTexture"):null;
            if(packed!=null)
            {
                string packedKey=Digest("metallic-smoothness|"+TextureKey(packed)+"|"+FloatKey(metallic)+"|"+FloatKey(rough));
                if(!Textures.TryGetValue(packedKey,out Texture texture))
                {
                    Texture2D read=ReadTexture(packed);Color[] pixels=read.GetPixels();for(int i=0;i<pixels.Length;i++)pixels[i]=new Color(pixels[i].b*metallic,0,0,1-pixels[i].g*rough);
                    read.SetPixels(pixels);read.Apply(true,false);read.name="texture_"+packedKey;read.anisoLevel=8;read.wrapModeU=packed.wrapModeU;read.wrapModeV=packed.wrapModeV;read.filterMode=packed.filterMode;
                    string path=Output+"/Textures/"+read.name+".asset";texture=SaveAsset(read,path);Textures.Add(packedKey,texture);Register(path,texture.name,"City shared",false);
                }
                m.SetTexture("_MetallicGlossMap",texture);m.SetFloat("_Metallic",1);m.SetFloat("_Smoothness",1);m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.SetTextureScale("_MetallicGlossMap",source.GetTextureScale("metallicRoughnessTexture"));m.SetTextureOffset("_MetallicGlossMap",source.GetTextureOffset("metallicRoughnessTexture"));
            }
            bool doubleSided=source.doubleSidedGI||(source.HasProperty("_Cull")&&source.GetFloat("_Cull")==0);
            m.SetFloat("_Cull",doubleSided?(float)CullMode.Off:(float)CullMode.Back);m.doubleSidedGI=doubleSided;
            bool alphaClip=(source.HasProperty("_AlphaClip")&&source.GetFloat("_AlphaClip")>.5f)||source.IsKeywordEnabled("_ALPHATEST_ON");
            if(alphaClip){m.SetFloat("_AlphaClip",1);m.SetFloat("_Cutoff",source.HasProperty("alphaCutoff")?source.GetFloat("alphaCutoff"):.5f);m.EnableKeyword("_ALPHATEST_ON");m.SetOverrideTag("RenderType","TransparentCutout");m.renderQueue=(int)RenderQueue.AlphaTest;}
            else if(source.HasProperty("_Surface")&&source.GetFloat("_Surface")>.5f)
            {
                m.SetFloat("_Surface",1);m.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);m.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);m.SetFloat("_ZWrite",0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");m.SetOverrideTag("RenderType","Transparent");m.renderQueue=(int)RenderQueue.Transparent;
            }
            string materialPath=Output+"/Materials/material_"+key+".mat";m=SaveAsset(m,materialPath);Materials.Add(key,m);Register(materialPath,"material_"+key,"City shared",false);return m;
        }
        private static void CopyTexture(Material source, Material target, string from, string to)
        {
            Texture texture=source.HasProperty(from)?source.GetTexture(from):null;if(texture==null)return;
            string key=TextureKey(texture);
            if(!Textures.TryGetValue(key,out Texture independent))
            {
                independent=CloneNativeTexture(texture);independent.name="texture_"+key;
                if(independent is Texture2D t){t.anisoLevel=8;t.wrapModeU=texture.wrapModeU;t.wrapModeV=texture.wrapModeV;}
                string path=Output+"/Textures/"+independent.name+".asset";independent=SaveAsset(independent,path);Textures.Add(key,independent);Register(path,independent.name,"City shared",false);
            }
            target.SetTexture(to,independent);target.SetTextureScale(to,source.GetTextureScale(from));target.SetTextureOffset(to,source.GetTextureOffset(from));
        }

        private static string MaterialKey(Material source)
        {
            if(ImportedMaterialKeys.TryGetValue(source.GetInstanceID(),out string cached))return cached;
            var key=new StringBuilder("gltf-material-v1|").Append(source.shader.name).Append('|').Append(source.doubleSidedGI);
            Color color=source.HasProperty("baseColorFactor")?source.GetColor("baseColorFactor"):source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):Color.white;
            key.Append('|').Append(ColorKey(color));
            foreach(string property in new[]{"roughnessFactor","metallicFactor","normalTexture_scale","occlusionTexture_strength","_Cull","_Surface","_AlphaClip","alphaCutoff"})
                key.Append('|').Append(property).Append('=').Append(source.HasProperty(property)?FloatKey(source.GetFloat(property)):"missing");
            foreach(string property in new[]{"baseColorTexture","normalTexture","occlusionTexture","metallicRoughnessTexture"})
            {
                Texture texture=source.HasProperty(property)?source.GetTexture(property):null;
                key.Append('|').Append(property).Append('=').Append(texture!=null?TextureKey(texture):"none");
                if(texture!=null){Vector2 scale=source.GetTextureScale(property),offset=source.GetTextureOffset(property);key.Append('|').Append(FloatKey(scale.x)).Append(',').Append(FloatKey(scale.y)).Append(',').Append(FloatKey(offset.x)).Append(',').Append(FloatKey(offset.y));}
            }
            foreach(string keyword in source.shaderKeywords.OrderBy(k=>k,StringComparer.Ordinal))key.Append('|').Append(keyword);
            string result=Digest(key.ToString());ImportedMaterialKeys.Add(source.GetInstanceID(),result);return result;
        }

        private static string TextureKey(Texture texture)
        {
            if(ImportedTextureKeys.TryGetValue(texture.GetInstanceID(),out string cached))return cached;
            Hash128 contents=texture.imageContentsHash;
            string identity;
            if(!contents.Equals(default(Hash128)))identity="content:"+contents;
            else if(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture,out string guid,out long localId))identity="asset:"+guid+":"+localId;
            else throw new InvalidDataException("Texture lacks a persistent content/asset identity: "+texture.name);
            int mipCount=texture is Texture2D texture2D?texture2D.mipmapCount:1;
            string key=Digest(identity+"|"+texture.width+"x"+texture.height+"|"+texture.graphicsFormat+"|"+mipCount+"|"+texture.wrapModeU+"|"+texture.wrapModeV+"|"+texture.filterMode);
            ImportedTextureKeys.Add(texture.GetInstanceID(),key);return key;
        }

        private static string FloatKey(float value)=>value.ToString("R",CultureInfo.InvariantCulture);
        private static string ColorKey(Color color)=>FloatKey(color.r)+","+FloatKey(color.g)+","+FloatKey(color.b)+","+FloatKey(color.a);
        private static string Digest(string value)
        {
            using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();
        }
        private static Texture2D ReadTexture(Texture source)
        {
            var rt=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);var previous=RenderTexture.active;
            try{Graphics.Blit(source,rt);RenderTexture.active=rt;var read=new Texture2D(source.width,source.height,TextureFormat.RGBA32,true,true);read.ReadPixels(new Rect(0,0,source.width,source.height),0,0);read.Apply();return read;}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);}
        }

        public static Texture2D CloneNativeTexture(Texture source)
        {
            if(!(source is Texture2D original))throw new InvalidDataException("Expected a 2D glTF texture: "+source.name);
            if(original.isReadable)return Object.Instantiate(original);
            // ImportReadableModel is the normal conversion path. This exact-format
            // readback also handles already imported GPU-only textures and tests.
            var clone=new Texture2D(original.width,original.height,original.graphicsFormat,
                original.mipmapCount>1?TextureCreationFlags.MipChain:TextureCreationFlags.None);
            try
            {
                if(clone.mipmapCount!=original.mipmapCount)throw new InvalidDataException("Unsupported partial texture mip chain: "+original.name);
                for(int mip=0;mip<original.mipmapCount;mip++)
                {
                    var request=AsyncGPUReadback.Request(original,mip,original.graphicsFormat);
                    request.WaitForCompletion();
                    if(request.hasError)throw new InvalidDataException("GPU texture readback failed: "+original.name+" mip "+mip);
                    clone.SetPixelData(request.GetData<byte>(),mip);
                }
                clone.Apply(false,false);clone.wrapModeU=original.wrapModeU;clone.wrapModeV=original.wrapModeV;
                clone.filterMode=original.filterMode;clone.anisoLevel=original.anisoLevel;
                return clone;
            }
            catch{Object.DestroyImmediate(clone);throw;}
        }

        /// <summary>Always refresh the local source copy; equal length and timestamps do not identify equal GLB contents.</summary>
        public static void ImportSourceModel(string sourcePath,string importedPath)
        {
            File.Copy(sourcePath,importedPath,true);
            ImportReadableModel(importedPath);
        }

        private static void ImportReadableModel(string path)
        {
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            AssetImporter importer=AssetImporter.GetAtPath(path);
            if(importer==null)throw new InvalidDataException("No glTF importer for "+path);
            var serialized=new SerializedObject(importer);
            SerializedProperty readable=serialized.FindProperty("importSettings.texturesReadable");
            if(readable==null)throw new InvalidDataException("glTFast importer lacks importSettings.texturesReadable: "+path);
            if(!readable.boolValue)
            {
                readable.boolValue=true;serialized.ApplyModifiedPropertiesWithoutUndo();importer.SaveAndReimport();
            }
        }
        private static T SaveAsset<T>(T value,string path) where T:Object
        {
            var old=AssetDatabase.LoadAssetAtPath<T>(path);
            if(old==null)
            {
                // Textures must become persistent before materials are serialized.
                // Meshes have no asset references and can safely be written in a batch.
                if(value is Material || value is Texture) PendingNativeAssets[path]=value;
                else AssetDatabase.CreateAsset(value,path);
                GeneratedAssetPaths[value.GetInstanceID()]=path;
                return value;
            }
            EditorUtility.CopySerialized(value,old);Object.DestroyImmediate(value);EditorUtility.SetDirty(old);
            GeneratedAssetPaths[old.GetInstanceID()]=path;
            return old;
        }
        private static string GeneratedPath(Object value)
        {
            if(value==null)throw new InvalidDataException("Missing generated native asset.");
            if(GeneratedAssetPaths.TryGetValue(value.GetInstanceID(),out string path))return path;
            path=AssetDatabase.GetAssetPath(value);
            if(string.IsNullOrEmpty(path))throw new InvalidDataException("Generated asset lacks a known persistent path: "+value.name);
            return path;
        }

        private static void ResetBakeState()
        {
            Records.Clear(); Materials.Clear(); Textures.Clear(); PendingRegistrations.Clear();
            GeneratedAssetPaths.Clear(); PendingNativeAssets.Clear(); PendingPrefabs.Clear();
            ImportedMaterialKeys.Clear(); ImportedTextureKeys.Clear(); meshSerial=0;
            LandmarkBuildingIds.Clear();
        }

        private static void FlushNativeAssetsAndPrefabs()
        {
            foreach(var pair in PendingNativeAssets.OrderBy(p=>p.Value is Texture?0:1).ThenBy(p=>p.Key,StringComparer.Ordinal))
                AssetDatabase.CreateAsset(pair.Value,pair.Key);
            PendingNativeAssets.Clear();
            AssetDatabase.SaveAssets();
            foreach(PendingPrefab pending in PendingPrefabs)
            {
                GameObject go=new GameObject(pending.id);
                try
                {
                    int part=0;
                    foreach(PrefabPart paths in pending.parts)
                    {
                        Mesh mesh=AssetDatabase.LoadAssetAtPath<Mesh>(paths.meshPath);
                        if(mesh==null)throw new InvalidDataException("Native mesh not imported before prefab creation: "+paths.meshPath);
                        GameObject child=new GameObject("Part "+part++);child.transform.SetParent(go.transform,false);
                        if(pending.layer==CityLayer.Collision)
                        {
                            var collider=child.AddComponent<MeshCollider>();collider.sharedMesh=mesh;Physics.BakeMesh(mesh.GetInstanceID(),false);
                        }
                        else
                        {
                            Material material=AssetDatabase.LoadAssetAtPath<Material>(paths.materialPath);
                            if(material==null)throw new InvalidDataException("Native material not imported before prefab creation: "+paths.materialPath);
                            child.AddComponent<MeshFilter>().sharedMesh=mesh;
                            var renderer=child.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
                            renderer.shadowCastingMode=pending.layer==CityLayer.Near||pending.layer==CityLayer.Skyline?ShadowCastingMode.Off:ShadowCastingMode.On;
                        }
                    }
                    string path=Output+"/Tiles/"+pending.id+".prefab";
                    if(PrefabUtility.SaveAsPrefabAsset(go,path)==null)throw new InvalidDataException("Prefab save failed: "+path);
                    Register(path,pending.id,"City "+pending.layer,true);
                }
                finally{Object.DestroyImmediate(go);}
            }
            PendingPrefabs.Clear();
        }
        private static string Safe(string s){foreach(char c in Path.GetInvalidFileNameChars())s=s.Replace(c,'_');return s.Replace('/','_').Replace('\\','_');}
        private static void Register(string path,string address,string groupName,bool separate)
        {
            if (string.IsNullOrEmpty(path)) throw new InvalidDataException("Cannot register an asset without a persistent path: " + address);
            // Prefab imports are suspended during StartAssetEditing. Resolve their
            // GUIDs and update Addressables only after the batch has resumed imports.
            PendingRegistrations[path] = new Registration { address = address, groupName = groupName, separate = separate };
        }

        private static void FlushRegistrations()
        {
            FlushNativeAssetsAndPrefabs();
            foreach (var pair in PendingRegistrations.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                string guid = AssetDatabase.AssetPathToGUID(pair.Key);
                if (string.IsNullOrEmpty(guid)) throw new InvalidDataException("Baked asset has no imported GUID: " + pair.Key);
                Registration registration = pair.Value;
                var group = settings.FindGroup(registration.groupName);
                if (group == null) group = settings.CreateGroup(registration.groupName, false, false, true, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
                var schema = group.GetSchema<BundledAssetGroupSchema>();
                schema.BundleMode = registration.separate ? BundledAssetGroupSchema.BundlePackingMode.PackSeparately : BundledAssetGroupSchema.BundlePackingMode.PackTogether;
                schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
                schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
                var entry = settings.CreateOrMoveEntry(guid, group);
                if (entry == null) throw new InvalidDataException("Addressables registration failed: " + pair.Key);
                entry.address = registration.address;
            }
            PendingRegistrations.Clear();
        }
        [MenuItem("Manhattan/Validate independent tile dependencies")]
        public static void ValidateDependencies()
        {
            var catalog=AssetDatabase.LoadAssetAtPath<CityCatalog>("Assets/Resources/CityCatalog.asset");
            IEnumerable<CityTileRecord> records=Records.Count>0?Records:catalog!=null?catalog.tiles:throw new InvalidDataException("Bake a catalog before validating tile dependencies.");
            int validated=0,total=Records.Count>0?Records.Count:catalog.tiles.Length;
            var checkedMaterials=new HashSet<int>();var paths=new List<string>(total);
            foreach(var record in records)
            {
                string path=Output+"/Tiles/"+record.id+".prefab";
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if(prefab==null)throw new InvalidDataException("Missing baked tile: "+record.id);
                foreach(var filter in prefab.GetComponentsInChildren<MeshFilter>(true))ValidateNative(filter.sharedMesh,Output+"/Meshes/",record.id);
                foreach(var collider in prefab.GetComponentsInChildren<MeshCollider>(true))ValidateNative(collider.sharedMesh,Output+"/Meshes/",record.id);
                foreach(var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
                    foreach(var material in renderer.sharedMaterials)
                    {
                        if(material!=null&&!checkedMaterials.Add(material.GetInstanceID()))continue;
                        ValidateNative(material,Output+"/Materials/",record.id);
                        foreach(string property in material.GetTexturePropertyNames())
                        {
                            Texture texture=material.GetTexture(property);
                            if(texture!=null)ValidateNative(texture,Output+"/Textures/",record.id);
                        }
                    }
                paths.Add(path);
                validated++;
                if(validated%500==0||validated==total)Debug.Log("CITY_VALIDATE "+validated+"/"+total+" independent native tiles");
            }
            if(AssetDatabase.GetDependencies(paths.ToArray(),true).Any(IsSourceDependency))
            {
                // The normal path computes the union once. Only failures need the
                // slower per-prefab traversal to identify the offending tile.
                foreach(string path in paths)
                    if(AssetDatabase.GetDependencies(path,true).Any(IsSourceDependency))
                        throw new InvalidDataException("Monolithic source dependency retained by "+Path.GetFileNameWithoutExtension(path));
            }
        }
        private static bool IsSourceDependency(string path)=>path.StartsWith("Assets/CitySource/",StringComparison.Ordinal)||path.EndsWith(".glb",StringComparison.OrdinalIgnoreCase);
        private static void ValidateNative(Object asset,string prefix,string tile)
        {
            if(asset==null||!AssetDatabase.GetAssetPath(asset).StartsWith(prefix,StringComparison.Ordinal))
                throw new InvalidDataException("Missing or non-native prefab dependency in "+tile+": "+(asset==null?"null":asset.name));
        }

        [MenuItem("Manhattan/Prune obsolete city Addressables entries")]
        public static void PruneAddressables()
        {
            var catalog=AssetDatabase.LoadAssetAtPath<CityCatalog>("Assets/Resources/CityCatalog.asset");
            if(catalog==null||catalog.tiles==null||catalog.tiles.Length==0)
                throw new InvalidDataException("Save a nonempty city catalog before pruning Addressables.");
            var addressables=AddressableAssetSettingsDefaultObject.GetSettings(false);
            if(addressables==null)throw new InvalidDataException("Addressables settings unavailable.");
            var retained=new HashSet<string>(StringComparer.Ordinal);
            var prefabPaths=new List<string>(catalog.tiles.Length);
            foreach(var record in catalog.tiles)
            {
                string prefabPath=Output+"/Tiles/"+record.id+".prefab";
                if(!File.Exists(prefabPath)||string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(prefabPath)))
                    throw new InvalidDataException("Cannot prune around a missing catalog prefab: "+record.id);
                prefabPaths.Add(prefabPath);
            }
            foreach(string dependency in AssetDatabase.GetDependencies(prefabPaths.ToArray(),true))
            {
                string guid=AssetDatabase.AssetPathToGUID(dependency);
                if(!string.IsNullOrEmpty(guid))retained.Add(guid);
            }
            int removed=0;
            // The baker only owns its City-prefixed groups. Game assets managed by
            // teammates in other groups must keep their own Addressables entries.
            foreach(var group in addressables.groups.Where(g=>g!=null&&g.Name.StartsWith("City ",StringComparison.Ordinal)))
                foreach(var entry in group.entries.ToArray())
                    if(!retained.Contains(entry.guid)&&addressables.RemoveAssetEntry(entry.guid))removed++;
            AssetDatabase.SaveAssets();
            Debug.Log("CITY_PRUNE_COMPLETE: removed "+removed+" obsolete entries; retained "+retained.Count+" recursive catalog dependency assets.");
        }

        /// <summary>Exercise real GLB conversion and newly created batched assets without baking the whole city.</summary>
        public static void ValidateFixtureBake()
        {
            string fixture=Path.GetFullPath(Path.Combine(Application.dataPath,"../../assets/fixtures/NearestNearDetail.glb"));
            string importedPath="Assets/CitySource/BakeValidation_"+Guid.NewGuid().ToString("N")+".glb";
            var existingPaths=new HashSet<string>(AssetDatabase.GetAllAssetPaths(),StringComparer.Ordinal);
            GameObject instance=null;
            try
            {
                foreach(string folder in new[]{"Meshes","Materials","Textures","Tiles"})Directory.CreateDirectory(Output+"/"+folder);
                Directory.CreateDirectory("Assets/CitySource");AssetDatabase.Refresh();
                settings=AddressableAssetSettingsDefaultObject.GetSettings(true);
                if(settings==null)throw new InvalidOperationException("Addressables settings unavailable.");
                ResetBakeState();
                File.Copy(fixture,importedPath);
                AssetDatabase.ImportAsset(importedPath,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
                GameObject imported=AssetDatabase.LoadAssetAtPath<GameObject>(importedPath);
                if(imported==null)throw new InvalidDataException("Fixture GLB import failed.");
                instance=(GameObject)PrefabUtility.InstantiatePrefab(imported);
                string texturePath=Output+"/Textures/texture_batch_validation.asset";
                string syntheticId="Near_BatchAssetValidation_0_0";
                Material textured;
                AssetDatabase.StartAssetEditing();
                try
                {
                    var texture=new Texture2D(2,2,TextureFormat.RGBA32,true,true){name="Batch validation texture"};
                    texture.SetPixels(new[]{Color.red,Color.blue,Color.green,Color.white});texture.Apply();
                    texture=SaveAsset(texture,texturePath);Register(texturePath,"texture_batch_validation","City shared",false);
                    textured=SimpleMaterial("Batch validation textured",new Color(.173f,.239f,.337f));
                    textured.SetTexture("_BaseMap",texture);EditorUtility.SetDirty(textured);
                    var synthetic=new Tile{id=syntheticId,layer=CityLayer.Near,origin=Vector3.zero};
                    AddTriangle(synthetic,textured,Vector3.zero,Vector3.up,Vector3.right);
                    AddTriangle(synthetic,SimpleMaterial("Batch validation second",new Color(.731f,.449f,.227f)),Vector3.forward,Vector3.up+Vector3.forward,Vector3.right+Vector3.forward);
                    SaveTile(synthetic);
                    var collision=new Tile{id="Collision_BatchAssetValidation_0_0",layer=CityLayer.Collision,origin=Vector3.zero};
                    foreach(var pair in synthetic.pieces)collision.pieces.Add(pair.Key,pair.Value);
                    SaveTile(collision);
                    BakeModel(instance,CityLayer.Near,"BakeValidationFixture");
                }
                finally{AssetDatabase.StopAssetEditing();}
                FlushRegistrations();AssetDatabase.SaveAssets();ValidateDependencies();
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Output+"/Tiles/"+syntheticId+".prefab");
                Material[] materials=prefab.GetComponentsInChildren<MeshRenderer>(true).Select(r=>r.sharedMaterial).ToArray();
                if(materials.Length!=2||materials.Select(AssetDatabase.GetAssetPath).Distinct().Count()!=2)
                    throw new InvalidDataException("Batched materials were merged or lost.");
                var persisted=AssetDatabase.LoadAssetAtPath<Material>(GeneratedPath(textured));
                Texture persistedTexture=persisted.GetTexture("_BaseMap");
                if(persistedTexture==null||AssetDatabase.GetAssetPath(persistedTexture)!=texturePath)
                    throw new InvalidDataException("Native material lost its newly created texture reference.");
                long actualTriangles=Records.Where(r=>r.id.StartsWith("Near_BakeValidationFixture_",StringComparison.Ordinal)).Sum(r=>(long)r.triangles);
                if(actualTriangles!=7086)throw new InvalidDataException("Fixture triangle count changed: "+actualTriangles);
                Debug.Log("CITY_BAKE_FIXTURE_PASSED: native meshes, collider, two materials, texture references, independent GLB tiles; "+Records.Count+" tiles; "+actualTriangles+" source triangles.");
            }
            finally
            {
                if(instance!=null)Object.DestroyImmediate(instance);
                var cleanup=new HashSet<string>(GeneratedAssetPaths.Values.Where(p=>!existingPaths.Contains(p)),StringComparer.Ordinal);
                foreach(var record in Records)cleanup.Add(Output+"/Tiles/"+record.id+".prefab");
                foreach(string path in cleanup)
                {
                    if(settings!=null)settings.RemoveAssetEntry(AssetDatabase.AssetPathToGUID(path));
                    AssetDatabase.DeleteAsset(path);
                }
                AssetDatabase.DeleteAsset(importedPath);
                AssetDatabase.SaveAssets();ResetBakeState();
            }
        }

        public static void ValidateLandmarkFallbacks()
        {
            string relative="Assets/CitySource/LandmarkDataValidation_"+Guid.NewGuid().ToString("N");
            string data=Path.GetFullPath(relative+"/Manhattan_Source_and_Materials/data");
            var existing=new HashSet<string>(AssetDatabase.GetAllAssetPaths(),StringComparer.Ordinal);
            var rows=new JArray();var candidates=new JObject();
            GameObject sourceRoot=null;Material sourceMaterial=null;var sourceMeshes=new List<Mesh>();
            try
            {
                foreach(string folder in new[]{"Meshes","Materials","Textures","Tiles"})Directory.CreateDirectory(Output+"/"+folder);
                Directory.CreateDirectory(data);
                for(int i=0;i<7;i++)
                {
                    string id=i<6?"90000"+i:"ordinary";float x=500020+i*20,z=500020;
                    rows.Add(new JObject{["id"]=id+"_0",["height_m"]=i<6?400:20,["rings"]=new JArray { new JArray(
                        new JArray(-x,-z),new JArray(-x-16,-z),new JArray(-x-16,-z-16),new JArray(-x,-z-16),new JArray(-x,-z)) }});
                    if(i<6)candidates[LandmarkNames[i]]=new JArray(new JObject{["doitt_id"]=id});
                }
                File.WriteAllText(Path.Combine(data,"buildings_metric.json"),rows.ToString());
                File.WriteAllText(Path.Combine(data,"landmark_candidates.json"),candidates.ToString());
                AssetDatabase.Refresh();settings=AddressableAssetSettingsDefaultObject.GetSettings(true);ResetBakeState();
                sourceRoot=new GameObject("Landmark validation source");sourceMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));
                for(int i=0;i<6;i++)
                {
                    var mesh=new Mesh{vertices=new[]{new Vector3(500020+i*20,0,500020),new Vector3(500028+i*20,450,500020),new Vector3(500020+i*20,450,500028)},triangles=new[]{0,1,2}};
                    mesh.RecalculateBounds();sourceMeshes.Add(mesh);
                    var child=new GameObject("Landmark "+LandmarkNames[i]+" / Facade / validation");child.transform.SetParent(sourceRoot.transform,false);
                    child.AddComponent<MeshFilter>().sharedMesh=mesh;child.AddComponent<MeshRenderer>().sharedMaterial=sourceMaterial;
                }
                BuildingRecord[] buildings;
                AssetDatabase.StartAssetEditing();
                try
                {
                    buildings=BakeBuildings(Path.GetFullPath(relative),false);
                    BakeModel(sourceRoot,CityLayer.Core,"LandmarkValidationFixture");
                }
                finally{AssetDatabase.StopAssetEditing();}
                FlushRegistrations();AssignReplacementCoverage(Records);ValidateDependencies();
                if(buildings.Length!=7||buildings.Count(b=>b.id.StartsWith("90000",StringComparison.Ordinal))!=6)
                    throw new InvalidDataException("Landmark building metadata was lost.");
                var regular=Records.Single(r=>r.id=="Skyline_1000_1000");
                var collision=Records.Single(r=>r.id=="Collision_1000_1000");
                var heroes=Records.Where(r=>r.id.StartsWith("Skyline_Landmark_LandmarkValidationFixture_",StringComparison.Ordinal)).ToArray();
                if(regular.triangles!=10||collision.triangles!=70)throw new InvalidDataException("Metric render proxies were not isolated from preserved collision proxies.");
                if(heroes.Length!=6||heroes.Any(r=>r.triangles!=1||r.requiredReplacementCount!=1))
                    throw new InvalidDataException("Six exact fallback geometries or Core coverage counts are missing.");
                if(Records.Where(r=>r.layer==CityLayer.Core).Sum(r=>r.triangles)!=6)
                    throw new InvalidDataException("Detailed Core lost its landmark geometries.");
                Debug.Log("CITY_LANDMARK_FIXTURE_PASSED: six far silhouettes; all seven metadata/collision proxies; ordinary skyline and detailed Core preserved.");
            }
            finally
            {
                if(sourceRoot!=null)Object.DestroyImmediate(sourceRoot);
                if(sourceMaterial!=null)Object.DestroyImmediate(sourceMaterial);
                foreach(var mesh in sourceMeshes)if(mesh!=null)Object.DestroyImmediate(mesh);
                var cleanup=new HashSet<string>(GeneratedAssetPaths.Values.Where(p=>!existing.Contains(p)),StringComparer.Ordinal);
                foreach(var record in Records)cleanup.Add(Output+"/Tiles/"+record.id+".prefab");
                foreach(string path in cleanup)
                {
                    if(settings!=null)settings.RemoveAssetEntry(AssetDatabase.AssetPathToGUID(path));
                    AssetDatabase.DeleteAsset(path);
                }
                AssetDatabase.DeleteAsset(relative);AssetDatabase.SaveAssets();ResetBakeState();
            }
        }
    }
}

