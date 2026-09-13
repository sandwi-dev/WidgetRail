[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleaseRoot,
    [Parameter(Mandatory)][string]$CompilerPath,
    [string]$OutputRoot
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'ReleasePackaging.psm1') -Force -DisableNameChecking
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$packagingCommit = (& git -C $repository rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or @(& git -C $repository status --porcelain --untracked-files=normal).Count -ne 0) {
    throw 'Installer packaging requires a clean committed checkout.'
}
$ReleaseRoot = Assert-ReleasePath $ReleaseRoot
$CompilerPath = Assert-ReleasePath $CompilerPath
if (!$OutputRoot) { $OutputRoot = Join-Path $repository 'artifacts/installers' }
$OutputRoot = Assert-ReleasePath $OutputRoot
$compilerLibrary = Join-Path ([IO.Path]::GetDirectoryName($CompilerPath)) 'ISCmplr.dll'
if ((Get-FileHash -LiteralPath $CompilerPath).Hash -ne '0A8757031B33777E4C9CBFFEE40F11A5062B36D25CBE144C1DB73B6102B80AD7' -or
    (Get-FileHash -LiteralPath $compilerLibrary).Hash -ne '85A1E3090D3A5B85319F001B7C8F9ECFAD45F37EFF030A67BBE29EF58B7AA2C3' -or
    (Get-AuthenticodeSignature -LiteralPath $CompilerPath).Status -ne 'Valid') {
    throw 'Use the signed Inno Setup 6.7.3 compiler. No tools are installed by this script.'
}
$roots = @(Get-ChildItem -LiteralPath $ReleaseRoot -Directory)
if ($roots.Count -ne 2) { throw 'Supply the version folder containing both verified release editions.' }
$manifests = @{}
foreach ($root in $roots) {
    Test-ReleaseInventory $root.FullName
    $manifest = Get-Content -LiteralPath (Join-Path $root.FullName 'release.json') -Raw | ConvertFrom-Json
    if ($manifest.edition -notin @('production', 'developer') -or $manifests.ContainsKey($manifest.edition) -or
        $manifest.version -notmatch '^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.]+)?$' -or
        $manifest.sourceCommit -notmatch '^[0-9a-f]{40}$' -or
        $manifest.packagingCommit -notmatch '^[0-9a-f]{40}$' -or $manifest.architecture -ne 'x64') {
        throw 'Invalid installer release identity.'
    }
    $manifests[$manifest.edition] = @{ Root = $root.FullName; Manifest = $manifest }
}
$production = $manifests.production.Manifest
$developer = $manifests.developer.Manifest
if ($production.version -cne $developer.version -or $production.sourceCommit -cne $developer.sourceCommit -or
    $production.packagingCommit -cne $developer.packagingCommit) {
    throw 'Both editions must come from the same release source.'
}
$final = Assert-ReleasePath (Join-Path $OutputRoot $production.version) -Within $OutputRoot
if (Test-Path -LiteralPath $final) { throw 'Installer destination already exists.' }
$stage = Join-Path $OutputRoot ('.staging-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
# Inno's source scanner still has MAX_PATH constraints. A short private staging
# directory supports repositories in long Codex/worktree paths as well.
$work = Join-Path ([IO.Path]::GetTempPath()) ('wrail-setup-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -Force | Out-Null
function Quoted([string]$Value) {
    if ($Value.Contains('"') -or $Value.Contains("`r") -or $Value.Contains("`n")) { throw 'Unsupported installer value.' }
    return '"' + $Value + '"'
}
foreach ($edition in @('production', 'developer')) {
    $entry = $manifests[$edition]
    $manifest = $entry.Manifest
    $editionWork = Join-Path $work $edition
    New-Item -ItemType Directory -Path $editionWork | Out-Null
    $payload = Join-Path $editionWork 'payload'
    Copy-ReleaseTree $entry.Root $payload $editionWork
    Test-ReleaseInventory $payload
    $display = if ($edition -eq 'developer') { 'WidgetRail Developer' } else { 'WidgetRail' }
    $name = $display.Replace(' ', '-') + '-' + $manifest.version + '-win-x64-setup'
    $payloadId = $manifest.version + '-' + $edition + '-' + $manifest.sourceCommit.Substring(0, 12) + '-' + $manifest.packagingCommit.Substring(0, 12)
    $dependencies = Get-Content (Join-Path $payload 'prerequisites/runtime-dependencies.json') -Raw | ConvertFrom-Json
    # Runtime compatibility is independent of the redistributable package version.
    $requirements = Get-Content (Join-Path $repository 'eng/installer/requirements.json') -Raw | ConvertFrom-Json
    $gameVersion = [version]$requirements.gameInputMinimumFileVersion
    if ($gameVersion -lt [version]'3.3.221.0' -or $gameVersion -gt [version]$dependencies.gameInput.fileVersion) {
        throw 'Invalid GameInput minimum runtime version.'
    }
    foreach ($redist in @(@{Name='GameInputRedist.msi';Hash=$dependencies.gameInput.sha256}, @{Name='MicrosoftEdgeWebview2Setup.exe';Hash=$dependencies.webView2.sha256})) {
        $path = Join-Path $payload ('prerequisites/' + $redist.Name)
        if ((Get-FileHash $path).Hash -ine $redist.Hash -or (Get-AuthenticodeSignature $path).Status -ne 'Valid') { throw 'Prerequisite integrity/signature check failed.' }
    }
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($pair in @{
        DisplayName = $display; AppVersion = $manifest.version; OutputDirectory = $stage
        InstallerName = $name; PayloadId = $payloadId; Edition = $edition
    }.GetEnumerator()) { $lines.Add('#define ' + $pair.Key + ' ' + (Quoted $pair.Value)) }
    $lines.Add('#define GameInputVersionMS ' + (($gameVersion.Major -shl 16) + $gameVersion.Minor))
    $lines.Add('#define GameInputVersionLS ' + (($gameVersion.Build -shl 16) + $gameVersion.Revision))
    $lines.Add('[Files]')
    $installerFiles = @($manifest.files.path) + @('release.json') | Sort-Object @{ Expression = { if ($_ -like 'prerequisites/*') { 0 } else { 1 } } }, @{ Expression = { $_ } }
    foreach ($relative in $installerFiles) {
        $source = Assert-ReleasePath (Join-Path $payload $relative) -Within $payload
        $directory = [IO.Path]::GetDirectoryName($relative.Replace('/', '\'))
        $destination = '{app}\versions\' + $payloadId
        if ($directory) { $destination += '\' + $directory }
        $lines.Add('Source: ' + (Quoted $source) + '; DestDir: ' + (Quoted $destination) + '; Flags: ignoreversion')
    }
    [IO.File]::WriteAllLines((Join-Path $editionWork 'Payload.iss'), $lines, [Text.UTF8Encoding]::new($true))
    Copy-Item -LiteralPath (Join-Path $repository 'eng/installer/WidgetRail.iss') -Destination $editionWork
    Copy-Item -LiteralPath (Join-Path $repository 'eng/installer/UninstallData.iss') -Destination $editionWork
    Copy-Item -LiteralPath (Join-Path $repository 'assets/branding/widgetrail.ico') -Destination $editionWork
    & $CompilerPath /Qp (Join-Path $editionWork 'WidgetRail.iss')
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed for $edition. Staging retained at $stage" }
    Test-ReleaseInventory $entry.Root
    Test-ReleaseInventory $payload
}
if ((& git -C $repository rev-parse HEAD).Trim() -cne $packagingCommit -or
    @(& git -C $repository status --porcelain --untracked-files=normal).Count -ne 0) {
    throw 'Installer source changed during compilation. Outputs remain staged.'
}
$checksums = @(Get-ChildItem -LiteralPath $stage -Filter '*.exe' | Sort-Object Name | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name
})
[IO.File]::WriteAllLines((Join-Path $stage 'SHA256SUMS.txt'), $checksums)
Write-ReleaseJson (Join-Path $stage 'installer-build.json') ([ordered]@{
    version = $production.version; sourceCommit = $production.sourceCommit
    packagingCommit = $packagingCommit
    compiler = 'Inno Setup 6.7.3'; compilerSha256 = (Get-FileHash -LiteralPath $CompilerPath).Hash
    signed = $false; scope = 'per-user'; runtimeProvisioning = 'private-dotnet-and-microsoft-prerequisite-installers'
})
[IO.Directory]::Move($stage, $final)
Write-Output "Installers ready: $final"
