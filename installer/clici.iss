; clici installer — per-user, no admin. Compile via tools/Build-Installer.ps1.
#define AppName "clici"
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\artifacts\publish\win-x64\clici.exe"
#endif
#ifndef Rid
  #define Rid "win-x64"
#endif

[Setup]
; Stable AppId — never change it, or upgrades/uninstall will not track.
AppId={{B7A6E4C2-1F3D-4E8A-9C5B-8D2E1A6F4B90}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=clici
DefaultDirName={localappdata}\Programs\clici
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts\installer
OutputBaseFilename=clici-{#AppVersion}-{#Rid}-setup
SetupIconFile=..\src\clici.App\Assets\clici.ico
UninstallDisplayIcon={app}\clici.exe
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Detect a running instance through clici's own SingleInstanceGuard mutex.
; AppMutex + CloseApplications alone CANNOT close clici's hidden-window tray app
; (verified by the build-task spike: a re-install while running aborted with
; exit code 1), so the [Code] section below force-closes clici.exe before
; install/uninstall. clici's config writes are atomic (temp file + move), so a
; forced stop cannot corrupt config — worst case is a lost pending edit.
AppMutex=Local\clici
CloseApplications=yes
CloseApplicationsFilter=clici.exe

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\clici"; Filename: "{app}\clici.exe"; WorkingDir: "{app}"

[Tasks]
Name: "startup"; Description: "Start clici when I sign in"; GroupDescription: "Startup:"

[Registry]
; Value name and quoting MUST match StartupRegistration (value 'clici' = quoted exe path).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "clici"; ValueData: """{app}\clici.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\clici.exe"; Description: "Launch clici now"; Flags: nowait postinstall skipifsilent

[Code]
// AppMutex + CloseApplications cannot close clici's hidden-window tray app, so
// force-close any running clici.exe before install and before uninstall. Runs
// early (InitializeSetup/InitializeUninstall) so the file is unlocked in time.
procedure StopRunningClici;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/IM clici.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function InitializeSetup(): Boolean;
begin
  StopRunningClici;
  Result := True;
end;

function InitializeUninstall(): Boolean;
begin
  StopRunningClici;
  Result := True;
end;
