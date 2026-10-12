# 맨해튼: 팀원 실행 안내

공유 저장소: [sshs-ai-club/manhattan-headtracked-viewer](https://github.com/sshs-ai-club/manhattan-headtracked-viewer). 조직의 현재 구성원 전원에게 [manhattan-contributors 팀](https://github.com/orgs/sshs-ai-club/teams/manhattan-contributors)을 통해 쓰기 권한을 부여했습니다. 새 조직 구성원이 들어오면 조직 관리자나 팀 관리자가 이 팀에도 추가합니다.

## 함께 수정하기

아래 준비를 마친 뒤 자신의 작업 브랜치를 만들고 조직 저장소에 올립니다.

```powershell
git switch -c codex/my-feature
# 작업한 파일을 커밋한 뒤
git push -u origin HEAD
```

GitHub에서 `codex/roof-flicker-20261013`를 대상으로 Pull Request를 엽니다. 현재 테스트 브랜치는 개발 중인 버전이므로 `main` 반영은 검증·리뷰 후에 진행합니다. 버그와 재현 사진은 저장소의 Issues에 기록합니다. 원본 도시 ZIP·생성 타일·빌드·개인 보정·추적 환경은 커밋하지 않습니다.

외벽 겹침과 먼 지붕 번쩍임 수정까지 확인할 실행 대상은 공개 저장소의 **`codex/roof-flicker-20261013`** 브랜치입니다. 기존 외벽 수정 브랜치 `codex/city-overlap-20261012`에서 분리한 후속 수정이며 아직 `main`에 병합하지 않았습니다. 기존 `v0.1.0-preview`는 다른 커밋의 비공개 초안 배포본입니다. 현재 이 브랜치의 공개 실행 ZIP은 없으므로 아래 순서로 빌드합니다. 개발 중인 테스트 버전이며 검증 범위는 [지붕 번쩍임 검증 기록](roof-flicker-validation.md), [외벽 겹침 검증 기록](aerial-overlap-validation.md), [투영 검증 기록](physical-viewport-validation.md)과 [스트리밍 검증 기록](current-streaming-validation.md)에 있습니다.

앞선 외벽 수정은 [PR #6](https://github.com/sshs-ai-club/manhattan-headtracked-viewer/pull/6)에서 검토할 수 있습니다. 겹치는 면의 대응 판정과 로딩 경계의 깊이 우선순위를 함께 수정했으며 Surface 비교는 그대로 사용할 수 있습니다. 상세 모델이 아직 로딩되지 않은 먼 건물의 단색 대체 면은 남을 수 있습니다.

## 처음 한 번 준비

Windows x64에서 다음을 설치합니다.

- Git과 Git LFS
- GitHub CLI (`gh`): 현재 setup 스크립트의 로그인 확인 및 도시 원본 Release 다운로드에 사용
- Python **3.13.5** (`python --version`으로 확인)
- Unity CLI (`unity`)와 Unity **6000.3.22f1**, Windows 빌드 지원 및 활성 Unity 라이선스

PowerShell에서 아래 명령을 순서대로 실행합니다. 각 단계가 성공한 뒤 다음 단계로 진행합니다.

```powershell
gh auth login
git lfs install
git clone --branch codex/roof-flicker-20261013 https://github.com/sshs-ai-club/manhattan-headtracked-viewer.git
cd manhattan-headtracked-viewer
git lfs pull
git branch --show-current
powershell -NoProfile -ExecutionPolicy Bypass -File tools/setup.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/bake.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test.ps1 -DataOnly
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build.ps1
```

브랜치 출력이 `codex/city-overlap-20261012`인지 확인합니다. 도시 원본은 수 GB이고 첫 다운로드·변환·빌드에는 시간이 걸립니다. setup은 고정 기술 커밋과 추적기를 준비하고, bake는 도시 타일을 생성합니다. build는 Addressables와 현재 v7 면 대응 자료까지 생성한 뒤 실행 파일을 만듭니다. 빌드 중에는 같은 프로젝트를 Unity Editor에서 열지 않습니다. 기존 v5/v6 자료를 복사해서 재사용하지 않습니다.

다른 Python이 기본값이라면 setup에 `-PythonExe 'C:\Path\To\Python313\python.exe'`를 추가합니다. Unity가 기본 설치 위치에 없다면 setup과 build에 `-EditorPath 'C:\Path\To\Editor\Unity.exe'`를 추가합니다. 테스트에서 오류가 나면 해당 출력과 단계명을 공유하고, 성공한 것으로 간주하지 않습니다.

## 실행

먼저 웹캠 없이 도시와 이동을 확인합니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run.ps1 -NoTracker
```

종료한 뒤 웹캠을 사용하려면:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run.ps1 -Camera 0
```

다른 웹캠은 `-Camera 1`을 사용합니다. `Alt+F4`로 종료하면 추적기도 정리됩니다. 강제 정리는 같은 저장소에서 다음을 실행합니다.

실행 후 **Settings → WEBCAM**에서도 이름으로 카메라를 선택할 수 있습니다. **Refresh webcam list**는 연결된 장치 목록을 갱신합니다. 선택값은 다음 실행에 복원되며 명시적인 `-Camera`가 우선합니다. 연결 방식이 필요하면 `-Camera 1 -CameraBackend msmf`를 사용합니다. EShare는 가상 장치를 열어도 외부 영상이 연결되기 전에는 검은 프레임을 보낼 수 있습니다. 게임의 **Black video** 상태를 확인하고 EShare 쪽 영상 연결을 완료합니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run.ps1 -Stop
```

생성 실행 파일은 `Builds/ManhattanViewer/ManhattanViewer.exe`입니다. 실행 파일과 데이터 폴더는 함께 유지합니다.

## 처음 화면과 조작

실제 게임 표시 영역의 너비·높이와 눈에서 화면까지의 거리를 **cm**로 입력하고 `Enter Manhattan`을 누릅니다. 웹캠을 사용하면 정면 자세에서 Settings의 `Capture current face reference`로 기준을 잡고 `Save physical calibration`으로 저장합니다. 패널 전체 치수와 창 모드 보정은 [상세 보정 안내](windows-release.md#실측-입력과-개인-보정)를 따릅니다.

| 조작 | 동작 |
| --- | --- |
| 도시 화면 클릭 | 마우스 잠금·이동 시작 |
| W / S, A / D | 전진·후진, 좌우 이동 |
| Q / E | 아래·위 이동 |
| 마우스 | 시선 방향 회전 |
| Esc / Settings | 포인터 해제·설정 열기 |
| Alt+F4 | 종료 |

Settings의 Tracking / Window / Surface를 하나씩 바꾸어 비교합니다. 가까운 창틀이나 건물 모서리를 보며 머리를 천천히 움직입니다. 개인 보정·설정·북마크는 GitHub에 올리지 않습니다. 문제가 있으면 사용한 브랜치·`git rev-parse HEAD` 결과, 실패 단계, 실행 파일 옆 로그에서 관련 오류를 전달합니다.
