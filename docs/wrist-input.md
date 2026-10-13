# 손목 장치 입력 (조준과 발사)

손목에 차는 장치(아두이노 + 자이로 센서)가 보내는 `yaw,pitch,fire` 값을 받아 **조준 레이**와 **발사/재보정 이벤트**로 바꾸는 컴포넌트입니다.
이동용 `IMotionInputSource`와는 별개입니다. 이 PR은 입력 쪽만 담고, 이동과 거미줄 연결은 포함하지 않습니다.

## 구성

`ManhattanViewer/Assets/WebShooter/Input/` (기존 파일은 수정하지 않았습니다. `HeadTracked.Display` 패키지만 참조합니다)

| 파일 | 역할 |
|---|---|
| `Runtime/WristSource.cs` | 수신 공통 기반과 한 줄 해석(`WristProtocol`). 깨진 줄은 버립니다 |
| `Runtime/WristUdpReceiver.cs` | UDP(`127.0.0.1:8766`)로 받습니다. **기본 경로** |
| `Runtime/WristSerialReceiver.cs` | 시리얼/블루투스 COM을 직접 읽습니다. API 호환 수준이 .NET Framework일 때만 컴파일됩니다. 이 프로젝트는 .NET Standard 2.1이라 현재는 **컴파일에서 빠집니다** |
| `Runtime/HandAimController.cs` | 보정, 각도에서 화면 좌표로 변환, 눈에서 화면 점을 지나는 레이, `Fired`/`Recentered` 이벤트 |
| `Tests/EditMode`, `Tests/PlayMode` | 파서, 매핑, 레이 기하, UDP 전체 경로 테스트 |
| `tools/fake_wrist.py` | 장치 없이 테스트하는 가짜 송신기 (표준 라이브러리만 사용) |

## 전송 형식

한 줄에 `yaw,pitch,fire`, UDP 텍스트입니다. 예: `12.5,-3.0,0`

- `yaw`: 오른쪽이 +, `pitch`: 위가 + (도). 범위를 벗어나거나 깨진 줄은 버립니다.
- `fire`: `0` 없음, `1` 발사, `2` 재보정(센서를 2초 이상 가림).

## 사용 방법

1. 장면의 오브젝트에 `WristUdpReceiver`와 `HandAimController`를 추가합니다.
2. `HandAimController`의 `Source`에 `WristUdpReceiver`를, `Display`에 카메라의 `HeadTrackedDisplay`를 지정합니다. 코드에서는 `aim.Configure(source, display)`이고, `ManhattanBootstrap.Display`로 얻을 수 있습니다.
3. 이벤트를 연결합니다.
   ```csharp
   aim.Fired += ray => { /* ray로 Physics.Raycast 등 */ };
   aim.Recentered += () => { };
   ```

장치 없이 시험하려면 따로 터미널에서 `python tools/fake_wrist.py`를 실행합니다. 손이 흔들리고 3초마다 발사하며, `--recenter-at 20`이면 20초에 재보정합니다.

## 동작 규칙

- 데이터가 처음 도착하고 `Start Delay Seconds`(기본 3초)가 지난 뒤의 손 방향이 화면 중앙입니다. 그동안 손을 화면 중앙으로 향합니다.
- 보정 전의 `fire`는 무시합니다.
- 좌우 ±`Yaw Range`도, 상하 ±`Pitch Range`도가 화면 끝입니다.
- 조준 레이: `Display`가 있으면 **눈 위치에서 화면 위의 조준점을 지나는 직선**이라 눈이 움직이면 같은 화면 점도 다른 방향이 됩니다. `Display`를 비우면 일반 카메라 뷰포트 레이를 씁니다.

## 확인한 것과 못 한 것

확인함 (Unity 6000.3.22f1, 이 저장소의 `main` `15f68d9` 위, `unity test --filter WebShooter.Input`)
- EditMode 19개 중 19개 통과.
- PlayMode 3개 중 3개 통과. 실제 UDP 소켓, 워커 스레드, `Update` 루프로 수신, 보정, 조준, 발사, 재보정을 확인합니다. 보내는 쪽은 테스트 코드입니다.
- 이 확인은 `codex/web-swing` PR(#12)의 코드가 같이 들어 있는 프로젝트에서 했습니다. `WebShooter.Input`은 그 코드를 참조하지 않지만, 이 브랜치만 따로 빌드해 본 것은 아닙니다.

못 함
- 실제 아두이노나 블루투스 연결. 테스트는 가짜 송신기와 테스트 코드가 보낸 UDP 줄만 썼습니다.
- 웹캠 머리 추적과 함께 쓰는 실제 화면. 레이 기하는 `HeadTrackedDisplay`의 눈/화면 규약을 코드로 읽고 단위 테스트했습니다.
- `WristSerialReceiver`. 컴파일에서 빠지고, .NET Framework로 바꿔 컴파일해 본 적도 없습니다.
- 장면에 연결되는 곳이 아직 없습니다. 이 PR만으로는 아무 동작도 바뀌지 않습니다.
- 고정 벤치마크는 돌리지 않았습니다. 이 코드는 컴포넌트를 장면에 붙이기 전에는 실행되지 않습니다.

## 이 PR에 포함하지 않은 것

- 아두이노 펌웨어와 PC 쪽 시리얼/블루투스를 UDP로 넘기는 브리지(`wrist_bridge.py`)는 작성자의 별도 프로젝트에 있고, 그 프로젝트의 `webshooter.py`에 의존해서 단독 실행이 되지 않아 포함하지 않았습니다. 위 전송 형식으로 보내면 어떤 송신기든 쓸 수 있습니다.
- 손목 입력으로 거미줄을 쏘는 연결(`HandAimController.Fired`에서 `WebSwingMotionInput.TryFire(Ray)` 호출)은 거미줄 스윙 PR(#12)이 병합된 뒤 별도 PR로 올릴 예정입니다.
