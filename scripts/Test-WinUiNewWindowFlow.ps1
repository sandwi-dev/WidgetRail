[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$FixtureConfiguration,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$IncludeNotifications,
    [switch]$IncludeSettingsMenus,
    [switch]$SettingsUpdatesOnly
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh evidence directory.' }
$configuration = Get-Content -LiteralPath $FixtureConfiguration -Raw | ConvertFrom-Json
if (!(Test-Path -LiteralPath (Join-Path $configuration.InstallationRoot 'runtime/Bridge/webapp-fixture.marker'))) {
    throw 'Only the synthetic BridgeWebAppFixture installation is supported.'
}
New-Item -ItemType Directory -Path $output | Out-Null
$configuration.SettingsRoot = Join-Path $output 'profile'
New-Item -ItemType Directory -Path $configuration.SettingsRoot | Out-Null
[IO.File]::WriteAllText((Join-Path $configuration.SettingsRoot 'automatic-app-updates.txt'), $(if ($SettingsUpdatesOnly) { 'enabled' } else { 'disabled' }))
$configuration.InitialWidgetId = 'webapp-a'
$configPath = Join-Path $output 'shell.json'
$configuration | ConvertTo-Json | Set-Content -LiteralPath $configPath
$result = Join-Path $output 'result.json'
$exe = Join-Path $root 'src/OverlayFrontend.WinUI/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/OverlayFrontend.WinUI.exe'
$arguments = @(
    '--shell-no-controller', '--validation-platform-activation', ('--shell-config="{0}"' -f $configPath),
    ('--validate-webapp-shell="{0}"' -f $result), '--webapp-new-window-flow', '--webapp-popup-placement',
    '--webapp-popup-presentation', '--webapp-pin-dialog-lifecycle', '--webapp-shutdown')
if ($IncludeNotifications) { $arguments += '--webapp-notifications'; if ($IncludeSettingsMenus) { $arguments += '--widget-notifications' } }
if ($IncludeSettingsMenus) { $arguments += '--settings-updates-menus' }
if ($SettingsUpdatesOnly) { $arguments += '--settings-updates-only' }
$fixture = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden -PassThru -ArgumentList $arguments
$fixture.Id | Set-Content (Join-Path $output 'pid.txt')
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(240)
    while (!(Test-Path -LiteralPath $result) -and !$fixture.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
    if (!(Test-Path -LiteralPath $result)) { throw 'Native validation did not produce a completed result.' }
    $summary = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
    $summary | Select-Object passed, step, @{n='checks';e={$_.checks.Count}}, error
    if (!$summary.passed) { throw 'Native new-window validation failed.' }
}
finally {
    if (!$fixture.HasExited) {
        & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $fixture.Id
        if (!$fixture.WaitForExit(20000)) { throw 'The owned fixture did not finish shutdown.' }
    }
}
