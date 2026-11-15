#ifndef MyAppVersion
  #define MyAppVersion "3.0.1"
#endif
#define MyAppExe "ThreeFingerWindowDrag.exe"

[Setup]
AppId={code:GetAppId}
AppName=Glide
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
AppPublisher=Glide
AppVerName=Glide {#MyAppVersion}
DefaultDirName={localappdata}\Programs\Glide
DefaultGroupName=Glide
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\releases
OutputBaseFilename=Glide-{#MyAppVersion}-Setup
SetupIconFile=..\modern\Assets\Glide.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName=Glide
UninstallFilesDir={app}\uninstall
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
WizardResizable=yes
CloseApplications=yes
RestartApplications=no
UsePreviousTasks=yes
UsePreviousLanguage=no
SetupLogging=yes

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
korean.LaunchAtSignIn=Windows 로그인 시 자동 시작
english.LaunchAtSignIn=Launch Glide at Windows sign-in
korean.StartOptions=시작 옵션:
english.StartOptions=Startup options:
korean.DesktopShortcut=바탕 화면 바로가기 만들기
english.DesktopShortcut=Create a desktop shortcut
korean.ShortcutOptions=바로가기:
english.ShortcutOptions=Shortcuts:
korean.RunGlide=Glide 실행
english.RunGlide=Launch Glide
korean.QuitBeforeInstall=실행 중인 Glide를 트레이 메뉴에서 종료한 뒤 다시 시도해 주세요.
english.QuitBeforeInstall=Quit Glide from its tray menu, then try again.
korean.QuitBeforeUninstall=Glide를 트레이 메뉴에서 종료한 뒤 제거해 주세요.
english.QuitBeforeUninstall=Quit Glide from its tray menu before uninstalling.

[Tasks]
Name: "autostart"; Description: "{cm:LaunchAtSignIn}"; GroupDescription: "{cm:StartOptions}"; Check: not IsTestMode
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:ShortcutOptions}"; Flags: unchecked; Check: not IsTestMode

[Files]
Source: "..\dist\*"; DestDir: "{app}"; Excludes: "settings.ini,crash.log,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "installed-mode.txt"; DestDir: "{app}"; DestName: "installed-mode"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\Glide"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"; Check: not IsTestMode
Name: "{userdesktop}\Glide"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"; Tasks: desktopicon; Check: not IsTestMode

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Glide"; ValueData: """{app}\{#MyAppExe}"" --tray"; Tasks: autostart; Check: not IsTestMode

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "{cm:RunGlide}"; Flags: nowait postinstall skipifsilent; Check: not IsTestMode

[Code]
const RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

function IsTestMode: Boolean;
begin
  Result := ExpandConstant('{param:TESTMODE|0}') = '1';
end;

function GetAppId(Param: String): String;
begin
  Result := 'Glide.ThreeFingerWindowDrag';
  if IsTestMode then Result := Result + '.InstallerTest';
end;

function StopGlide: Boolean;
var Wnd: HWND; I: Integer;
begin
  Result := True;
  if IsTestMode then Exit;
  if not CheckForMutexes('Local\ThreeFingerWindowDrag.1') then Exit;
  Wnd := FindWindowByWindowName('Glide · 세 손가락 창 이동');
  if Wnd = 0 then Wnd := FindWindowByWindowName('Glide · Three-finger window control');
  if Wnd <> 0 then PostMessage(Wnd, $8000 + 28, 0, 0);
  for I := 1 to 50 do begin
    if not CheckForMutexes('Local\ThreeFingerWindowDrag.1') then Exit;
    Sleep(100);
  end;
  Result := False;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not StopGlide then
    Result := CustomMessage('QuitBeforeInstall');
end;

function InitializeUninstall: Boolean;
begin
  Result := StopGlide;
  if not Result then MsgBox(CustomMessage('QuitBeforeUninstall'), mbError, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Existing, OwnedCommand: String;
begin
  if (CurUninstallStep = usUninstall) and not IsTestMode then begin
    OwnedCommand := '"' + ExpandConstant('{app}\{#MyAppExe}') + '" --tray';
    if RegQueryStringValue(HKCU, RunKey, 'Glide', Existing) and
       (CompareText(Existing, OwnedCommand) = 0) then
      RegDeleteValue(HKCU, RunKey, 'Glide');
  end;
end;
