using System.Collections.Generic;
using HeadTracked.Display;
using UnityEngine;
using UnityEngine.Rendering;
namespace WebShooter.Swing
{
    // One skinned draw per hand. Geometry lives at the physical screen, behind its depth-tested frame.
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
        private float extension, grip;
        public bool LeftSelected { get; private set; }
        public Vector3 WebOrigin { get; private set; }
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
            glove.SetColor("_BaseColor",new Color(.48f,.018f,.03f));
            fabric=new Texture2D(128,128,TextureFormat.RGBA32,false); fabric.name="Glove web stitching";
            var pixels=new Color[128*128];
            for(int y=0;y<128;y++) for(int x=0;x<128;x++)
            {
                float ring=Mathf.Repeat(y+6f*Mathf.Sin(x*Mathf.PI/16),32);
                bool seam=x%32<2 || ring<1.8f;
                pixels[y*128+x]=seam?new Color(.18f,.18f,.18f):Color.white;
            }
            fabric.SetPixels(pixels);fabric.Apply();glove.SetTexture("_BaseMap",fabric);
            glove.SetFloat("_Smoothness",.26f); glove.enableInstancing=true;
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
                    // Extend rounded caps beyond the bone endpoints so joints overlap continuously.
                    float radius=t<.15f?Mathf.Sin(t/.15f*Mathf.PI*.5f):t>.85f?Mathf.Sin((1f-t)/.15f*Mathf.PI*.5f):1f;
                    radius=Mathf.Max(.015f,radius);
                    if(bone==2) radius*=Mathf.Lerp(.68f,.95f,Mathf.Clamp01(t*2));
                    float axial=bone<2?t*1.08f-.04f:t*1.3f-.15f;
                    for(int s=0;s<=sides;s++)
                    {
                        float a=s*2*Mathf.PI/sides;var n=new Vector3(Mathf.Cos(a),Mathf.Sin(a),0);
                        vertices.Add(new Vector3(n.x*radius,n.y*radius,axial));normals.Add(n);uv.Add(new Vector2(s/(float)sides,t));
                        weights.Add(new BoneWeight{boneIndex0=bone,weight0=1});
                        if(r<rings && s<sides){int v=start+r*(sides+1)+s;triangles.Add(v);triangles.Add(v+1);triangles.Add(v+sides+1);triangles.Add(v+1);triangles.Add(v+sides+2);triangles.Add(v+sides+1);}
                    }
                }
            }
            h.mesh=new Mesh{name=name+" mesh"};h.mesh.SetVertices(vertices);h.mesh.SetUVs(0,uv);h.mesh.SetTriangles(triangles,0);h.mesh.boneWeights=weights.ToArray();h.mesh.bindposes=bind;h.mesh.RecalculateNormals();
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
            bool active=input.Enabled && (input.Flying || input.Body.Attached);
            float dt=input.Paused?0:Mathf.Min(Time.deltaTime,.1f);
            extension=Mathf.MoveTowards(extension,active?1:0,dt*9);
            grip=Mathf.MoveTowards(grip,input.Body.Attached?1:0,dt*7);
            Rect aperture=display.ProjectionScreenRectMeters;
            float scale=Mathf.Clamp(aperture.width/.55f,.55f,1.8f)*.78f;
            VisibleHands=0;
            for(int i=0;i<2;i++)
            {
                var h=hands[i];if(h==null)continue;
                h.root.SetParent(display.ScreenPlane,false);h.root.localPosition=Vector3.zero;h.root.localRotation=Quaternion.identity;h.root.localScale=Vector3.one;
                float side=i==0?-1:1;bool selected=(i==0)==LeftSelected;
                h.renderer.enabled=ShouldShow(input.Enabled,input.Paused,selected,extension);
                if(h.renderer.enabled) VisibleHands++;
                float reach=selected?Mathf.SmoothStep(0,1,extension):0;
                Vector3 wrist=new Vector3(aperture.center.x+side*aperture.width*(.36f-.13f*reach),aperture.yMin+aperture.height*(-.05f+.34f*reach),WristDepth(display.EyePositionMeters.z,scale,reach));
                Vector3 elbow=new Vector3(aperture.center.x+side*aperture.width*.52f,aperture.yMin-.13f*scale,Mathf.Lerp(.16f,-.055f,reach)*scale);
                Vector3 shoulder=new Vector3(aperture.center.x+side*aperture.width*.65f,aperture.yMin-.32f*scale,.28f*scale);
                if(selected) SelectedWristDepth=wrist.z;
                Vector3 target=input.Body.Attached?input.Body.Anchor:input.ShotTarget;
                var worldOrigin=display.ScreenPlane.TransformPoint(wrist);
                // Fingers point up/out during the web gesture; the palm turns towards tension when gripping.
                Quaternion pose=Quaternion.Euler(-65f+grip*22f,side*(12f-18f*grip),side*(-12f+grip*18f));
                if(selected && active)
                {
                    var camera=Camera.main;
                    if(camera!=null){var toward=display.ScreenPlane.InverseTransformDirection(target-input.Body.Position);if(toward.sqrMagnitude>.1f) pose=Quaternion.Slerp(pose,Quaternion.LookRotation(toward.normalized,Vector3.up),grip*.35f);}
                }
                Segment(h.bones[0],shoulder,elbow,.047f*scale,.04f*scale);
                Segment(h.bones[1],elbow,wrist,.032f*scale,.027f*scale);
                Vector3 palmEnd=wrist+pose*Vector3.forward*.09f*scale;
                Segment(h.bones[2],wrist,palmEnd,.035f*scale,.017f*scale);
                int bone=3;
                for(int finger=0;finger<5;finger++)
                {
                    bool thumb=finger==4;
                    float x=thumb?-.043f:((finger-1.5f)*.021f);
                    Vector3 joint=wrist+pose*new Vector3(x*side,0,thumb?.032f:.078f)*scale;
                    float curl=selected?Mathf.Lerp((finger==1 || finger==2)?1.5f:.12f,1.35f,grip):.65f;
                    float length=thumb?.028f:(finger==3?.024f: finger==1?.035f:.031f);
                    Quaternion digit=pose*Quaternion.Euler(0,thumb?-side*55f:side*(finger-1.5f)*4f,0);
                    for(int part=0;part<3;part++)
                    {
                        digit*=Quaternion.Euler(curl*(part==0?38:65),0,0);
                        Vector3 end=joint+digit*Vector3.forward*length*scale*(part==0?1:part==1?.76f:.62f);
                        Segment(h.bones[bone++],joint,end,(thumb?.010f:.0087f)*scale,(thumb?.009f:.008f)*scale);joint=end;
                    }
                }
                if(selected) WebOrigin=display.ScreenPlane.TransformPoint(wrist+pose*new Vector3(0,Mathf.Lerp(-.023f,-.012f,grip),Mathf.Lerp(.025f,.075f,grip))*scale);
            }
        }
        private void OnDestroy(){foreach(var h in hands)if(h!=null){if(h.mesh!=null)Destroy(h.mesh);if(h.root!=null)Destroy(h.root.gameObject);}if(glove!=null)Destroy(glove);if(fabric!=null)Destroy(fabric);}
    }
}
