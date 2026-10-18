[CmdletBinding()]
param([switch]$SelfTest)
if (!$SelfTest) { throw 'Use -SelfTest.' }
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleasePackaging.psm1') -Force -DisableNameChecking
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture = Join-Path $repository ('artifacts/release-tests/' + [guid]::NewGuid().ToString('N'))
$null = Assert-ReleasePath $fixture -Within $repository
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$build = Join-Path $fixture 'build'
$supplemental = Join-Path $fixture 'supplemental'
$content = Get-Content -LiteralPath (Join-Path $repository 'eng/release-content.json') -Raw | ConvertFrom-Json

function Put([string]$Path, [string]$Text) {
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($Path)) -Force | Out-Null
    [IO.File]::WriteAllText($Path, $Text)
}
function Check([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Fails([scriptblock]$Action, [string]$Message) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    Check $failed $Message
}
function Make-Package([string]$Root, [string]$Id, [string]$Runtime) {
    $package = Join-Path $Root "runtime/$Runtime"
    $application = $Id -eq 'game-help'
    $entry = if ($application) { 'payload/Widget.exe' } else { 'payload/Widget.dll' }
    Put (Join-Path $package $entry) 'fixture entrypoint'
    Put (Join-Path $package 'styles/default.wrss') '.root { gap: 4px; }'
    Put (Join-Path $package '.wrail-integrity.json') '{}'
    Write-ReleaseJson (Join-Path $package 'manifest.json') ([ordered]@{
        id = "widgetrail.test.$Id"; version = '0.1.0'
        entrypoint = $(if ($application) { @{ runtime = 'full-trust-application-v1'; executable = $entry } } else { @{ runtime = 'dotnet-worker'; assembly = $entry } })
    })
    return [ordered]@{ id = $Id; packageId = "widgetrail.test.$Id"; instanceId = "$Id.default"; packageRoot = "runtime/$Runtime"; icon = 'settings'; quickActions = @() }
}
foreach ($name in $content.sharedRuntimeDirectories) { Put (Join-Path $supplemental "$name/fixture.txt") 'runtime fixture' }
$desktop = @($content.runtimeRequirements | Where-Object name -CEQ '.NET Desktop')[0]
$desktopRoot = Join-Path $supplemental "dotnet/shared/Microsoft.WindowsDesktop.App/$($desktop.version)"
foreach ($file in @('System.Windows.Forms.dll', 'PresentationFramework.dll', 'Microsoft.WindowsDesktop.App.deps.json')) {
    Put (Join-Path $desktopRoot $file) 'desktop runtime fixture'
}
foreach ($file in $content.rootFiles) { Put (Join-Path $build "frontend/$file") "fixture $file" }
Put (Join-Path $build 'frontend/Assets/Fonts/xbox.ttf') 'font fixture'
Put (Join-Path $build 'frontend/Assets/Fonts/playstation.ttf') 'font fixture'
Put (Join-Path $build 'frontend/Assets/Fonts/guide.ttf') 'font fixture'
Put (Join-Path $build 'frontend/Assets/required-resource.xml') '<resource>must survive payload copying</resource>'
Put (Join-Path $build 'frontend/frontend.pdb') 'symbols do not ship'
Put (Join-Path $build 'DoNotShipTests.exe') 'test only'
Put (Join-Path $build 'developer-machine.env') 'must not ship'
foreach ($name in $content.hostRuntimeDirectories) {
    Put (Join-Path $build "runtime/$name/$name.dll") $name
}
Put (Join-Path $build 'runtime/Bridge/Bridge.pdb') 'do not ship'
Put (Join-Path $build 'runtime/WidgetWorkerHost/WidgetWorkerHost.exe') 'worker'
Put (Join-Path $build 'runtime/Settings/SettingsWidget.Worker.exe') 'settings'
Put (Join-Path $build 'runtime/Performance/PerformanceCollector.exe') 'FPS helper'
Put (Join-Path $build 'runtime/Performance/PresentMon.exe') 'pinned Intel collector'
Put (Join-Path $build 'runtime/Performance/PresentMon-LICENSE.txt') 'MIT license'
Write-ReleaseJson (Join-Path $build 'runtime/Settings/manifest.json') @{ id = 'widgetrail.firstparty.settings'; version = '0.1.0' }
$baseWidgets = @(
    foreach ($id in @($content.productionWidgets)) {
        if ($id -cnotin @($content.supplementalWidgets.id)) { Make-Package $build $id $id }
    }
)
Write-ReleaseJson (Join-Path $build 'widget-catalog.json') ([ordered]@{
    catalogVersion = 1; genericWorkerExecutable = 'runtime/WidgetWorkerHost/WidgetWorkerHost.exe'
    widgets = @(@{ id = 'settings'; workerExecutable = 'runtime/Settings/SettingsWidget.Worker.exe' })
    bundledWidgets = $baseWidgets
})
$extras = @(foreach ($widget in $content.supplementalWidgets) { Make-Package $supplemental $widget.id $widget.runtime })
Write-ReleaseJson (Join-Path $supplemental 'supplemental-widgets.json') $extras
Put (Join-Path $supplemental 'tools/wrail/wrail.exe') 'CLI'
Put (Join-Path $supplemental 'tools/wrail/templates/ControllerWidget/template.json') '{"templateVersion":2}'
Put (Join-Path $supplemental 'licenses/Microsoft.GameInput-LICENSE.txt') 'fixture notice'

function Assemble([string]$Out) {
    New-WidgetRailReleaseFolders -BuildRoot $build -SupplementalRoot $supplemental -RepositoryRoot $repository `
        -OutputRoot $Out -Version '0.1.0-preview.1' -SourceCommit ('a' * 40) -SdkVersion '0.3.0-dev' -Content $content
}
$first = Assemble (Join-Path $fixture 'one')
$production = Join-Path $first 'WidgetRail-0.1.0-preview.1-win-x64'
$bare = Join-Path $first 'WidgetRail-Bare-0.1.0-preview.1-win-x64'
$prodCatalog = Get-Content (Join-Path $production 'widget-catalog.json') -Raw | ConvertFrom-Json
$bareCatalog = Get-Content (Join-Path $bare 'widget-catalog.json') -Raw | ConvertFrom-Json
Check (@($prodCatalog.bundledWidgets).Count -eq 1 -and $prodCatalog.bundledWidgets[0].id -ceq 'marketplace') 'Only Catalogue may remain a trusted bundled widget.'
$prodInitial = Get-Content (Join-Path $production 'initial-widgets.json') -Raw | ConvertFrom-Json
$bareInitial = Get-Content (Join-Path $bare 'initial-widgets.json') -Raw | ConvertFrom-Json
Check (@($bareInitial.widgets).Count -eq 0) 'Bare may not seed optional widgets.'
$expectedPackages = @($content.productionWidgets | Where-Object { $_ -ne 'marketplace' } | ForEach-Object { "widgetrail.test.$_" })
Check ((@($prodInitial.widgets.id) -join '|') -ceq ($expectedPackages -join '|')) 'Production initial packages differ.'
foreach ($widget in $prodInitial.widgets) {
    $archivePath = Join-Path $production $widget.package
    Check ((Get-FileHash $archivePath).Hash -ieq $widget.sha256) 'Initial package hash differs.'
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        Check (@($archive.Entries | Where-Object FullName -CEQ '.wrail-integrity.json').Count -eq 0) 'Host integrity file leaked into package.'
        Check (@($archive.Entries | Where-Object FullName -CEQ 'manifest.json').Count -eq 1) 'Initial package manifest missing.'
        Check (@($archive.Entries | Where-Object FullName -CEQ 'payload/Widget.dll').Count -eq 1) 'Initial package entrypoint missing.'
    } finally { $archive.Dispose() }
}
Check (@($bareCatalog.widgets).Count -eq 1 -and $bareCatalog.widgets[0].id -ceq 'settings' -and
    @($bareCatalog.bundledWidgets).Count -eq 1 -and $bareCatalog.bundledWidgets[0].id -ceq 'marketplace') 'Bare must contain only Settings and Catalogue.'
foreach ($edition in @($production, $bare)) {
    foreach ($file in @('PerformanceCollector.exe', 'PresentMon.exe', 'PresentMon-LICENSE.txt')) {
        Check (Test-Path -LiteralPath (Join-Path $edition "runtime/Performance/$file")) 'Shared FPS capability or license missing from an edition.'
    }
}
foreach ($optional in @(@('discord','Discord'), @('game-help','GameHelp'), @('display-profiles','DisplayProfiles'), @('twitch','Twitch'), @('mapgenie','MapGenie'))) {
    foreach ($edition in @($production, $bare)) {
        $editionCatalog = Get-Content (Join-Path $edition 'widget-catalog.json') -Raw | ConvertFrom-Json
        Check (@($editionCatalog.bundledWidgets | Where-Object id -EQ $optional[0]).Count -eq 0) 'Catalogue-only widget is preinstalled.'
        Check (!(Test-Path (Join-Path $edition "runtime/$($optional[1])"))) 'Catalogue-only payload is in installer output.'
    }
}
foreach ($widget in $prodCatalog.bundledWidgets) {
    if ($widget.id -ne 'marketplace') {
        Check (!(Test-Path (Join-Path $bare $widget.packageRoot))) 'Optional widget leaked into Bare.'
    }
}
$prodWidget = @($prodCatalog.bundledWidgets | Where-Object id -CEQ 'marketplace')[0]
$bareWidget = @($bareCatalog.bundledWidgets | Where-Object id -CEQ 'marketplace')[0]
Check ((Get-FileHash (Join-Path $production "$($prodWidget.packageRoot)/manifest.json")).Hash -ceq
    (Get-FileHash (Join-Path $bare "$($bareWidget.packageRoot)/manifest.json")).Hash) 'Catalogue edition payloads differ.'
foreach ($edition in @($production, $bare)) {
    foreach ($sample in @('sdk-gallery','clock-sample','full-application-reference','embedded-media-sample')) {
        $editionCatalog = Get-Content (Join-Path $edition 'widget-catalog.json') -Raw | ConvertFrom-Json
        Check (@($editionCatalog.bundledWidgets | Where-Object id -CEQ $sample).Count -eq 0) 'Development sample leaked into a release.'
    }
}
# Identical services/capabilities are available after installing a widget in Bare.
foreach ($relative in @(Get-ReleaseFiles $bare)) {
    if ($relative -in @('release.json','widget-catalog.json','initial-widgets.json','START-HERE.txt')) { continue }
    Check ((Get-FileHash (Join-Path $bare $relative)).Hash -ceq (Get-FileHash (Join-Path $production $relative)).Hash) "Shared edition file differs: $relative"
}
Check ((Get-Content (Join-Path $bare 'release.json') -Raw | ConvertFrom-Json).edition -ceq 'bare') 'Bare edition identity missing.'
foreach ($editionRoot in @($production, $bare)) {
    foreach ($fontFile in @('xbox.ttf', 'playstation.ttf', 'guide.ttf')) {
        Check (Test-Path (Join-Path $editionRoot "Assets/Fonts/$fontFile")) 'WinUI controller font missing.'
    }
    Check (Test-Path (Join-Path $editionRoot 'Assets/required-resource.xml')) 'Required frontend XML was silently dropped.'
    Check (!(Test-Path (Join-Path $editionRoot 'frontend.pdb'))) 'Frontend symbols leaked into release.'
    Check (!(Test-Path (Join-Path $editionRoot 'deployment'))) 'Identity deployment payload must not ship.'
    Check ((Test-Path (Join-Path $editionRoot 'tools/wrail/templates/ControllerWidget/template.json')) -and (Test-Path (Join-Path $editionRoot 'wrail.cmd'))) 'Shared CLI or launcher missing.'
}
Check (!(Test-Path (Join-Path $production 'DoNotShipTests.exe')) -and !(Test-Path (Join-Path $production 'developer-machine.env')) -and
    !(Test-Path (Join-Path $production 'runtime/Bridge/Bridge.pdb'))) 'Build debris leaked into release.'
# A selected optional widget must never exist simultaneously as a trusted fallback.
foreach ($widget in $baseWidgets | Where-Object id -CNE 'marketplace') {
    Check (!(Test-Path (Join-Path $production $widget.packageRoot))) 'Trusted fallback would resurrect a removed optional widget.'
}
Write-Output 'PASS edition catalogs, tools and explicit distribution roots'
Check ((Get-Content (Join-Path $production 'release.json') -Raw | ConvertFrom-Json).packaging -ceq 'winui-unpackaged') 'Release must use unpackaged WinUI.'

$second = Assemble (Join-Path $fixture 'two')
$files = Get-ReleaseFiles $first
Check (($files -join '|') -ceq ((Get-ReleaseFiles $second) -join '|')) 'Repeated release layout differs.'
foreach ($relative in $files) {
    Check ((Get-FileHash (Join-Path $first $relative)).Hash -ceq (Get-FileHash (Join-Path $second $relative)).Hash) "Repeated content differs: $relative"
}
Write-Output 'PASS deterministic folder contents and manifest hashes from identical inputs'

Fails { Assemble (Join-Path $fixture 'one') } 'An existing release was overwritten.'
Test-ReleaseInventory $production
Write-Output 'PASS immutable release destination'

$hostPath = Join-Path $production 'OverlayFrontend.WinUI.exe'
$original = [IO.File]::ReadAllBytes($hostPath)
Put $hostPath 'tampered'
Fails { Test-ReleaseInventory $production } 'Modified executable was accepted.'
[IO.File]::WriteAllBytes($hostPath, $original)
Put (Join-Path $production 'extra.txt') 'unexpected'
Fails { Test-ReleaseInventory $production } 'Unlisted file was accepted.'
Write-Output 'PASS output tamper and extra-file detection'

$nativeInput = Join-Path $build 'frontend/OverlayFrontend.WinUI.exe'
$original = [IO.File]::ReadAllBytes($nativeInput)
Remove-Item -LiteralPath $nativeInput
Fails { Assemble (Join-Path $fixture 'missing') } 'Missing native input was accepted.'
Check (!(Test-Path (Join-Path $fixture 'missing/0.1.0-preview.1'))) 'Failed release became visible.'
[IO.File]::WriteAllBytes($nativeInput, $original)
Write-Output 'PASS missing input cannot publish a partial release'

$desktopInput = Join-Path $desktopRoot 'System.Windows.Forms.dll'
Remove-Item -LiteralPath $desktopInput
Fails { Assemble (Join-Path $fixture 'missing-desktop') } 'Release without its declared Desktop runtime was accepted.'
Put $desktopInput 'desktop runtime fixture'
Write-Output 'PASS missing Desktop runtime cannot publish a release'
$runtimeInput = Join-Path $build 'frontend/Microsoft.ui.xaml.dll'
$runtimeBytes = [IO.File]::ReadAllBytes($runtimeInput)
Remove-Item -LiteralPath $runtimeInput
Fails { Assemble (Join-Path $fixture 'missing-winui-runtime') } 'Missing self-contained WinUI runtime was accepted.'
[IO.File]::WriteAllBytes($runtimeInput, $runtimeBytes)
Write-Output 'PASS missing self-contained WinUI runtime cannot publish a release'

$originalRoots = $content.rootFiles
$content.rootFiles = @('../outside.dll')
Put (Join-Path $fixture 'outside.dll') 'outside'
Fails { Assemble (Join-Path $fixture 'escape') } 'Input traversal was accepted.'
$content.rootFiles = $originalRoots
Write-Output 'PASS input path containment'

Put (Join-Path $build 'runtime/Bridge/secret.env') 'unexpected'
Fails { Assemble (Join-Path $fixture 'state') } 'Unexpected state was accepted.'
Remove-Item -LiteralPath (Join-Path $build 'runtime/Bridge/secret.env')
Write-Output 'PASS runtime state rejection'

$junction = Join-Path $build 'runtime/Bridge/linked'
$target = Join-Path $fixture 'external-directory'
New-Item -ItemType Directory -Path $target | Out-Null
New-Item -ItemType Junction -Path $junction -Target $target | Out-Null
Fails { Assemble (Join-Path $fixture 'junction') } 'Reparse point was followed.'
Write-Output 'PASS reparse-point rejection'
Write-Output 'Release packaging self-test passed.'
