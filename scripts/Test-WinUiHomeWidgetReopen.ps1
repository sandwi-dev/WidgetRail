[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$FrontendDirectory,
    [Parameter(Mandatory)][string]$InstallationRoot,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$out = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $out) { throw 'Choose a new verification directory.' }
$exe = (Resolve-Path -LiteralPath (Join-Path $FrontendDirectory 'OverlayFrontend.WinUI.exe')).Path
$profile = Join-Path $out 'profile'
$catalog = Join-Path $out 'catalog'
$null = New-Item -ItemType Directory -Path $profile,$catalog -Force
$configPath = Join-Path $out 'shell.json'
@{InstallationRoot=(Resolve-Path -LiteralPath $InstallationRoot).Path; SettingsRoot=$profile; InstalledCatalogRoot=$catalog} |
    ConvertTo-Json | Set-Content -LiteralPath $configPath
$arguments = @('--show','--validation-platform-activation',('"--shell-config={0}"' -f $configPath))
$desktop = New-Object -ComObject WScript.Shell
$app = $null
$checks = [Collections.Generic.List[string]]::new()
function Show { Start-Process -FilePath $exe -WindowStyle Hidden -ArgumentList $arguments -PassThru }
function Ui([string[]]$Command) {
    $result = (& winapp ui @Command -a $app.Id --json 2>$null) | ConvertFrom-Json
    if (!$result -or $result.error) { throw "WinUI action failed: $Command $($result.error.message)" }
    return $result
}
function WaitFor([string]$Id) {
    if (!(Ui @('wait-for',$Id,'-t','15000')).found) { throw "Missing $Id" }
}
function InvokeControl([string]$Id) {
    WaitFor $Id
    if (!$desktop.AppActivate($app.Id)) { throw 'Could not activate the fixture overlay.' }
    $null = Ui @('click',$Id)
}
function Reopen {
    $close = @((Ui @('inspect','--interactive')).windows.elements | Where-Object name -Like 'Guide button: Close')
    if ($close.Count -ne 1) { throw 'Missing overlay Close action.' }
    InvokeControl $close[0].automationId
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do { Start-Sleep -Milliseconds 30; $app.Refresh() } while ($app.MainWindowHandle -ne 0 -and $timer.Elapsed.TotalSeconds -lt 8)
    if ($app.MainWindowHandle -ne 0 -or $app.HasExited) { throw 'Overlay did not hide while remaining resident.' }
    $activation = Show
    try {
        if (!$activation.WaitForExit(10000) -or $app.HasExited) { throw 'Reopening did not activate the resident host.' }
    } finally { $activation.Dispose() }
}
function CloseFixture {
    if ($app -and !$app.HasExited) {
        & "$PSScriptRoot/Close-WinUiTestShell.ps1" -AppPid $app.Id
        if (!$app.WaitForExit(10000)) { throw 'Fixture failed to shut down.' }
    }
}
$failure = $null
try {
    $app = Show
    WaitFor 'Widget.category.overlay'
    $checks.Add('Fresh startup defaults to Settings')
    InvokeControl 'Overlay.Widget.marketplace'
    InvokeControl 'Widget.marketplace.tab.themes'
    WaitFor 'Widget.theme.search'
    Reopen
    WaitFor 'Widget.theme.search'
    $checks.Add('Hide/show with Settings as home resumes Catalogue on its Themes tab')
    CloseFixture
    $app.Dispose()
    $app = $null
    $settingsPath = Join-Path $profile 'platform-settings.json'
    if (!(Test-Path -LiteralPath $settingsPath)) {
        $null = [Reflection.Assembly]::LoadFrom((Join-Path $InstallationRoot 'runtime/Settings/PlatformSettings.dll'))
        $paths = [WidgetRail.PlatformSettings.PlatformSettingsPaths]::new($profile)
        $store = [WidgetRail.PlatformSettings.PlatformSettingsStore]::new($paths)
        $null = $store.ReplaceAsync([WidgetRail.PlatformSettings.PlatformSettingsDocument]::Default).GetAwaiter().GetResult()
    }
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json -AsHashtable
    $settings.homeWidgetId = 'marketplace'
    $settings | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $settingsPath
    $app = Show
    WaitFor 'Widget.marketplace.tab.browse'
    $checks.Add('A fresh process opens the configured Catalogue home')
    InvokeControl 'Overlay.Widget.settings'
    WaitFor 'Widget.category.overlay'
    Reopen
    WaitFor 'Widget.category.overlay'
    $checks.Add('Hide/show with Catalogue as home resumes Settings')
} catch {
    $failure = $_.Exception.ToString()
    if ($app -and !$app.HasExited) {
        Ui @('inspect','--interactive') | ConvertTo-Json -Depth 12 | Set-Content "$out/failure-tree.json"
    }
    throw
}
finally {
    CloseFixture
    if ($app) { $app.Dispose() }
    @{passed=(!$failure);checks=$checks;error=$failure} | ConvertTo-Json | Set-Content "$out/result.json"
}
