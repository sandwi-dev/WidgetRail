[CmdletBinding()]
param(
    [Parameter(Mandatory)][string[]]$WidgetId,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [switch]$IncludeSettings,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a new isolated workspace directory.' }
$catalogDefinition = Get-Content (Join-Path $repository 'eng/widget-catalog.json') -Raw | ConvertFrom-Json
$widgets = @(foreach ($id in $WidgetId) {
    $entry = @($catalogDefinition.bundledWidgets | Where-Object id -CEQ $id)
    if ($entry.Count -ne 1) { throw "Unknown bundled widget: $id" }
    $entry[0]
})
if ($widgets.Count -eq 0 -or @($WidgetId | Select-Object -Unique).Count -ne $WidgetId.Count) { throw 'Choose distinct bundled widget IDs.' }
$sources = @{}
foreach ($manifestPath in Get-ChildItem (Join-Path $repository 'src/FirstPartyWidgets') -Filter manifest.json -Recurse -File) {
    if ($manifestPath.FullName -match '[\\/](bin|obj)[\\/]') { continue }
    $manifest = Get-Content -LiteralPath $manifestPath.FullName -Raw | ConvertFrom-Json
    if ($widgets.packageId -ccontains $manifest.id) { $sources[$manifest.id] = $manifestPath.DirectoryName }
}
$sources['widgetrail.samples.embedded-media'] = Join-Path $repository 'samples/EmbeddedMediaWidget'
foreach ($widget in $widgets) {
    if (-not $sources.ContainsKey($widget.packageId)) { throw "No first-party source for $($widget.packageId)." }
}
$installation = Join-Path $output 'installation'
$catalog = Join-Path $output 'catalog'
$profile = Join-Path $output 'profile'
$logs = Join-Path $output 'logs'
$null = New-Item -ItemType Directory -Path $installation,$catalog,$profile,$logs -Force
function Publish([string]$Project, [string]$Destination, [string]$Name) {
    & dotnet publish $Project -c $Configuration -r win-x64 --self-contained false -o $Destination `
        -p:ContinuousIntegrationBuild=true -p:CopyOutputSymbolsToPublishDirectory=false `
        "-bl:$logs/$Name-$([guid]::NewGuid().ToString('N')).binlog" *> (Join-Path $logs "$Name.log")
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $Name. See $logs/$Name.log" }
}
Publish (Join-Path $repository 'src/WidgetBridge/WidgetBridge.csproj') (Join-Path $installation 'runtime/Bridge') 'bridge'
Publish (Join-Path $repository 'src/WidgetWorkerHost/WidgetWorkerHost.csproj') (Join-Path $installation 'runtime/WidgetWorkerHost') 'worker'
Publish (Join-Path $repository 'tools/BundledWidgetPackageSeal/BundledWidgetPackageSeal.csproj') (Join-Path $output 'seal') 'seal'
if ($IncludeSettings) {
    $settings = Join-Path $installation 'runtime/Settings'
    Publish (Join-Path $repository 'src/FirstPartyWidgets/SettingsWidget.Worker/SettingsWidget.Worker.csproj') $settings 'settings'
    $source = Join-Path $repository 'src/FirstPartyWidgets/SettingsWidget'
    foreach ($name in @('manifest.json','styles','assets')) {
        $from = Join-Path $source $name
        if (Test-Path -LiteralPath $from -PathType Container) {
            foreach ($file in Get-ChildItem -LiteralPath $from -File -Recurse) {
                $to = Join-Path $settings ([IO.Path]::GetRelativePath($source,$file.FullName))
                New-Item -ItemType Directory -Path (Split-Path $to) -Force | Out-Null
                Copy-Item -LiteralPath $file.FullName -Destination $to -Force
            }
        } else { Copy-Item -LiteralPath $from -Destination $settings -Force }
    }
    New-Item -ItemType Directory -Path (Join-Path $settings 'payload') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $settings 'SettingsWidget.dll') -Destination (Join-Path $settings 'payload/SettingsWidget.dll')
    # Seal exactly the publication that the runtime tests consume.
    & (Join-Path $output 'seal/BundledWidgetPackageSeal.exe') $installation $settings *> (Join-Path $logs 'settings-seal.log')
    if ($LASTEXITCODE -ne 0) { throw 'Trusted Settings sealing failed.' }
}
foreach ($widget in $widgets) {
    $source = $sources[$widget.packageId]
    $manifest = Get-Content (Join-Path $source 'manifest.json') -Raw | ConvertFrom-Json
    $assembly = [IO.Path]::GetFileNameWithoutExtension($manifest.entrypoint.assembly)
    $published = Join-Path $output "published/$($widget.id)"
    $package = [IO.Path]::GetFullPath((Join-Path $installation $widget.packageRoot))
    if (-not $package.StartsWith($installation + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Bundled package path escaped the installation.' }
    $payload = Join-Path $package 'payload'
    $null = New-Item -ItemType Directory -Path $payload -Force
    Publish (Join-Path $source "$assembly.csproj") $published $widget.id
    # Copy the same host-shared boundary as release packaging, without deleting
    # generated trees or mutating any existing candidate/package version.
    foreach ($file in Get-ChildItem -LiteralPath $published) {
        if ($file.Name -in @('WidgetSdk.dll','WidgetProtocol.dll','manifest.json','styles','assets') -or
            $file.Extension -in @('.pdb')) { continue }
        Copy-Item -LiteralPath $file.FullName -Destination $payload -Recurse
    }
    foreach ($name in @('manifest.json','styles','assets')) {
        $path = Join-Path $source $name
        if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $package -Recurse }
    }
    & (Join-Path $output 'seal/BundledWidgetPackageSeal.exe') $installation $package *> (Join-Path $logs "$($widget.id)-seal.log")
    if ($LASTEXITCODE -ne 0) { throw "Bundled integrity sealing failed: $($widget.id)" }
}
@{
    catalogVersion=$catalogDefinition.catalogVersion
    genericWorkerExecutable=$catalogDefinition.genericWorkerExecutable
    widgets=@(if ($IncludeSettings) { $catalogDefinition.widgets })
    bundledWidgets=$widgets
} | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $installation 'widget-catalog.json') -Encoding utf8
$options = Join-Path $output 'shell-options.json'
@{
    InstallationRoot=$installation; SettingsRoot=$profile; InstalledCatalogRoot=$catalog
    InitialWidgetId=$widgets[0].id; LayoutDiagnosticsPath=(Join-Path $output 'layout.json')
} | ConvertTo-Json | Set-Content $options -Encoding utf8
@{ SourceCommit=(& git -C $repository rev-parse HEAD); Configuration=$Configuration; WidgetIds=$WidgetId; ShellConfiguration=$options } |
    ConvertTo-Json | Set-Content (Join-Path $output 'provenance.json') -Encoding utf8
Write-Output $options


