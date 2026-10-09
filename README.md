# Manhattan Head Tracked Viewer

맨해튼을 WASD·마우스로 탐색하면서 웹캠 기반 Head Tracking의 물리적 비대칭 투영을 확인하는 Windows Unity 프로젝트입니다. 재사용 추적·투영 코드는 [head-tracked-display](https://github.com/lollipop190000/head-tracked-display)에 있으며, 이 저장소는 도시 스트리밍·이동·게임 확장·팀 협업을 담당합니다.

Unity **6000.3.22f1**, URP **17.3.0**, 기본 이동 속도 **10m/s**이며 Settings에서 **1~50m/s**를 선택합니다. 기준 목표는 RTX 4070 Laptop 8GB에서 1920×1200·60fps입니다. 실제 측정 결과와 미검증 항목은 [검증 기록](docs/validation.md)에 기록합니다.

현재 고정된 기술 커밋은 [`960d5de55037a3fe2739ed055c8de091b8c54aed`](https://github.com/lollipop190000/head-tracked-display/commit/960d5de55037a3fe2739ed055c8de091b8c54aed)입니다. 패키지 데모 39 EditMode·22 PlayMode, URP 없는 최소 소비 프로젝트 39 EditMode·11 PlayMode, Test Framework도 없는 런타임 소비 프로젝트의 Windows 빌드·smoke 종료 코드 0, 공용 런처 21개 검사를 통과했습니다. 이 저장소의 데이터 검사 27개와 PowerShell 검사 8개도 통과했습니다. **맨해튼 독립 실행 성능, 웹캠 수동 검증, GitHub 새 복제본 재현 검증은 아직 미완료**입니다.

## 복제 → 준비 → 빌드 → 실행

Windows에 Git, Git LFS, GitHub CLI(`gh`), Python **3.13.5**, Unity CLI(`unity`), Unity **6000.3.22f1**와 Windows 빌드 모듈을 설치합니다. GitHub 계정은 이 비공개 저장소와 자산 Release에 접근할 수 있어야 합니다. Unity 라이선스도 활성화되어 있어야 합니다.

```powershell
gh auth login
git clone https://github.com/lollipop190000/manhattan-headtracked-viewer.git
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

| 조작 | 동작 |
|---|---|
| 도시 화면 클릭 | 마우스 포인터 잠금·이동 시작 |
| W / S | 바라보는 방향으로 전진·후진 |
| A / D | 좌우 이동 |
| Q / E | 아래·위 이동 |
| 마우스 | yaw·pitch 회전 |
| Esc / Settings | 포인터 해제·오른쪽 설정창 |
| Alt+F4 | 플레이어 종료·추적기 정리 |

첫 실행에서 실제 화면 **너비·높이·관찰 거리**를 입력합니다. 머리 이동은 실측 단위 그대로 투영하며, 먼 스카이라인보다 가까운 창틀·창문·건물 모서리에서 효과를 확인하기 쉽습니다. 상세 카메라 보정·IPD·이동 속도·진단은 오른쪽 Settings에 있습니다.

자동 벤치마크·비교 캡처는 시작 입력창을 생략하므로, 먼저 일반 실행에서 실측 보정을 저장합니다. 개인 파일을 별도 경로에서 관리하려면 일반 실행과 벤치마크에 같은 `-CalibrationDirectory '<private calibration directory>'`를 전달합니다. 해당 경로의 보정·설정·북마크는 Git이나 자산 Release에 올리지 않습니다.

Head Tracking OFF는 현재 머리 오프셋을 유지하고 관측·필터링을 계속합니다. WASD 이동은 계속됩니다. 창틀 OFF는 프레임만 숨기고 Surface detail OFF는 같은 메시의 재질을 가역적으로 바꿉니다. 세 비교 스위치는 이동 위치·조명·품질을 변경하지 않습니다. 물리 보정, 비교·이동 설정, 명시적으로 저장한 위치 북마크는 별도 파일이며 시작할 때 북마크를 자동 복원하지 않습니다.

## 팀 개발

`main`은 빌드 가능한 기준으로 유지하고 `codex/<feature>` 또는 팀의 기능 브랜치에서 PR을 만듭니다. Unity 자산 변경 시 `.meta`도 함께 커밋합니다. 직접 수정할 큰 바이너리는 Git LFS를 사용합니다. 원본 ZIP은 [비공개 source Release](https://github.com/lollipop190000/manhattan-headtracked-viewer/releases/tag/city-source-v2)에 버전별로 게시합니다. 생성 도시 타일·빌드·로그·추적기 가상환경·얼굴 모델·개인 보정은 커밋하지 않습니다.

- [자산 버전·재현 방법](docs/assets.md)
- [입력·충돌·게임 요소 연결](docs/architecture.md)
- [성능 측정·검증 기준](docs/validation.md)
- [제3자 출처·이용 조건](THIRD_PARTY.md)

GitHub Actions는 패키지·추적기 커밋 일치, 원본 manifest, 안전한 추출·무결성 테스트를 실행합니다. Unity 검증은 `tools/test.ps1`, 빌드는 `tools/build.ps1`로 재현합니다. 패키지 기능 변경은 기술 저장소에서 최소 소비 프로젝트 검증을 거친 뒤 이 저장소의 고정 커밋을 함께 갱신합니다.

개발 중 커밋하지 않은 변경으로 로컬 빌드를 확인할 때는 `tools/build.ps1 -AllowDirty`를 사용합니다. 팀에 전달할 빌드는 변경을 커밋하고 고정된 소스 버전에서 다시 만듭니다.

`tools/build.ps1`는 실행 파일 옆 `Notices/`에 검증된 도시 원본 이용 조건과 라이선스·출처를 함께 준비합니다. Windows 배포 ZIP에도 이 폴더를 포함합니다. 성능 측정은 `tools/benchmark.ps1`가 1920×1200 Exclusive Fullscreen을 요청하며 AC·데스크톱 Hz·드라이버·전력 제한 가용성과 GPU 로그를 기록합니다.

세 스위치의 시각 비교는 `tools/run.ps1 -PlayerArguments`로 `--comparison-captures-out <directory> --quit-after-comparison-captures`를 명시적으로 실행합니다. 30초 준비 후 기준 화면·8가지 조합·왕복 복원 화면과 보고서를 만들며, 일반 시작이나 저장 설정을 바꾸지 않습니다. 명령 예제와 수동 이미지 확인 항목은 [비교 캡처 가이드](docs/validation.md#explicit-comparison-captures)에 있습니다. 실제 성능 분석은 5개 지역×두 속도의 10가지 경로, 별도 1200초 메모리 기록과 일치하는 충돌 프록시 OFF/ON 쌍을 확인합니다.

코드는 [MIT](LICENSE) 라이선스입니다. NYC 지리 데이터와 외부 모델·라이브러리의 조건은 별도로 유지합니다.
