# Windows 배포본 실행 — v0.1.0

Windows x64 배포 ZIP을 전부 압축 해제하면 Unity Editor 설치나 도시 원본 다운로드·변환 없이 맨해튼을 탐색할 수 있습니다. 웹캠 Head Tracking은 아래 절차로 고정 버전의 Python 추적기를 별도로 준비합니다.

[정식 Release](https://github.com/sshs-ai-club/manhattan-headtracked-viewer/releases/tag/v0.1.0)에서 `ManhattanViewer-v0.1.0-Windows-x64.zip`을 내려받고 전체 압축을 풉니다. GitHub가 자동 제공하는 **Source code.zip**은 실행 파일이 아닙니다. 웹캠 없는 탐색에는 Python·Git·Unity 설치나 로그인이 필요 없습니다.

## 웹캠 없이 바로 실행

`ManhattanViewer.exe`가 있는 폴더에서 PowerShell을 열고 실행합니다.

```powershell
.\ManhattanViewer.exe --no-tracker
```

처음에는 실제 화면 너비·높이와 눈에서 화면까지의 거리를 **cm**로 입력하고 `Enter Manhattan`을 누릅니다. `--no-tracker`는 웹캠 추적기를 시작하지 않고 플레이어의 추적 수신도 끕니다. WASD·마우스 이동, 창틀, 도시 렌더링은 사용할 수 있습니다.

배포 폴더 전체를 함께 이동합니다. 실행 파일과 `ManhattanViewer_Data`를 분리하면 실행에 필요한 도시 타일과 데이터가 누락됩니다.

## 웹캠 추적기 준비와 실행

공개 저장소를 복제할 Git, **Python 3.13.5**, 웹캠을 준비합니다. 의존성과 얼굴 모델을 내려받을 인터넷 연결이 필요합니다. 배포 빌드와 함께 안내된 저장소 커밋을 사용합니다. 추적기 커밋은 `tools/technology-version.json`에 고정되어 있습니다. 소스를 직접 빌드하려면 [개발 안내](https://github.com/sshs-ai-club/manhattan-headtracked-viewer/blob/v0.1.0/README.md#build-from-source)를 따릅니다.

```powershell
git clone --branch v0.1.0 https://github.com/sshs-ai-club/manhattan-headtracked-viewer.git
cd manhattan-headtracked-viewer
powershell -NoProfile -ExecutionPolicy Bypass -File tools/setup.ps1 -SkipSource -SkipUnity
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run.ps1 -PlayerPath 'C:\Games\ManhattanViewer\ManhattanViewer.exe' -Camera 0
```

`-PlayerPath`를 압축 해제한 실행 파일의 **절대 경로**로 바꿉니다. 이후의 `tools/...` 명령은 이 저장소 루트에서 실행합니다. `setup`은 고정 기술 저장소를 `.tools/head-tracked-display`에 받고, 버전이 고정된 Python 환경과 SHA-256으로 고정된 얼굴 모델을 준비합니다. `-SkipSource -SkipUnity`로 도시 원본과 Unity 준비를 생략하므로 다음 `run` 명령으로 배포본을 실행하면 됩니다.

`python`이 다른 버전이라면 setup 명령에 `-PythonExe 'C:\Path\To\Python313\python.exe'`를 추가합니다. 다른 웹캠은 `-Camera 1`처럼 선택합니다. 런처는 플레이어와 추적기를 함께 관리하며, 게임을 `Alt+F4`로 닫을 때 추적기도 정리합니다. 강제로 정리해야 할 때는 새 PowerShell 창에서 **같은 실행 파일 경로**를 전달합니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run.ps1 -PlayerPath 'C:\Games\ManhattanViewer\ManhattanViewer.exe' -Stop
```

## 실측 입력과 개인 보정

Settings → **WEBCAM**에서 장치 이름·연결 방식·번호를 확인하고 클릭해 전환합니다. Python 추적기가 실행 중이어야 합니다. **Refresh webcam list**는 장치 목록을 갱신합니다. 성공한 선택은 `tracker_camera_preference.json`에 저장되며 도시 위치나 물리 보정 파일을 변경하지 않습니다. 명시적인 `-Camera`가 저장값보다 우선합니다. EShare에서 검은 프레임만 들어오면 **Black video** 상태가 표시됩니다. 휴대폰/외부 영상 연결은 EShare에서 완료해야 합니다. 카메라가 달라지면 렌즈 위치·intrinsics와 얼굴 기준은 새 장치에 맞춰 확인합니다.

화면의 실제 표시 영역 너비·높이를 재고, 평소 정면을 보는 자세에서 눈과 화면 평면 사이의 거리를 입력합니다. 화면 베젤 크기나 해상도 픽셀 수를 물리 길이 대신 입력하지 않습니다. 기본 모드의 치수와 웹캠 위치는 게임 전체 표시 영역과 그 중심 기준입니다. 창을 옮기거나 크기를 바꾸면 두 10 cm ruler와 웹캠 중심 기준을 다시 확인합니다.

패널 전체 치수를 입력하고 창 모드에서도 사용하려면 Settings → Viewport check의 **Dimensions are full monitor panel**을 켭니다. 치수·웹캠 위치·눈 좌표의 기준은 패널 중심입니다. Windows Player는 물리 픽셀 기준 client 위치와 크기를 측정하고 `Camera.rect`를 한 번 더 반영합니다. 창 전체가 한 패널 안에 있어야 하며 최소화·다른 모니터·오프스크린 렌더링이면 상태 메시지를 해결한 뒤 다시 확인합니다. 이 옵션은 viewer preference이며 기존 physical calibration을 자동 수정하거나 저장하지 않습니다.

오른쪽 `Settings`에서 `Screen width (cm)`, `Screen height (cm)`, `Viewing distance (cm)`를 수정한 뒤 `Save physical calibration`으로 저장합니다. 웹캠을 켠 상태에서 정면 기준 자세를 잡고 `Capture current face reference`를 누릅니다. IPD·웹캠 위치·카메라 보정과 필터는 같은 설정창의 상세 항목에 있습니다.

**LIGHTING**의 **Move sun slowly / Pause sun movement**는 오후 태양 이동을 켜고 멈춥니다. 슬라이더는 이동을 멈추고 오후 위치를 선택합니다. **Reset afternoon light**로 원래 오후를 복원합니다. 이 미리보기는 개인 보정·북마크·설정 파일에 저장하지 않으며 매 실행은 고정 오후로 시작합니다. 비교 촬영은 태양을 먼저 멈춘 뒤 진행합니다. [조명 검증 기록](https://github.com/sshs-ai-club/manhattan-headtracked-viewer/blob/v0.1.0/docs/sunlight-validation.md)을 참고합니다.

기본 개인 저장 위치는 `%USERPROFILE%\AppData\LocalLow\HeadTrackedDisplay\ManhattanViewer`입니다. 별도 개인 폴더를 사용하려면 첫 실행부터 같은 `-CalibrationDirectory`를 전달합니다. 이 옵션은 플레이어와 추적기에 함께 적용됩니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run.ps1 -PlayerPath 'C:\Games\ManhattanViewer\ManhattanViewer.exe' -Camera 0 -CalibrationDirectory "$env:LOCALAPPDATA\ManhattanViewer-Private"
```

이후 실행과 벤치마크에도 같은 개인 경로를 사용합니다. 웹캠 없이 이 보정을 사용할 때는 같은 명령에 `-NoTracker`를 추가합니다. `physical-calibration.json`, `tracker_runtime_calibration.json`, `viewer-preferences.json`, `position-bookmark.json`은 용도별로 저장되며 Git이나 배포 ZIP에 넣지 않습니다. 위치 북마크는 `Settings`에서 명시적으로 불러올 때만 적용됩니다.

## 조작과 확인

| 조작 | 동작 |
|---|---|
| 도시 화면 클릭 | 포인터 잠금·이동 시작 |
| W / S | 바라보는 수평 방향으로 전진·후진 |
| A / D | 좌우 이동 |
| Q / E | 아래·위 이동 |
| 마우스 | yaw·pitch 회전 |
| Esc / Settings | 포인터 해제·오른쪽 설정창 |
| Alt+F4 | 게임 종료 |

Windows에 페어링한 HID 컨트롤러도 사용할 수 있습니다. 게임패드는 왼쪽 스틱·D-pad로 이동하고 왼쪽·오른쪽 숄더 버튼으로 내려가고 올라갑니다. 일반 HID 조이스틱은 스틱·햇과 버튼 1·2를 사용하며 장치에 따라 매핑이 다를 수 있습니다. 키보드·마우스도 계속 사용할 수 있고, 실행 옵션 `--no-controller`로 컨트롤러 입력을 끕니다.

기본 속도는 10m/s이며 `Movement speed (m/s)`에서 1~100m/s로 바꿉니다. `Tracking`, `Window`, `Surface`는 각각 머리 오프셋 적용, 얇은 입체 창틀, 재질 표면 디테일을 제어합니다. Tracking OFF는 현재 머리 오프셋을 유지하면서 WASD 이동을 계속합니다. 가까운 창틀·창문·건물 모서리를 보며 천천히 좌우·상하·앞뒤로 머리를 움직여 투영을 확인합니다.

웹캠이 열리지 않으면 다른 카메라 앱을 닫고 Windows의 카메라 접근 권한과 `-Camera` 번호를 확인합니다. `Settings`의 `Show diagnostics`는 추적 상태·결과 지연·프레임 시간·타일 로딩 상태를 표시합니다. 런처 실행 로그는 실행 파일 옆 `Player.log`, `tracker-output.log`, `tracker-error.log`에 생성됩니다. 직접 실행한 플레이어의 기본 로그는 위 개인 저장 위치의 `Player.log`입니다.

1920×1200·60fps와 장시간 메모리·웹캠 수동 검증은 [검증 기준과 기록](https://github.com/sshs-ai-club/manhattan-headtracked-viewer/blob/v0.1.0/docs/release-v0.1.0.md)에서 해당 배포 빌드의 결과를 확인합니다. 목표 수치가 성능 측정 결과를 대신하지 않습니다.

벤치마크의 `benchmark.json`은 실행 마지막의 카메라 AA 설정, VSync,
목표 프레임률, Frame Timing 통계 기능 상태와 추적기 요청 여부도 기록합니다.
`cameraAntiAliasing`, `vSyncCount`, `targetFrameRate`,
`frameTimingStatisticsEnabled`, `trackerRequested`를 원본 보고서와 함께 보존합니다.
이 값들은 종료 시점의 설정이며 실행 내내 설정이 같았다는 증거나 달성한 FPS가
아닙니다. 설정한 AA가 실제 모든 렌더 패스에서 사용됐는지는 별도 렌더 검사가
필요합니다. CPU/GPU 프레임 시간, 관측 유효율과 화면 품질도 각각 확인합니다.

## 문제가 생겼을 때

- 실행 파일이 없으면 `Source code.zip` 대신 Windows x64 ZIP을 받았는지 확인합니다.
- 데이터 폴더 오류가 나면 ZIP 전체를 새 빈 폴더에 압축 해제합니다. 파일 하나만 이동하지 않습니다.
- 웹캠 없이 확인하려면 실행 파일 폴더에서 `./ManhattanViewer.exe --no-tracker`를 사용합니다.
- 흰 선의 안정화는 Settings → FLIGHT & IMAGE의 **Temporal anti-aliasing**을 켠 상태에서 적용됩니다.
- 추적기 준비에서 Python 버전 오류가 나면 3.13.5의 실행 파일을 `-PythonExe`로 지정합니다.
- 웹캠은 Windows 개인정보 설정에서 데스크톱 앱 접근을 허용하고, 다른 카메라 앱을 닫은 뒤 올바른 장치를 고릅니다.
- 버전·GPU·해상도·재현 순서와 개인 정보가 제거된 로그를 저장소 Issues에 남깁니다.

## 배포 ZIP에 포함할 파일

배포 준비자는 아래 항목만 빌드 폴더에서 복사하고, 이 가이드를 ZIP 루트의 `README-Windows.md`로 함께 넣습니다.

| 항목 | 포함 범위 |
|---|---|
| `ManhattanViewer.exe` | 실행 파일 |
| `UnityPlayer.dll` | Unity 런타임 |
| `UnityCrashHandler64.exe` | 크래시 처리 |
| `ManhattanViewer_Data/` | 하위 파일 전체, 도시 Addressables 포함 |
| `MonoBleedingEdge/` | 하위 파일 전체 |
| `D3D12/` | 하위 파일 전체 |
| `Notices/` | 하위 파일 전체와 `manifest.json` |

`tools/build.ps1`는 빌드 후 원본 라이선스·이용 조건·출처를 `Notices/`에 복사하고 크기와 SHA-256을 검증합니다. 현재 검증한 목록은 **67개 원문 파일**이며 Unity·추적 패키지·도시 원본과 `CityDetailRenderer`의 Unity 라이선스·NOTICE를 포함합니다. 목록은 `Notices/manifest.json`을 기준으로 보존하며 파일 내용을 수정하지 않습니다. 이 manifest에는 도시 소스 버전과 기술 커밋도 기록됩니다.

빌드 도구는 고정 Editor를 직접 실행하며 `-EditorPath`를 지원합니다. Addressables 콘텐츠 빌드 → 비동기 ownership 생성 → Player 빌드 순서로 완료를 기다립니다. 이 비동기 진입점을 `unity run/build`나 `-quit`로 실행하면 생성 중 조기 종료될 수 있으므로 `tools/build.ps1`를 사용합니다. 시간 초과나 실패 시 이 도구가 시작한 Editor만 종료하고 오류와 해당 로그 경로를 반환합니다.

비동기 생성이 끝난 뒤 실제 Player 빌드는 `PlayerBuildDispatch`가 편집기의 예약 실행 시점에 시작합니다. Unity player loop 안에서 곧바로 빌드하면 거절되는 문제를 피하며, 결과와 예외를 원래 빌드 작업으로 돌려줍니다. 독립 Windows 시험 빌드에서도 동일한 공개 코드를 그대로 사용해 실제 실행 파일 생성을 확인했습니다. 이 검사는 전체 도시 배포본의 이식성·성능 합격과는 별개입니다.

`ManhattanViewer.provenance.json`의 로컬 개발 경로, `ManhattanViewer_BurstDebugInformation_DoNotShip/`, 실행·추적기 로그, `tracker-session-*.heartbeat`, 개인 보정, 소스 ZIP, 추적기 가상환경·얼굴 모델은 배포 ZIP에서 제외합니다. 배포 ZIP의 SHA-256과 해당 빌드의 검증 기록은 Release에 함께 제공합니다.
