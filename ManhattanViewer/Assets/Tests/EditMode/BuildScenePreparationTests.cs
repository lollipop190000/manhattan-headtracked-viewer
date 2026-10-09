using System;
using System.Linq;
using Manhattan.Editor;
using Manhattan.Viewer;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class BuildScenePreparationTests
{
    [Test]
    public void PreparingExistingViewerScenePreservesAuthoredObjectsAndUsesRuntimeCatalogLookup()
    {
        string folder = "Assets/CitySource/BuildSceneValidation_" + Guid.NewGuid().ToString("N");
        string scenePath = folder + "/Authored.unity";
        Scene scene = default;
        try
        {
            if (!AssetDatabase.IsValidFolder("Assets/CitySource")) AssetDatabase.CreateFolder("Assets", "CitySource");
            AssetDatabase.CreateFolder("Assets/CitySource", folder.Substring(folder.LastIndexOf('/') + 1));
            var catalog = ScriptableObject.CreateInstance<CityCatalog>(); AssetDatabase.CreateAsset(catalog, folder + "/Catalog.asset");
            var frame = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/WindowFrameMaterial.mat");
            Assert.That(frame, Is.Not.Null);
            // EditMode runs in an isolated batch Editor. Prepare its own scene
            // with the supported Editor API, including an untitled initial state.
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var authored = new GameObject("Team authored content");
            if (authored.scene != scene) SceneManager.MoveGameObjectToScene(authored, scene);
            authored.transform.position = new Vector3(42, 7, -9); authored.SetActive(false);
            var existing = new GameObject("Authored bootstrap");
            if (existing.scene != scene) SceneManager.MoveGameObjectToScene(existing, scene);
            var bootstrap = existing.AddComponent<ManhattanBootstrap>(); bootstrap.catalog = catalog;
            EditorSceneManager.SaveScene(scene, scenePath);

            Scene prepared = BuildAutomation.PrepareViewerScene(scenePath, frame);

            Assert.That(prepared.handle, Is.EqualTo(scene.handle));
            Assert.That(authored, Is.Not.Null); Assert.That(authored.activeSelf, Is.False);
            Assert.That(authored.transform.position, Is.EqualTo(new Vector3(42, 7, -9)));
            Assert.That(existing.GetComponent<ManhattanBootstrap>(), Is.SameAs(bootstrap));
            Assert.That(bootstrap.catalog, Is.Null);
            Assert.That(bootstrap.windowFrameMaterial, Is.SameAs(frame));
            Assert.That(prepared.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ManhattanBootstrap>(true)).Count(), Is.EqualTo(1));
            BuildAutomation.PrepareViewerScene(scenePath, frame);
            Assert.That(prepared.GetRootGameObjects().Length, Is.EqualTo(2), "Repeated builds must not recreate or duplicate authored objects.");
        }
        finally
        {
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(folder);
        }
    }
}
