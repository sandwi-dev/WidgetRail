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

Require (!$installer.Contains("function HasNet8")) "Setup must not require global .NET."
Require ($installer.Contains("Code = 3010") -and $installer.Contains("NeedsRestart := True")) "Prerequisite restart must be handled."
Require ($installer.Contains("CompatibleGameInputFile") -and $installer.Contains("HKLM32, 'SOFTWARE\Microsoft\GameInput'")) "GameInput must use compatible redistributable detection."
Require ($installer.Contains("/passive /norestart") -and $installer.Contains("/silent /install")) "Signed vendor installers must own runtime setup."
Write-Output "PASS bundled runtime and prerequisite setup contracts"
