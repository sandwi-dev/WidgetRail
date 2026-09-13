[CmdletBinding()]
param(
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'ReleasePackaging.psm1') -Force -DisableNameChecking
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$null = Assert-ReleasePath $repository
if (!$OutputRoot) { $OutputRoot = Join-Path $repository 'artifacts/releases' }
$OutputRoot = Assert-ReleasePath $OutputRoot
$properties = [xml](Get-Content -LiteralPath (Join-Path $repository 'eng/WidgetRailRelease.props') -Raw)
$version = [string]$properties.Project.PropertyGroup.WidgetRailReleaseVersion
$fileVersion = [string]$properties.Project.PropertyGroup.WidgetRailFileVersion
$sdkProperties = [xml](Get-Content -LiteralPath (Join-Path $repository 'eng/WidgetSdkRelease.props') -Raw)
$sdkVersion = [string]$sdkProperties.Project.PropertyGroup.WidgetSdkReleaseVersion
$content = Get-Content -LiteralPath (Join-Path $repository 'eng/release-content.json') -Raw | ConvertFrom-Json

function Get-SourceCommit {
    $commit = (& git -C $repository rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw 'Cannot establish release source revision.' }
    $status = @(& git -C $repository status --porcelain --untracked-files=normal)
    if ($LASTEXITCODE -ne 0 -or $status.Count -ne 0) { throw 'Release folders require a clean committed checkout.' }
    return $commit
}

$commit = Get-SourceCommit
if (Test-Path -LiteralPath (Join-Path $OutputRoot $version)) { throw "Release $version already exists in $OutputRoot." }
$buildRoot = Join-Path $repository 'src/OverlayHost/out/Release'
# The release build must never replace the executable of a running candidate.
$running = @(Get-CimInstance Win32_Process -Filter "Name='OverlayHost.exe'" |
    Where-Object { $_.ExecutablePath -and $_.ExecutablePath.Equals((Join-Path $buildRoot 'OverlayHost.exe'), [StringComparison]::OrdinalIgnoreCase) })
if ($running.Count -ne 0) { throw 'Close the host running from this checkout before building release folders.' }
$work = Join-Path $repository ('artifacts/release-build/' + [guid]::NewGuid().ToString('N'))
$null = Assert-ReleasePath $work -Within $repository
New-Item -ItemType Directory -Path $work -Force | Out-Null

$lockPath = Join-Path $repository 'artifacts/release-build.lock'
try { $lease = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
catch { throw 'Another release build owns this checkout, or its build lock is unavailable.' }
try {

function Publish-ReleaseProject {
    param([string]$Project, [string]$Destination)
    $binlog = Join-Path $work ([guid]::NewGuid().ToString('N') + '.binlog')
    & dotnet publish (Join-Path $repository $Project) --configuration Release --no-self-contained --output $Destination `
        -m:1 -p:BuildInParallel=false -nr:false -p:ContinuousIntegrationBuild=true `
        -p:CopyOutputSymbolsToPublishDirectory=false "-bl:$binlog" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Release publish failed: $Project" }
}

& pwsh -NoProfile -File (Join-Path $repository 'src/OverlayHost/build.ps1') -Configuration Release -SkipTests -SkipPackaging
if ($LASTEXITCODE -ne 0) { throw 'Native/runtime release build failed.' }
if ((Get-SourceCommit) -cne $commit) { throw 'Source revision changed during the release build.' }
$hostVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $buildRoot 'OverlayHost.exe'))
if ($hostVersion.ProductVersion -cne $version -or $hostVersion.FileVersion -cne $fileVersion) {
    throw 'Native application version differs from the canonical release version.'
}
foreach ($managed in @('Bridge/WidgetBridge.dll', 'WidgetWorkerHost/WidgetWorkerHost.dll', 'Settings/SettingsWidget.Worker.dll')) {
    $metadata = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $buildRoot "runtime/$managed"))
    if ($metadata.FileVersion -cne $fileVersion -or !$metadata.ProductVersion.StartsWith($version, [StringComparison]::Ordinal)) {
        throw "Managed application version differs: $managed"
    }
}

