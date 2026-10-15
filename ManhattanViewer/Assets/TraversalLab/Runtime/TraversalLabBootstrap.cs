using System;
using UnityEngine;
namespace Manhattan.TraversalLab
{
    public sealed class TraversalLabBootstrap : MonoBehaviour
    {
        private SeededTraversalMap preview;
        private void Awake()
        {
            Application.runInBackground=true;Time.fixedDeltaTime=1f/60;
            if(LabArguments.Has("--traversal-agent"))
            {
                var type=Type.GetType("Manhattan.TraversalLab.TraversalAgentLauncher, Manhattan.Traversal.ML");
                if(type==null){Debug.LogError("Install the pinned ML-Agents package before building the agent environment.");Application.Quit(2);return;}
                type.GetMethod("Install").Invoke(null,new object[]{gameObject});return;
            }
            if(LabArguments.Has("--scripted-flow"))
            {
                var type=Type.GetType("Manhattan.TraversalLab.ScriptedFlowBenchmark, Manhattan.Traversal.Lab");
                if(type!=null)gameObject.AddComponent(type);return;
            }
            preview=new SeededTraversalMap(LabArguments.Int("--map-seed",17),true);
            var camera=new GameObject("Map overview").AddComponent<Camera>();camera.tag="MainCamera";camera.transform.position=new Vector3(40,48,-45);camera.transform.LookAt(Vector3.up*5);
            var light=new GameObject("Map sun").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(50,-30,0);
        }
        private void OnDestroy()=>preview?.Dispose();
    }
}
