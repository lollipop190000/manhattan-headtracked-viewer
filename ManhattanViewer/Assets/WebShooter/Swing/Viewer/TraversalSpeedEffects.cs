using UnityEngine;
using UnityEngine.Rendering;
namespace WebShooter.Swing
{
    [DefaultExecutionOrder(1100)]
    public sealed class TraversalSpeedEffects : MonoBehaviour
    {
        [SerializeField] private bool speedFov;
        [SerializeField] private bool cameraShake;
        [SerializeField] private bool windSound;
        [SerializeField] private bool edgeLines;
        [SerializeField,Min(0)] private float effectStartSpeed=20;
        [SerializeField,Min(1)] private float fullEffectSpeed=65;
        [SerializeField,Range(0,.3f)] private float fovExpansion=.15f;
        [SerializeField,Range(0,.01f)] private float shakeMetres=.002f;
        [SerializeField,Range(0,.3f)] private float windVolume=.12f;
        private WebSwingMotionInput motion;
        private Camera view;
        private AudioSource wind;
        private AudioClip clip;
        private float amount;
        private Matrix4x4 savedProjection;
        private Vector3 savedPosition;
        private bool applied;
        public void Configure(WebSwingMotionInput input,Camera camera)
        {
            motion=input;view=camera;
            if(Manhattan.Viewer.ManhattanBootstrap.HasArgument("--speed-fx"))speedFov=cameraShake=windSound=edgeLines=true;
        }
        private void OnEnable(){RenderPipelineManager.beginCameraRendering+=Begin;RenderPipelineManager.endCameraRendering+=End;}
        private void OnDisable(){RenderPipelineManager.beginCameraRendering-=Begin;RenderPipelineManager.endCameraRendering-=End;Restore();if(wind!=null)wind.Stop();}
        private void LateUpdate()
        {
            if(motion==null)return;if(view==null)view=Camera.main;
            float target=motion.Enabled && !motion.Paused?Mathf.InverseLerp(effectStartSpeed,Mathf.Max(effectStartSpeed+1,fullEffectSpeed),motion.Body.Velocity.magnitude):0;
            amount=Mathf.Lerp(amount,target,1f-Mathf.Exp(-5f*Time.unscaledDeltaTime));
            if(windSound && amount>.01f)
            {
                if(wind==null)CreateWind();wind.volume=windVolume*amount;wind.pitch=.7f+amount*.6f;if(!wind.isPlaying)wind.Play();
            }
            else if(wind!=null)wind.Stop();
        }
        private void CreateWind()
        {
            const int rate=22050;var noise=new float[rate*2];var random=new System.Random(7331);float filtered=0;
            for(int i=0;i<noise.Length;i++){filtered=Mathf.Lerp(filtered,(float)random.NextDouble()*2-1,.08f);noise[i]=filtered*.6f;}
            clip=AudioClip.Create("Procedural traversal wind",noise.Length,1,rate,false);clip.SetData(noise,0);
            wind=gameObject.AddComponent<AudioSource>();wind.clip=clip;wind.loop=true;wind.playOnAwake=false;wind.spatialBlend=0;wind.volume=0;
        }
        private void Begin(ScriptableRenderContext context,Camera camera)
        {
            float charge=motion!=null && !motion.Paused?motion.Launch.FeedbackAmplitude:0;
            if(camera!=view || applied || charge<=0 && (amount<.001f || (!speedFov && !cameraShake)))return;
            savedProjection=camera.projectionMatrix;savedPosition=camera.transform.position;applied=true;
            if(speedFov){var p=savedProjection;float scale=1f/(1f+fovExpansion*amount);p[0,0]*=scale;p[1,1]*=scale;camera.projectionMatrix=p;}
            if(cameraShake)camera.transform.position+=camera.transform.right*(Mathf.Sin(Time.time*43f)*shakeMetres*amount)+camera.transform.up*(Mathf.Sin(Time.time*57f)*shakeMetres*amount*.6f);
            // One render-scoped owner restores the pose. A second shake component
            // would restore stale transforms when begin/end callbacks interleave.
            if(charge>0)camera.transform.position+=camera.transform.right*(Mathf.Sin(Time.time*47f)*charge)+camera.transform.up*(Mathf.Sin(Time.time*61f)*charge*.65f);
        }
        private void End(ScriptableRenderContext context,Camera camera){if(camera==view)Restore();}
        private void Restore(){if(!applied || view==null)return;view.projectionMatrix=savedProjection;view.transform.position=savedPosition;applied=false;}
        private void OnGUI()
        {
            if(!edgeLines || amount<.01f || Event.current.type!=EventType.Repaint)return;
            var matrix=GUI.matrix;var color=GUI.color;
            GUI.BeginClip(new Rect(16,16,Mathf.Max(1,Screen.width-32),Mathf.Max(1,Screen.height-32)));
            var center=new Vector2((Screen.width-32)*.5f,(Screen.height-32)*.5f);GUI.color=new Color(.83f,.88f,.91f,amount*.18f);
            for(int i=0;i<16;i++)
            {
                float angle=(i+.23f)*Mathf.PI*2/16;var point=center+new Vector2(Mathf.Cos(angle)*center.x*.85f,Mathf.Sin(angle)*center.y*.85f);
                var direction=(point-center).normalized;GUIUtility.RotateAroundPivot(Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg,point);
                GUI.DrawTexture(new Rect(point.x,point.y,35+amount*100,1.3f),Texture2D.whiteTexture);GUI.matrix=matrix;
            }
            GUI.color=color;GUI.matrix=matrix;GUI.EndClip();
        }
        private void OnDestroy(){Restore();if(clip!=null)Destroy(clip);}
    }
}
