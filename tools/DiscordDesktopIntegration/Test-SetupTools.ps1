param([string]$StagedBuild)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixture = Join-Path $repo ('artifacts/discord-desktop/setup-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$checks = 0
function Check([bool]$Condition,[string]$Message) {
    if (!$Condition) { throw $Message }
    $script:checks++
}
function MustReject([scriptblock]$Action,[string]$Message) {
    $rejected=$false
    try { & $Action | Out-Null } catch { $rejected=$true }
    Check $rejected $Message
}
$installation = Join-Path $fixture 'Discord'
foreach ($version in @('app-1.0.2','app-1.0.10')) {
    $directory=Join-Path $installation $version
    New-Item -ItemType Directory -Path (Join-Path $directory 'resources') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $directory 'Discord.exe'),'Synthetic fixture, not an executable')
    [IO.File]::WriteAllText((Join-Path $directory 'resources/app.asar'),'Synthetic fixture, not an archive')
}
[IO.File]::WriteAllText((Join-Path $installation 'app-1.0.2/resources/_app.asar'),'Unclassified backup fixture')
$before = @(Get-ChildItem -LiteralPath $installation -Recurse -File | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName).Hash })
$report = & "$PSScriptRoot/Read-DiscordInstallation.ps1" -InstallationRoot $installation -SkipRunningProcesses -IncludeFileHashes | ConvertFrom-Json
Check ($report.installations.Count -eq 2) 'Inventory must retain every version instead of silently selecting the newest directory.'
Check ($report.runningInstallationCount -eq 0 -and $null -eq $report.suggestedRunningExecutable) 'Disk presence is not running-process evidence.'
Check (($report.installations | Where-Object versionDirectory -eq 'app-1.0.2').loaderState -eq 'modified-layout-requires-review') 'A backup is an unclassified modification, not proof of Vencord.'
Check (($report.installations | Where-Object versionDirectory -eq 'app-1.0.10').loaderState -eq 'asar-present-unclassified') 'An ASAR alone is not proof of a vanilla compatible installation.'
Check ($report.installations[0].appAsar.sha256 -match '^[A-F0-9]{64}$') 'Requested file provenance must be recorded.'
$after = @(Get-ChildItem -LiteralPath $installation -Recurse -File | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName).Hash })
Check (($before -join ',') -eq ($after -join ',')) 'Read-only inventory modified the fixture.'
$withoutHash = & "$PSScriptRoot/Read-DiscordInstallation.ps1" -InstallationRoot $installation -SkipRunningProcesses | ConvertFrom-Json
Check ($null -eq $withoutHash.installations[0].appAsar.sha256) 'Unrequested file hashing must remain absent.'
$missing = & "$PSScriptRoot/Read-DiscordInstallation.ps1" -InstallationRoot (Join-Path $fixture 'missing') -SkipRunningProcesses | ConvertFrom-Json
Check ($missing.installations.Count -eq 0) 'Missing installation must be a normal empty inventory.'

