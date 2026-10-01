Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleasePackaging.psm1') -DisableNameChecking

# Publish the unpackaged WinUI application and the private services used by Inno Setup.
function New-WinUiPublishedPayload {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$OutputDirectory)
    $repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $output=Assert-ReleasePath $OutputDirectory
    if(Test-Path -LiteralPath $output){throw 'Choose a fresh publication directory.'}
$properties=[xml](Get-Content -LiteralPath (Join-Path $repository 'eng/WidgetRailRelease.props') -Raw)
$version=[string]$properties.Project.PropertyGroup.WidgetRailReleaseVersion
$fileVersion=[string]$properties.Project.PropertyGroup.WidgetRailFileVersion
$content=Get-Content -LiteralPath (Join-Path $repository 'eng/release-content.json') -Raw | ConvertFrom-Json
$catalog=Get-Content -LiteralPath (Join-Path $repository 'eng/widget-catalog.json') -Raw | ConvertFrom-Json
$build=Join-Path $output 'build';$developer=Join-Path $output 'developer';$logs=Join-Path $output 'logs'
New-Item -ItemType Directory -Path $build,$developer,$logs -Force | Out-Null
$sourceCommit=(& git -C $repository rev-parse HEAD).Trim()
$dirty=@(& git -C $repository status --porcelain --untracked-files=normal).Count -ne 0
Write-ReleaseJson (Join-Path $output 'provenance.json') @{schemaVersion=1;sourceCommit=$sourceCommit;dirty=$dirty;version=$version;registered=$false;launched=$false;trustStoreChanged=$false}
function Publish([string]$Project,[string]$Destination,[string]$Name,[string[]]$Additional=@(),[switch]$SelfContained) {
    $arguments=@('publish',(Join-Path $repository $Project),'-c','Release','-r','win-x64','--self-contained',$(if($SelfContained){'true'}else{'false'}),'-o',$Destination,
        '-m:1','-p:BuildInParallel=false','-nr:false','-p:ContinuousIntegrationBuild=true','-p:CopyOutputSymbolsToPublishDirectory=false',"-bl:$logs/$Name.binlog",'-v:quiet')+$Additional
    & dotnet @arguments *> (Join-Path $logs "$Name.txt")
    if($LASTEXITCODE -ne 0){throw "Publication failed: $Name. See $logs/$Name.txt"}
}
function Copy-Publication([string]$Source,[string]$Destination,[switch]$Widget,[switch]$SkipAssets) {
    foreach($relative in Get-ReleaseFiles $Source){
        if([IO.Path]::GetExtension($relative) -eq '.pdb'){continue}
        if($Widget -and $relative -in @('WidgetSdk.dll','WidgetProtocol.dll')){continue}
        if(($Widget -or $SkipAssets) -and ($relative -eq 'manifest.json' -or $relative -match '^(styles|assets)/')){continue}
        $to=Assert-ReleasePath (Join-Path $Destination $relative) -Within $output
        New-Item -ItemType Directory -Path (Split-Path $to) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $Source $relative) -Destination $to
    }
}
function Copy-Assets([string]$Source,[string]$Destination) {
    foreach($name in @('manifest.json','styles')){
        $path=Join-Path $Source $name
        if(Test-Path -LiteralPath $path){
            $null=Assert-ReleasePath $path -Within $repository
            if(Test-Path -LiteralPath $path -PathType Container){$null=Get-ReleaseFiles $path}
            Copy-Item -LiteralPath $path -Destination $Destination -Recurse
        }
    }
    # Encoded artwork compiled into a widget assembly is not a loose package
    # asset. Copy only declared icon resources, matching the package contract.
    $manifest=Get-Content -LiteralPath (Join-Path $Source 'manifest.json') -Raw | ConvertFrom-Json
    if($manifest.PSObject.Properties.Name -contains 'iconAssets'){
        foreach($icon in $manifest.iconAssets.PSObject.Properties){
            $from=Assert-ReleasePath (Join-Path $Source $icon.Value.path) -Within $Source
            $to=Assert-ReleasePath (Join-Path $Destination $icon.Value.path) -Within $Destination
            New-Item -ItemType Directory -Path (Split-Path $to) -Force | Out-Null
            Copy-Item -LiteralPath $from -Destination $to
        }
    }
}
$platform=Join-Path $output 'native/platform';$preview=Join-Path $output 'native/preview'
& (Join-Path $PSScriptRoot 'Build-OverlayPlatform.ps1') -Configuration Release -OutputDirectory $platform *> (Join-Path $logs 'native-platform.txt')
if($LASTEXITCODE -ne 0){throw 'Native platform build failed.'}
& (Join-Path $PSScriptRoot 'Build-WinUiWindowPreview.ps1') -Configuration Release -OutputDirectory $preview *> (Join-Path $logs 'native-preview.txt')
if($LASTEXITCODE -ne 0){throw 'Native preview build failed.'}
$frontend=Join-Path $output 'frontend-published'
# Release owns trimming/ReadyToRun in the frontend project. A global override
# propagates trim analysis into unrelated service-library project references.
$frontendProperties=@('-p:Platform=x64','-p:EnableWidgetValidation=false',
    '-p:WindowsPackageType=None','-p:WindowsAppSDKSelfContained=true','-p:EnableMsixTooling=true',
    '-p:GenerateAppxPackageOnBuild=false','-p:EnableWinAppRunSupport=false',
    "-p:OverlayPlatformInteropPath=$platform/OverlayPlatformInterop.dll","-p:WindowPreviewNativePath=$preview/WinUiWindowPreviewNative.dll")
