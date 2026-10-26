param(
    [Parameter(Mandatory)][string]$NativeHostDirectory,
    [string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$nativeHost=[IO.Path]::GetFullPath($NativeHostDirectory)
foreach($name in @('OverlayPlatformInterop.dll','WinUiWindowPreviewNative.dll')){
    if(!(Test-Path -LiteralPath (Join-Path $nativeHost $name))){throw "Missing native host dependency: $name"}
}
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo ('artifacts/discord-webview/builds/'+[guid]::NewGuid().ToString('N'))}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Choose a fresh output folder; a running prototype must not be overwritten.'}
New-Item -ItemType Directory -Force "$output/logs" | Out-Null
& "$PSScriptRoot/Build-Native.ps1" -OutputDirectory "$output/native"
Push-Location $repo
try{
    & dotnet publish src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj -c Release -m:1 -nr:false -p:UseSharedCompilation=false -p:Platform=x64 -p:EnableWidgetValidation=true -p:PublishTrimmed=false -p:PublishReadyToRun=false "-p:OverlayPlatformInteropPath=$nativeHost/OverlayPlatformInterop.dll" "-p:WindowPreviewNativePath=$nativeHost/WinUiWindowPreviewNative.dll" -o "$output/candidate" "-bl:$output/logs/publish-{}.binlog" -v minimal
    if($LASTEXITCODE){throw 'Prototype host publication failed'}
}finally{Pop-Location}
Copy-Item "$output/native/WebRtcCapturePrototype.dll" "$output/candidate"
& "$PSScriptRoot/Stage.ps1"
[IO.File]::WriteAllText((Join-Path $repo 'artifacts/discord-webview/latest-candidate.txt'),"$output/candidate")
Write-Output "Prototype ready: $output/candidate"
