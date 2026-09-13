; Compiled only through scripts/Build-Installer.ps1 from a verified inventory.
#include "Payload.iss"

[Setup]
AppId={{B72CEDF4-8B83-4B69-9E63-F90C6F54AA31}
AppName={#DisplayName}
AppVersion={#AppVersion}
AppPublisher=WidgetRail
DefaultDirName={localappdata}\Programs\WidgetRail
DisableDirPage=yes
UsePreviousAppDir=no
UsePreviousTasks=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDirectory}
OutputBaseFilename={#InstallerName}
UninstallDisplayIcon={app}\versions\{#PayloadId}\OverlayHost.exe
UninstallDisplayName=WidgetRail
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupMutex=Local\WidgetRail.Setup
AppMutex=Local\WidgetRail.OverlayHost.Running
CloseApplications=no
RestartApplications=no
DisableProgramGroupPage=yes
SetupLogging=yes

[Tasks]
Name: startup; Description: "Start WidgetRail when I sign in (starts quietly; Windows Startup Apps restrictions still apply)"; Flags: unchecked

[Icons]
Name: "{userprograms}\WidgetRail"; Filename: "{app}\versions\{#PayloadId}\OverlayHost.exe"; Parameters: "--show"; WorkingDir: "{app}\versions\{#PayloadId}"

[Registry]
Root: HKCU; Subkey: "Software\WidgetRail\Installation"; ValueType: string; ValueName: "ApplicationRoot"; ValueData: "{app}\versions\{#PayloadId}"
Root: HKCU; Subkey: "Software\WidgetRail\Installation"; ValueType: string; ValueName: "Edition"; ValueData: "{#Edition}"

[Code]
const
  InstallKey = 'Software\WidgetRail\Installation';
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
var
  PreviousRoot: String;
  PreviousCommand: String;
  ForeignStartup: Boolean;
  StartupWasEnabled: Boolean;
  StartupSelectionInitialized: Boolean;
  DeleteUserData: Boolean;
  OwnsInstallation: Boolean;

#include "UninstallData.iss"

function ApplicationRoot: String;
begin
  Result := ExpandConstant('{app}\versions\{#PayloadId}');
end;

function StartupCommand(Root: String): String;
begin
  Result := '"' + Root + '\OverlayHost.exe"';
end;

function CompatibleGameInputFile(Path: String): Boolean;
var
  MS, LS: Cardinal;
begin
  Result := GetVersionNumbers(Path, MS, LS) and
    ((MS > {#GameInputVersionMS}) or ((MS = {#GameInputVersionMS}) and (LS >= {#GameInputVersionLS})));
end;

function HasGameInput: Boolean;
var
  Directory: String;
begin
  Result := CompatibleGameInputFile(ExpandConstant('{sys}\GameInputRedist.dll')) or
    CompatibleGameInputFile(ExpandConstant('{sys}\GameInput.dll'));
  if not Result and RegQueryStringValue(HKLM32, 'SOFTWARE\Microsoft\GameInput', 'RedistDir', Directory) then
    Result := CompatibleGameInputFile(AddBackslash(Directory) + 'GameInputRedist.dll');
end;

function HasWebView2: Boolean;
var
  Version: String;
  Key: String;
begin
  Key := 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  Result := (RegQueryStringValue(HKCU, Key, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKLM32, Key, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKLM64, Key, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;

procedure InitializeWizard;
var
  Current: String;
begin
  RegQueryStringValue(HKCU, InstallKey, 'ApplicationRoot', PreviousRoot);
  PreviousCommand := StartupCommand(PreviousRoot);
  ForeignStartup := RegValueExists(HKCU, RunKey, 'WidgetRail') and
    ((not RegQueryStringValue(HKCU, RunKey, 'WidgetRail', Current)) or
    (PreviousRoot = '') or (CompareText(Current, PreviousCommand) <> 0));
  StartupWasEnabled := (PreviousRoot <> '') and not ForeignStartup and
    RegQueryStringValue(HKCU, RunKey, 'WidgetRail', Current);
  WizardForm.WelcomeLabel2.Caption :=
    'Install WidgetRail for your Windows account. Your widgets and settings are kept when upgrading or changing edition.' + #13#10#13#10 +
    '.NET is included. Setup installs Microsoft GameInput and WebView2 if needed. GameInput may request administrator approval; WebView2 may need an internet connection. Exclusive controller drivers remain optional and are not installed here.';
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID <> wpSelectTasks then Exit;
  { Inno populates tasks on page entry, after InitializeWizard. Restore the
    default only once so Back/Next navigation preserves the user's choice. }
  if not StartupSelectionInitialized then begin
    if StartupWasEnabled then
      WizardSelectTasks('startup');
    StartupSelectionInitialized := True;
  end;
  { This page contains only startup. Preserve registrations owned elsewhere. }
  if ForeignStartup then
    WizardSelectTasks('!startup');
  WizardForm.TasksList.Enabled := not ForeignStartup;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo,
  MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + NewLine + MemoTasksInfo + NewLine + NewLine +
    'Microsoft components:' + NewLine + Space + '.NET is included with WidgetRail.';
  if not HasGameInput then
    Result := Result + NewLine + Space + 'Install GameInput (Windows will ask for administrator approval).' +
      NewLine + Space + 'If its update gets stuck, restart your PC normally, then run WidgetRail setup again.';
  if not HasWebView2 then
    Result := Result + NewLine + Space + 'Download and install WebView2 (internet connection needed).';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
  GameInputLog, LogArguments: String;
begin
  Result := '';
  if FindWindowByClassName('WidgetRail.OverlayHost') <> 0 then begin
    Result := 'Quit WidgetRail from its Settings header, then try again. Setup will not force it to close.';
    Exit;
  end;
  try
    if not HasGameInput then begin
      ExtractTemporaryFile('GameInputRedist.msi');
      LogArguments := '';
      GameInputLog := ExpandConstant('{localappdata}\WidgetRail\logs\GameInput-setup.log');
      if ForceDirectories(ExtractFileDir(GameInputLog)) then begin
        LogArguments := ' /L*v "' + GameInputLog + '"';
        Log('GameInput installer log: ' + GameInputLog);
      end else
        Log('Could not prepare GameInput log directory; continuing without the additional log.');
      if not ShellExec('runas', ExpandConstant('{sys}\msiexec.exe'),
        '/i "' + ExpandConstant('{tmp}\GameInputRedist.msi') + '" /passive /norestart' + LogArguments,
        '', SW_SHOWNORMAL, ewWaitUntilTerminated, Code) then begin
        Result := 'GameInput could not be installed. Allow its Windows approval prompt, then try again.';
        Exit;
      end;
      Log('GameInput installer exit code: ' + IntToStr(Code));
      if (Code = 3010) or (Code = 1641) then begin
        NeedsRestart := True;
        Result := 'GameInput needs a Windows restart. Restart your PC, then run WidgetRail setup again.';
        Exit;
      end;
      if (Code <> 0) or not HasGameInput then begin
        if Code = 1602 then
          Result := 'GameInput installation was cancelled. Retry when you are ready, or cancel WidgetRail setup.'
        else if Code = 1618 then
          Result := 'Another Windows installation is running. Wait for it to finish, then retry, or cancel WidgetRail setup.'
        else if Code = 1622 then
          Result := 'GameInput could not write its setup log. Check your free disk space and access to your user folder, then retry.'
        else
          Result := 'GameInput could not finish updating. Restart your PC, then run WidgetRail setup again. You can cancel setup now.';
        Exit;
      end;
    end;
    if not HasWebView2 then begin
      ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
      if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'), '/silent /install',
          '', SW_HIDE, ewWaitUntilTerminated, Code) then begin
        Result := 'WebView2 could not start installing. Please try again.';
        Exit;
      end;
      if not HasWebView2 then begin
        Result := 'WebView2 could not finish installing. Check your internet connection, then try again.';
        Exit;
      end;
    end;
  except
    Result := 'A required Microsoft component could not be prepared. Run setup again to retry.';
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Current: String;
  Owned: Boolean;
begin
  if CurStep <> ssPostInstall then Exit;
  Owned := not RegValueExists(HKCU, RunKey, 'WidgetRail');
  if RegQueryStringValue(HKCU, RunKey, 'WidgetRail', Current) then
    Owned := ((PreviousRoot <> '') and (CompareText(Current, PreviousCommand) = 0)) or
      (CompareText(Current, StartupCommand(ApplicationRoot)) = 0);
  if Owned and not ForeignStartup then begin
    if WizardIsTaskSelected('startup') then begin
      if not RegWriteStringValue(HKCU, RunKey, 'WidgetRail', StartupCommand(ApplicationRoot)) then
        MsgBox('WidgetRail was installed, but startup could not be enabled. Try again from WidgetRail Settings.', mbError, MB_OK);
    end else if not RegDeleteValue(HKCU, RunKey, 'WidgetRail') then
      Log('Startup entry was absent or could not be removed.');
  end;
  { Never write or delete Windows-owned StartupApproved values. }
end;

function InitializeUninstall: Boolean;
var
  Choice: Integer;
begin
  if CheckForMutexes('Local\WidgetRail.Setup') then begin
    MsgBox('Another WidgetRail setup is open. Close it before uninstalling.', mbError, MB_OK);
    Result := False;
    Exit;
  end;
  { Unlike SetupMutex, this also covers uninstall's entire file-removal phase. }
  CreateMutex('Local\WidgetRail.Setup');
  Result := not CheckForMutexes('Local\WidgetRail.OverlayHost.Running') and
    (FindWindowByClassName('WidgetRail.OverlayHost') = 0);
  if not Result then
    MsgBox('Quit WidgetRail from its Settings header before uninstalling.', mbError, MB_OK);
  if not Result then Exit;
  { Silent uninstall preserves data and never treats suppressed UI as consent. }
  if UninstallSilent then Exit;
  Choice := MsgBox('Also delete all WidgetRail data for this Windows account?' + #13#10#13#10 +
    'This includes installed widgets, settings, sign-ins, caches, logs and isolated-widget data, including data shared with development copies.' + #13#10#13#10 +
    'Yes: permanently delete all data.' + #13#10 +
    'No (recommended): keep data for reinstalling.' + #13#10 +
    'Cancel: do not uninstall.',
    mbConfirmation, MB_YESNOCANCEL or MB_DEFBUTTON2);
  DeleteUserData := Choice = IDYES;
  Result := Choice <> IDCANCEL;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Root, Current: String;
begin
  if (CurUninstallStep = usDone) and OwnsInstallation and
     not DeleteUserData and not UninstallSilent then begin
    MsgBox('Your WidgetRail data was kept.' + #13#10#13#10 +
      'To remove saved data manually later, press Win+R, enter %LOCALAPPDATA%, and delete only the WidgetRail folder. You can also delete the WidgetRail folder inside %TEMP%.' + #13#10#13#10 +
      'For complete cleanup of Windows sandbox profiles too, reinstall WidgetRail and choose Yes to delete all data when uninstalling.', mbInformation, MB_OK);
    Exit;
  end;
  if CurUninstallStep = usPostUninstall then begin
    if DeleteUserData and OwnsInstallation then begin
      try
        if not RemoveAllWidgetRailData then
          MsgBox('WidgetRail was uninstalled, but some data could not be removed. Close applications using that data and retry cleanup. Saved data is in %LOCALAPPDATA%\WidgetRail and temporary files are in %TEMP%\WidgetRail.', mbError, MB_OK);
      except
        Log('WidgetRail data cleanup failed: ' + GetExceptionMessage);
        MsgBox('WidgetRail was uninstalled, but some data could not be removed. Close applications using that data and retry cleanup. Saved data is in %LOCALAPPDATA%\WidgetRail and temporary files are in %TEMP%\WidgetRail.', mbError, MB_OK);
      end;
    end;
    Exit;
  end;
  if CurUninstallStep <> usUninstall then Exit;
  if RegQueryStringValue(HKCU, InstallKey, 'ApplicationRoot', Root) and
     (CompareText(Root, ApplicationRoot) = 0) then begin
    OwnsInstallation := True;
    if RegQueryStringValue(HKCU, RunKey, 'WidgetRail', Current) and
       (CompareText(Current, StartupCommand(Root)) = 0) then
      RegDeleteValue(HKCU, RunKey, 'WidgetRail');
    RegDeleteValue(HKCU, InstallKey, 'ApplicationRoot');
    RegDeleteValue(HKCU, InstallKey, 'Edition');
    RegDeleteKeyIfEmpty(HKCU, InstallKey);
    RegDeleteKeyIfEmpty(HKCU, 'Software\WidgetRail');
  end;
  { Inno removes logged payloads; separate data cleanup requires the opt-in above. }
end;