$analyzers=Join-Path $env:USERPROFILE '.codex/skills/winui-dev-workflow/analyzer/Microsoft.WindowsAppSDK.Analyzers.targets'
if(Test-Path -LiteralPath $analyzers){$frontendProperties+="-p:CustomAfterMicrosoftCommonTargets=$analyzers"}
Publish 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj' $frontend 'frontend' $frontendProperties -SelfContained
foreach($binary in @('OverlayFrontend.WinUI.exe','OverlayFrontend.WinUI.dll')){
    $metadata=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $frontend $binary))
    if($metadata.FileVersion -cne $fileVersion -or !$metadata.ProductVersion.StartsWith($version,[StringComparison]::Ordinal)){throw "Frontend canonical version mismatch: $binary"}
}
foreach($native in @(@('OverlayPlatformInterop.dll',$platform),@('WinUiWindowPreviewNative.dll',$preview))){
    if((Get-FileHash -LiteralPath (Join-Path $frontend $native[0])).Hash -cne (Get-FileHash -LiteralPath (Join-Path $native[1] $native[0])).Hash){throw "Published native boundary differs: $($native[0])"}
}
$frontendRuntime=Get-Content -LiteralPath (Join-Path $frontend 'OverlayFrontend.WinUI.runtimeconfig.json') -Raw | ConvertFrom-Json
if(!($frontendRuntime.runtimeOptions.includedFrameworks | Where-Object {$_.name -ceq 'Microsoft.NETCore.App' -and $_.version -like '10.*'}) -or
   !(Test-Path -LiteralPath (Join-Path $frontend 'coreclr.dll'))){throw 'Frontend must include its self-contained .NET 10 runtime.'}
