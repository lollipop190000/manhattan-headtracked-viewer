using Manhattan.Viewer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WebShooter.Swing
{
    /// <summary>
    /// 아두이노 없이 마우스로 거미줄을 쏘는 임시 입력. 팀 코드는 수정하지 않고, 씬이 뜬 뒤 스스로 ManhattanBootstrap에 붙는다.
    /// 마우스가 캡처된 상태(화면을 한 번 클릭한 뒤)에서 십자선(화면 중앙) 방향으로 쏜다.
    /// 첫 거미줄이 건물에 붙기 전까지는 기존 자유 비행이고, 붙은 뒤부터 스윙 물리로 움직인다.
    ///   좌클릭: 발사(이전 줄은 끊고 갈아탄다)   우클릭: 줄 끊기(속도 유지)   R: 자유 비행으로 복귀   E/Q: 줄 감기/풀기
    /// </summary>
    public sealed class MouseWebShooter : MonoBehaviour
    {
        private ManhattanBootstrap app;
        private WebSwingMotionInput swing;
        private Camera cam;
        private GameObject rope, tip;
        private bool capturedLastFrame;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (ManhattanBootstrap.HasArgument("-runTests")) return;
            new GameObject("Mouse Web Shooter").AddComponent<MouseWebShooter>();
        }

        private void Update()
        {
            if (swing == null) { TryConnect(); return; }

            var keyboard = app.KeyboardInput;
            bool active = keyboard.InputEnabled && keyboard.Captured;
            var mouse = Mouse.current;
            var keys = Keyboard.current;
            // 화면을 처음 잡는 클릭(캡처)은 발사로 치지 않는다
            if (active && capturedLastFrame && mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame) Fire();
                if (mouse.rightButton.wasPressedThisFrame) swing.Release();
                if (keys != null && keys.rKey.wasPressedThisFrame) { swing.Release(); swing.Enabled = false; }
            }
            capturedLastFrame = active;
        }

        private void TryConnect()
        {
            if (app == null) app = FindFirstObjectByType<ManhattanBootstrap>();
            if (app == null || app.Flight == null || app.Origin == null || app.Streamer == null || app.KeyboardInput == null) return;
            // 지상에서도 시작할 수 있게: 붙는 순간 앵커 쪽으로 끌려가고(attachPull), 가까워진 만큼 줄을 줄인다(래칫)
            var settings = new SwingSettings { attachPull = 22f, ratchetRope = true };
            swing = new WebSwingMotionInput(app.Flight.InputSource, app.Flight, app.Origin, settings) { Enabled = false };
            app.Flight.InputSource = swing;                                                // 꺼진 동안은 기존 입력이 그대로 통과한다
            app.Flight.TranslationConstraint = new CityCollisionConstraint(app.Origin);    // 건물에 막히게
            app.Streamer.CollisionProxiesEnabled = true;                                   // 거미줄이 맞을 충돌 프록시를 불러온다 (필수)
        }

        private void Fire()
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            bool wasActive = swing.Enabled;
            if (!wasActive) swing.Activate();
            bool hit = swing.TryFire(cam.ViewportPointToRay(new Vector3(.5f, .5f, 0f)));
            if (!hit && !wasActive) swing.Enabled = false;   // 허공을 쐈으면 자유 비행 그대로
        }

        private void LateUpdate()
        {
            bool show = swing != null && swing.Enabled && swing.Body.Attached && cam != null;
            if (rope != null) rope.SetActive(show);
            if (tip != null) tip.SetActive(show);
            if (!show) return;
            if (rope == null) { rope = MakePrimitive(PrimitiveType.Cylinder); tip = MakePrimitive(PrimitiveType.Sphere); }

            Transform t = cam.transform;
            Vector3 start = t.position + t.right * .25f - t.up * .2f + t.forward * .6f;   // 손 위치쯤
            Vector3 end = swing.Body.Anchor - app.Origin.Offset;                           // 도시 좌표 -> 월드 좌표
            Vector3 d = end - start;
            float length = d.magnitude;
            if (length < .01f) return;
            float width = .02f + length * .001f;   // 멀리서도 보이게 거리에 비례해서 굵게
            rope.transform.SetPositionAndRotation(start + d * .5f, Quaternion.FromToRotation(Vector3.up, d));
            rope.transform.localScale = new Vector3(width, length * .5f, width);          // 기본 실린더는 높이 2
            tip.transform.position = end;
            tip.transform.localScale = Vector3.one * (width * 4f);
        }

        private static Material webMaterial;

        private static GameObject MakePrimitive(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sharedMaterial = WebMaterial();
            return go;
        }

        /// <summary>
        /// 프리미티브의 기본 재질은 빌드에서 URP 셰이더가 없어 핑크색이 된다.
        /// 뷰어가 창틀에 쓰는 URP Lit 재질(Resources/WindowFrameMaterial)은 빌드에 들어 있으므로 복제해서 흰색으로 쓴다.
        /// </summary>
        private static Material WebMaterial()
        {
            if (webMaterial != null) return webMaterial;
            var template = Resources.Load<Material>("WindowFrameMaterial");
            webMaterial = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            webMaterial.name = "Web Line";
            webMaterial.SetColor("_BaseColor", new Color(.95f, .95f, .95f));
            webMaterial.SetFloat("_Metallic", 0f);
            webMaterial.SetFloat("_Smoothness", .15f);
            webMaterial.SetFloat("_ReceiveShadows", 0f);
            webMaterial.EnableKeyword("_RECEIVE_SHADOWS_OFF");
            return webMaterial;
        }

        private void OnGUI()
        {
            if (swing == null || app.KeyboardInput == null || !app.KeyboardInput.Captured) return;
            GUI.Label(new Rect(Screen.width * .5f - 10f, Screen.height * .5f - 12f, 20f, 24f), "+");
            GUI.Label(new Rect(12f, Screen.height - 28f, 900f, 24f),
                "Click: web  |  Right click: release  |  R: free flight  |  E/Q: reel in/out");
        }

        private void OnDestroy()
        {
            if (rope != null) Destroy(rope);
            if (tip != null) Destroy(tip);
        }
    }
}
