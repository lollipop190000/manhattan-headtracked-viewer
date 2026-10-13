# Windows 실행 빠른 안내 — v0.1.1

[Release](https://github.com/sshs-ai-club/manhattan-headtracked-viewer/releases/tag/v0.1.1)의
`ManhattanViewer-v0.1.1-Windows-x64.zip` 하나로 웹캠 없는 탐색과 웹캠 머리 추적을 모두 사용할 수 있습니다.
Unity·Python·Git·pip 설치나 도시 원본 다운로드는 필요하지 않습니다.

1. ZIP **전체**를 `C:\Games\ManhattanViewer`처럼 쓰기 가능한 폴더에 압축 해제합니다.
2. `Run-HeadTrackedDemo.cmd`를 더블 클릭합니다.
3. 메뉴 **1**은 웹캠 없는 탐색, **2**는 웹캠 머리 추적입니다.
4. 첫 화면에서 실제 표시 영역 너비·높이·눈에서 화면까지 거리를 cm로 입력합니다.
5. 웹캠 모드에서는 Settings → WEBCAM에서 카메라를 이름으로 선택하고 정면을 본 뒤
   **Capture current face reference**를 누릅니다.

게임 창을 클릭하면 마우스가 잠깁니다. WASD 이동, Q/E 하강·상승, 마우스 시점,
Esc 설정, Alt+F4 종료입니다. 게임 종료 시 추적기도 정리됩니다.

새 PC에서 Microsoft DLL이 없다고 나오면 메뉴 **4**로 공식 Microsoft 런타임을 설치합니다.
이 단계에만 인터넷과 Windows 관리자 승인이 필요할 수 있습니다. 일반 실행 중에는
추가 프로그램이나 모델을 다운로드하지 않습니다. Windows 10/11 x64, DirectX 11 GPU가 필요합니다.

압축을 푼 폴더에서 PowerShell을 열면 같은 실행 도구로 모드를 직접 고를 수 있습니다.

```powershell
.\Run-HeadTrackedDemo.cmd -NoTracker
.\Run-HeadTrackedDemo.cmd -Webcam
.\Run-HeadTrackedDemo.cmd -Check -ListCameras
.\Run-HeadTrackedDemo.cmd -Camera 1
.\Run-HeadTrackedDemo.cmd -Stop
```

웹캠이 열리지 않으면 다른 카메라 앱을 닫고 Windows의 데스크톱 앱 카메라 접근을
허용합니다. EShare·OBS 같은 가상 카메라는 소스가 연결되기 전 검은 화면을 보낼 수
있으므로 실제 내장 카메라를 먼저 고릅니다. 개인 보정·설정은 사용자 AppData에 별도로
저장되며 배포 ZIP에는 포함하지 않습니다.

화면·카메라 보정, 다른 연결 방식, 개인 파일, 오류 해결과 전체 설치 설명은
[English Windows guide](windows-release.md)에 있습니다.
