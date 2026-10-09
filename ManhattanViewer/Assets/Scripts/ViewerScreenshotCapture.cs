using System.Collections;
using System;
using System.IO;
using UnityEngine;

namespace Manhattan.Viewer
{
    /// <summary>Standalone visual QA: wait for streaming, capture the rendered city without UI, then optionally exit.</summary>
    public sealed class ViewerScreenshotCapture : MonoBehaviour
    {
        public void Begin(string path, float delaySeconds, bool quitAfterCapture) =>
            StartCoroutine(Capture(Path.GetFullPath(path), Mathf.Max(0f, delaySeconds), quitAfterCapture));

        private IEnumerator Capture(string path, float delay, bool quit)
        {
            yield return new WaitForSecondsRealtime(delay);
            bool succeeded = false;
            yield return CaptureImage(path, result => succeeded = result);
            Debug.Log(succeeded ? "Visual QA screenshot saved: " + path : "Visual QA screenshot did not finish: " + path);
            if (quit) Application.Quit(succeeded ? 0 : 1);
        }

        public static IEnumerator CaptureImage(string path, Action<bool> completed)
        {
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var enabled = new bool[canvases.Length];
            for (int i = 0; i < canvases.Length; i++) { enabled[i] = canvases[i].enabled; canvases[i].enabled = false; }
            long previousWrite;
            try
            {
                yield return new WaitForEndOfFrame();
                previousWrite = File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0;
                ScreenCapture.CaptureScreenshot(path);
                yield return null;
                yield return new WaitForEndOfFrame();
            }
            finally
            {
                for (int i = 0; i < canvases.Length; i++) if (canvases[i] != null) canvases[i].enabled = enabled[i];
            }
            float timeout = Time.realtimeSinceStartup + 10f;
            while ((!File.Exists(path) || new FileInfo(path).Length == 0 || File.GetLastWriteTimeUtc(path).Ticks <= previousWrite) && Time.realtimeSinceStartup < timeout) yield return null;
            bool succeeded = File.Exists(path) && new FileInfo(path).Length > 0 && File.GetLastWriteTimeUtc(path).Ticks > previousWrite;
            completed?.Invoke(succeeded);
        }
    }
}
