using System.Collections.Generic;
using HeadTracked.Display;
using UnityEngine;
using UnityEngine.Rendering;
namespace WebShooter.Swing
{
    // One skinned draw per hand, with connected wrist and finger surfaces across articulated joints.
    [DefaultExecutionOrder(900)]
    public sealed class TraversalHands : MonoBehaviour
    {
        private sealed class Hand
        {
            public Transform root;
            public readonly Transform[] bones=new Transform[18];
            public SkinnedMeshRenderer renderer;
            public Mesh mesh;
        }
        private readonly Hand[] hands=new Hand[2];
        private WebSwingMotionInput input;
        private HeadTrackedDisplay display;
        private Material glove;
        private Texture2D fabric;
        private float grip;
        private readonly float[] handExtension=new float[2];
        private readonly Quaternion[] smoothedPose = { Quaternion.identity, Quaternion.identity };
        public bool LeftSelected { get; private set; }
        public Vector3 WebOrigin { get; private set; }
        public Vector3 LeftWebOrigin { get; private set; }
        public Vector3 RightWebOrigin { get; private set; }
        public static float WristDepth(float eyeZ, float scale, float reach) =>
            Mathf.Lerp(.08f * scale, -Mathf.Min(.20f * scale, Mathf.Abs(eyeZ) * .45f), reach);
        public static bool ShouldShow(bool enabled, bool paused, bool selected, float extension) =>
            enabled && !paused && selected && extension > .01f;
        public int VisibleHands { get; private set; }
        public float SelectedWristDepth { get; private set; }
        public void Select(float viewportX) { if(viewportX<.47f) LeftSelected=true; else if(viewportX>.53f) LeftSelected=false; }
        public void Configure(WebSwingMotionInput motion,HeadTrackedDisplay target)
        {
            input=motion; display=target;
            var template=Resources.Load<Material>("WindowFrameMaterial");
            glove=new Material(template); glove.name="Crimson articulated glove";
            glove.SetShaderPassEnabled("MotionVectors",true);
            glove.SetColor("_BaseColor",new Color(.62f,.025f,.04f));
            fabric=new Texture2D(256,256,TextureFormat.RGBA32,true); fabric.name="Fine glove web stitching";
            var pixels=new Color[256*256];
            for(int y=0;y<256;y++) for(int x=0;x<256;x++)
            {
                float ring=Mathf.Repeat(y+9f*Mathf.Sin(x*Mathf.PI/32),64);
                bool seam=x%64<2 || ring<2f;
                float weave=.92f+.08f*((x+y)%2);
                pixels[y*256+x]=seam?new Color(.14f,.14f,.14f):new Color(weave,weave,weave);
            }
            fabric.SetPixels(pixels);fabric.Apply();glove.SetTexture("_BaseMap",fabric);
            glove.SetFloat("_Smoothness",.18f); glove.enableInstancing=true;
            for(int i=0;i<2;i++) hands[i]=Build(i==0?"Left glove":"Right glove");
        }
        private Hand Build(string name)
        {
            var h=new Hand(); h.root=new GameObject(name).transform; h.root.SetParent(transform,false);
            var uv=new List<Vector2>();var vertices=new List<Vector3>();var normals=new List<Vector3>();var triangles=new List<int>();var weights=new List<BoneWeight>();
            var bind=new Matrix4x4[h.bones.Length];
            for(int bone=0;bone<h.bones.Length;bone++)
            {
                h.bones[bone]=new GameObject("Joint "+bone).transform;h.bones[bone].SetParent(h.root,false);bind[bone]=Matrix4x4.identity;
                int start=vertices.Count; const int rings=14,sides=20;
                for(int r=0;r<=rings;r++)
                {
                    float t=r/(float)rings;
                    // Tapered anatomy instead of individually capped capsules. Only fingertips have caps.
                    bool fingertip=bone>=3 && (bone-3)%3==2;
                    float radius=bone==0?Mathf.Lerp(1f,.78f,t):bone==1?Mathf.Lerp(1.2f,.69f,t):
                        bone==2?Mathf.Lerp(.64f,1f,Mathf.Sin(t*Mathf.PI*.5f)):
                        Mathf.Lerp(1f,.88f,t)*(1f+.055f*Mathf.Sin(t*Mathf.PI));
                    if(fingertip && t>.7f) radius*=Mathf.Sqrt(Mathf.Max(.0001f,1f-Mathf.Pow((t-.7f)/.3f,2)));
                    float axial=t;
                    for(int s=0;s<=sides;s++)
                    {
                        float a=s*2*Mathf.PI/sides;var n=new Vector3(Mathf.Cos(a),Mathf.Sin(a),0);
                        // Flatten palm/back slightly; keep rounded sides and fuller thumb-side palm.
                        float palmDepth=bone==2 ? 1f+.18f*Mathf.Sin(t*Mathf.PI) : 1f;
                        vertices.Add(new Vector3(n.x*radius,n.y*radius*palmDepth,axial));
                        // Bind-space bones overlap; recalculating normals from the joint bridge triangles
                        // produces inverted dark cuffs. Use the local tube's outward surface normals.
                        float endNormal=fingertip && t>.7f ? (t-.7f)/Mathf.Max(.015f,1f-t) : 0f;
                        normals.Add(new Vector3(n.x,n.y/palmDepth,endNormal).normalized);
                        uv.Add(new Vector2(s/(float)sides,bone>=3?((bone-3)%3+t)/3f:t));
                        weights.Add(new BoneWeight{boneIndex0=bone,weight0=1});
                        if(r<rings && s<sides){int v=start+r*(sides+1)+s;triangles.Add(v);triangles.Add(v+1);triangles.Add(v+sides+1);triangles.Add(v+1);triangles.Add(v+sides+2);triangles.Add(v+sides+1);}
                    }
                }
                if(bone==2)
                {
                    // Close the knuckle end of the palm; proximal fingers emerge from this surface.
                    int center=vertices.Count;vertices.Add(Vector3.forward);normals.Add(Vector3.forward);uv.Add(new Vector2(.5f,1));
                    weights.Add(new BoneWeight{boneIndex0=bone,weight0=1});
                    int rim=start+rings*(sides+1);
                    for(int s=0;s<sides;s++){triangles.Add(center);triangles.Add(rim+s);triangles.Add(rim+s+1);}
                }
                if(bone==1 || bone==2 || (bone>=3 && (bone-3)%3!=0))
                {
                    int previous=start-(sides+1);
                    for(int s=0;s<sides;s++)
                    {
                        triangles.Add(previous+s);triangles.Add(previous+s+1);triangles.Add(start+s);
                        triangles.Add(previous+s+1);triangles.Add(start+s+1);triangles.Add(start+s);
                    }
                }
            }
            h.mesh=new Mesh{name=name+" mesh"};h.mesh.SetVertices(vertices);h.mesh.SetUVs(0,uv);h.mesh.SetNormals(normals);h.mesh.SetTriangles(triangles,0);h.mesh.boneWeights=weights.ToArray();h.mesh.bindposes=bind;
            h.renderer=h.root.gameObject.AddComponent<SkinnedMeshRenderer>();h.renderer.sharedMesh=h.mesh;h.renderer.bones=h.bones;h.renderer.rootBone=h.root;h.renderer.sharedMaterial=glove;
            h.renderer.localBounds=new Bounds(Vector3.zero,Vector3.one*3);h.renderer.shadowCastingMode=ShadowCastingMode.Off;h.renderer.receiveShadows=false;h.renderer.updateWhenOffscreen=false;
            return h;
        }
        private static void Segment(Transform bone,Vector3 from,Vector3 to,float width,float depth)
        {
            bone.localPosition=from;bone.localRotation=Quaternion.LookRotation(to-from,Vector3.up);bone.localScale=new Vector3(width,depth,Vector3.Distance(from,to));
        }
        private void LateUpdate()
        {
            if(display==null || display.ScreenPlane==null || input==null) return;
            bool dual=input.Launch.Charging;
            bool active=input.Enabled && (dual || input.Flying || input.Body.Attached);
            float dt=input.Paused?0:Mathf.Min(Time.deltaTime,.1f);
            grip=Mathf.MoveTowards(grip,dual?Mathf.Lerp(.35f,1,input.Launch.Charge):input.Body.Attached?1:0,dt*7);
            Rect aperture=display.ProjectionScreenRectMeters;
            float scale=Mathf.Clamp(aperture.width/.55f,.55f,1.8f)*.78f;
            VisibleHands=0;
            for(int i=0;i<2;i++)
            {
                var h=hands[i];if(h==null)continue;
                h.root.SetParent(display.ScreenPlane,false);h.root.localPosition=Vector3.zero;h.root.localRotation=Quaternion.identity;h.root.localScale=Vector3.one;
                float side=i==0?-1:1;bool selected=dual || (i==0)==LeftSelected;
                handExtension[i]=Mathf.MoveTowards(handExtension[i],active && selected?1:0,dt*9);
                h.renderer.enabled=ShouldShow(input.Enabled,input.Paused,selected || handExtension[i]>.01f,handExtension[i]);
                if(h.renderer.enabled) VisibleHands++;
                float reach=Mathf.SmoothStep(0,1,handExtension[i]);
                Vector3 wrist=new Vector3(aperture.center.x+side*aperture.width*(.36f-.13f*reach),aperture.yMin+aperture.height*(-.05f+.34f*reach),WristDepth(display.EyePositionMeters.z,scale,reach));
                if(dual){wrist.x+=side*aperture.width*.09f;wrist.y-=input.Launch.Charge*.035f*scale;wrist.z-=input.Launch.Charge*.025f*scale;}
                Vector3 elbow=new Vector3(aperture.center.x+side*aperture.width*.52f,aperture.yMin-.13f*scale,Mathf.Lerp(.16f,-.055f,reach)*scale);
                Vector3 shoulder=new Vector3(aperture.center.x+side*aperture.width*.65f,aperture.yMin-.32f*scale,.28f*scale);
                if(selected) SelectedWristDepth=wrist.z;
                Vector3 target=dual?(i==0?input.Launch.Left:input.Launch.Right):input.Body.Attached?input.Body.Anchor:input.ShotTarget;
                var worldOrigin=display.ScreenPlane.TransformPoint(wrist);
                // Fingers point up/out during the web gesture; the palm turns towards tension when gripping.
                Quaternion pose=Quaternion.Euler(-48f+grip*13f,side*(10f-16f*grip),side*(-18f+grip*30f));
                if(selected && active)
                {
                    var camera=Camera.main;
                    if(camera!=null){var toward=display.ScreenPlane.InverseTransformDirection(target-input.Body.Position);if(toward.sqrMagnitude>.1f) pose=Quaternion.Slerp(pose,Quaternion.LookRotation(toward.normalized,Vector3.up),grip*.35f);}
                }
                if(handExtension[i]<.05f) smoothedPose[i]=pose;
                smoothedPose[i]=Quaternion.Slerp(smoothedPose[i],pose,1f-Mathf.Exp(-16f*dt));pose=smoothedPose[i];
                Segment(h.bones[0],shoulder,elbow,.039f*scale,.033f*scale);
                Segment(h.bones[1],elbow,wrist,.029f*scale,.024f*scale);
                Vector3 palmEnd=wrist+pose*Vector3.forward*.078f*scale;
                Segment(h.bones[2],wrist,palmEnd,.032f*scale,.013f*scale);
                int bone=3;
                for(int finger=0;finger<5;finger++)
                {
                    bool thumb=finger==4;
                    float x=thumb?-.025f:((finger-1.5f)*.017f);
                    Vector3 joint=wrist+pose*new Vector3(x*side,thumb?-.006f:0,thumb?.027f:.075f-(finger==3?.006f:0))*scale;
                    float length=thumb?.025f:(finger==3?.023f: finger==1?.033f:.030f);
                    Quaternion digit=pose*Quaternion.Euler(thumb?-18f:0,thumb?-side*Mathf.Lerp(46f,28f,grip):side*(finger-1.5f)*3f,thumb?side*Mathf.Lerp(28f,65f,grip):0);
                    for(int part=0;part<3;part++)
                    {
                        float folded=part==0?64f:part==1?88f:42f;
                        float gesture=(finger==1 || finger==2)?folded:part==0?8f:4f;
                        float bend=thumb?Mathf.Lerp(part==0?12f:8f,part==0?36f:40f,grip):Mathf.Lerp(gesture,folded+(finger==3?4f:0),grip);
                        digit*=Quaternion.Euler(bend,0,0);
                        Vector3 end=joint+digit*Vector3.forward*length*scale*(part==0?1:part==1?.76f:.62f);
                        float taper=part==0?1f:part==1?.88f:.76f;
                        Segment(h.bones[bone++],joint,end,(thumb?.0105f:finger==3?.0068f:.0078f)*taper*scale,(thumb?.009f:.007f)*taper*scale);joint=end;
                    }
                }
                var webOrigin=display.ScreenPlane.TransformPoint(wrist+pose*new Vector3(0,Mathf.Lerp(-.023f,-.012f,grip),Mathf.Lerp(.025f,.075f,grip))*scale);
                if(i==0)LeftWebOrigin=webOrigin;else RightWebOrigin=webOrigin;
                if(selected)WebOrigin=webOrigin;
            }
        }
        private void OnDestroy(){foreach(var h in hands)if(h!=null){if(h.mesh!=null)Destroy(h.mesh);if(h.root!=null)Destroy(h.root.gameObject);}if(glove!=null)Destroy(glove);if(fabric!=null)Destroy(fabric);}
    }
}
