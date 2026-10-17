using Manhattan.Viewer;
using UnityEngine;
namespace WebShooter.Swing
{
    [DefaultExecutionOrder(1010)]
    public sealed class DualWebLaunchVisual : MonoBehaviour
    {
        private WebSwingMotionInput motion;
        private FloatingOrigin origin;
        private TraversalHands hands;
        private Camera view;
        private Material material;
        private Texture2D texture;
        private readonly LineRenderer[] lines=new LineRenderer[2];
        private readonly Vector3[] anchors=new Vector3[2];
        private float fade;
        private readonly AnimationCurve[] widths=new AnimationCurve[2];
        private readonly WebAnchorMark[] marks=new WebAnchorMark[2];
        private bool wasCharging;
        public int VisibleLines { get; private set; }
        public void Configure(WebSwingMotionInput input,FloatingOrigin floatingOrigin,TraversalHands gloves,Camera camera)
        {
            motion=input;origin=floatingOrigin;hands=gloves;view=camera;
            material=new Material(Resources.Load<Shader>("TraversalWebStrand"));texture=WebTextureFactory.Strand(5);
            material.SetTexture("_MainTex",texture);material.SetColor("_Color",new Color(.83f,.85f,.84f,.9f));
            markTexture=WebTextureFactory.RadialMark();markMaterial=new Material(material);markMaterial.SetTexture("_MainTex",markTexture);
            for(int i=0;i<2;i++)
            {
                var go=new GameObject(i==0?"Left launch web":"Right launch web");go.transform.SetParent(transform,false);
                var line=go.AddComponent<LineRenderer>();lines[i]=line;line.useWorldSpace=true;line.sharedMaterial=material;
                line.textureMode=LineTextureMode.Tile;line.textureScale=new Vector2(2,1);line.positionCount=25;
                line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;line.enabled=false;
                widths[i]=AnimationCurve.Linear(0,.6f,1,1.1f);line.widthCurve=widths[i];
            }
        }
        private void LateUpdate()
        {
            if(motion==null || view==null)return;
            bool active=motion.Enabled && !motion.Paused && motion.Launch.Charging;
            if(active && !wasCharging)
            {
                for(int i=0;i<2;i++)
                {
                    if(marks[i]!=null)Destroy(marks[i].gameObject);
                    var go=GameObject.CreatePrimitive(PrimitiveType.Quad);go.name="Launch web surface mark";go.layer=2;
                    Destroy(go.GetComponent<Collider>());go.transform.SetParent(transform,false);
                    marks[i]=go.AddComponent<WebAnchorMark>();
                    marks[i].Configure(i==0?motion.Launch.Left:motion.Launch.Right,i==0?motion.Launch.LeftNormal:motion.Launch.RightNormal,
                        origin,markMaterial,()=>motion.Paused,.35f);
                }
            }
            if(!active && wasCharging)foreach(var mark in marks)if(mark!=null)mark.Release(1.2f);
            wasCharging=active;
            if(active){anchors[0]=motion.Launch.Left;anchors[1]=motion.Launch.Right;fade=.18f;}
            else if(!motion.Paused)fade=Mathf.Max(0,fade-Time.deltaTime);
            VisibleLines=0;var offset=origin!=null?origin.Offset:Vector3.zero;
            for(int i=0;i<2;i++)
            {
                var line=lines[i];line.enabled=active || fade>0;if(!line.enabled)continue;VisibleLines++;
                var start=hands!=null?(i==0?hands.LeftWebOrigin:hands.RightWebOrigin):view.transform.position;
                var end=anchors[i]-offset;var d=end-start;
                float progress=active?Mathf.Clamp01(motion.Launch.Age/.09f):1;
                for(int n=0;n<25;n++)
                {
                    float t=n/24f;var p=start+d*(t*progress);
                    p+=view.transform.up*(Mathf.Sin(t*Mathf.PI*6-Time.time*28)*Mathf.Sin(t*Mathf.PI)*.12f*(1-progress));
                    line.SetPosition(n,p);
                }
                float pixel=2*Mathf.Max(1,Vector3.Dot(d,view.transform.forward))/(Mathf.Max(.01f,Mathf.Abs(view.projectionMatrix.m11))*Mathf.Max(1,view.pixelHeight));
                widths[i].MoveKey(1,new Keyframe(1,Mathf.Max(1.1f,pixel/.0012f)));line.widthCurve=widths[i];
                line.widthMultiplier=.0012f*(active?1:fade/.18f);
            }
        }
        private Material markMaterial;private Texture2D markTexture;
        private void OnDestroy(){if(material!=null)Destroy(material);if(texture!=null)Destroy(texture);if(markMaterial!=null)Destroy(markMaterial);if(markTexture!=null)Destroy(markTexture);}
    }
}
