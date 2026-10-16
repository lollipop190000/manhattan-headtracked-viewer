using UnityEngine;
namespace WebShooter.Swing
{
    [System.Serializable]
    public sealed class ReconnectAssistSettings
    {
        public bool enabled;
        public bool bufferMisses=true,aimTolerance=true,quickAttachment=true;
        [Range(.1f,2f)] public float releaseWindowSeconds=1.2f;
        [Range(.05f,.6f)] public float bufferSeconds=.35f;
        [Range(0,15)] public float aimConeDegrees=12f;
        [Range(.01f,.05f)] public float attachmentSeconds=.04f;
    }
    public sealed class TraversalTuning : MonoBehaviour
    {
        [SerializeField] private SwingSettings settings = Defaults();
        [SerializeField] private ReconnectAssistSettings reconnect = new ReconnectAssistSettings
            {enabled=!Manhattan.Viewer.ManhattanBootstrap.HasArgument("--reconnect-off")};
        public SwingSettings Settings => settings;
        public ReconnectAssistSettings Reconnect => reconnect;
        public static SwingSettings Defaults()
        {
            var s=WebSwingMotionInput.TraversalSettings();s.features.SetFlow(!Manhattan.Viewer.ManhattanBootstrap.HasArgument("--flow-off"));
            if(Manhattan.Viewer.ManhattanBootstrap.HasArgument("--pacing-on"))s.features.SetPacing(true);
            return s;
        }
    }
}