$developer = Join-Path $work 'developer'
New-Item -ItemType Directory -Path $developer | Out-Null
Publish-ReleaseProject 'tools/WrailCli/WrailCli.csproj' (Join-Path $developer 'tools/wrail')
Publish-ReleaseProject 'tools/BundledWidgetPackageSeal/BundledWidgetPackageSeal.csproj' (Join-Path $work 'seal')
Publish-ReleaseProject 'tools/ReleaseVerifier/ReleaseVerifier.csproj' (Join-Path $work 'verify')
$verifier = Join-Path $work 'verify/ReleaseVerifier.exe'
$seal = Join-Path $work 'seal/BundledWidgetPackageSeal.exe'
$extra = @(foreach ($widget in $content.developerWidgets) {
    $package = Join-Path $developer "runtime/$($widget.runtime)"
    $payload = Join-Path $package 'payload'
    $project = "$($widget.project)/$($widget.assembly).csproj"
    Publish-ReleaseProject $project $payload
    foreach ($relative in @(Get-ReleaseFiles $payload | Where-Object { [IO.Path]::GetExtension($_) -eq '.pdb' })) {
        $symbol = Assert-ReleasePath (Join-Path $payload $relative) -Within $work
        Remove-Item -LiteralPath $symbol -Force
    }
    foreach ($name in @('WidgetSdk.dll', 'WidgetProtocol.dll', 'manifest.json')) {
        $path = Assert-ReleasePath (Join-Path $payload $name) -Within $work
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
    $copiedStyles = Assert-ReleasePath (Join-Path $payload 'styles') -Within $work
    if (Test-Path -LiteralPath $copiedStyles) { Remove-Item -LiteralPath $copiedStyles -Recurse -Force }
    foreach ($path in @('manifest.json', 'styles', 'assets')) {
        $source = Join-Path $repository "$($widget.project)/$path"
        if (Test-Path -LiteralPath $source) {
            $null = Assert-ReleasePath $source -Within $repository
            # Check descendants before copying any package asset tree.
            if (Test-Path -LiteralPath $source -PathType Container) { $null = Get-ReleaseFiles $source }
            Copy-Item -LiteralPath $source -Destination $package -Recurse -Force
        }
    }
    & $seal $developer $package | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Developer package seal failed: $($widget.id)" }
    $manifest = Get-Content -LiteralPath (Join-Path $package 'manifest.json') -Raw | ConvertFrom-Json
    [ordered]@{
        id = $widget.id; packageId = $manifest.id; instanceId = "$($widget.id).default"
        packageRoot = "runtime/$($widget.runtime)"; icon = $manifest.presentation.icon; quickActions = @()
    }
})
Write-ReleaseJson (Join-Path $developer 'developer-widgets.json') @($extra)
& (Join-Path $PSScriptRoot 'Get-ReleaseRuntimes.ps1') -Destination $developer
$licenses = Join-Path $developer 'licenses'
New-Item -ItemType Directory -Path $licenses | Out-Null
$nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
$nativeDependencies = [xml](Get-Content -LiteralPath (Join-Path $repository 'src/OverlayHost/NativeDependencies.csproj') -Raw)
$gameInput = @($nativeDependencies.Project.ItemGroup.PackageReference | Where-Object Include -EQ 'Microsoft.GameInput')[0]
foreach ($name in @('LICENSE.txt', 'NOTICE.txt')) {
    $source = Join-Path $nuget "microsoft.gameinput/$($gameInput.Version)/$name"
    $null = Assert-ReleasePath $source
    Copy-Item -LiteralPath $source -Destination (Join-Path $licenses "Microsoft.GameInput-$name")
}
if ((Get-SourceCommit) -cne $commit) { throw 'Source revision changed during developer publication.' }
$result = New-WidgetRailReleaseFolders -BuildRoot $buildRoot -DeveloperRoot $developer `
    -RepositoryRoot $repository -OutputRoot $OutputRoot -Version $version `
    -SourceCommit $commit -SdkVersion $sdkVersion -Content $content -VerifyCatalog {
        param($folder)
        & $verifier $folder | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Real catalog validation failed: $folder" }
    } -BeforePublish {
        if ((Get-SourceCommit) -cne $commit) { throw "Source changed before release publication." }
    }
Write-Output "Release folders created: $result"
Write-Output 'No installer, startup registration, signing, upload or public release was performed.'

} finally { $lease.Dispose() }
