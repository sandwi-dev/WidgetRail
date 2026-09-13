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
Name: startup; Description: "Start WidgetRail when I sign in"; Flags: unchecked

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

function ApplicationRoot: String;
begin
  Result := ExpandConstant('{app}\versions\{#PayloadId}');
end;

function StartupCommand(Root: String): String;
begin
  Result := '"' + Root + '\OverlayHost.exe"';
end;

function HasNet8: Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetValueNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App', Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Pos('8.0.', Names[I]) = 1 then Result := True;
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
  if (PreviousRoot <> '') and not ForeignStartup and
     RegQueryStringValue(HKCU, RunKey, 'WidgetRail', Current) then
    WizardSelectTasks('startup');
  if ForeignStartup then begin
    WizardForm.TasksList.ItemEnabled[0] := False;
    WizardSelectTasks('!startup');
  end;
  WizardForm.WelcomeLabel2.Caption :=
    'Install WidgetRail for your Windows account. Your widgets and settings are kept when upgrading or changing edition.' + #13#10#13#10 +
    'Requires the Microsoft .NET 8 x64 runtime and Microsoft GameInput. Web media also needs Microsoft Edge WebView2. Controller isolation drivers are optional and are not installed by this setup.';
  WizardForm.TasksList.ItemCaption[0] := 'Start WidgetRail when I sign in (starts quietly; Windows Startup Apps restrictions still apply)';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if FindWindowByClassName('WidgetRail.OverlayHost') <> 0 then
    Result := 'Quit WidgetRail from its Settings header, then try again. Setup will not force it to close.'
  else if not HasNet8 then
    Result := 'Install the Microsoft .NET 8 runtime for Windows x64, then try again: https://dotnet.microsoft.com/download/dotnet/8.0'
  else if not FileExists(ExpandConstant('{sys}\GameInput.dll')) then
    Result := 'Microsoft GameInput is required. Install Microsoft GameInput from https://aka.ms/gameinput and then try again.';
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
begin
  Result := FindWindowByClassName('WidgetRail.OverlayHost') = 0;
  if not Result then
    MsgBox('Quit WidgetRail from its Settings header before uninstalling. Your widgets and settings will be kept.', mbError, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Root, Current: String;
begin
  if CurUninstallStep <> usUninstall then Exit;
  if RegQueryStringValue(HKCU, InstallKey, 'ApplicationRoot', Root) and
     (CompareText(Root, ApplicationRoot) = 0) then begin
    if RegQueryStringValue(HKCU, RunKey, 'WidgetRail', Current) and
       (CompareText(Current, StartupCommand(Root)) = 0) then
      RegDeleteValue(HKCU, RunKey, 'WidgetRail');
    RegDeleteValue(HKCU, InstallKey, 'ApplicationRoot');
    RegDeleteValue(HKCU, InstallKey, 'Edition');
    RegDeleteKeyIfEmpty(HKCU, InstallKey);
  end;
  { Inno removes only logged installation files. Never delete the user data root. }
end;
