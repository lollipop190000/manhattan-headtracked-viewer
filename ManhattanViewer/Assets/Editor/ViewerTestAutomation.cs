using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Manhattan.Editor
{
    /// <summary>Explicit isolated CLI test setup. Does not run during normal Editor startup.</summary>
    public static class ViewerTestAutomation
    {
        public static void PreparePlayMode()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-runTests") < 0 || Array.IndexOf(args, "--no-tracker") < 0)
                throw new InvalidOperationException("Test preparation requires -runTests and --no-tracker.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!Application.isBatchMode)
            {
                EditorApplication.ExecuteMenuItem("Window/General/Game");
                var gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
                gameView.Show(); gameView.Focus();
                Application.runInBackground = true;
            }
        }
    }
}
