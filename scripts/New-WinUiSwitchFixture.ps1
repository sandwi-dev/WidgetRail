[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BridgeInstallation,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a fresh fixture output directory.' }
$fixture = Join-Path $root 'tests/WidgetSwitchFixture/bin/Debug/net8.0-windows10.0.19041.0/win-x64'
if (-not (Test-Path (Join-Path $fixture 'WidgetSwitchFixture.exe'))) { throw 'Build WidgetSwitchFixture first.' }
$installation = Join-Path $OutputDirectory 'installation'
$null = New-Item -ItemType Directory -Path (Join-Path $installation 'runtime') -Force
Copy-Item -LiteralPath (Join-Path $BridgeInstallation 'runtime/Bridge') -Destination (Join-Path $installation 'runtime/Bridge') -Recurse
Copy-Item -LiteralPath $fixture -Destination (Join-Path $installation 'runtime/Fixture') -Recurse
@'
.switch-surface { width: 100%; height: 100%; padding: 28px; gap: 16px; background: #162d46; }
.audio-surface { background: #245d3c; }
.wide-peer-surface { background: #653040; }
.now-playing-surface { background: #294e6b; }
.games-surface { background: #655019; }
.switch-title { font-size: 28px; color: #ffffff; }
.switch-detail { color: #ffffff; }
.switch-button { min-height: 48px; }
'@ | Set-Content (Join-Path $installation 'runtime/Fixture/default.wrss')
$definitions = @(
    @('audio-mixer','Audio Mixer','normal',0),
    @('wide-peer','Wide Peer','normal',800),
    @('now-playing','Now Playing','normal',600),
    @('games-apps','Games','normal',0),
    @('settings','Intentional Empty','empty',0),
    @('network-controls','Intentional Loading','loading',0),
    @('spotify','Intentional Failure','failure',0)
)
$widgets = foreach ($definition in $definitions) {
    @{
        id=$definition[0]; packageId=('widgetrail.tests.'+$definition[0]); publisherId='widgetrail.tests'
        name=$definition[1]; instanceId=($definition[0]+'.default'); icon='settings'
        workerExecutable='runtime/Fixture/WidgetSwitchFixture.exe'; styleFile='runtime/Fixture/default.wrss'
        workerArguments=@('--initial-delay-ms',[string]$definition[3],'--view-kind',$definition[2])
        residencyPolicy=@{schemaVersion=1;mode='keep-alive'}; declaredCapabilities=@(); quickActions=@()
    }
}
@{catalogVersion=1;widgets=@($widgets);bundledWidgets=@()} | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $installation 'widget-catalog.json')
@{
    InstallationRoot=$installation; SettingsRoot=(Join-Path $OutputDirectory 'settings'); InstalledCatalogRoot=(Join-Path $OutputDirectory 'catalog')
    InitialWidgetId='audio-mixer'; SwitchDiagnosticsPath=(Join-Path $OutputDirectory 'switches.log')
} | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'shell.json')
Write-Output (Join-Path $OutputDirectory 'shell.json')
