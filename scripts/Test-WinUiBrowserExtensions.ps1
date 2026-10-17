[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory, [Parameter(Mandatory)][string]$ExtensionFolder, [switch]$PointerInput)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh evidence directory.' }
$bridgeBuild = Join-Path $root 'tests/WidgetBridge.Tests/bin/Debug/net8.0-windows10.0.19041.0/win-x64'
$exe = Join-Path $root 'src/OverlayFrontend.WinUI/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/OverlayFrontend.WinUI.exe'
if (!(Test-Path -LiteralPath (Join-Path $bridgeBuild 'WidgetBridge.Tests.exe')) -or !(Test-Path -LiteralPath $exe)) {
    throw 'Build WidgetBridge.Tests and the x64 Debug frontend before running this fixture.'
}
$installation = Join-Path $output 'installation'
$bridge = Join-Path $installation 'runtime/Bridge'
$profile = Join-Path $output 'profile'
New-Item -ItemType Directory -Path $bridge, $profile | Out-Null
Copy-Item -Path (Join-Path $bridgeBuild '*') -Destination $bridge -Recurse
Copy-Item -LiteralPath (Join-Path $bridge 'WidgetBridge.Tests.exe') -Destination (Join-Path $bridge 'WidgetBridge.exe')
[IO.File]::WriteAllText((Join-Path $bridge 'webapp-fixture.marker'), 'Synthetic extension management fixture')
[IO.File]::WriteAllText((Join-Path $bridge 'extensions-settings-fixture.marker'), 'Real Settings extension pages')
[IO.File]::WriteAllText((Join-Path $installation 'widget-catalog.json'), '{}')
[IO.File]::WriteAllText((Join-Path $profile 'automatic-app-updates.txt'), 'disabled')
$config = Join-Path $output 'shell.json'
@{ InstallationRoot = $installation; SettingsRoot = $profile; InstalledCatalogRoot = (Join-Path $profile 'widgets'); InitialWidgetId = 'webapp-a' } |
    ConvertTo-Json | Set-Content -LiteralPath $config
$result = Join-Path $output 'result.json'
$fixtureArguments = @(
    '--shell-no-controller', '--validation-platform-activation', ('--shell-config="{0}"' -f $config),
    ('--validate-webapp-shell="{0}"' -f $result), '--extensions-ui-only', ('--extension-folder="{0}"' -f ([IO.Path]::GetFullPath($ExtensionFolder))))
if ($PointerInput) { $fixtureArguments += '--extensions-pointer-input' }
$fixture = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden -PassThru -ArgumentList $fixtureArguments
$fixture.Id | Set-Content (Join-Path $output 'pid.txt')
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(110)
    $lastPointerSequence = 0
    while (!(Test-Path -LiteralPath $result) -and !$fixture.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        if ($PointerInput -and (Test-Path -LiteralPath ($result + '.pointer.json'))) {
            # A request can be observed mid-write. Retry on the next poll.
            $request = $null
            try { $request = Get-Content -LiteralPath ($result + '.pointer.json') -Raw | ConvertFrom-Json } catch { }
            if ($request -and $request.sequence -gt $lastPointerSequence) {
                & winapp ui click $request.target -a $fixture.Id --json | Out-File (Join-Path $output ('pointer-' + $request.sequence + '.json'))
                if ($LASTEXITCODE -ne 0) { throw "Mouse input failed for $($request.target)." }
                $lastPointerSequence = $request.sequence
            }
        }
        Start-Sleep -Milliseconds 100
    }
    if (!(Test-Path -LiteralPath $result)) { throw 'Native extension management did not produce a completed result.' }
    $summary = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
    $summary | Select-Object passed, @{n='checks';e={$_.checks.Count}}, commits, error
    if (!$summary.passed) { throw 'Native extension management validation failed.' }
}
finally {
    if (!$fixture.HasExited) {
        & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $fixture.Id
        if (!$fixture.WaitForExit(20000)) { throw 'The owned fixture did not finish shutdown.' }
    }
}

