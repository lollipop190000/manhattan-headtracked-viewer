# 수평 이동과 Near 진단 검증

2026-10-10 검증. 기준 소스는 viewer `650b5747d5777b2d506f6879441a1f2d99539d5c`, 기술 패키지는 `960d5de55037a3fe2739ed055c8de091b8c54aed`입니다. Unity 6000.3.22f1 / URP 17.3.0을 사용합니다.

## 실행과 데이터 준비

[README](../README.md)의 복제·준비·빌드 절차와 [자산 다운로드 가이드](assets.md)를 따릅니다. `assets/source-manifest.json`의 ZIP SHA-256을 검증하고 원본에서 생성 자산을 준비해야 합니다. 코드 복제만으로 생성 도시가 생기지는 않습니다. 대용량 원본·생성 타일·빌드·개인 보정은 Git에 넣지 않습니다. 최근 별도로 받은 replacement형 new_version은 이 변경으로 자동 마이그레이션되지 않습니다.

이미 준비한 Windows 플레이어에서 웹캠 없이 확인할 때:

```powershell
.\ManhattanViewer.exe --no-tracker
```

프로젝트의 공용 실행 경로는 `tools/run.ps1 -NoTracker`입니다. 도시 화면을 클릭해 입력을 활성화하고 Settings에서 Diagnostics를 켭니다. 실제 화면 크기·거리를 측정해 개인 폴더에 저장합니다. 얼굴 기준 CaptureReference와 물리 화면 보정 저장은 별도 작업입니다.

## 동작

WASD는 시선 방향을 도시 수평면에 투영해 이동합니다. 위나 아래를 보더라도 WASD로 높이가 변하지 않습니다. Q/E만 world-up 방향으로 높이를 바꿉니다. 수직에 가까운 시점에서는 yaw로 수평 방향을 정합니다. Analog 크기와 대각 입력의 기존 속도 계약, 절대 benchmark 경로의 높이, translation constraint와 floating origin, headtracking gain·보정은 유지합니다.

Near 진단은 로드된 인스턴스 수, 활성 계층 수, 최근접 **loaded** 타일 bounds까지 거리, 실제 활성 renderer property block의 opacity 최소·최대, enabled renderer와 visibility flag를 표시합니다. 최근접 bounds는 실제 보이는 facade의 거리가 아닙니다. Visibility flag는 화면 픽셀 확인이 아닙니다. Near는 건물 본체 위의 additive 창틀이며 본체 교체로 해석하지 않습니다.

## 완료된 검사

| 검사 | 결과 |
|---|---|
| Unity EditMode | 177/177, 실패·skip·inconclusive 0 |
| Unity PlayMode | 45/45, 실패·skip·inconclusive 0 |
| Python portable | 81/81 |
| PowerShell parser | 8 assertions |
| 독립 고정 소스 검토 | P1/P2 및 차단 사유 없음 |

기존 158 Edit / 42 Play 검사 이름을 모두 유지했습니다. 새 검사는 수직 시점·yaw·roll·analog·대각 이동, 실제 controller의 Q/E와 원점 이동, 절대 공중 경로, Near loaded/active·비활성 최근접·opacity 0·Surface OFF를 포함합니다. 테스트 소스 고정 JSON SHA-256은 `c07ff79187853b5e7f61ecf594e332b2bd598001054fa75618f17d68d408c88d`이며 patch SHA-256은 `c2870d7e332f0a16775cf78e7be51300559523948112f17597b813e03395187b`입니다.

Managed allocation 0은 `GetNearDiagnostics()`를 100회 warmup한 뒤 1,000회 조회한 범위만 해당합니다. HUD 문자열·native·GPU 비용까지 보증하지 않습니다.

실제 로컬 v2 도시 번들을 읽기 전용으로 사용한 Windows UI QA에서는 35 loaded /17 active Near, 실제 opacity 0.010..1.000,47 enabled renderers를 확인했습니다. 1920x1200에서 진단 문장 전체가1100x360 영역 안에 들어왔습니다. 이는 웹캠 없는 합성 보정과 숨긴 창의 ScreenSpaceCamera/RenderTexture 캡처입니다. 일반 overlay presentation·키보드 하드웨어·사용자 조작 합격과 성능 수치로 취급하지 않습니다. 원본 저장소와 개인 설정은 보존했습니다.

재현 가능한 검사:

```powershell
pwsh -File tools/test.ps1 -DataOnly -PythonExe '<Python 3.13 executable>'
pwsh -File tools/test.ps1 -PythonExe '<Python 3.13 executable>'
```

## 남은 한계

원래 사용자 항공 사진의 시점 좌표가 없으며, 별도 고정 시점에서 큰 대각선 얼룩을 확실히 재현하지 못했습니다. 그림자·Surface·TAA를 한 번에 하나씩 비교했고, 부분 Core/Skyline 교체에서 실제 번들 메시의 중복 삼각형이 확인됐습니다. 이 입력/HUD 커밋은 지붕 ownership 수정을 포함하지 않습니다. 임의 tile 숨김이나 shadow bias/depth offset 변경으로 해결했다고 주장하지 않습니다.

Near 표시와 입력 회귀는 실제 웹캠 수동 검증·장시간 성능/메모리 합격을 대신하지 않습니다. 이 검증에서는 웹캠을 시작하거나 얼굴 이미지를 저장하지 않았습니다.
