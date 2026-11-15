#ifndef MyAppVersion
  #define MyAppVersion "3.0.0"
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

[Tasks]
Name: "autostart"; Description: "Windows 로그인 시 자동 시작"; GroupDescription: "시작 옵션:"; Check: not IsTestMode
Name: "desktopicon"; Description: "바탕 화면 바로가기 만들기"; GroupDescription: "바로가기:"; Flags: unchecked; Check: not IsTestMode

[Files]
Source: "..\dist\*"; DestDir: "{app}"; Excludes: "settings.ini,crash.log,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "installed-mode.txt"; DestDir: "{app}"; DestName: "installed-mode"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\Glide"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"; Check: not IsTestMode
Name: "{userdesktop}\Glide"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"; Tasks: desktopicon; Check: not IsTestMode

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Glide"; ValueData: """{app}\{#MyAppExe}"" --tray"; Tasks: autostart; Check: not IsTestMode

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Glide 실행"; Flags: nowait postinstall skipifsilent; Check: not IsTestMode

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
    Result := '실행 중인 Glide를 트레이 메뉴에서 종료한 뒤 다시 시도해 주세요.';
end;

function InitializeUninstall: Boolean;
begin
  Result := StopGlide;
  if not Result then MsgBox('Glide를 트레이 메뉴에서 종료한 뒤 제거해 주세요.', mbError, MB_OK);
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
