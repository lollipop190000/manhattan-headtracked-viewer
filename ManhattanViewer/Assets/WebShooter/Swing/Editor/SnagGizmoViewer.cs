using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WebShooter.Swing;
namespace Manhattan.Editor
{
    public sealed class SnagGizmoViewer : EditorWindow
    {
        private struct Marker { public Vector3 position; public bool stuck; public string label; }
        private readonly List<Marker> markers=new List<Marker>();
        private Label status;
        private Vector3 origin;
        private float size=.6f;
        [MenuItem("Tools/Traversal/Load Snag CSV")]
        public static void Open()=>GetWindow<SnagGizmoViewer>("Snag locations").Show();
        public void CreateGUI()
        {
            rootVisualElement.Add(new Button(Choose){text="Load session CSV…"});
            status=new Label("Red: snag · Orange: stuck");rootVisualElement.Add(status);
            var offset=new Vector3Field("City origin offset");offset.RegisterValueChangedCallback(e=>{origin=e.newValue;SceneView.RepaintAll();});rootVisualElement.Add(offset);
            var radius=new Slider("Marker size",.1f,3f){value=size};radius.RegisterValueChangedCallback(e=>{size=e.newValue;SceneView.RepaintAll();});rootVisualElement.Add(radius);
        }
        private void OnEnable(){SceneView.duringSceneGui+=Draw;}
        private void OnDisable(){SceneView.duringSceneGui-=Draw;}
        private void Choose()
        {
            string path=EditorUtility.OpenFilePanel("Snag CSV",Application.persistentDataPath,"csv");if(string.IsNullOrEmpty(path))return;
            markers.Clear();int snags=0,stucks=0;
            using(var reader=File.OpenText(path))foreach(var row in TraversalCsv.Read(reader))
            {
                if(row.Length<11 || !float.TryParse(row[2],NumberStyles.Float,CultureInfo.InvariantCulture,out float x))continue;
                bool stuck=row[5]=="stuck";if(stuck)stucks++;else snags++;
                markers.Add(new Marker{position=new Vector3(x,float.Parse(row[3],CultureInfo.InvariantCulture),float.Parse(row[4],CultureInfo.InvariantCulture)),
                    stuck=stuck,label=row[5]+" · "+row[6]+" · "+row[7]});
            }
            status.text=$"{Path.GetFileName(path)} · {snags} snag / {stucks} stuck";SceneView.RepaintAll();
        }
        private void Draw(SceneView view)
        {
            foreach(var m in markers)
            {
                var p=m.position-origin;Handles.color=m.stuck?new Color(1,.55f,0):Color.red;
                Handles.SphereHandleCap(0,p,Quaternion.identity,size,EventType.Repaint);
                if(Vector3.Distance(view.camera.transform.position,p)<40)Handles.Label(p+Vector3.up*size,m.label);
            }
        }
    }
}
