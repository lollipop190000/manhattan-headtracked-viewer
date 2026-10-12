using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace Manhattan.Editor
{
    /// <summary>Invoke on the owning Editor thread. Async import/ownership
    /// continuations can run inside Unity's player loop, where BuildPlayer is
    /// forbidden. Dispatch the synchronous build at a one-shot Editor boundary
    /// and await its result without blocking that loop.</summary>
    public static class PlayerBuildDispatch
    {
        public static Task<BuildReport> BuildAsync(BuildPlayerOptions options)
        {
            var completion = new TaskCompletionSource<BuildReport>(TaskCreationOptions.RunContinuationsAsynchronously);
            EditorApplication.delayCall += () =>
            {
                try { completion.SetResult(BuildPipeline.BuildPlayer(options)); }
                catch (Exception error) { completion.SetException(error); }
            };
            return completion.Task;
        }
    }
}
