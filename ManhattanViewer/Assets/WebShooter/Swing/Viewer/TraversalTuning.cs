using UnityEngine;
namespace WebShooter.Swing
{
    public sealed class TraversalTuning : MonoBehaviour
    {
        [SerializeField] private SwingSettings settings = Defaults();
        public SwingSettings Settings => settings;
        public static SwingSettings Defaults()
        {
            var s=WebSwingMotionInput.TraversalSettings();s.features.SetFlow(true);return s;
        }
    }
}