$plugin = Join-Path $fixture 'plugin'
New-Item -ItemType Directory -Path $plugin | Out-Null
foreach ($name in @('index.ts','native.ts','protocol.mjs','protocol.test.mjs')) { [IO.File]::WriteAllText((Join-Path $plugin $name),'// synthetic staging fixture') }
$output = Join-Path $fixture 'planned-output'
$plan = & "$PSScriptRoot/Stage-Vencord.ps1" -OutputDirectory $output -PluginDirectory $plugin -PlanOnly | ConvertFrom-Json
Check ($plan.pluginFiles.Count -eq 3 -and !@($plan.pluginFiles | Where-Object path -eq 'protocol.test.mjs').Count) 'Tests must not enter the shipped plugin source set.'
Check (!$plan.installPerformed -and !$plan.launchPerformed -and !$plan.productionSafeToInstall) 'Staging must not imply installation approval or runtime qualification.'
Check (!(Test-Path -LiteralPath $output)) 'PlanOnly must not create build output or fetch source.'
MustReject { & "$PSScriptRoot/Stage-Vencord.ps1" -OutputDirectory (Join-Path $repo 'unexpected-vencord-output') -PluginDirectory $plugin -PlanOnly } 'Output outside the isolated artifact subtree must be rejected.'
MustReject { & "$PSScriptRoot/Stage-Vencord.ps1" -OutputDirectory $fixture -PluginDirectory $plugin -PlanOnly } 'Existing output must not be reused or overwritten.'
MustReject { & "$PSScriptRoot/Stage-Vencord.ps1" -OutputDirectory (Join-Path $fixture 'missing-plugin-output') -PluginDirectory (Join-Path $fixture 'missing-plugin') -PlanOnly } 'Incomplete plugin source must fail before any build writes.'
if ($StagedBuild) {
    $staged = [IO.Path]::GetFullPath($StagedBuild)
    $provenance = Get-Content -LiteralPath (Join-Path $staged 'provenance.json') -Raw | ConvertFrom-Json
    Check ($provenance.buildCompleted -and $provenance.bundleScopeVerified) 'The staged build has not passed its artifact validation.'
    Check ($provenance.registeredPlugins.Count -eq 1 -and $provenance.registeredPlugins[0] -eq './userplugins/widgetRailDesktop') 'Only the WidgetRail plugin may be registered.'
    Check ($provenance.registeredNativeHelpers.Count -eq 1 -and $provenance.registeredNativeHelpers[0] -eq './userplugins/widgetRailDesktop/native') 'Only its native helpers may be registered.'
    $patcher = [IO.File]::ReadAllText((Join-Path $staged 'source/dist/patcher.js'))
    Check (!$patcher.Contains('DANGEROUS_ENABLE_DEVTOOLS_ONLY_ENABLE_IF_YOU_KNOW_WHAT_YOURE_DOING')) 'The diagnostic build must not force-enable Discord developer tools.'
    $patcherMap = Get-Content -LiteralPath (Join-Path $staged 'source/dist/patcher.js.map') -Raw | ConvertFrom-Json
    Check (!@($patcherMap.sources | Where-Object {$_ -match 'persistAfterDiscordUpdates'}).Count) 'The diagnostic build must not contain the automatic host repatcher.'
    Check (!(Test-Path -LiteralPath $provenance.bootstrapProposal.dataDirectory)) 'Staging must not launch Vencord or initialize its settings directory.'
    Check (!$provenance.installPerformed -and !$provenance.launchPerformed) 'Build validation must not be mistaken for installation or launch.'
    if ($provenance.bootstrapProposal.PSObject.Properties.Name -contains 'archivePath') {
        Check ($provenance.bootstrapProposal.roundTripVerified -and $provenance.bootstrapProposal.packagingLibrary -eq '@electron/asar@4.0.1') 'Bootstrap must be packaged and round-trip verified by the pinned upstream library.'
        Check ((Get-FileHash -LiteralPath (Join-Path $staged $provenance.bootstrapProposal.archivePath)).Hash -eq $provenance.bootstrapProposal.archiveSha256) 'Bootstrap archive changed after review.'
    }
    if ($provenance.bootstrapProposal.PSObject.Properties.Name -contains 'missingRuntimeFallback') {
        Check ($provenance.bootstrapProposal.missingRuntimeFallback -eq 'original-sibling-asar-before-hooks' -and !$provenance.bootstrapProposal.partialInitializationFallback) 'Bootstrap must fall back before hooks only, never after partial initialization.'
        Check ((Get-FileHash -LiteralPath (Join-Path $staged $provenance.bootstrapProposal.path)).Hash -eq $provenance.bootstrapProposal.sha256) 'Bootstrap entrypoint changed after review.'
        Check ((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'Bootstrap.cjs')).Hash -eq $provenance.bootstrapProposal.templateSha256) 'The staged bootstrap template is not the reviewed source.'
    }
    foreach ($artifact in $provenance.artifacts) {
        Check ((Get-FileHash -LiteralPath (Join-Path $staged $artifact.path)).Hash -eq $artifact.sha256) 'Staged build artifact differs from recorded provenance.'
    }
}
$result=[pscustomobject]@{passed=$true;checks=$checks;fixture=$fixture;scope='Synthetic files only; no installed client/profile access, modification, launch or installation.'}
$result|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $fixture 'result.json')
$result|ConvertTo-Json
