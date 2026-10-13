# 거미줄 스윙 (마우스 발사)

화면 중앙(십자선)이 가리키는 건물에 거미줄을 붙이고, 줄에 매달려 진자처럼 움직이는 이동 방식입니다.
아두이노 손목 장치 없이 마우스만으로 쓸 수 있습니다. 이 PR은 마우스 발사까지만 포함합니다.

## 조작

화면을 한 번 클릭해 마우스를 잡은 뒤부터 동작합니다. 마우스를 잡는 첫 클릭은 발사로 치지 않습니다.

| 입력 | 동작 |
|---|---|
| 좌클릭 | 발사. 이전 줄은 먼저 끊고, 건물에 맞으면 새로 붙입니다(갈아타기). 허공이면 자유 비행 그대로입니다 |
| 우클릭 | 줄 끊기. 속도가 유지된 채 포물선으로 날아갑니다 |
| R | 자유 비행으로 복귀 |
| E / Q | 줄 감기 / 풀기 (거미줄이 붙어 있을 때) |
| WASD | 공중 조향(약하게), 자유 비행 중에는 기존 이동 |

첫 거미줄이 건물에 붙기 전까지는 기존 자유 비행입니다. 붙는 순간 앵커 쪽으로 끌려가고(`attachPull`), 가까워진 만큼 줄이 줄어(`ratchetRope`) 팽팽해지면 진자로 이어집니다.
줄은 안쪽으로 당기지 않기 때문에 이 두 가지가 없으면 지상(눈높이 1.85 m)에서는 붙여도 움직이지 않습니다. 건물 위쪽을 보고 쏘는 편이 잘 됩니다.

## 구성

`ManhattanViewer/Assets/WebShooter/Swing/` (기존 파일은 수정하지 않았습니다)

| 폴더 | 내용 |
|---|---|
| `Core/` | `SwingPhysics`, `SwingSettings`. Unity 물리를 쓰지 않는 순수 코드(점 질량 + 늘어나지 않는 줄). `attachPull`, `ratchetRope`는 기본 꺼짐이라 이 값을 켜지 않으면 순수 진자입니다 |
| `Viewer/` | `WebSwingMotionInput`(`IMotionInputSource`를 감싸 스윙 물리를 적용), `MouseWebShooter`(마우스 입력, 거미줄 그리기) |

`MouseWebShooter`는 `RuntimeInitializeOnLoadMethod(AfterSceneLoad)`로 스스로 설치되고, `ManhattanBootstrap`이 준비되면 다음을 합니다.

- `Flight.InputSource`를 `WebSwingMotionInput`으로 감쌉니다. 스윙이 꺼져 있는 동안은 기존 입력이 그대로 통과합니다.
- `Flight.TranslationConstraint = new CityCollisionConstraint(Origin)`
- `Streamer.CollisionProxiesEnabled = true` (거미줄이 맞을 충돌 프록시 로드, 레이어 30)

`-runTests` 인수가 있으면 설치하지 않습니다.

**주의:** 위 설정 때문에 자유 비행 중에도 충돌 프록시가 로드된 곳에서는 건물을 통과하지 못합니다. 기존 동작과 달라지는 부분입니다.

거미줄은 `WindowFrameMaterial`을 복제한 흰색 URP Lit 재질의 실린더와 구입니다(임시 표시). 기본 프리미티브 재질은 이 프로젝트 빌드에서 셰이더가 없어 분홍색으로 나왔습니다.

## 확인한 것과 못 한 것

확인함
- `main` `15f68d9`와 합친 상태에서 `tools/build.ps1 -AllowDirty`로 Windows 플레이어가 컴파일되고 빌드됩니다. 실행 로그에 예외가 없습니다.
- 병합 전(`e76e933` 기반) 빌드에서 작성자가 직접 실행해 써 보았고 동작한다고 했습니다. 항목별 확인 기록은 남기지 않았습니다.

못 함
- `15f68d9`와 합친 빌드에서 거미줄 상호작용을 자세히 다시 확인하지는 못했습니다.
- 스윙 물리 단위 테스트(EditMode 34개, PlayMode 7개)는 별도 작업 폴더에서 통과했지만, `attachPull`/`ratchetRope`를 추가한 뒤에는 다시 돌리지 않았고 이 PR에 포함하지 않았습니다.
- `tools/test.ps1`의 Unity 테스트는 돌리지 않았습니다.

## 알려진 한계

- 지면 높이를 `y = 0`으로 가정합니다(`SwingSettings.groundHeight`). 실제 도시 지형과 다르면 바닥 처리가 어긋날 수 있습니다.
- 충돌 프록시가 건물을 얼마나 덮는지에 따라 거미줄이 안 붙는 곳이 있을 수 있습니다. 사거리는 250 m입니다.
- 중력, 공기 저항, 끌려가는 속도(22 m/s) 등은 일반적인 값으로 정한 것이라 플레이하며 조정이 필요합니다.
- 거미줄 시각 효과는 임시입니다.

## 다음 단계

- 손목 장치(아두이노)로 같은 `WebSwingMotionInput.TryFire(Ray)`를 부르는 입력 컴포넌트는 별도 PR로 올릴 예정입니다.
