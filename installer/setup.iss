; Inno Setup script for VulcansLivingWP — free live wallpaper for Windows 10/11.
; Build with: ISCC.exe installer\setup.iss

#define AppName "VulcansLivingWP"
#define AppVersion "1.2.3"
#define AppPublisher "VulcansLivingWP"
#define AppExe "VulcansLivingWP.exe"
; The project folder keeps its original name; only the executable was renamed.
#define BuildDir "..\src\FluxWall\bin\Release\net48"

[Setup]
AppId={{D9A6C9EA-7B4F-4D9E-8B6A-0F17E4B7E58A}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
; The 1.0.x/1.1.x releases live in the 32-bit Program Files folder; this
; installer runs in 32-bit mode, so {autopf} resolves to that same folder and
; an existing install is upgraded in place.
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayName={#AppName} (Free Live Wallpaper)
UninstallDisplayIcon={app}\{#AppExe}
OutputBaseFilename=VulcansLivingWP_Setup
OutputDir=out
SetupIconFile=app.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Admin by default so the app lands in C:\Program Files (x86)\VulcansLivingWP.
; /CURRENTUSER is allowed so the installer can also be used without elevation
; (and so the automated tests can install into a scratch folder).
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline dialog
MinVersion=6.3
ArchitecturesAllowed=x64compatible
AllowNoIcons=yes
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; \
  GroupDescription: "Additional icons:"; Flags: unchecked
Name: "autostart"; Description: "Start VulcansLivingWP when Windows &starts (recommended for animated wallpapers)"; \
  GroupDescription: "Startup:"

[Files]
; Remove the pre-rename executable from an older install before copying ours.
Source: "{#BuildDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\{#AppExe}.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "app.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion; DestName: "README.md"

[InstallDelete]
; 1.0.x shipped FluxWall.exe and 1.1.x could leave a second VulcansLivingWP.exe
; in the original folder. Leaving either behind means an old pinned shortcut
; can still launch the pre-1.2 UI (without automatic switching). StopApp runs
; before this section, so neither executable is locked.
Type: files; Name: "{app}\FluxWall.exe"
Type: files; Name: "{app}\FluxWall.exe.config"
Type: files; Name: "{app}\{#AppExe}"
Type: files; Name: "{app}\{#AppExe}.config"
Type: files; Name: "{app}\README.txt"

[Icons]
Name: "{group}\VulcansLivingWP"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall VulcansLivingWP"; Filename: "{uninstallexe}"
Name: "{autodesktop}\VulcansLivingWP"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Apply the installer's choices silently, then optionally start the app.
Filename: "{app}\{#AppExe}"; Parameters: "--settray=on --setrestore=on {code:StartupSwitch}"; \
  Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExe}"; Description: "Launch VulcansLivingWP now"; \
  Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\probe-*.png"

[Code]
const
  AppMutexName = 'VulcansLivingWP-SingleInstance';
  LegacyMutexName = 'FluxWall-SingleInstance';

function StartupSwitch(Param: String): String;
begin
  if WizardIsTaskSelected('autostart') then
    Result := '--setstartup=on'
  else
    Result := '--setstartup=off';
end;

procedure SetRunValue(const Value: String);
var
  Key: String;
begin
  Key := 'Software\Microsoft\Windows\CurrentVersion\Run';
  if Value <> '' then
    RegWriteStringValue(HKEY_CURRENT_USER, Key, 'VulcansLivingWP', Value)
  else if RegValueExists(HKEY_CURRENT_USER, Key, 'VulcansLivingWP') then
    RegDeleteValue(HKEY_CURRENT_USER, Key, 'VulcansLivingWP');
end;

{ True while any VulcansLivingWP instance is running.

  The current app and the pre-rename 1.0.x app use different named mutexes
  (VulcansLivingWP-SingleInstance and FluxWall-SingleInstance). Checking only
  the new name made Setup believe nothing was running while the old app was
  still holding FluxWall.exe open, so the old executable survived the upgrade.
  The kernel answers both names directly; no tasklist shelling is involved. }
function AnyInstanceRunning(): Boolean;
begin
  Result := CheckForMutexes(AppMutexName + ' ' + LegacyMutexName);
end;

function LegacyInstanceRunning(): Boolean;
begin
  Result := CheckForMutexes(LegacyMutexName);
end;

procedure KillImage(const ImageName: String);
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'),
       '/C taskkill /IM ' + ImageName + ' /T /F >nul 2>&1',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Log('taskkill ' + ImageName + ' exit code = ' + IntToStr(ResultCode));
end;

function WaitForNoInstances(Milliseconds: Integer): Boolean;
var
  I: Integer;
begin
  Result := not AnyInstanceRunning();
  for I := 0 to (Milliseconds div 500) - 1 do
  begin
    if Result then
      Break;
    Sleep(500);
    Result := not AnyInstanceRunning();
  end;
end;

{ Stops the app without ever starting its user interface.

  Older builds were stopped with "VulcansLivingWP.exe --stopwall", which is the
  normal application: it opened the main window first and, when an instance was
  already running, only displayed the "already running" message box. The
  --shutdown switch is handled before any window or mutex, signals the running
  instance and waits for it to exit. }
function StopApp(const AppDir: String): Boolean;
var
  Exe: String;
  ResultCode: Integer;
begin
  Exe := AppDir + '\{#AppExe}';
  Result := True;

  { 0. 1.0.x predates --shutdown and uses its own mutex. Terminate it before
       the install-delete step tries to remove FluxWall.exe. Without this the
       locked old executable survives the upgrade, and an old pinned shortcut
       keeps opening the 1.0.x UI, which has no automatic-switching settings. }
  if LegacyInstanceRunning() then
  begin
    Log('StopApp: pre-rename FluxWall.exe is running; terminating it.');
    KillImage('FluxWall.exe');
    WaitForNoInstances(12000);
  end;

  if not AnyInstanceRunning() then
  begin
    Log('StopApp: nothing running, cleaning the Run entry only.');
    SetRunValue('');
    Exit;
  end;

  { 1. Ask the running instance to exit through its dedicated channel. This
       never creates a window and never enters the normal startup path. }
  ResultCode := -1;
  if FileExists(Exe) then
  begin
    Exec(Exe, '--shutdown', AppDir, SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Log('StopApp: --shutdown helper exit code = ' + IntToStr(ResultCode));
  end
  else
    Log('StopApp: executable missing from ' + AppDir);

  { 2. Give it up to 12 seconds to release the mutex (it stops the wallpaper and
       saves settings first). }
  Result := WaitForNoInstances(12000);

  { 3. Last resort: terminate only our own executables, after the clean request. }
  if not Result then
  begin
    Log('StopApp: clean shutdown timed out, terminating the process.');
    KillImage('{#AppExe}');
    KillImage('FluxWall.exe');
    Result := WaitForNoInstances(6000);
  end;
  if Result then
    Log('StopApp: finished, nothing is running any more.')
  else
    Log('StopApp: finished, the process is still running.');
end;

procedure RemoveLegacyExecutables();
var
  LegacyDir: String;
begin
  { A machine-wide 1.0.x/1.1.x install always lives in the 32-bit Program Files
    folder. If the user installs to a different folder, remove the old
    executables there so an old pinned shortcut cannot launch the old UI. Skip
    this for per-user installs: they must never delete a machine-wide copy. }
  if not IsAdminInstallMode() then
    Exit;

  LegacyDir := ExpandConstant('{commonpf32}\{#AppName}');
  if CompareText(LegacyDir, ExpandConstant('{app}')) = 0 then
    Exit;

  if DeleteFile(LegacyDir + '\{#AppExe}') then
    Log('Removed legacy executable ' + LegacyDir + '\{#AppExe}');
  if DeleteFile(LegacyDir + '\{#AppExe}.config') then
    Log('Removed legacy config ' + LegacyDir + '\{#AppExe}.config');
  if DeleteFile(LegacyDir + '\FluxWall.exe') then
    Log('Removed pre-rename executable ' + LegacyDir + '\FluxWall.exe');
  if DeleteFile(LegacyDir + '\FluxWall.exe.config') then
    Log('Removed pre-rename config ' + LegacyDir + '\FluxWall.exe.config');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  { Upgrade while the app is running (often minimised to the tray). Stop it
    before any file is replaced. }
  if not StopApp(ExpandConstant('{app}')) then
    Result := 'VulcansLivingWP is still running and could not be stopped. ' +
              'Please close it from the system tray and run Setup again.'
  else
    RemoveLegacyExecutables();
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if not WizardIsTaskSelected('autostart') then
      SetRunValue('');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    { Runs before any file is removed, and never launches the application. }
    if not StopApp(ExpandConstant('{app}')) then
      MsgBox('VulcansLivingWP could not be closed automatically. ' +
             'Some files may remain until the process is ended from Task Manager.',
             mbError, MB_OK);
    SetRunValue('');
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    if DirExists(ExpandConstant('{localappdata}\VulcansLivingWP')) and
       (MsgBox('Delete VulcansLivingWP settings, cached thumbnails and logs?' + #13#10 +
                'Your original video files are never touched.',
                mbConfirmation, MB_YESNO) = IDYES) then
      DelTree(ExpandConstant('{localappdata}\VulcansLivingWP'), True, True, True);
  end;
end;
