using System;
using System.IO;
using HeadTracked.Display;
using UnityEngine;

namespace Manhattan.Viewer
{
    [Serializable]
    public sealed class ViewerPreferences
    {
        public bool tracking = true, windowFrame = true, surfaceDetail = true;
        public float movementSpeed = 10f;
        public float mouseSensitivity = .09f;
        public bool temporalAA = true;
        public bool diagnostics;
        public bool fullPanelDimensions;
        public float filterCutoff = 2f;
        public float filterSpeedCoefficient = 15f;
    }
    [Serializable]
    public sealed class ViewerBookmark
    {
        public string name = "Saved position";
        public Vector3 cityPosition;
        public Vector3 eulerAngles;
    }
    public static class ViewerPersistence
    {
        public const string CalibrationFile = "physical-calibration.json";
        public const string PreferencesFile = "viewer-preferences.json";
        public const string BookmarkFile = "position-bookmark.json";
        public static string DirectoryPath
        {
            get
            {
                try
                {
                    string configured = Environment.GetEnvironmentVariable("HEADTRACKED_CALIBRATION_DIRECTORY");
                    if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
                }
                catch (Exception) { /* A malformed optional path falls back to the normal player directory. */ }
                return Application.persistentDataPath;
            }
        }
        public static bool HasCalibration => File.Exists(Path.Combine(DirectoryPath, CalibrationFile));
        public static T Load<T>(string file, T fallback) where T : class
        {
            try
            {
                string path = Path.Combine(DirectoryPath, file);
                if (!File.Exists(path)) return fallback;
                return JsonUtility.FromJson<T>(File.ReadAllText(path)) ?? fallback;
            }
            catch (Exception e) { Debug.LogWarning("Could not load " + file + ": " + e.Message); return fallback; }
        }
        public static bool Save<T>(string file, T value)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                string destination = Path.Combine(DirectoryPath, file);
                string temporary = destination + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(value, true));
                if (File.Exists(destination)) File.Delete(destination);
                File.Move(temporary, destination); return true;
            }
            catch (Exception e) { Debug.LogError("Could not save " + file + ": " + e.Message); return false; }
        }
        public static bool ValidatePhysical(float width, float height, float distance) =>
            float.IsFinite(width) && float.IsFinite(height) && float.IsFinite(distance) &&
            width >= .1f && width <= 3f && height >= .1f && height <= 2f && distance >= .25f && distance <= 1.5f;
        public static void RestoreCalibration(DisplayCalibration target)
        {
            var saved = Load<DisplayCalibration>(CalibrationFile, null);
            if (saved == null || !ValidatePhysical(saved.screenWidth, saved.screenHeight, saved.referenceEyeDistanceFromScreen)) return;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(saved), target);
        }
    }
}