foreach($runtimeFile in @('Microsoft.ui.xaml.dll','Microsoft.WindowsAppRuntime.dll','OverlayFrontend.WinUI.pri')){
    if(!(Test-Path -LiteralPath (Join-Path $frontend $runtimeFile) -PathType Leaf)){throw "Frontend must include its self-contained Windows App SDK runtime: $runtimeFile"}
}
foreach($entry in @(@('WidgetBridge','Bridge'),@('WidgetWorkerHost','WidgetWorkerHost'))){
    Publish "src/$($entry[0])/$($entry[0]).csproj" (Join-Path $build "runtime/$($entry[1])") $entry[1]
}
Publish 'tools/BundledWidgetPackageSeal/BundledWidgetPackageSeal.csproj' (Join-Path $output 'seal') 'seal'
Publish 'tools/ReleaseVerifier/ReleaseVerifier.csproj' (Join-Path $output 'verify') 'verify'
& (Join-Path $output 'verify/ReleaseVerifier.exe') --frontend $frontend *> (Join-Path $logs 'frontend-cleanup-verification.txt')
if($LASTEXITCODE -ne 0){throw "Published frontend cleanup verification failed. See $logs/frontend-cleanup-verification.txt"}
$seal=Join-Path $output 'seal/BundledWidgetPackageSeal.exe'
function Seal([string]$Root,[string]$Package,[string]$Name){
    & $seal $Root $Package *> (Join-Path $logs "$Name-seal.txt")
    if($LASTEXITCODE -ne 0){throw "Package sealing failed: $Name"}
}
$settings=Join-Path $build 'runtime/Settings'
Publish 'src/FirstPartyWidgets/SettingsWidget.Worker/SettingsWidget.Worker.csproj' (Join-Path $output 'settings-published') 'settings'
Copy-Publication (Join-Path $output 'settings-published') $settings -SkipAssets
Copy-Assets (Join-Path $repository 'src/FirstPartyWidgets/SettingsWidget') $settings
New-Item -ItemType Directory -Path (Join-Path $settings 'payload') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $settings 'SettingsWidget.dll') -Destination (Join-Path $settings 'payload/SettingsWidget.dll')
Seal $build $settings 'settings'
foreach($relative in @('Bridge/WidgetBridge.dll','WidgetWorkerHost/WidgetWorkerHost.dll','Settings/SettingsWidget.Worker.dll')){
    $metadata=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $build "runtime/$relative"))
    if($metadata.FileVersion -cne $fileVersion -or !$metadata.ProductVersion.StartsWith($version,[StringComparison]::Ordinal)){throw "Managed service canonical version mismatch: $relative"}
}
$sources=@{}
foreach($manifest in Get-ChildItem -LiteralPath (Join-Path $repository 'src/FirstPartyWidgets') -Filter manifest.json -Recurse -File){
    if($manifest.FullName -match '[\\/](bin|obj)[\\/]'){continue}
    $definition=Get-Content -LiteralPath $manifest.FullName -Raw | ConvertFrom-Json
    $sources[$definition.id]=$manifest.DirectoryName
}
$sources['widgetrail.samples.embedded-media']=Join-Path $repository 'samples/EmbeddedMediaWidget'
function Package([string]$Source,[string]$Root,[string]$Relative,[string]$Name){
    $manifest=Get-Content -LiteralPath (Join-Path $Source 'manifest.json') -Raw | ConvertFrom-Json
    $assembly=[IO.Path]::GetFileNameWithoutExtension($manifest.entrypoint.assembly)
    $published=Join-Path $output "published/$Name";$package=Assert-ReleasePath (Join-Path $Root $Relative) -Within $Root
    Publish ([IO.Path]::GetRelativePath($repository,(Join-Path $Source "$assembly.csproj"))) $published $Name
    New-Item -ItemType Directory -Path (Join-Path $package 'payload') -Force | Out-Null
    Copy-Publication $published (Join-Path $package 'payload') -Widget
    Copy-Assets $Source $package
    Seal $Root $package $Name
    return $manifest
}
foreach($widget in $catalog.bundledWidgets){
    if(!$sources.ContainsKey($widget.packageId)){throw "No source for bundled widget $($widget.id)"}
    $null=Package $sources[$widget.packageId] $build $widget.packageRoot $widget.id
}
Write-ReleaseJson (Join-Path $build 'widget-catalog.json') $catalog
$extras=@(foreach($widget in $content.developerWidgets){
    $manifest=Package (Join-Path $repository $widget.project) $developer "runtime/$($widget.runtime)" $widget.id
    [ordered]@{id=$widget.id;packageId=$manifest.id;instanceId="$($widget.id).default";packageRoot="runtime/$($widget.runtime)";icon=$manifest.presentation.icon;quickActions=@()}
})
Write-ReleaseJson (Join-Path $developer 'developer-widgets.json') $extras
Publish 'tools/WrailCli/WrailCli.csproj' (Join-Path $developer 'tools/wrail') 'cli'
& (Join-Path $PSScriptRoot 'Get-ReleaseRuntimes.ps1') -Destination $developer *> (Join-Path $logs 'runtimes.txt')
if($LASTEXITCODE -ne 0){throw 'Runtime preparation failed.'}
$nuget=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path $env:USERPROFILE '.nuget/packages'}
$sdkProject=[xml](Get-Content -LiteralPath (Join-Path $repository 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') -Raw)
$sdkVersion=[string]$sdkProject.SelectSingleNode('//PackageReference[@Include="Microsoft.WindowsAppSDK"]').Version
$licenses=Join-Path $developer 'licenses';New-Item -ItemType Directory -Path $licenses -Force | Out-Null
$runtimeLock=Get-Content -LiteralPath (Join-Path $repository 'eng/runtime-dependencies.json') -Raw | ConvertFrom-Json
foreach($name in @('LICENSE.txt','NOTICE.txt')){Copy-Item -LiteralPath (Join-Path $nuget "microsoft.gameinput/$($runtimeLock.gameInput.version)/$name") -Destination (Join-Path $licenses "Microsoft.GameInput-$name")}
foreach($name in @('license.txt','NOTICE.txt')){Copy-Item -LiteralPath (Join-Path $nuget "microsoft.windowsappsdk/$sdkVersion/$name") -Destination (Join-Path $licenses "Microsoft.WindowsAppSDK-$name")}
Copy-Publication $frontend (Join-Path $build 'frontend')
$publication=[ordered]@{schemaVersion=1;buildRoot=$build;developerRoot=$developer;frontendDirectory=$frontend;sourceCommit=$sourceCommit;dirty=$dirty;version=$version;fileVersion=$fileVersion;verifier=(Join-Path $output 'verify/ReleaseVerifier.exe');packaging='winui-unpackaged';registered=$false;launched=$false}
Write-ReleaseJson (Join-Path $output 'publication.json') $publication
return [pscustomobject]$publication
}
Export-ModuleMember -Function New-WinUiPublishedPayload
