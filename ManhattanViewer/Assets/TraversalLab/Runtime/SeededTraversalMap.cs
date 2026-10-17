using System;
using System.Collections.Generic;
using UnityEngine;
namespace Manhattan.TraversalLab
{
    public sealed class TraversalMapLayout
    {
        public readonly List<Bounds> Buildings=new List<Bounds>();
        public readonly List<Vector3> StreetNodes=new List<Vector3>();
        public readonly int Seed;
        public TraversalMapLayout(int seed)
        {
            Seed=seed;var random=new System.Random(seed);
            for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++)
            {
                float w=7f+(float)random.NextDouble()*4f,d=7f+(float)random.NextDouble()*4f,h=6f+(float)random.NextDouble()*18f;
                Buildings.Add(new Bounds(new Vector3(x*14,h*.5f,z*14),new Vector3(w,h,d)));
                if(random.NextDouble()<.3)
                    Buildings.Add(new Bounds(new Vector3(x*14+w*.3f,h*.3f,z*14+d*.35f),new Vector3(w*.5f,h*.6f,d*.5f)));
            }
            // A one-metre gap exercises narrow-clearance contacts without disconnecting the street grid.
            Buildings.Add(new Bounds(new Vector3(-23,4,0),new Vector3(1.2f,8,12)));
            Buildings.Add(new Bounds(new Vector3(-20.8f,4,0),new Vector3(1.2f,8,12)));
            for(int z=-21;z<=21;z+=14)for(int x=-21;x<=21;x+=14)StreetNodes.Add(new Vector3(x,1.85f,z));
        }
        public void PickEndpoints(int episode,out Vector3 start,out Vector3 goal,float roofFraction=.2f)
        {
            var random=new System.Random(unchecked(Seed*486187739+episode*16777619));
            int first=random.Next(StreetNodes.Count);start=StreetNodes[first];
            int last=(first+1+random.Next(StreetNodes.Count-1))%StreetNodes.Count;goal=StreetNodes[last];
            if(random.NextDouble()<roofFraction)
            {var roof=Buildings[random.Next(Buildings.Count-2)];goal=new Vector3(roof.center.x,roof.max.y+1.85f,roof.center.z);}
        }
    }
    public sealed class SeededTraversalMap : IDisposable
    {
        public TraversalMapLayout Layout { get; }
        public GameObject Root { get; }
        public SeededTraversalMap(int seed,bool render)
        {
            Layout=new TraversalMapLayout(seed);Root=new GameObject("Traversal map "+seed);
            for(int i=0;i<Layout.Buildings.Count;i++)
            {
                var b=Layout.Buildings[i];var box=render?GameObject.CreatePrimitive(PrimitiveType.Cube):new GameObject();
                box.name="Building "+seed+"-"+i;box.layer=30;box.transform.SetParent(Root.transform,false);
                box.transform.position=b.center;box.transform.localScale=b.size;
                if(!render)box.AddComponent<BoxCollider>();
                else
                {
                    var material=Resources.Load<Material>("WindowFrameMaterial");
                    if(material!=null)box.GetComponent<Renderer>().sharedMaterial=material;
                }
            }
            var floor=new GameObject("Ground");floor.transform.SetParent(Root.transform,false);floor.transform.position=Vector3.down*.5f;
            floor.AddComponent<BoxCollider>().size=new Vector3(70,1,70);Physics.SyncTransforms();
        }
        public void Dispose()
        {
            if(Root==null)return;Root.SetActive(false);
            if(Application.isPlaying)UnityEngine.Object.Destroy(Root);else UnityEngine.Object.DestroyImmediate(Root);
        }
    }
}
