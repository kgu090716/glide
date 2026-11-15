# 세 손가락 창 이동

터치패드에서 세 손가락을 움직이면 **커서 아래에 있는 창을 본문 어디서든 이동**하는 Windows 프로그램입니다. 터치패드를 누르거나 제목 표시줄을 잡을 필요가 없습니다.

## 실행

1. `dist/ThreeFingerWindowDrag.exe`를 더블클릭합니다. 설치 과정은 없습니다.
2. 프로그램의 **터치패드 설정 열기**를 누릅니다.
3. Windows 설정의 **세 손가락 제스처**에서 **살짝 밀기(Swipes)**와 **탭(Taps)**을 모두 **아무 작업 안 함(Nothing)**으로 바꿉니다. Windows 버전에 따라 명칭이 조금 다를 수 있습니다.
4. 프로그램으로 돌아와 **위의 Windows 세 손가락 설정을 변경했습니다**를 체크합니다.
5. 이동할 창의 본문 위에 커서를 놓고, 세 손가락을 터치패드에 댄 채 움직입니다. 손가락을 떼면 이동이 끝납니다.

Windows 기본 세 손가락 제스처를 끄는 이유는 작업 보기, 앱 전환, 검색과의 충돌을 막기 위해서입니다. 프로그램은 Windows 설정이나 드라이버를 자동 변경하지 않습니다. 이 확인란은 사용자의 확인이며, 설정이 실제 변경되었는지를 자동 검사하지는 않습니다. 네 손가락 제스처는 계속 사용할 수 있습니다.

## 조작

- **Ctrl + Alt + F9**: 기능 켜기 / 일시정지. 다른 프로그램이 단축키를 먼저 사용하고 있으면 트레이 메뉴를 사용하세요.
- **Esc**: 현재 창 이동 중지. 손가락을 떼고 다시 대면 다시 이동할 수 있습니다.
- **이동 속도**: 터치패드 1mm 이동당 창이 움직이는 픽셀 수. 장치가 물리 크기를 보고하지 않으면 추정 크기를 사용하므로 실제 감각은 다를 수 있습니다.
- **최대화된 창은 원래 크기로 복원해서 이동**: 켜져 있으면 최대화 창도 이동 시 복원됩니다.
- **X 또는 트레이로 숨김**: 백그라운드에서 계속 실행합니다.
- **작업 표시줄의 숨겨진 아이콘 → 세 손가락 창 이동 → 종료**: 완전히 종료합니다.
- **다시 검색**: 연결이 바뀌었거나 절전에서 돌아온 후 장치를 다시 찾습니다.
- **진단 저장**: 장치 정보, 수신 보고서 수, 파서 오류를 텍스트로 저장합니다.

설정은 실행 파일 옆의 `settings.ini`에 저장됩니다. 처음 배포한 폴더처럼 쓰기 가능한 위치에서 실행하세요. 자동 시작은 등록하지 않습니다. 필요하면 실행 파일의 바로가기에 `--tray` 옵션을 붙여 트레이에서 시작할 수 있습니다.

## 지원 범위

- Windows 10 / 11, .NET Framework 4.5 이상. 이 PC에서는 .NET Framework 4.x 컴파일러로 빌드합니다.
- **Windows Precision Touchpad(정밀 터치패드)**가 Raw Input에 digitizer Usage Page `0x0D`, Usage `0x05` 접촉 데이터를 공개해야 합니다.
- 일반 마우스로만 보이는 구형 Synaptics/ELAN 전용 드라이버, 일부 제조사 장치, 원격 데스크톱이나 가상 머신에서 접촉 정보가 전달되지 않는 경우에는 사용할 수 없습니다. 단순히 세 손가락 제스처가 된다는 사실만으로 이 프로그램과의 호환성이 보장되지는 않습니다.
- 일반 창의 자식 컨트롤 위에서도 최상위 창을 선택합니다. 브라우저 본문, 편집기, 탐색기 등의 제목 표시줄 밖에서 사용할 수 있도록 구현했습니다.
- 바탕화면, 작업 표시줄, 메뉴, 테두리 없는 전체 화면은 이동 대상에서 제외합니다. 네 손가락, 손바닥 접촉, 실제 좌우 버튼 클릭 중에도 이동하지 않습니다.
- 관리자 권한으로 열린 앱은 권한 수준 때문에 이동하지 못할 수 있습니다. 필요한 경우에만 이 프로그램도 **관리자 권한으로 실행**하세요.
- 다중 모니터에서 창을 이동할 수 있으며, 제목 표시줄 일부를 다시 잡을 수 있도록 화면 밖 이동을 제한합니다. Windows 기본 Aero Snap 동작은 별도로 구현하지 않았습니다.

