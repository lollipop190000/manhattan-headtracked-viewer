using System;
using Manhattan.Viewer;
using UnityEngine;
namespace WebShooter.Swing
{
    [DefaultExecutionOrder(1001)]
    public sealed class WebAnchorMark : MonoBehaviour
    {
        private Vector3 city,normal;
        private FloatingOrigin origin;
        private Renderer markRenderer;
        private MaterialPropertyBlock properties;
        private Func<bool> paused;
        private float remaining=-1,duration;
        public static Quaternion SurfaceRotation(Vector3 normal)=>Quaternion.LookRotation(normal.sqrMagnitude>.1f?normal:Vector3.up,
            Mathf.Abs(normal.y)>.95f?Vector3.forward:Vector3.up);
        public void Configure(Vector3 position,Vector3 surfaceNormal,FloatingOrigin floatingOrigin,Material material,Func<bool> isPaused,float diameter)
        {
            city=position;normal=surfaceNormal.normalized;origin=floatingOrigin;paused=isPaused;
            properties=new MaterialPropertyBlock();
            markRenderer=GetComponent<Renderer>();markRenderer.sharedMaterial=material;
            markRenderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            transform.rotation=SurfaceRotation(normal);transform.localScale=Vector3.one*diameter;Place();
        }
        public void Release(float seconds){if(remaining>=0)return;duration=Mathf.Max(.01f,seconds);remaining=duration;}
        private void Place()=>transform.position=city-(origin!=null?origin.Offset:Vector3.zero)+normal*.015f;
        private void LateUpdate()
        {
            Place();if(remaining<0)return;
            if(paused==null || !paused())remaining-=Time.deltaTime;
            if(remaining<=0){Destroy(gameObject);return;}
            properties.SetColor("_Color",new Color(.9f,.91f,.9f,remaining/duration));markRenderer.SetPropertyBlock(properties);
        }
    }
}
