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
$developer = Join-Path $fixture 'developer'
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
    Put (Join-Path $package 'payload/Widget.dll') 'fixture assembly'
    Put (Join-Path $package 'styles/default.wrss') '.root { gap: 4px; }'
    Put (Join-Path $package '.wrail-integrity.json') '{}'
    Write-ReleaseJson (Join-Path $package 'manifest.json') ([ordered]@{
        id = "widgetrail.test.$Id"; version = '0.1.0'
        entrypoint = @{ assembly = 'payload/Widget.dll' }
    })
    return [ordered]@{ id = $Id; packageId = "widgetrail.test.$Id"; instanceId = "$Id.default"; packageRoot = "runtime/$Runtime"; icon = 'settings'; quickActions = @() }
}
foreach ($name in $content.sharedRuntimeDirectories) { Put (Join-Path $developer "$name/fixture.txt") 'runtime fixture' }
$desktop = @($content.runtimeRequirements | Where-Object name -CEQ '.NET Desktop')[0]
$desktopRoot = Join-Path $developer "dotnet/shared/Microsoft.WindowsDesktop.App/$($desktop.version)"
foreach ($file in @('System.Windows.Forms.dll', 'PresentationFramework.dll', 'Microsoft.WindowsDesktop.App.deps.json')) {
    Put (Join-Path $desktopRoot $file) 'desktop runtime fixture'
}
foreach ($file in $content.rootFiles) { Put (Join-Path $build $file) "fixture $file" }
Put (Join-Path $build 'DoNotShipTests.exe') 'test only'
Put (Join-Path $build 'developer-machine.env') 'must not ship'
foreach ($name in $content.hostRuntimeDirectories) {
    Put (Join-Path $build "runtime/$name/$name.dll") $name
}
Put (Join-Path $build 'runtime/Bridge/Bridge.pdb') 'do not ship'
Put (Join-Path $build 'runtime/WidgetWorkerHost/WidgetWorkerHost.exe') 'worker'
Put (Join-Path $build 'runtime/Settings/SettingsWidget.Worker.exe') 'settings'
Write-ReleaseJson (Join-Path $build 'runtime/Settings/manifest.json') @{ id = 'widgetrail.firstparty.settings'; version = '0.1.0' }
$baseWidgets = @(
    foreach ($id in @($content.productionWidgets) + @($content.developerExistingWidgets)) { Make-Package $build $id $id }
)
Write-ReleaseJson (Join-Path $build 'widget-catalog.json') ([ordered]@{
    catalogVersion = 1; genericWorkerExecutable = 'runtime/WidgetWorkerHost/WidgetWorkerHost.exe'
    widgets = @(@{ id = 'settings'; workerExecutable = 'runtime/Settings/SettingsWidget.Worker.exe' })
    bundledWidgets = $baseWidgets
})
$extras = @(foreach ($widget in $content.developerWidgets) { Make-Package $developer $widget.id $widget.runtime })
Write-ReleaseJson (Join-Path $developer 'developer-widgets.json') $extras
Put (Join-Path $developer 'tools/wrail/wrail.exe') 'CLI'
Put (Join-Path $developer 'tools/wrail/templates/ControllerWidget/template.json') '{"templateVersion":2}'
Put (Join-Path $developer 'licenses/Microsoft.GameInput-LICENSE.txt') 'fixture notice'

function Assemble([string]$Out) {
    New-WidgetRailReleaseFolders -BuildRoot $build -DeveloperRoot $developer -RepositoryRoot $repository `
        -OutputRoot $Out -Version '0.1.0-preview.1' -SourceCommit ('a' * 40) -SdkVersion '0.3.0-dev' -Content $content
}
$nativeBuild = Get-Content -LiteralPath (Join-Path $repository 'src/OverlayHost/build.ps1') -Raw
$hostStart = $nativeBuild.IndexOf('$hostArguments = $hostCompileArguments', [StringComparison]::Ordinal)
$hostEnd = $nativeBuild.IndexOf('& $cl $hostArguments', $hostStart, [StringComparison]::Ordinal)
Check ($hostStart -ge 0 -and $hostEnd -gt $hostStart) 'Production host compilation is missing.'
$hostLink = $nativeBuild.Substring($hostStart, $hostEnd - $hostStart)
Check ($hostLink.Contains("'/link', " + '$versionResource', [StringComparison]::Ordinal)) 'Production host omits its version resource.'
Check ($hostLink.Contains('/Brepro', [StringComparison]::Ordinal) -and $hostLink.Contains('/PDBALTPATH:%_PDB%', [StringComparison]::Ordinal)) 'Production host omits deterministic link metadata.'
$first = Assemble (Join-Path $fixture 'one')
$production = Join-Path $first 'WidgetRail-0.1.0-preview.1-win-x64'
$dev = Join-Path $first 'WidgetRail-Developer-0.1.0-preview.1-win-x64'
$prodCatalog = Get-Content (Join-Path $production 'widget-catalog.json') -Raw | ConvertFrom-Json
$devCatalog = Get-Content (Join-Path $dev 'widget-catalog.json') -Raw | ConvertFrom-Json
Check (@($prodCatalog.bundledWidgets).Count -eq 7) 'Production catalog differs.'
Check (@($devCatalog.bundledWidgets).Count -eq 11) 'Developer catalog differs.'
Check (@($prodCatalog.bundledWidgets | Where-Object id -EQ 'display-profiles').Count -eq 1 -and
       @($devCatalog.bundledWidgets | Where-Object id -EQ 'display-profiles').Count -eq 1) 'Display Profiles is missing from an edition.'
Check (!(Test-Path (Join-Path $production 'runtime/embedded-media-sample'))) 'Developer content leaked into Production.'
foreach ($editionRoot in @($production, $dev)) {
    Check ((Test-Path (Join-Path $editionRoot 'tools/wrail/templates/ControllerWidget/template.json')) -and (Test-Path (Join-Path $editionRoot 'wrail.cmd'))) 'Shared CLI or launcher missing.'
}
Check (!(Test-Path (Join-Path $production 'DoNotShipTests.exe')) -and !(Test-Path (Join-Path $production 'developer-machine.env')) -and
    !(Test-Path (Join-Path $production 'runtime/Bridge/Bridge.pdb'))) 'Build debris leaked into release.'
Write-Output 'PASS edition catalogs, tools and explicit distribution roots'

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

$hostPath = Join-Path $production 'OverlayHost.exe'
$original = [IO.File]::ReadAllBytes($hostPath)
Put $hostPath 'tampered'
Fails { Test-ReleaseInventory $production } 'Modified executable was accepted.'
[IO.File]::WriteAllBytes($hostPath, $original)
Put (Join-Path $production 'extra.txt') 'unexpected'
Fails { Test-ReleaseInventory $production } 'Unlisted file was accepted.'
Write-Output 'PASS output tamper and extra-file detection'

$nativeInput = Join-Path $build 'OverlayHost.exe'
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
Write-Output 'Release packaging self-test passed (9 scenarios).'
