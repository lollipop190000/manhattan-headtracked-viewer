using UnityEngine;
namespace WebShooter.Swing
{
    public sealed class TraversalTuning : MonoBehaviour
    {
        [SerializeField] private SwingSettings settings = Defaults();
        public SwingSettings Settings => settings;
        public static SwingSettings Defaults()
        {
            var s=WebSwingMotionInput.TraversalSettings();s.features.SetFlow(!Manhattan.Viewer.ManhattanBootstrap.HasArgument("--flow-off"));
            if(Manhattan.Viewer.ManhattanBootstrap.HasArgument("--pacing-on"))s.features.SetPacing(true);
            return s;
        }
    }
}
