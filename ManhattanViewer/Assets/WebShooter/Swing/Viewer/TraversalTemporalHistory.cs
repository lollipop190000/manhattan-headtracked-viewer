using UnityEngine;
using UnityEngine.Rendering.Universal;
namespace WebShooter.Swing
{
    // URP's baseBlendFactor is HISTORY weight (.95 keeps 95% of the old frame).
    // Reduce it during traversal disocclusion without changing the saved AA switch.
    [DefaultExecutionOrder(1150)]
    public sealed class TraversalTemporalHistory : MonoBehaviour
    {
        [SerializeField] private bool adaptiveHistory=true;
        [SerializeField,Range(0,.95f)] private float movingHistory=.55f;
        private WebSwingMotionInput motion;private UniversalAdditionalCameraData data;
        private float original;private bool configured;
        private int contacts;private float contactUntil;
        private Camera view;private Vector3 previousView,previousBody;private Quaternion previousRotation;private bool sampled;
        public float CurrentWeight=>data!=null?data.taaSettings.baseBlendFactor:0;
        public static float Weight(float original,float moving,float speed,bool contact,bool charging,float headSpeed=0,float angularSpeed=0) =>
            Mathf.Lerp(original,Mathf.Min(original,moving),contact || charging?1:
                Mathf.Max(Mathf.InverseLerp(5,25,speed),Mathf.Max(Mathf.InverseLerp(.02f,.25f,headSpeed),Mathf.InverseLerp(5,60,angularSpeed))));
        public void Configure(WebSwingMotionInput input,Camera camera)
        {
            motion=input;view=camera;if(view==null)return;data=view.GetUniversalAdditionalCameraData();
            adaptiveHistory&=!Manhattan.Viewer.ManhattanBootstrap.HasArgument("--history-off");
            original=data.taaSettings.baseBlendFactor;configured=true;
        }
        private void LateUpdate()
        {
            if(!configured)return;
            if(contacts!=motion.Body.ContactSequence && Mathf.Abs(motion.Body.ContactNormal.y)<.2f)contactUntil=Time.unscaledTime+.2f;
            contacts=motion.Body.ContactSequence;
            float dt=Mathf.Max(.001f,Time.unscaledDeltaTime);
            var p=view.transform.position;var rotation=view.transform.rotation;var body=motion.Body.Position;
            float headSpeed=sampled?((p-previousView)-(body-previousBody)).magnitude/dt:0;
            float angularSpeed=sampled?Quaternion.Angle(rotation,previousRotation)/dt:0;
            float speed=motion.Enabled && !motion.Paused?motion.Body.Velocity.magnitude:0;
            float target=adaptiveHistory?Weight(original,movingHistory,speed,Time.unscaledTime<contactUntil,
                motion.Launch.Charging && !motion.Paused,headSpeed,angularSpeed):original;
            data.taaSettings.baseBlendFactor=target;
            previousView=p;previousBody=body;previousRotation=rotation;sampled=true;
        }
        private void OnDisable(){if(configured && data!=null)data.taaSettings.baseBlendFactor=original;}
    }
}
