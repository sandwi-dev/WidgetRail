$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$installer = Get-Content -LiteralPath (Join-Path $repository 'eng/installer/WidgetRail.iss') -Raw
$startup = Get-Content -LiteralPath (Join-Path $repository 'src/PlatformSettings/StartupRegistration.cs') -Raw
$native = Get-Content -LiteralPath (Join-Path $repository 'src/OverlayHost/main.cpp') -Raw
function Require([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
foreach ($setting in @('PrivilegesRequired=lowest', 'UsePreviousTasks=no', 'CloseApplications=no',
    'RestartApplications=no', 'Flags: unchecked', 'AppMutex=Local\WidgetRail.OverlayHost.Running')) {
    Require ($installer.Contains($setting)) "Installer contract missing: $setting"
}
Require (!$installer.Contains('[InstallDelete]') -and !$installer.Contains('[UninstallDelete]') -and
    !$installer.Contains('DelTree(')) 'Installer must remove only logged installed files.'
Require ($installer.Contains('CompareText(Current, StartupCommand(Root)) = 0')) 'Uninstall must verify startup ownership.'
Require ($installer.Contains('not ForeignStartup') -and $installer.Contains('CompareText(Current, PreviousCommand) = 0')) 'Upgrade must preserve foreign entries.'
Require (!$installer.Contains("RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Explorer")) 'Do not bypass Windows startup approval.'
foreach ($key in @('Software\WidgetRail\Installation', 'Software\Microsoft\Windows\CurrentVersion\Run')) {
    Require ($installer.Contains($key) -and $startup.Contains($key)) 'Installer and Settings registration contract differs.'
}
Require ($native.Contains('Local\\WidgetRail.OverlayHost.Running') -and
    $native.Contains('Local\\WidgetRail.Setup')) 'Host must participate in installer lifetime protection.'
Require ($installer.Contains("CreateMutex('Local\WidgetRail.Setup')") -and
    $installer.Contains("not CheckForMutexes('Local\WidgetRail.OverlayHost.Running')")) 'Uninstall must block new launches and wait for cleanup.'
Require ($installer.Contains("StartupCommand(ApplicationRoot)") -and
    !$installer.Contains("StartupCommand(ApplicationRoot) +")) 'Startup must use only the hidden application command.'
Write-Output 'PASS installer scope, startup ownership, Windows approval, and process lifetime contracts'

# Inno creates the task items on entry to wpSelectTasks, not InitializeWizard.
# An indexed caption/enable assignment crashed both installers before page one;
# early WizardSelectTasks calls also silently lost the existing startup choice.
$initializeWizard = [regex]::Match($installer, '(?s)procedure InitializeWizard;.*?(?=\r?\n(?:procedure|function) )').Value
Require ($initializeWizard.Length -gt 0) 'Expected an InitializeWizard handler.'
Require ($initializeWizard -notmatch 'TasksList|WizardSelectTasks') 'Do not access unpopulated tasks in InitializeWizard.'
Require ($installer -notmatch 'TasksList\.(?:ItemCaption|ItemEnabled|Checked)\s*\[') 'Startup setup must not depend on a task row index.'
$taskPage = [regex]::Match($installer, '(?s)procedure CurPageChanged\(CurPageID: Integer\);.*?(?=\r?\n(?:procedure|function) )').Value
Require ($taskPage.Contains('if CurPageID <> wpSelectTasks then Exit;') -and
    $taskPage.Contains('if not StartupSelectionInitialized then begin') -and
    $taskPage.Contains('if StartupWasEnabled then') -and
    $taskPage.Contains("WizardSelectTasks('startup')") -and
    $taskPage.Contains('StartupSelectionInitialized := True;')) 'Restore startup only after task creation and preserve edits on Back/Next.'
Require ($taskPage.Contains("WizardSelectTasks('!startup')") -and
    $taskPage.Contains('WizardForm.TasksList.Enabled := not ForeignStartup;')) 'Foreign startup entries must remain unselected and unavailable.'
Write-Output 'PASS task initialization timing and startup selection contracts'

Require (!$installer.Contains("function HasNet8")) "Setup must not require global .NET."
Require ($installer.Contains("Code = 3010") -and $installer.Contains("NeedsRestart := True")) "Prerequisite restart must be handled."
Require ($installer.Contains("CompatibleGameInputFile") -and $installer.Contains("HKLM32, 'SOFTWARE\Microsoft\GameInput'")) "GameInput must use compatible redistributable detection."
Require ($installer.Contains("/passive /norestart") -and $installer.Contains("/silent /install")) "Signed vendor installers must own runtime setup."
Write-Output "PASS bundled runtime and prerequisite setup contracts"

$builder = Get-Content -LiteralPath (Join-Path $repository 'scripts/Build-Installer.ps1') -Raw
$requirements = Get-Content -LiteralPath (Join-Path $repository 'eng/installer/requirements.json') -Raw | ConvertFrom-Json
$minimum = [version]$requirements.gameInputMinimumFileVersion
Require ($minimum -eq [version]'3.3.221.0') 'Keep the physically verified GameInput compatibility floor.'
foreach ($version in @('3.3.221.0','3.5.262.0','3.5.270.0')) {
    Require ([version]$version -ge $minimum) "Compatible GameInput rejected: $version"
}
Require ([version]'0.2309.26100.9278' -lt $minimum -and [version]'3.2.0.0' -lt $minimum) 'Unverified/OS legacy GameInput must not satisfy setup.'
Require ($builder.Contains('$requirements.gameInputMinimumFileVersion')) 'Compatibility must not be tied to the SDK package version.'
Require ($installer.Contains('/L*v') -and $installer.Contains('GameInput installer exit code:')) 'GameInput installation must leave diagnostic evidence.'
Require ($installer.Contains('If its update gets stuck, restart your PC normally') -and
    $installer.Contains('GameInput could not finish updating. Restart your PC')) 'Setup must explain stalled and failed updates.'
Require ($installer.Contains('if UninstallSilent then Exit;') -and
    $installer.Contains('MB_YESNOCANCEL or MB_DEFBUTTON2') -and
    $installer.Contains('DeleteUserData := Choice = IDYES;') -and
    $installer.Contains('DeleteUserData and OwnsInstallation')) 'Data cleanup must require explicit consent and installation ownership.'
Require ($installer.Contains('if CurUninstallStep = usPostUninstall then begin')) 'Data cleanup must follow payload removal.'
Require ($installer.Contains('To remove saved data manually later')) 'Uninstall must explain later manual deletion.'
Write-Output 'PASS GameInput compatibility, recovery guidance, data consent and cleanup ordering contracts'
