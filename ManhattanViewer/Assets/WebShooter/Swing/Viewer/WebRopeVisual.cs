using System.Collections.Generic;
using Manhattan.Viewer;
using UnityEngine;
namespace WebShooter.Swing
{
    [DefaultExecutionOrder(1000)]
    public sealed class WebRopeVisual : MonoBehaviour
    {
        [SerializeField,Range(20,30)] private int flightSegments=26;
        [SerializeField,Min(0)] private float firingAmplitude=.22f;
        [SerializeField,Range(1,8)] private float waveTurns=3f;
        [SerializeField,Range(.1f,.15f)] private float settleSeconds=.12f;
        [SerializeField,Min(0)] private float tremorStrength=1f;
        [SerializeField,Range(0,.02f)] private float maximumTremor=.004f;
        [SerializeField,Min(.0001f)] private float thickness=.0012f;
        [SerializeField,Range(2,12)] private int strandCount=5;
        [SerializeField,Min(.1f)] private float textureRepeatsPerMetre=2f;
        [SerializeField] private Transform emitter;
        [SerializeField,Min(.05f)] private float attachmentMarkSize=.5f;
        [SerializeField,Min(.1f)] private float attachmentMarkFade=1.5f;
        private WebSwingMotionInput input;
        private FloatingOrigin origin;
        private TraversalHands hands;
        private LineRenderer line;
        private Material material;
        private Texture2D strandTexture;
        private Texture2D markTexture;
        private Material markMaterial;
        private readonly List<WebAnchorMark> marks=new List<WebAnchorMark>(16);
        private Transform blob;
        private readonly Vector3[] points=new Vector3[512],lastCityKnots=new Vector3[16];
        private readonly List<Vector3> knots=new List<Vector3>(16);
        private bool previouslyVisible;
        private int lastCount;
        private float fade;
        private float attachmentAge;
        private bool wasAttached;
        private bool pendingMark;
        private Vector3 pendingMarkCity;
        public int RenderedPoints=>line!=null?line.positionCount:0;
        public IReadOnlyList<Vector3> VisibleKnots=>knots;
        public Transform Emitter { get=>emitter; set=>emitter=value; }
        public void Configure(WebSwingMotionInput motion,FloatingOrigin floatingOrigin,TraversalHands traversalHands=null)
        {
            input=motion;origin=floatingOrigin;hands=traversalHands;
            line=gameObject.AddComponent<LineRenderer>();line.useWorldSpace=true;line.positionCount=2;line.widthMultiplier=thickness;
            line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            material=new Material(Resources.Load<Shader>("TraversalWebStrand"));
            material.SetColor("_Color",new Color(.83f,.85f,.84f,.88f));strandTexture=WebTextureFactory.Strand(strandCount);
            material.SetTexture("_MainTex",strandTexture);line.sharedMaterial=material;line.enabled=false;
            line.textureMode=LineTextureMode.Tile;line.textureScale=new Vector2(textureRepeatsPerMetre,1);
            line.widthCurve=new AnimationCurve(new Keyframe(0,.65f),new Keyframe(1,1.15f));
            markTexture=WebTextureFactory.RadialMark();markMaterial=new Material(material);markMaterial.SetTexture("_MainTex",markTexture);
            input.WebAttached+=AddMark;input.WebReleased+=ReleaseMarks;
            var tip=GameObject.CreatePrimitive(PrimitiveType.Sphere);tip.name="Travelling web knot";tip.layer=2;
            Destroy(tip.GetComponent<Collider>());tip.transform.SetParent(transform,false);tip.transform.localScale=Vector3.one*.025f;
            var renderer=tip.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            blob=tip.transform;tip.SetActive(false);
        }
        private void AddMark(Vector3 city)
        {
            pendingMark=true;pendingMarkCity=city;
        }
        private void CreatePendingMark()
        {
            pendingMark=false;Vector3 city=pendingMarkCity;
            marks.RemoveAll(m=>m==null);
            if(marks.Count>=16){Destroy(marks[0].gameObject);marks.RemoveAt(0);}
            var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);quad.name="Web attachment mark";quad.layer=2;Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(transform,true);var mark=quad.AddComponent<WebAnchorMark>();
            mark.Configure(city,input.ShotSurfaceNormal,origin,markMaterial,()=>input.Paused,attachmentMarkSize);marks.Add(mark);
        }
        private void ReleaseMarks(){pendingMark=false;foreach(var mark in marks)if(mark!=null)mark.Release(attachmentMarkFade);}
        private void LateUpdate()
        {
            if(input==null || Camera.main==null)return;
            bool visualAttached=input.Body.Attached && !input.Flying;
            if(visualAttached && !wasAttached)attachmentAge=0;
            else if(!input.Paused)attachmentAge+=Time.deltaTime;
            wasAttached=visualAttached;
            if(pendingMark && visualAttached)CreatePendingMark();
            Vector3 offset=origin!=null?origin.Offset:Vector3.zero;
            bool visible=input.Enabled && (input.Flying?input.ShotAge>=WebSwingMotionInput.GestureSeconds:input.Body.Attached);
            Vector3 start=emitter!=null?emitter.position:hands!=null?hands.WebOrigin:
                Camera.main.transform.position+Camera.main.transform.right*.25f-Camera.main.transform.up*.2f+Camera.main.transform.forward*.6f;
            Vector3 end=input.Flying?Vector3.Lerp(start,input.ShotTarget-offset,input.ShotProgress):input.ShotTarget-offset;
            knots.Clear();
            if(visible)
            {
                knots.Add(start);
                if(!input.Flying && input.Body.Attached && input.Route.Points.Count>0)
                    foreach(var p in input.Route.Points)knots.Add(p-offset);
                else knots.Add(end);
                lastCount=knots.Count;
                for(int i=0;i<lastCount;i++)lastCityKnots[i]=knots[i]+offset;
                fade=0;
            }
            else
            {
                if(previouslyVisible)fade=.3f;
                if(!input.Paused)fade=Mathf.Max(0,fade-Time.deltaTime);
                for(int i=0;i<lastCount;i++)knots.Add(lastCityKnots[i]-offset-Vector3.up*(1f-fade/.3f)*(1f-i/(float)Mathf.Max(1,lastCount-1)));
            }
            previouslyVisible=visible;line.enabled=visible||fade>0;
            blob.gameObject.SetActive(visible && input.Flying);
            if(blob.gameObject.activeSelf){blob.position=end;blob.localScale=Vector3.one*Mathf.Lerp(.032f,.018f,input.ShotProgress);}
            if(!line.enabled)return;
            if(WebVisualMath.RequiredPoints(knots.Count,flightSegments)>points.Length)
            {line.enabled=false;return;}
            float amplitude=input.Flying?WebVisualMath.FiringAmplitude(firingAmplitude,input.ShotProgress):
                input.Body.Attached?WebVisualMath.AttachmentAmplitude(firingAmplitude,attachmentAge,settleSeconds):0;
            if(input.Body.Attached && !input.Flying && attachmentAge>=settleSeconds)
                amplitude=WebVisualMath.TremorAmplitude(input.Body.Velocity.magnitude,input.Body.TensionAcceleration,tremorStrength,maximumTremor);
            int count=WebVisualMath.Resample(knots,points,flightSegments,amplitude,Time.time*24f,waveTurns);
            line.positionCount=count;
            for(int i=0;i<count;i++)line.SetPosition(i,points[i]);
            line.widthMultiplier=thickness*(visible?1:fade/.3f);
        }
        private void OnDestroy()
        {
            if(input!=null){input.WebAttached-=AddMark;input.WebReleased-=ReleaseMarks;}
            if(material!=null)Destroy(material);if(strandTexture!=null)Destroy(strandTexture);
            if(markTexture!=null)Destroy(markTexture);if(markMaterial!=null)Destroy(markMaterial);
        }
    }
}
