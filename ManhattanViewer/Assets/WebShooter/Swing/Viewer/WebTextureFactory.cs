using UnityEngine;
namespace WebShooter.Swing
{
    public static class WebTextureFactory
    {
        public static Texture2D Strand(int strands)
        {
            strands=Mathf.Clamp(strands,2,12);
            var texture=new Texture2D(256,64,TextureFormat.RGBA32,true){name="Procedural braided web",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear};
            var pixels=new Color[256*64];
            for(int y=0;y<64;y++)for(int x=0;x<256;x++)
            {
                float u=x/256f,v=y/63f;
                float filament=Mathf.Pow(.5f+.5f*Mathf.Cos((u*4f+v*strands)*Mathf.PI*2),5f);
                float edge=Mathf.SmoothStep(0,.18f,v)*(1f-Mathf.SmoothStep(.82f,1f,v));
                float grey=.69f+.28f*filament;
                pixels[y*256+x]=new Color(grey,grey,grey,edge*(.55f+.45f*filament));
            }
            texture.SetPixels(pixels);texture.Apply();return texture;
        }
    }
}
