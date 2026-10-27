param([Parameter(Mandatory)][string]$CandidateDirectory,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$candidate=[IO.Path]::GetFullPath($CandidateDirectory)
$exe=Join-Path $candidate 'OverlayFrontend.WinUI.exe'
$bridge=Join-Path $repo 'src/WidgetBridge/bin/Debug/net8.0-windows10.0.19041.0/win-x64'
$worker=Join-Path $repo 'tests/PinningWidgetFixture/bin/Debug/net8.0'
$seal=Join-Path $repo 'tools/BundledWidgetPackageSeal/bin/Debug/net8.0/BundledWidgetPackageSeal.dll'
foreach($file in @($exe,$seal,(Join-Path $bridge 'WidgetBridge.exe'),(Join-Path $worker 'PinningWidgetFixture.exe'))){if(!(Test-Path -LiteralPath $file)){throw "Build the fixture prerequisites first: $file"}}
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo ('artifacts/discord-webview/pinning-runs/'+[guid]::NewGuid().ToString('N'))}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a fresh evidence directory.'}
$installation=Join-Path $output 'installation'
$profile=Join-Path $output 'profile'
New-Item -ItemType Directory -Force "$installation/runtime",$profile | Out-Null
Copy-Item -LiteralPath $bridge -Destination "$installation/runtime/Bridge" -Recurse
Copy-Item -LiteralPath $worker -Destination "$installation/runtime/Fixture" -Recurse
$widgets=@(foreach($id in @('pinning-gallery','pinning-gallery-peer')){
    $package=Join-Path $installation ('packages/'+$id)
    New-Item -ItemType Directory -Force $package|Out-Null
    Copy-Item -LiteralPath $worker -Destination "$package/payload" -Recurse
    Copy-Item -LiteralPath "$repo/samples/SdkGalleryWidget/assets","$repo/samples/SdkGalleryWidget/styles" -Destination $package -Recurse
    $manifest=Get-Content -LiteralPath "$repo/samples/SdkGalleryWidget/manifest.json" -Raw|ConvertFrom-Json
    $manifest.id='widgetrail.tests.'+$id;$manifest.publisher='widgetrail.tests'
    $manifest|ConvertTo-Json -Depth 20|Set-Content -LiteralPath "$package/manifest.json"
    & dotnet $seal $installation $package|Out-Host
    if($LASTEXITCODE){throw 'Fixture package sealing failed.'}
    @{id=$id;packageId=('widgetrail.tests.'+$id);publisherId='widgetrail.tests';name=$id;instanceId=($id+'.default');icon='settings';
      pinningSupported=$true;fullWidgetPinningSupported=$true;workerExecutable='runtime/Fixture/PinningWidgetFixture.exe';
      iconPackageRoot=('packages/'+$id);styleFile='runtime/Fixture/default.wrss';declaredCapabilities=@();quickActions=@();residencyPolicy=@{schemaVersion=1;mode='keep-alive'}}
})
Copy-Item -LiteralPath "$repo/samples/SdkGalleryWidget/styles/default.wrss" -Destination "$installation/runtime/Fixture/default.wrss"
@{catalogVersion=1;widgets=$widgets;bundledWidgets=@()}|ConvertTo-Json -Depth 10|Set-Content -LiteralPath "$installation/widget-catalog.json"
@{version=1;step='complete';paused=$false}|ConvertTo-Json|Set-Content -LiteralPath "$profile/introduction-progress.json"
@{InstallationRoot=$installation;SettingsRoot=$profile;InstalledCatalogRoot="$output/catalog";InitialWidgetId='pinning-gallery';LayoutDiagnosticsPath="$output/layout.json"}|ConvertTo-Json|Set-Content -LiteralPath "$output/shell.json"
$result=Join-Path $output 'result.json'
$process=Start-Process -FilePath $exe -ArgumentList @(('"--shell-config='+$output+'/shell.json"'),'--shell-no-controller',('"--validate-pin-requests='+$result+'"')) -WindowStyle Hidden -PassThru
try{
    $deadline=[DateTime]::UtcNow.AddSeconds(120)
    while(!(Test-Path -LiteralPath $result)){
        if($process.HasExited){throw "Fixture exited with $($process.ExitCode). Evidence: $output"}
        if([DateTime]::UtcNow -gt $deadline){throw "Pin fixture timed out. Evidence: $output"}
        Start-Sleep -Milliseconds 250
    }
    $data=Get-Content -LiteralPath $result -Raw|ConvertFrom-Json
    if(!$data.passed){throw $data.error}
    if($data.checks.Count -lt 9){throw 'Pin fixture did not complete the expected checks.'}
    $data.checks|ForEach-Object{Write-Output "PASS $_"}
    Write-Output "Evidence: $result"
}finally{
    if(!$process.HasExited){Stop-Process -Id $process.Id}
    $process.Dispose()
}
