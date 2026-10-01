[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BridgeInstallation,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh appearance evidence directory.' }
if (Get-Process -Name 'OverlayFrontend.WinUI' -ErrorAction SilentlyContinue) { throw 'Close the current candidate before appearance validation.' }
$null = New-Item -ItemType Directory -Path $output
$configuration = Join-Path $output 'shell.json'
$resultPath = Join-Path $output 'result.json'
@{
    InstallationRoot=(Resolve-Path -LiteralPath $BridgeInstallation).Path
    SettingsRoot=(Join-Path $output 'profile')
    InstalledCatalogRoot=(Join-Path $output 'catalog')
    InitialWidgetId='settings'
    SwitchDiagnosticsPath=(Join-Path $output 'switches.log')
} | ConvertTo-Json | Set-Content -LiteralPath $configuration
$launch = winapp run (Join-Path $root 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') `
    --no-build --arch x64 -p Platform=x64 --detach --json `
    --args "--shell-no-controller --validation-platform-activation --shell-config=`"$configuration`" --validate-appearance-settings=`"$resultPath`"" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $launch.ProcessId -le 0) { throw 'Appearance fixture did not launch.' }
$owned = [int]$launch.ProcessId
$launch | ConvertTo-Json | Set-Content (Join-Path $output 'launch.json')
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(150)
    $result = $null
    do {
        if (Test-Path -LiteralPath $resultPath) {
            try { $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json } catch { }
            if ($result) { break }
        }
        if (-not (Get-Process -Id $owned -ErrorAction SilentlyContinue)) { throw 'Appearance fixture exited before publishing its result.' }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not $result -or $result.passed -ne $true -or $result.checks.Count -lt 30) {
        throw "Appearance fixture failed or timed out: $($result.error)"
    }
    "Passed $($result.checks.Count) native Settings appearance checks."
} finally {
    if (Get-Process -Id $owned -ErrorAction SilentlyContinue) {
        & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $owned
        Wait-Process -Id $owned -Timeout 15 -ErrorAction SilentlyContinue
        if (Get-Process -Id $owned -ErrorAction SilentlyContinue) { throw 'Appearance fixture did not close normally.' }
    }
}
