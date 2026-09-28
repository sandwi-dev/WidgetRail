[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [string]$OutputDirectory,
    [switch]$AcceptFullTrust
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $AcceptFullTrust) { throw 'The real Playnite sample is a full-trust application. Review it and pass -AcceptFullTrust to prepare this isolated workspace.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repository ('artifacts/winui-playnite/' + [guid]::NewGuid().ToString('N'))
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
# A new root avoids mutable installed versions and never modifies a user's catalog.
if (Test-Path -LiteralPath $output) { throw "Choose a new output directory: $output" }
New-Item -ItemType Directory -Path $output | Out-Null
$installation = Join-Path $output 'installation'
$catalog = Join-Path $output 'catalog'
$profile = Join-Path $output 'profile'
$logs = Join-Path $output 'logs'
New-Item -ItemType Directory -Path $installation,$catalog,$profile,$logs | Out-Null
function Publish-WorkspaceProject([string]$RelativeProject, [string]$Destination, [string]$Name) {
    & dotnet publish (Join-Path $repository $RelativeProject) -c $Configuration -r win-x64 --self-contained false `
        --output $Destination "-bl:$logs/$Name-$([guid]::NewGuid().ToString('N')).binlog" *> (Join-Path $logs "$Name.log")
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $Name. See $logs/$Name.log" }
}
Publish-WorkspaceProject 'src/WidgetBridge/WidgetBridge.csproj' (Join-Path $installation 'runtime/Bridge') 'bridge'
Publish-WorkspaceProject 'src/WidgetWorkerHost/WidgetWorkerHost.csproj' (Join-Path $installation 'runtime/WidgetWorkerHost') 'worker'
Publish-WorkspaceProject 'src/FirstPartyWidgets/SettingsWidget.Worker/SettingsWidget.Worker.csproj' (Join-Path $installation 'runtime/Settings') 'settings'
Publish-WorkspaceProject 'tools/BundledWidgetPackageSeal/BundledWidgetPackageSeal.csproj' (Join-Path $output 'seal') 'seal'
& (Join-Path $output 'seal/BundledWidgetPackageSeal.exe') $installation (Join-Path $installation 'runtime/Settings') *> (Join-Path $logs 'settings-seal.log')
if ($LASTEXITCODE -ne 0) { throw "Settings integrity sealing failed. See $logs/settings-seal.log" }

# Preserve the production Settings descriptor; other bundled packages are added
# when their corresponding workers are staged, never as missing catalog entries.
$productCatalog = Get-Content -LiteralPath (Join-Path $repository 'src/OverlayHost/widget-catalog.json') -Raw | ConvertFrom-Json
@{
    catalogVersion = $productCatalog.catalogVersion
    genericWorkerExecutable = $productCatalog.genericWorkerExecutable
    widgets = @($productCatalog.widgets | Where-Object id -eq 'settings')
    bundledWidgets = @()
} | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $installation 'widget-catalog.json') -Encoding utf8

$packageOutput = Join-Path $output 'playnite-package'
& (Join-Path $repository 'samples/PlayniteLibraryWidget/Build-CommunityPackage.ps1') `
    -Configuration $Configuration -OutputDirectory $packageOutput *> (Join-Path $logs 'playnite-package.log')
if ($LASTEXITCODE -ne 0) { throw "Playnite packaging failed. See $logs/playnite-package.log" }
$manifest = Get-Content -LiteralPath (Join-Path $repository 'samples/PlayniteLibraryWidget/manifest.json') -Raw | ConvertFrom-Json
$package = Join-Path $packageOutput "$($manifest.id)-$($manifest.version).wrwidget"
$cli = Join-Path $repository "tools/WrailCli/bin/$Configuration/net8.0/wrail.exe"
& $cli install $package --catalog $catalog --accept-full-trust *> (Join-Path $logs 'install.log')
if ($LASTEXITCODE -ne 0) { throw "Isolated package install failed. See $logs/install.log" }
& $cli enable $manifest.id --catalog $catalog --accept-full-trust *> (Join-Path $logs 'enable.log')
if ($LASTEXITCODE -ne 0) { throw "Isolated package enable failed. See $logs/enable.log" }
$options = Join-Path $output 'shell-options.json'
@{
    InstallationRoot = $installation
    SettingsRoot = $profile
    InstalledCatalogRoot = $catalog
    InitialWidgetId = $manifest.id
} | ConvertTo-Json | Set-Content -LiteralPath $options -Encoding utf8
@{
    Configuration = $Configuration
    SourceCommit = (& git -C $repository rev-parse HEAD)
    PackageSha256 = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
    ShellConfiguration = $options
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'provenance.json') -Encoding utf8
Write-Output $options
