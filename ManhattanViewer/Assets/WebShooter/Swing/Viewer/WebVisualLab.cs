using System;
using System.Collections;
using System.IO;
using Manhattan.Viewer;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace WebShooter.Swing
{
    /// <summary>Opt-in isolated visual fixture. Does not change the Manhattan scene asset or saved calibration.</summary>
    public sealed class WebVisualLab : MonoBehaviour
    {
        private RenderTexture captureTarget;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if(ManhattanBootstrap.HasArgument("--web-visual-lab"))new GameObject("Web visual lab").AddComponent<WebVisualLab>();
        }
        private IEnumerator Start()
        {
            foreach(var root in gameObject.scene.GetRootGameObjects())if(root!=gameObject)Destroy(root);
            yield return null;
            var player=new GameObject("Visual fixture player");var flight=player.AddComponent<FreeFlightController>();flight.enabled=false;
            var settings=WebSwingMotionInput.TraversalSettings();settings.gravity=0;settings.airDrag=0;
            var motion=new WebSwingMotionInput(null,flight,null,settings);
            var camera=new GameObject("Visual fixture camera").AddComponent<Camera>();camera.tag="MainCamera";camera.nearClipPlane=.05f;camera.farClipPlane=100;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            captureTarget=new RenderTexture(1280,800,24,RenderTextureFormat.ARGB32){name="Web fixture camera target"};captureTarget.Create();
            camera.targetTexture=captureTarget;camera.aspect=1280f/800;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.1f,.16f,.23f);
            var light=new GameObject("Visual fixture light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(45,-30,0);
            var template=Resources.Load<Material>("WindowFrameMaterial");var grey=new Material(template);grey.SetColor("_BaseColor",new Color(.22f,.28f,.31f));
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Visual wall";wall.layer=30;wall.transform.position=new Vector3(0,103,24);wall.transform.localScale=new Vector3(14,12,1);wall.GetComponent<Renderer>().sharedMaterial=grey;
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(0,98,12);floor.transform.localScale=new Vector3(35,.2f,40);floor.GetComponent<Renderer>().sharedMaterial=grey;
            var actor=GameObject.CreatePrimitive(PrimitiveType.Capsule);actor.name="Neutral third-person proxy";actor.transform.SetParent(player.transform,false);actor.transform.localPosition=Vector3.down*.9f;Destroy(actor.GetComponent<Collider>());
            actor.GetComponent<Renderer>().sharedMaterial=grey;
            var emitter=new GameObject("Wrist emitter").transform;emitter.SetParent(player.transform,false);emitter.localPosition=new Vector3(.25f,-.3f,.5f);
            var visual=player.AddComponent<WebRopeVisual>();visual.Configure(motion,null);visual.Emitter=emitter;
            string folder=Application.persistentDataPath;var args=Environment.GetCommandLineArgs();
            for(int i=0;i+1<args.Length;i++)if(args[i]=="--visual-output")folder=args[i+1];Directory.CreateDirectory(folder);
            for(int view=0;view<2;view++)
            {
                motion.Release();flight.SetPose(Vector3.up*100,Quaternion.identity);motion.Activate();
                actor.GetComponent<Renderer>().enabled=view==1;
                camera.transform.position=flight.CityPosition+(view==0?Vector3.zero:new Vector3(5,3,-8));
                camera.transform.LookAt(new Vector3(0,101,18));Physics.SyncTransforms();
                if(!motion.TryFire(new Ray(flight.CityPosition,wall.transform.position-flight.CityPosition)))
                {Debug.LogError("WEB_VISUAL_LAB ray missed fixture");Application.Quit(2);yield break;}
                for(int frame=0;frame<65;frame++)
                {
                    var c=motion.Sample(1f/120);flight.SetPose(c.cityPosition,c.cityRotation);yield return new WaitForEndOfFrame();
                    if(frame==18 || frame==26 || frame==60)
                    {
                        // A hidden standalone window may not present a backbuffer.
                        // Explicitly submit the same camera to the GPU target.
                        var request=new UniversalRenderPipeline.SingleCameraRequest{destination=captureTarget};
                        RenderPipeline.SubmitRenderRequest(camera,request);
                        var previous=RenderTexture.active;RenderTexture.active=captureTarget;
                        var shot=new Texture2D(captureTarget.width,captureTarget.height,TextureFormat.RGB24,false);
                        try{shot.ReadPixels(new Rect(0,0,captureTarget.width,captureTarget.height),0,0);shot.Apply();}
                        finally{RenderTexture.active=previous;}
                        var pixels=shot.GetPixels32();long brightness=0;for(int i=0;i<pixels.Length;i+=53)brightness+=pixels[i].r+pixels[i].g+pixels[i].b;
                        if(brightness<100){Debug.LogError($"WEB_VISUAL_LAB empty GPU camera capture enabled={camera.enabled} active={camera.gameObject.activeInHierarchy} pipeline={GraphicsSettings.currentRenderPipeline?.name}");Destroy(shot);Application.Quit(5);yield break;}
                        File.WriteAllBytes(Path.Combine(folder,(view==0?"first":"third")+"-"+frame+".png"),shot.EncodeToPNG());Destroy(shot);
                        Debug.Log($"WEB_VISUAL_LAB view={view} frame={frame} points={visual.RenderedPoints} progress={motion.ShotProgress:F3} attached={motion.Body.Attached}");
                    }
                }
                motion.Release();yield return new WaitForSeconds(.5f);
            }
            motion.Collision.Dispose();Destroy(grey);Application.Quit(0);
        }
        private void OnDestroy(){if(captureTarget!=null){captureTarget.Release();Destroy(captureTarget);}}
    }
}
