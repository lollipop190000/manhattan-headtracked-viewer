> **WIP shared branch; do not merge as a release.**
> Tests and performance measurements belong to their recorded source revisions. Current scoped no-tracker Windows measurements and earlier failed runs are in [streaming validation](docs/current-streaming-validation.md). Real webcam/global flicker and release acceptance remain pending. See [ownership status](docs/ownership-wip-status.md) and [viewport integration checks](docs/physical-viewport-validation.md).

# Manhattan Head Tracked Viewer

팀원이 외벽 겹침과 먼 지붕 번쩍임 수정이 포함된 `codex/roof-flicker-20261013` 버전을 실행하려면 [브랜치 지정 실행 안내](docs/team-quickstart.md)를 따릅니다. 저장소와 도시 원본 Release는 공개되어 있습니다. 기존 Windows preview 초안은 다른 커밋이므로 이 브랜치의 실행 파일로 사용하지 않습니다.

맨해튼을 WASD·마우스로 탐색하면서 웹캠 기반 Head Tracking의 물리적 비대칭 투영을 확인하는 Windows Unity 프로젝트입니다. 재사용 추적·투영 코드는 [head-tracked-display](https://github.com/lollipop190000/head-tracked-display)에 있으며, 이 저장소는 도시 스트리밍·이동·게임 확장·팀 협업을 담당합니다.

Unity **6000.3.22f1**, URP **17.3.0**, 기본 이동 속도 **10m/s**이며 Settings에서 **1~100m/s**를 선택합니다. 기준 목표는 RTX 4070 Laptop 8GB에서 1920×1200·60fps입니다. 실제 측정 결과와 미검증 항목은 [검증 기록](docs/validation.md)에 기록합니다.

현재 기술 패키지는 [`c5ddab14d12dcc8acc8b31048f5752686984f3ab`](https://github.com/lollipop190000/head-tracked-display/commit/c5ddab14d12dcc8acc8b31048f5752686984f3ab)에 고정되어 있습니다. 부분 카메라 영역과 물리 화면의 창 위치를 반영하는 투영을 추가했습니다. URP 없는 최소 소비 프로젝트와 합성 Windows Player 검사는 통과했습니다. [현재 투영 검증 기록](docs/physical-viewport-validation.md)을 확인합니다.

일반적인 스트리밍 준비 완료와 실제 표시 전환을 다른 프레임으로 나누는 작업 경계를 추가했습니다. 기존 표시를 복구해야 하는 경우에는 즉시 처리합니다. 소스 `338c090`과 당시 대응 자료로 측정한 1920×1200·100m/s·웹캠 없는 진단에서 Midtown 반복 streaming P95는 1.8049/1.8441ms였습니다. 작업이 활발한 구간의 P95는 여전히 2ms를 넘으며, 이후 면 대응 수정의 성능·전체 지역·지붕 번쩍임·배포본 합격은 별도 검증이 필요합니다. 측정한 정확한 소스, 실패했던 중간 정책, CPU/GPU 시간과 남은 한계는 [현재 스트리밍 검증 기록](docs/current-streaming-validation.md)에 있습니다.

높은 위치에서 단색 원거리 면이 상세 건물의 창문을 가리는 문제를 재현했습니다. 같은 벽의 삼각형 분할이 다르거나 한 벽이 여러 도시 구역으로 나뉜 경우가 원인이었습니다. v7은 그 벽에 필요한 이웃 구역까지 요청하고, 상세 면들이 모두 표시될 때 덮개를 제거하며, 한쪽이 빠지면 복원합니다. 이웃 요청은 연쇄 확장하지 않으며 기존 메모리 제한을 따릅니다. 바깥 로딩 경계에 남는 부분 겹침은 실행 중 복제한 단색 재질의 깊이 우선순위로 안정시킵니다. 원본 재질과 Surface 비교 동작은 보존합니다. 현재 코드에는 기존 built bundle에서 **v7 면 대응 자료를 명시적으로 재생성**해야 합니다. [항공 시점 검증 기록](docs/aerial-overlap-validation.md)과 [Skyline 대응 자료 생성 안내](docs/skyline-ownership.md)를 따릅니다.

먼 지붕의 남은 번쩍임은 반올림된 단색 배경 지붕과 상세 지붕의 약 0.2mm 높이 차이에서 재현됐습니다. `codex/roof-flicker-20261013`은 기존 외벽 수정에 더해, 해당 배경 건물만 렌더링할 때 1mm 낮춰 겹침을 해소합니다. 원본 도시 자산·조명·추적 패키지·비교 스위치는 그대로 유지합니다. [지붕 번쩍임 검증 기록](docs/roof-flicker-validation.md)을 참고하세요.

## Windows 배포본 실행

배포 ZIP 전체를 압축 해제하면 Unity 설치 없이 실행할 수 있습니다. 실행 파일 폴더에서 웹캠 없이 탐색하려면 다음을 사용합니다.

```powershell
.\ManhattanViewer.exe --no-tracker
```

웹캠을 사용하려면 이 저장소를 복제한 폴더에서 Git·Python **3.13.5**로 추적기를 준비하고 배포 실행 파일의 절대 경로를 전달합니다. 도시 원본 다운로드·Unity 준비·빌드는 필요하지 않습니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/setup.ps1 -SkipSource -SkipUnity
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run.ps1 -PlayerPath 'C:\Games\ManhattanViewer\ManhattanViewer.exe' -Camera 0
```

첫 실행에서 실제 화면 너비·높이·관찰 거리를 cm로 입력합니다. 보정을 별도 개인 폴더에 보관하려면 `run`에 `-CalibrationDirectory '<private calibration directory>'`를 전달합니다. [Windows 배포 실행 가이드](docs/windows-release.md)에 종료·상세 보정·배포 파일 목록과 라이선스 보존 절차가 있습니다. 빌드별 성능과 웹캠 수동 검증 상태는 [검증 기록](docs/validation.md)을 확인합니다.

## 복제 → 준비 → 빌드 → 실행

Windows에 Git, Git LFS, GitHub CLI(`gh`), Python **3.13.5**, Unity CLI(`unity`), Unity **6000.3.22f1**와 Windows 빌드 모듈을 설치합니다. 저장소와 자산 Release는 공개되어 있습니다. 현재 setup 도구는 `gh auth status`를 확인하므로 GitHub CLI에 로그인합니다. Unity 라이선스도 활성화되어 있어야 합니다.

```powershell
gh auth login
git clone --branch codex/roof-flicker-20261013 https://github.com/sshs-ai-club/manhattan-headtracked-viewer.git
cd manhattan-headtracked-viewer
git lfs install --local
git lfs pull
powershell -NoProfile -ExecutionPolicy Bypass -File tools/setup.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/bake.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run.ps1 -Camera 0
```

`python`이 다른 버전이면 setup에 `-PythonExe '<Python 3.13.5 executable>'`를 전달합니다. 원본 ZIP이 이미 있다면 `-SourceArchiveDirectory '<ZIP folder>'`를 전달해 다운로드를 생략합니다. `tools/setup.ps1`는 SHA-256 검증, 원본 압축 해제, 고정 커밋의 기술 저장소·추적기 준비, Unity 패키지 설치를 수행합니다. `tools/bake.ps1`는 원본에서 실행용 타일·카탈로그를 생성합니다. 전체 자산은 수 GB이고 첫 import/bake는 일반 실행보다 오래 걸립니다.

Unity가 기본 Hub 설치 위치에 없으면 setup에 `-EditorPath '<Unity.exe>'`를 전달합니다. 패키지 설치는 비동기 Unity PackageManager 요청을 완료한 뒤 종료하기 위해 이 실행 파일을 직접 호출하며, 나머지 빌드·테스트·변환은 Unity CLI를 사용합니다.

Unity에서 작업하려면 준비 후 `unity open ./ManhattanViewer --editor-version 6000.3.22f1`을 실행합니다. 원본과 생성 자산을 준비한 뒤 열어야 팀원의 작업 환경이 동일해집니다.

```powershell
# 웹캠 없이 이동·도시 렌더링 확인
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run.ps1 -NoTracker
# 다른 카메라 / 종료
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run.ps1 -Camera 1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run.ps1 -Stop
```

`tools/run.ps1`는 고정 기술 저장소의 **단일 CMD 진입점** `Run-HeadTrackedDemo.cmd`를 호출하며 프로젝트·플레이어·추적기 경로만 전달합니다. 팀원이 사용하는 PC의 형제 폴더 경로를 참조하지 않습니다.

## 조작과 보정

Settings → **WEBCAM**에서 장치 이름을 보고 카메라를 선택하고 **Refresh webcam list**로 목록을 갱신합니다. Python 추적기가 실행 중일 때 사용할 수 있고, 도시를 다시 시작하지 않고 전환합니다. 마지막으로 선택한 장치는 개인 폴더의 `tracker_camera_preference.json`에 따로 저장됩니다. 명령에 `-Camera 1`처럼 번호를 명시하면 그 실행에서는 저장한 선택보다 우선합니다. 장치 이름 옆 `dshow` / `msmf`는 Windows 연결 방식이며 번호는 방식별로 다를 수 있습니다. 필요하면 `tools/run.ps1 -Camera 1 -CameraBackend msmf`로 명시합니다. EShare가 검은 영상을 보내면 상태에 **Black video**가 표시되며 EShare에서 휴대폰/외부 영상 연결을 먼저 완료해야 합니다. 카메라를 바꾸면 해당 카메라의 위치·보정과 얼굴 기준을 다시 확인합니다.

| 조작 | 동작 |
|---|---|
| 도시 화면 클릭 | 마우스 포인터 잠금·이동 시작 |
| W / S | 바라보는 방향으로 전진·후진 |
| A / D | 좌우 이동 |
| Q / E | 아래·위 이동 |
| 마우스 | yaw·pitch 회전 |
| Esc / Settings | 포인터 해제·오른쪽 설정창 |
| Alt+F4 | 플레이어 종료·추적기 정리 |

첫 실행에서 실제 화면 **너비·높이·관찰 거리**를 입력합니다. 머리 이동은 실측 단위 그대로 투영하며, 먼 스카이라인보다 가까운 창틀·창문·건물 모서리에서 효과를 확인하기 쉽습니다. 상세 카메라 보정·IPD·이동 속도·진단은 오른쪽 Settings에 있습니다. 기본 치수는 게임 전체 표시 영역의 실측값입니다. 패널 전체 치수로 창 모드까지 사용하려면 Settings → Viewport check의 **Dimensions are full monitor panel**을 켜고 패널 중심 기준으로 웹캠 위치를 확인합니다. Windows Player가 창 위치·크기와 카메라 영역을 함께 반영하며, 기존 개인 보정 파일은 자동 변환하지 않습니다.

자동 벤치마크·비교 캡처는 시작 입력창을 생략하므로, 먼저 일반 실행에서 실측 보정을 저장합니다. 개인 파일을 별도 경로에서 관리하려면 일반 실행과 벤치마크에 같은 `-CalibrationDirectory '<private calibration directory>'`를 전달합니다. 해당 경로의 보정·설정·북마크는 Git이나 자산 Release에 올리지 않습니다.

Head Tracking OFF는 현재 머리 오프셋을 유지하고 관측·필터링을 계속합니다. WASD 이동은 계속됩니다. 창틀 OFF는 프레임만 숨기고 Surface detail OFF는 같은 메시의 재질을 가역적으로 바꿉니다. 세 비교 스위치는 이동 위치·조명·품질을 변경하지 않습니다. 물리 보정, 비교·이동 설정, 명시적으로 저장한 위치 북마크는 별도 파일이며 시작할 때 북마크를 자동 복원하지 않습니다.

## 팀 개발

`main`은 빌드 가능한 기준으로 유지하고 `codex/<feature>` 또는 팀의 기능 브랜치에서 PR을 만듭니다. Unity 자산 변경 시 `.meta`도 함께 커밋합니다. 직접 수정할 큰 바이너리는 Git LFS를 사용합니다. 원본 ZIP은 [공개 source Release](https://github.com/sshs-ai-club/manhattan-headtracked-viewer/releases/tag/city-source-v2)에 버전별로 게시합니다. 생성 도시 타일·빌드·로그·추적기 가상환경·얼굴 모델·개인 보정은 커밋하지 않습니다.

- [자산 버전·재현 방법](docs/assets.md)
- [입력·충돌·게임 요소 연결](docs/architecture.md)
- [성능 측정·검증 기준](docs/validation.md)
- [제3자 출처·이용 조건](THIRD_PARTY.md)

GitHub Actions는 패키지·추적기 커밋 일치, 원본 manifest, 안전한 추출·무결성 테스트를 실행합니다. Unity 검증은 `tools/test.ps1`, 빌드는 `tools/build.ps1`로 재현합니다. 패키지 기능 변경은 기술 저장소에서 최소 소비 프로젝트 검증을 거친 뒤 이 저장소의 고정 커밋을 함께 갱신합니다.

개발 중 커밋하지 않은 변경으로 로컬 빌드를 확인할 때는 `tools/build.ps1 -AllowDirty`를 사용합니다. 팀에 전달할 빌드는 변경을 커밋하고 고정된 소스 버전에서 다시 만듭니다.

`tools/build.ps1`는 고정 Unity Editor를 직접 실행하고 도시 대응 데이터 생성이 끝난 뒤 Player를 빌드합니다. Editor 경로를 자동으로 찾지 못하면 `-EditorPath`로 지정합니다. 기존 번들에서 대응 데이터만 다시 만들 때는 [ownership 생성 안내](docs/skyline-ownership.md)의 `tools/bake_ownership.ps1`를 사용합니다.

멀리 있는 건물의 텍스처를 단색 외형이 가리던 문제는 원본 자산을 바꾸지 않고 대응 판정을 수정했습니다. 같은 위치의 실제 Windows Player 수정 전후 결과는 [벽면 검증 기록](docs/beige-wall-validation.md)에 있습니다.

`tools/build.ps1`는 실행 파일 옆 `Notices/`에 검증된 도시 원본 이용 조건과 라이선스·출처를 함께 준비합니다. Windows 배포 ZIP에도 이 폴더를 포함합니다. 성능 측정은 `tools/benchmark.ps1`가 1920×1200 Exclusive Fullscreen을 요청하며 AC·데스크톱 Hz·드라이버·전력 제한 가용성과 GPU 로그를 기록합니다.

반복 측정 전체 목록은 `tools/benchmark_suite.ps1 -DryRun`으로 확인하고, 실측 보정 준비 후 `tools/benchmark_suite.ps1 -CalibrationDirectory '<private calibration directory>'`로 순차 실행합니다. 기본 웹캠 사용·12가지 실행의 측정 시간은 약 2시간 5분이며 준비/로딩 시간이 추가됩니다. GUID 폴더에 원본 로그와 진행 JSON을 보존합니다. `-CaseFilter`로 일부만 선택하고, `-NoTracker`·`-DurationOverride`는 명시적인 기초 비교·탐색 용도로 사용합니다. 완료 상태가 성능 합격을 의미하지는 않습니다.

세 스위치의 시각 비교는 `tools/run.ps1 -PlayerArguments`로 `--comparison-captures-out <directory> --quit-after-comparison-captures`를 명시적으로 실행합니다. 30초 준비 후 기준 화면·8가지 조합·왕복 복원 화면과 보고서를 만들며, 일반 시작이나 저장 설정을 바꾸지 않습니다. 명령 예제와 수동 이미지 확인 항목은 [비교 캡처 가이드](docs/validation.md#explicit-comparison-captures)에 있습니다. 실제 성능 분석은 5개 지역×두 속도의 10가지 경로, 별도 1200초 메모리 기록과 일치하는 충돌 프록시 OFF/ON 쌍을 확인합니다.

팀이 작성한 코드는 [MIT](LICENSE) 라이선스입니다. Unity URP에서 파생한 셰이더는 [별도 출처와 Unity Companion License](ManhattanViewer/Assets/Shaders/NOTICE.md)를 따릅니다. NYC 지리 데이터와 외부 모델·라이브러리의 조건도 별도로 유지합니다.

소스 하나(`282030_0`)의 실제 Windows Player 교체·Surface OFF/ON·취소·복원을 격리된 환경에서 검증했습니다. 기존 면 492개만 교체하고 주변 메시 19개를 보존했으며, 원본 6,068개 삼각형과 좌표·노멀·UV가 일치했습니다. [소스 교체 검증 기록](docs/source-replacement-validation.md)에 정확한 실행 버전, 이미지 비교 오차와 실패 이력을 남겼습니다. 일반 도시 스트리밍 연결·모델 자동 활성화·광학 동등성·성능 합격은 아직 완료되지 않았습니다.

Legacy merged Skyline의 primitive ownership, offline 재생성 및 검증 한계는 [소유권 문서](docs/skyline-ownership.md)에 정리했습니다.
