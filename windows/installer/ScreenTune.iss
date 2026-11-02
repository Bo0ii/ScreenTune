; Per-user installer for ScreenTune for Windows (Inno Setup 6). No admin rights needed.
; CI builds it from the self-contained publish folder:
;   ISCC.exe /DAppVersion=1.0.N /DSourceDir=<publish folder> /O<out dir> ScreenTune.iss
; It installs where tools/publish.ps1 -Install does, so it upgrades either kind of copy.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\win-x64"
#endif

[Setup]
AppId={{D3B7C9E2-6A4F-4F5B-9C1E-2B8A7F0E5D41}
AppName=ScreenTune
AppVersion={#AppVersion}
AppVerName=ScreenTune {#AppVersion}
AppPublisher=Amr Ahmed
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\ScreenTune
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputBaseFilename=ScreenTune-Setup
SetupIconFile=..\src\ScreenTune\Assets\ScreenTune.ico
UninstallDisplayIcon={app}\ScreenTune.exe
UninstallDisplayName=ScreenTune
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; The running copy is quit in PrepareToInstall so it restores the screens first.
CloseApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[InstallDelete]
; Same as a mirror copy: files from an older build never linger next to the new ones.
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\ScreenTune"; Filename: "{app}\ScreenTune.exe"; Comment: "Per-screen color tuning"
Name: "{userdesktop}\ScreenTune"; Filename: "{app}\ScreenTune.exe"; Comment: "Per-screen color tuning"; Tasks: desktopicon

[Registry]
; Launch at startup is a Run value the app writes itself; remove it with the app.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "ScreenTune"; Flags: uninsdeletevalue dontcreatekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"; ValueType: none; ValueName: "ScreenTune"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\ScreenTune.exe"; Description: "Start ScreenTune"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
// "ScreenTune.exe --quit" asks the running copy to put every screen back and exit.
// Wait for its hidden window to go away so its files are free to replace.
procedure QuitRunningCopy();
var
  ResultCode, Waited: Integer;
begin
  if FindWindowByClassName('ScreenTune.SystemWindow') = 0 then Exit;
  if FileExists(ExpandConstant('{app}\ScreenTune.exe')) then
    Exec(ExpandConstant('{app}\ScreenTune.exe'), '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Waited := 0;
  while (FindWindowByClassName('ScreenTune.SystemWindow') <> 0) and (Waited < 5000) do
  begin
    Sleep(100);
    Waited := Waited + 100;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  QuitRunningCopy();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then QuitRunningCopy();
end;
