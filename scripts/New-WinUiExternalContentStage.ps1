[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$FrontendDirectory,
    [Parameter(Mandatory)][string]$InstallationDirectory,
    [Parameter(Mandatory)][string]$GeneratedManifest,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.-]{2,49}$')][string]$PackageName = 'WidgetRail.WinUI.ExternalProbe',
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$')][string]$Version = '0.1.0.0'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# Offline staging only: this script does not build/publish, register, launch,
# sign, change certificate trust, alter startup entries, or write source payloads.
function FullPath([string]$Path) { [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Path)) }
function Within([string]$Child, [string]$Parent) {
    $Child.Equals($Parent, [StringComparison]::OrdinalIgnoreCase) -or
        $Child.StartsWith($Parent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}
function CheckNoLinks([string]$Path) {
    $entry = Get-Item -LiteralPath $Path -Force
    while ($null -ne $entry) {
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reparse-point path is not admitted: $($entry.FullName)" }
        $entry = if ($entry -is [IO.DirectoryInfo]) { $entry.Parent } else { $entry.Directory }
    }
}
function CheckTree([string]$Root) {
    CheckNoLinks $Root
    foreach ($entry in Get-ChildItem -LiteralPath $Root -Recurse -Force) {
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reparse-point input is not admitted: $($entry.FullName)" }
    }
}
function Xml([string]$Path) {
    $settings = [Xml.XmlReaderSettings]::new(); $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $reader = [Xml.XmlReader]::Create($Path, $settings)
    try { $document = [Xml.XmlDocument]::new(); $document.XmlResolver = $null; $document.Load($reader); return ,$document }
    finally { $reader.Dispose() }
}
function RelativeFile([string]$Root, [string]$Relative) {
    if ([IO.Path]::IsPathRooted($Relative)) { throw "Expected a payload-relative path: $Relative" }
    $path = FullPath (Join-Path $Root $Relative)
    if (-not (Within $path $Root) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Payload file is missing or escapes its root: $Relative" }
    return $path
}
function Invoke-WinApp([string[]]$Arguments, [string]$Log) {
    & winapp @Arguments *> $Log
    if ($LASTEXITCODE -ne 0) { throw "WinApp failed. See $Log" }
}
$frontend = FullPath $FrontendDirectory
$installation = FullPath $InstallationDirectory
$sourceManifest = FullPath $GeneratedManifest
$output = FullPath $OutputDirectory
foreach ($source in @($frontend, $installation)) {
    if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw "Missing source directory: $source" }
    CheckTree $source
    if ((Within $output $source) -or (Within $source $output)) { throw 'Output must not overlap either input payload.' }
}
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh staging directory; existing content is never replaced.' }
$parent = Split-Path $output
while (-not (Test-Path -LiteralPath $parent)) { $parent = Split-Path $parent }
CheckNoLinks $parent
$protectedRoot = Join-Path $env:ProgramFiles 'WindowsApps'
if (Within $output (FullPath $protectedRoot)) { throw 'External content must not be staged in protected WindowsApps storage.' }
CheckNoLinks $sourceManifest
$buildManifest = Xml $sourceManifest
$ns = [Xml.XmlNamespaceManager]::new($buildManifest.NameTable)
$foundation = 'http://schemas.microsoft.com/appx/manifest/foundation/windows10'
$uap10 = 'http://schemas.microsoft.com/appx/manifest/uap/windows10/10'
$uap = 'http://schemas.microsoft.com/appx/manifest/uap/windows10'
$ns.AddNamespace('p', $foundation)
$applications = @($buildManifest.SelectNodes('/p:Package/p:Applications/p:Application', $ns))
if ($applications.Count -ne 1 -or $applications[0].Executable -cne 'OverlayFrontend.WinUI.exe') { throw 'Use the resolved generated WinUI manifest for the matching frontend build.' }
foreach ($reserved in @('runtime','dotnet','widget-catalog.json')) {
    if (Test-Path -LiteralPath (Join-Path $frontend $reserved)) { throw 'Use frontend-only build output; runtime/catalog content comes from the separate installation input.' }
}
$null = RelativeFile $frontend 'OverlayFrontend.WinUI.exe'
$null = RelativeFile $frontend 'OverlayFrontend.WinUI.dll'
$null = RelativeFile $frontend 'OverlayFrontend.WinUI.runtimeconfig.json'
$null = RelativeFile $frontend 'resources.pri'
$null = RelativeFile $installation 'widget-catalog.json'
$null = RelativeFile $installation 'runtime/Bridge/WidgetBridge.exe'
$catalog = Get-Content -LiteralPath (Join-Path $installation 'widget-catalog.json') -Raw | ConvertFrom-Json -AsHashtable
$catalogPaths = [Collections.Generic.List[string]]::new()
if ($catalog.ContainsKey('genericWorkerExecutable')) { $catalogPaths.Add($catalog.genericWorkerExecutable) }
foreach ($widget in $catalog['widgets']) {
    foreach ($key in @('workerExecutable','styleFile','iconPackageRoot')) {
        if ($widget.ContainsKey($key) -and $widget[$key]) { $catalogPaths.Add($widget[$key]) }
    }
}
foreach ($widget in $catalog['bundledWidgets']) { $catalogPaths.Add($widget.packageRoot) }
foreach ($relative in $catalogPaths) {
    if ([IO.Path]::IsPathRooted($relative)) { throw 'Catalog payload references must be relative for relocatable external staging.' }
    $candidate = FullPath (Join-Path $installation $relative)
    if (-not (Within $candidate $installation) -or -not (Test-Path -LiteralPath $candidate)) { throw 'Catalog payload reference is missing or escapes its installation.' }
}
$dependencies = @($buildManifest.SelectNodes('/p:Package/p:Dependencies/p:PackageDependency', $ns))
if (-not ($dependencies | Where-Object { $_.Name -like 'Microsoft.WindowsAppRuntime.*' })) { throw 'Generated manifest is missing its Windows App Runtime dependency.' }
$buildIdentity = $buildManifest.SelectSingleNode('/p:Package/p:Identity', $ns)
if ($buildIdentity.Name -eq $PackageName) { throw 'Use a separate evaluation identity rather than the existing frontend package identity.' }
$versionParts = $Version.Split('.') | ForEach-Object { [uint32]$_ }
if (@($versionParts | Where-Object { $_ -gt 65535 }).Count -gt 0) { throw 'Package version components must fit the MSIX version range.' }
$toolVersion = (& winapp --version).Trim()
if ($LASTEXITCODE -ne 0 -or [version]$toolVersion -lt [version]'0.7.0') { throw 'WinApp 0.7 or newer is required.' }
$payload = Join-Path $output 'external'
$identity = Join-Path $output 'identity'
$logs = Join-Path $output 'logs'
$null = New-Item -ItemType Directory -Path $payload,$identity,$logs,(Join-Path $output 'profile') -Force
foreach ($entry in Get-ChildItem -LiteralPath $frontend -Force) {
    if ($entry.Name -in @('AppX','publish','AppxManifest.xml','Package.appxmanifest') -or $entry.Extension -in @('.msix','.msixbundle','.pfx','.cer')) { continue }
    Copy-Item -LiteralPath $entry.FullName -Destination $payload -Recurse
}
# The trusted Bridge/worker payload is external too: per-AppContainer DACL grants
# must target owned files, never the protected identity package directory.
Copy-Item -LiteralPath (Join-Path $installation 'runtime') -Destination (Join-Path $payload 'runtime') -Recurse
Copy-Item -LiteralPath (Join-Path $installation 'widget-catalog.json') -Destination $payload
if (Test-Path -LiteralPath (Join-Path $installation 'dotnet')) {
    Copy-Item -LiteralPath (Join-Path $installation 'dotnet') -Destination (Join-Path $payload 'dotnet') -Recurse
}
$exe = Join-Path $payload 'OverlayFrontend.WinUI.exe'
$originalExeHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
$sourceAssemblyHash = (Get-FileHash -LiteralPath (Join-Path $frontend 'OverlayFrontend.WinUI.dll') -Algorithm SHA256).Hash
Invoke-WinApp @('manifest','generate',$identity,'--template','sparse','--package-name',$PackageName,'--publisher-name',$buildIdentity.Publisher,
    '--version',$Version,'--description','WidgetRail external-content deployment evaluation','--entrypoint',$exe) (Join-Path $logs 'manifest.log')
$manifestPath = Join-Path $identity 'Package.appxmanifest'
$manifest = Xml $manifestPath
$manager = [Xml.XmlNamespaceManager]::new($manifest.NameTable)
$manager.AddNamespace('p',$foundation); $manager.AddNamespace('uap',$uap)
$application = $manifest.SelectSingleNode('/p:Package/p:Applications/p:Application',$manager)
$application.SetAttribute('Id','App')
$application.SetAttribute('Executable','OverlayFrontend.WinUI.exe')
$packageDependencies = $manifest.SelectSingleNode('/p:Package/p:Dependencies',$manager)
foreach ($dependency in $dependencies) { $null=$packageDependencies.AppendChild($manifest.ImportNode($dependency,$true)) }
$desktop = $packageDependencies.SelectSingleNode('p:TargetDeviceFamily',$manager)
$sourceDesktop = $buildManifest.SelectSingleNode('/p:Package/p:Dependencies/p:TargetDeviceFamily[@Name="Windows.Desktop"]',$ns)
if ($null -ne $sourceDesktop) {
    if ([version]$sourceDesktop.MinVersion -gt [version]$desktop.MinVersion) { $desktop.SetAttribute('MinVersion',$sourceDesktop.MinVersion) }
    $desktop.SetAttribute('MaxVersionTested',$sourceDesktop.MaxVersionTested)
}
# WinApp's sparse template requests elevation by default. This evaluation does
# not need elevation and must not add that capability to the application.
foreach ($capability in @($manifest.SelectNodes('/p:Package/p:Capabilities/*',$manager))) {
    if ($capability.GetAttribute('Name') -eq 'allowElevation') { $null=$capability.ParentNode.RemoveChild($capability) }
}
$extensions = $buildManifest.SelectSingleNode('/p:Package/p:Extensions',$ns)
if ($null -ne $extensions) {
    foreach ($extension in $extensions.ChildNodes) {
        if ($extension.NodeType -ne [Xml.XmlNodeType]::Element) { continue }
        if ($extension.GetAttribute('Category') -ne 'windows.activatableClass.inProcessServer') { throw 'Unexpected activation extension requires a separate deployment review.' }
        foreach ($path in $extension.SelectNodes('.//p:Path',$ns)) { $null=RelativeFile $payload $path.InnerText }
    }
    $null=$manifest.DocumentElement.AppendChild($manifest.ImportNode($extensions,$true))
}
# Give sparse identity logos literal external paths, independent of the WinUI PRI.
Copy-Item -LiteralPath (Join-Path $identity 'Assets') -Destination (Join-Path $payload 'IdentityAssets') -Recurse
$manifest.SelectSingleNode('/p:Package/p:Properties/p:Logo',$manager).InnerText = 'IdentityAssets\StoreLogo.png'
$visual = $application.SelectSingleNode('uap:VisualElements',$manager)
$visual.SetAttribute('Square150x150Logo','IdentityAssets\MedTile.png')
$visual.SetAttribute('Square44x44Logo','IdentityAssets\AppList.png')
$manifest.Save($manifestPath)
Invoke-WinApp @('embed-identity',$exe,'--manifest',$manifestPath) (Join-Path $logs 'embed.log')
$embedded = Join-Path $output 'embedded-app.manifest'
Invoke-WinApp @('tool','mt',('-inputresource:'+ $exe + ';#1'),('-out:'+ $embedded)) (Join-Path $logs 'extract.log')
$embeddedDocument = Xml $embedded
$embeddedManager = [Xml.XmlNamespaceManager]::new($embeddedDocument.NameTable)
$embeddedManager.AddNamespace('asm','urn:schemas-microsoft-com:asm.v1')
$embeddedManager.AddNamespace('msix','urn:schemas-microsoft-com:msix.v1')
$binding = $embeddedDocument.SelectSingleNode('/asm:assembly/msix:msix',$embeddedManager)
if ($null -eq $binding -or $binding.packageName -cne $PackageName -or $binding.publisher -cne $buildIdentity.Publisher -or $binding.applicationId -cne 'App') { throw 'Embedded executable identity does not match the sparse package.' }
$package = Join-Path $output ($PackageName + '_' + $Version + '.msix')
Invoke-WinApp @('package',$manifestPath,'--no-sign','--output',$package) (Join-Path $logs 'package.log')
$zip = [IO.Compression.ZipFile]::OpenRead($package)
try {
    $members = @($zip.Entries.FullName)
    if ($members -contains 'AppxSignature.p7x' -or @($members | Where-Object { $_ -match '\.(exe|dll)$' }).Count -gt 0) { throw 'Evaluation package must be unsigned and identity-only.' }
} finally { $zip.Dispose() }
$assemblyHash = (Get-FileHash -LiteralPath (Join-Path $payload 'OverlayFrontend.WinUI.dll') -Algorithm SHA256).Hash
if ($assemblyHash -cne $sourceAssemblyHash -or $originalExeHash -cne (Get-FileHash -LiteralPath (Join-Path $frontend 'OverlayFrontend.WinUI.exe') -Algorithm SHA256).Hash) { throw 'Source payload changed during staging.' }
$frameworks = foreach ($config in @('OverlayFrontend.WinUI.runtimeconfig.json','runtime/Bridge/WidgetBridge.runtimeconfig.json','runtime/WidgetWorkerHost/WidgetWorkerHost.runtimeconfig.json')) {
    $configPath=Join-Path $payload $config
    if(Test-Path -LiteralPath $configPath) { @{path=$config; runtimeOptions=(Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json).runtimeOptions} }
}
$receipt = @{
    schemaVersion=1; winAppVersion=$toolVersion; packageName=$PackageName; publisher=$buildIdentity.Publisher; applicationId='App'; version=$Version
    sourceFrontend=$frontend; sourceInstallation=$installation; sourceManifest=$sourceManifest
    externalLocation=$payload; manifest=$manifestPath; unsignedPackage=$package; packageMembers=$members
    assemblySha256=$assemblyHash; originalExeSha256=$originalExeHash; embeddedExeSha256=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    minimumOs=$desktop.MinVersion; packageDependencies=@($dependencies | ForEach-Object { @{name=$_.Name;minimumVersion=$_.MinVersion;publisher=$_.Publisher} })
    runtimeRequirements=@($frameworks); launchArguments=@(('--settings-root='+(Join-Path $output 'profile')),('--installed-catalog-root='+(Join-Path $output 'profile/widgets')))
    registered=$false; launched=$false; signed=$false; trustStoreChanged=$false
}
$receipt | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'stage.json') -Encoding utf8
Write-Output (Join-Path $output 'stage.json')