## 입력이 안 될 때

설정 창의 **정밀 터치패드 감지 수**, **인식된 손가락**, **수신 횟수**를 확인하세요. 터치패드를 사용할 때 수신 횟수가 늘고 세 손가락을 대었을 때 손가락 수가 3으로 표시되어야 합니다. 창을 이동할 때 손가락을 모두 고정해 미끄러뜨려 주세요. 이동 도중 손가락 구성이 바뀌면 오작동을 막기 위해 그 동작을 취소합니다.

장치가 없거나 수신 횟수가 계속 0이라면 **다시 검색**과 노트북 제조사의 정밀 터치패드 드라이버를 확인하세요. 진단 파일을 공유하면 해당 장치의 보고서 형식과 입력 여부를 확인할 수 있습니다. 진단 파일에는 장치 경로 식별자가 포함됩니다.

## 소스와 빌드

별도 패키지 다운로드 없이 Windows의 .NET Framework C# 컴파일러와 WinForms를 사용합니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

`src/Touchpad.cs`가 Raw Input 장치를 등록하고 `hid.dll`의 descriptor 기반 파서로 접촉 ID, X/Y, Tip, Confidence, Contact Count, Scan Time을 읽습니다. 병렬/하이브리드 보고서를 프레임으로 합친 뒤, `src/Gesture.cs`가 정확히 세 손가락의 평균 이동량을 계산합니다. `src/WindowMover.cs`는 첫 접촉 때 커서 아래 최상위 창을 고정하고, 작은 이동 임계값을 넘으면 `SetWindowPos`로 창을 이동합니다. 입력이 450ms 이상 끊기면 현재 이동을 취소합니다.

입력만 기록하는 진단 실행도 가능합니다. 아래 예시는 15초 동안 입력을 관찰하고 종료하며, 창은 움직이지 않습니다. 일반 앱을 완전히 종료한 뒤 실행하세요.

```powershell
$process = Start-Process -FilePath .\dist\ThreeFingerWindowDrag.exe -ArgumentList '--diagnose', 'diagnostic.txt', '15' -PassThru -Wait -WindowStyle Hidden
$process.ExitCode
```

진단 종료 코드: `0` 지원 장치 감지, `2` 지원 장치 없음, `1` 실행 오류. 경로에 공백이 있으면 인수를 적절히 인용하세요.

테스트는 보고서 디코딩, 하이브리드 프레임, 손가락 수와 ID 변화, 손바닥 배제, 취소/타임아웃, 누적 이동량, 다중 모니터 좌표 제한을 확인합니다. 자동 테스트 통과만으로 실제 노트북의 세 손가락 조작이 검증되는 것은 아닙니다. 물리 터치패드에서의 확인이 필요합니다.

## 참고 문서

- [Microsoft: Windows Precision Touchpad Collection](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/touchpad-windows-precision-touchpad-collection)
- [Microsoft: Using Raw Input](https://learn.microsoft.com/en-us/windows/win32/inputdev/using-raw-input)
- [Microsoft: HidP_GetData](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/nf-hidpi-hidp_getdata)
- [Microsoft: Touch gestures for Windows](https://support.microsoft.com/en-us/windows/hardware/input-devices/touch-gestures-for-windows)
