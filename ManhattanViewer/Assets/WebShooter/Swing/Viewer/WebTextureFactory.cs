using UnityEngine;
namespace WebShooter.Swing
{
    public static class WebTextureFactory
    {
        public static Texture2D RadialMark()
        {
            var texture=new Texture2D(128,128,TextureFormat.RGBA32,true){name="Procedural attachment splat",wrapMode=TextureWrapMode.Clamp};
            var pixels=new Color[128*128];
            for(int y=0;y<128;y++)for(int x=0;x<128;x++)
            {
                var p=new Vector2((x-63.5f)/63.5f,(y-63.5f)/63.5f);float r=p.magnitude,a=Mathf.Atan2(p.y,p.x);
                float ray=Mathf.Pow(.5f+.5f*Mathf.Cos(a*13f+Mathf.Sin(r*8f)*.25f),28);
                float rings=Mathf.Pow(.5f+.5f*Mathf.Sin(r*55f+Mathf.Sin(a*6)*.8f),24)*.6f;
                float alpha=(1f-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.65f,1,r)))*Mathf.Max(ray,rings);
                pixels[y*128+x]=new Color(.9f,.91f,.9f,alpha);
            }
            texture.SetPixels(pixels);texture.Apply();return texture;
        }
        public static Texture2D Strand(int strands)
        {
            strands=Mathf.Clamp(strands,2,12);
            var texture=new Texture2D(256,64,TextureFormat.RGBA32,true){name="Procedural braided web",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear};
            var pixels=new Color[256*64];
            for(int y=0;y<64;y++)for(int x=0;x<256;x++)
            {
                float u=x/256f,v=y/63f;
                float filament=Mathf.Pow(.5f+.5f*Mathf.Cos((u*4f+v*strands)*Mathf.PI*2),5f);
                float edge=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.18f,v))*(1f-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.82f,1f,v)));
                float grey=.69f+.28f*filament;
                pixels[y*256+x]=new Color(grey,grey,grey,edge*(.55f+.45f*filament));
            }
            texture.SetPixels(pixels);texture.Apply();return texture;
        }
    }
}
