using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Linq;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using Manhattan.Viewer;

public sealed class TemporaryVisualBindingProbe:MonoBehaviour
{
    private sealed class Pose:IMotionInputSource {public Vector3 position;public Quaternion rotation;public MotionCommand Sample(float dt)=>new MotionCommand {hasAbsolutePose=true,cityPosition=position,cityRotation=rotation};}
    [Serializable]private sealed class Mat {public int slot,id;public string name,shader;public Color color;public float cull,surface;public string baseMap;}
    [Serializable]private sealed class Sub {public int submesh,originalIndices,finalIndices;public string originalHash,finalHash;}
    [Serializable]private sealed class Item {public string tile,layer,path,sourceMesh,finalMesh;public int sourceMeshId,finalMeshId,rendererId;public bool active,enabled,forceOff,visible,wanted;public Mat[] originalMaterials,finalMaterials;public Sub[] subs;}
    [Serializable]private sealed class Hit {public string tile,layer,path,mesh,material;public int submesh,ordinal,a,b,c;public float distance;public Vector3 cityHit;public bool frontFace;}
    [Serializable]private sealed class Pixel {public Vector2 topLeftPixel;public Hit[] hits;}
    [Serializable]private sealed class Report {public Vector3 city,euler;public bool surfaceDetails;public long masked;public Item[] renderers;public Pixel[] pixels;}
    private static object Field(object o,string name)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(o);
    private static string Hash(int[] data){var bytes=new byte[data.Length*4];Buffer.BlockCopy(data,0,bytes,0,bytes.Length);using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    private static Mat[] Mats(Material[] mats)=>mats.Select((m,i)=>m==null?new Mat {slot=i,name="NULL"}:new Mat {slot=i,id=m.GetInstanceID(),name=m.name,shader=m.shader.name,color=m.HasProperty("_BaseColor")?m.GetColor("_BaseColor"):Color.clear,cull=m.HasProperty("_Cull")?m.GetFloat("_Cull"):-1,surface=m.HasProperty("_Surface")?m.GetFloat("_Surface"):-1,baseMap=m.HasProperty("_BaseMap")&&m.GetTexture("_BaseMap")!=null?m.GetTexture("_BaseMap").name:"none"}).ToArray();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]private static void Attach(){if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MANHATTAN_BINDING_PROBE")))return;var a=UnityEngine.Object.FindFirstObjectByType<ManhattanBootstrap>();if(a!=null)a.gameObject.AddComponent<TemporaryVisualBindingProbe>();}
    private IEnumerator Start()
    {
        var app=GetComponent<ManhattanBootstrap>();var output=Environment.GetEnvironmentVariable("MANHATTAN_BINDING_PROBE");Directory.CreateDirectory(output);app.Display.TrackingEnabled=false;app.KeyboardInput.InputEnabled=false;foreach(var c in app.GetComponentsInChildren<Canvas>(true))c.enabled=false;
        var input=new Pose {position=new Vector3(1190.60266f,1.304856f,3515.447998f),rotation=Quaternion.Euler(317.97827f,343.10513f,0)};app.Flight.InputSource=input;
        for(int pose=0;pose<2;pose++)
        {
            if(pose==1){input.position=new Vector3(1225,150,3585);input.rotation=Quaternion.Euler(35,208.5f,0);}
            app.Flight.SetPose(input.position,input.rotation);app.Rendering.ResetHistory();int stable=0,last=-1;double until=Time.realtimeSinceStartupAsDouble+75;
            while(stable<90&&Time.realtimeSinceStartupAsDouble<until){yield return null;if(app.Streamer.PendingLoads==0&&!app.Streamer.OwnershipWorkPending&&last==app.Streamer.LoadedTiles)stable++;else stable=0;last=app.Streamer.LoadedTiles;}
            if(stable<90||app.Streamer.Failures!=0)throw new Exception("Residency not ready");
            string phase=pose==0?"street":"roof";yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,phase+".png"));yield return new WaitForEndOfFrame();yield return new WaitForEndOfFrame();
            Write(app,output,phase);yield return null;
        }
        Application.Quit();
    }
    public static void Write(ManhattanBootstrap app,string output,string phase)
    {
        var items=new List<Item>();var pixels=(phase=="street"?new[]{new Vector2(1670,550),new Vector2(1690,900),new Vector2(1000,750)}:new[]{new Vector2(600,400),new Vector2(1350,750),new Vector2(430,650)}).Select(p=>new Pixel {topLeftPixel=p}).ToArray();
        var rays=pixels.Select(p=>app.Rendering.targetCamera.ScreenPointToRay(new Vector3(p.topLeftPixel.x,Screen.height-p.topLeftPixel.y,0))).ToArray();var hits=pixels.Select(p=>new List<Hit>()).ToArray();
        foreach(var resident in (IEnumerable)Field(app.Streamer,"residents"))
        {
            var root=Field(resident,"instance") as GameObject;if(root==null)continue;var tile=(CityTileRecord)Field(resident,"tile");if(tile.layer!=CityLayer.Core&&tile.layer!=CityLayer.Skyline)continue;
            var prefab=((AsyncOperationHandle<GameObject>)Field(resident,"handle")).Result;
            foreach(var f in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer=f.GetComponent<MeshRenderer>();var mesh=f.sharedMesh;if(mesh==null||renderer==null)continue;string path=SkylineOwnershipBinding.Path(f.transform,root.transform);
                Transform original=prefab.transform;if(path.Length>0)foreach(var part in path.Split('/'))original=original.GetChild(int.Parse(part));var originalMesh=original.GetComponent<MeshFilter>().sharedMesh;var originalRenderer=original.GetComponent<MeshRenderer>();
                bool active=renderer.enabled&&!renderer.forceRenderingOff&&root.activeInHierarchy&&f.gameObject.activeInHierarchy;
                if(!rays.Any(ray=>renderer.bounds.IntersectRay(ray)))continue;
                var subs=new List<Sub>();for(int sub=0;sub<mesh.subMeshCount;sub++){var a=originalMesh.GetTriangles(sub);var b=mesh.GetTriangles(sub);subs.Add(new Sub {submesh=sub,originalIndices=a.Length,finalIndices=b.Length,originalHash=Hash(a),finalHash=Hash(b)});}
                items.Add(new Item {tile=tile.id,layer=tile.layer.ToString(),path=path,sourceMesh=originalMesh.name,finalMesh=mesh.name,sourceMeshId=originalMesh.GetInstanceID(),finalMeshId=mesh.GetInstanceID(),rendererId=renderer.GetInstanceID(),active=active,enabled=renderer.enabled,forceOff=renderer.forceRenderingOff,visible=(bool)Field(resident,"visible"),wanted=(bool)Field(resident,"wanted"),originalMaterials=Mats(originalRenderer.sharedMaterials),finalMaterials=Mats(renderer.sharedMaterials),subs=subs.ToArray()});
                if(!active)continue;var vertices=mesh.vertices;var materials=renderer.sharedMaterials;
                for(int r=0;r<rays.Length;r++)
                {
                    if(!renderer.bounds.IntersectRay(rays[r]))continue;var ray=rays[r];var inv=f.transform.worldToLocalMatrix;var o=inv.MultiplyPoint3x4(ray.origin);var d=inv.MultiplyVector(ray.direction);
                    for(int sub=0;sub<mesh.subMeshCount;sub++)
                    {
                        var indices=mesh.GetTriangles(sub);for(int i=0;i<indices.Length;i+=3)
                        {
                            int a=indices[i],b=indices[i+1],c=indices[i+2];var e1=vertices[b]-vertices[a];var e2=vertices[c]-vertices[a];var p=Vector3.Cross(d,e2);float determinant=Vector3.Dot(e1,p);if(Mathf.Abs(determinant)<1e-7f)continue;float inverse=1/determinant;var s=o-vertices[a];float u=Vector3.Dot(s,p)*inverse;if(u<0||u>1)continue;var q=Vector3.Cross(s,e1);float v=Vector3.Dot(d,q)*inverse;if(v<0||u+v>1)continue;float t=Vector3.Dot(e2,q)*inverse;if(t<0)continue;
                            var material=materials.Length>0?materials[Math.Min(sub,materials.Length-1)]:null;bool front=determinant>0;if(material!=null&&material.HasProperty("_Cull")&&material.GetFloat("_Cull")==2&&!front)continue;
                            hits[r].Add(new Hit {tile=tile.id,layer=tile.layer.ToString(),path=path,mesh=mesh.name,material=material==null?"NULL":material.name,submesh=sub,ordinal=i/3,a=a,b=b,c=c,distance=t,cityHit=ray.origin+ray.direction*t+app.Origin.Offset,frontFace=front});
                        }
                    }
                }
            }
        }
        for(int i=0;i<pixels.Length;i++)pixels[i].hits=hits[i].OrderBy(h=>h.distance).Take(20).ToArray();
        File.WriteAllText(Path.Combine(output,phase+"-binding-probe.json"),JsonUtility.ToJson(new Report {city=app.Flight.CityPosition,euler=app.Flight.transform.eulerAngles,surfaceDetails=app.Streamer.surfaces.EnabledDetails,masked=app.Streamer.MaskedSkylineTriangles,renderers=items.ToArray(),pixels=pixels},true));
    }
}
