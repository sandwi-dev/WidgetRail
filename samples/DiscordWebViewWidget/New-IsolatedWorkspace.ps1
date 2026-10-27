[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration='Debug',
    [string]$OutputDirectory,
    [switch]$AcceptFullTrust
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if(!$AcceptFullTrust){throw 'Review the Discord prototype and pass -AcceptFullTrust to install it in a fresh isolated catalog.'}
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(!$OutputDirectory){$OutputDirectory=Join-Path $repository ('artifacts/discord-live-'+[guid]::NewGuid().ToString('N').Substring(0,8))}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw "Choose a fresh workspace directory: $output"}
$installation=Join-Path $output 'installation'
$catalog=Join-Path $output 'catalog'
$profile=Join-Path $output 'profile'
# Chromium creates further named-profile subdirectories. Avoid the long fixture
# paths that previously caused native profile initialization failures.
if((Join-Path $profile ('WebBrowser/EBWebView/persistent-'+('x'*48))).Length -gt 230){throw 'Use a shorter isolated workspace path for WebView profiles.'}
$logs=Join-Path $output 'logs'
New-Item -ItemType Directory -Force $installation,$catalog,$profile,$logs|Out-Null
function Publish-Project([string]$RelativeProject,[string]$Destination,[string]$Name){
    & dotnet publish (Join-Path $repository $RelativeProject) -c $Configuration -r win-x64 --self-contained false `
        -m:1 -nr:false -p:UseSharedCompilation=false --output $Destination "-bl:$logs/$Name-{}.binlog" *> (Join-Path $logs "$Name.log")
    if($LASTEXITCODE){throw "Publish failed: $Name. See $logs/$Name.log"}
}
Publish-Project 'src/WidgetBridge/WidgetBridge.csproj' "$installation/runtime/Bridge" 'bridge'
Publish-Project 'src/WidgetWorkerHost/WidgetWorkerHost.csproj' "$installation/runtime/WidgetWorkerHost" 'worker'
Publish-Project 'src/FirstPartyWidgets/SettingsWidget.Worker/SettingsWidget.Worker.csproj' "$installation/runtime/Settings" 'settings'
Publish-Project 'tools/BundledWidgetPackageSeal/BundledWidgetPackageSeal.csproj' "$output/seal" 'seal'
Publish-Project 'tools/WrailCli/WrailCli.csproj' "$output/cli" 'cli'
& "$output/seal/BundledWidgetPackageSeal.exe" $installation "$installation/runtime/Settings" *> "$logs/settings-seal.log"
if($LASTEXITCODE){throw "Settings sealing failed. See $logs/settings-seal.log"}
$productCatalog=Get-Content -LiteralPath "$repository/eng/widget-catalog.json" -Raw|ConvertFrom-Json
@{
    catalogVersion=$productCatalog.catalogVersion
    genericWorkerExecutable=$productCatalog.genericWorkerExecutable
    widgets=@($productCatalog.widgets|Where-Object id -eq 'settings')
    bundledWidgets=@()
}|ConvertTo-Json -Depth 20|Set-Content -LiteralPath "$installation/widget-catalog.json" -Encoding utf8

$packageOutput=Join-Path $output 'discord-package'
& "$PSScriptRoot/Build-Package.ps1" -Configuration $Configuration -OutputDirectory $packageOutput *> "$logs/discord-package.log"
$manifest=Get-Content -LiteralPath "$packageOutput/package/manifest.json" -Raw|ConvertFrom-Json
$package=Join-Path $output "$($manifest.id)-$($manifest.version).wrwidget"
$cli=Join-Path $output 'cli/wrail.exe'
& $cli validate "$packageOutput/package" *> "$logs/validate.log"
if($LASTEXITCODE){throw "Package validation failed. See $logs/validate.log"}
& $cli pack "$packageOutput/package" --output $package *> "$logs/pack.log"
if($LASTEXITCODE){throw "Package creation failed. See $logs/pack.log"}
& $cli install $package --catalog $catalog --settings-root $profile --accept-full-trust *> "$logs/install.log"
if($LASTEXITCODE){throw "Isolated install failed. See $logs/install.log"}
& $cli enable $manifest.id --catalog $catalog --accept-full-trust *> "$logs/enable.log"
if($LASTEXITCODE){throw "Isolated enable failed. See $logs/enable.log"}
& $cli list --catalog $catalog --json *> "$logs/catalog.json"
if($LASTEXITCODE){throw "Isolated catalog verification failed. See $logs/catalog.json"}
$options=Join-Path $output 'shell-options.json'
@{
    InstallationRoot=$installation
    SettingsRoot=$profile
    InstalledCatalogRoot=$catalog
    InitialWidgetId=$manifest.id
    LayoutDiagnosticsPath=(Join-Path $output 'layout.json')
}|ConvertTo-Json|Set-Content -LiteralPath $options -Encoding utf8
@'
[CmdletBinding()]
param([Parameter(Mandatory)][string]$FrontendExecutable)
$ErrorActionPreference='Stop'
$frontend=[IO.Path]::GetFullPath($FrontendExecutable)
if(!(Test-Path -LiteralPath $frontend -PathType Leaf)){throw "Build and qualify the current frontend first: $frontend"}
$options=Join-Path $PSScriptRoot 'shell-options.json'
# The caller supplies the qualified frontend; all service/catalog/profile paths
# come from this new workspace. No production profile or credentials are copied.
Start-Process -FilePath $frontend -ArgumentList ('"--shell-config='+$options+'"') -WindowStyle Hidden -PassThru
'@|Set-Content -LiteralPath "$output/Launch.ps1" -Encoding utf8
@{
    Configuration=$Configuration
    SourceCommit=(& git -C $repository rev-parse HEAD)
    PackageSha256=(Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
    ShellConfiguration=$options
    Frontend='Caller must supply a qualified frontend built from this checkout.'
}|ConvertTo-Json|Set-Content -LiteralPath "$output/provenance.json" -Encoding utf8
Write-Output "Prepared: $options"
Write-Output "Launch after host validation: & '$output/Launch.ps1' -FrontendExecutable '<qualified frontend executable>'"
