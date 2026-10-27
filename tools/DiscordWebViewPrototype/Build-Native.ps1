param([string]$OutputDirectory,[string]$WebViewPackageDirectory,[switch]$Validation,[switch]$PolicyOnly)
$ErrorActionPreference='Stop'
if($PolicyOnly -and $Validation){throw 'PolicyOnly and Validation are separate build modes.'}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo 'artifacts/discord-webview/native'}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$vsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs=& $vsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vs){throw 'Visual Studio C++ tools are required'}
$vc=Get-ChildItem "$vs/VC/Tools/MSVC" -Directory | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
$sdkRoot=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10'
$sdk=Get-ChildItem "$sdkRoot/Include" -Directory | Where-Object {Test-Path "$($_.FullName)/um/Windows.h"} | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
$includes=@("/I$($vc.FullName)/include")+@('ucrt','shared','um','winrt','cppwinrt'|ForEach-Object{"/I$sdkRoot/Include/$($sdk.Name)/$_"})
$libraries=@("/LIBPATH:$($vc.FullName)/lib/x64","/LIBPATH:$sdkRoot/Lib/$($sdk.Name)/ucrt/x64","/LIBPATH:$sdkRoot/Lib/$($sdk.Name)/um/x64")
if(!$PolicyOnly -and !$WebViewPackageDirectory){
 [xml]$dependencies=Get-Content -Raw (Join-Path $repo 'src/OverlayPlatformInterop/NativeDependencies.csproj')
 $packageVersion=($dependencies.Project.ItemGroup.PackageReference | Where-Object Include -eq 'Microsoft.Web.WebView2').Version
 $packageRoot=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path $env:USERPROFILE '.nuget/packages'}
 $WebViewPackageDirectory=Join-Path $packageRoot "microsoft.web.webview2/$packageVersion"
}
if(!$PolicyOnly){
 $webViewPackage=[IO.Path]::GetFullPath($WebViewPackageDirectory)
 if(!(Test-Path -LiteralPath "$webViewPackage/build/native/include-winrt/WebView2Interop.h")){throw "Restore NativeDependencies.csproj before building capture-worker interop: $webViewPackage"}
}
[string[]]$validationDefines=if($Validation){@('/DCAPTURE_WORKER_VALIDATION')}else{@()}
$old=$env:PATH
try{
 $env:PATH="$($vc.FullName)/bin/Hostx64/x64;$sdkRoot/bin/$($sdk.Name)/x64;$old"
 if($Validation -or $PolicyOnly){
 # These compile the production header policies only. No capture worker, UI,
 # microphone, process audio source or WebView dependency is loaded by CI.
 & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 /DNOMINMAX @includes "$PSScriptRoot/CaptureQualityProbe.cpp" "/Fo:$OutputDirectory/CaptureQualityProbe.obj" "/Fe:$OutputDirectory/CaptureQualityProbe.exe" /link @libraries windowsapp.lib ole32.lib
 if($LASTEXITCODE){throw 'Capture quality probe build failed'}
 & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 /DNOMINMAX @includes "$PSScriptRoot/CaptureAudioQueueProbe.cpp" "/Fo:$OutputDirectory/CaptureAudioQueueProbe.obj" "/Fe:$OutputDirectory/CaptureAudioQueueProbe.exe" /link @libraries windowsapp.lib ole32.lib
 if($LASTEXITCODE){throw 'Capture audio queue probe build failed'}
 }
 if($PolicyOnly){return}
 & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 /permissive- /DUNICODE /D_UNICODE /DNOMINMAX /LD @validationDefines @includes "$PSScriptRoot/LiveCapture.cpp" "/Fo:$OutputDirectory/LiveCapture.obj" "/Fe:$OutputDirectory/WebRtcCapturePrototype.dll" /link @libraries d3d11.lib dxgi.lib d2d1.lib dwmapi.lib user32.lib ole32.lib windowsapp.lib mmdevapi.lib
 if($LASTEXITCODE){throw 'Live capture build failed'}
 if($Validation){
 & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 /DUNICODE /D_UNICODE @includes "$PSScriptRoot/MediaFixture.cpp" "/Fo:$OutputDirectory/CaptureFixture.obj" "/Fe:$OutputDirectory/WindowCaptureFixture.exe" /link @libraries user32.lib gdi32.lib winmm.lib
 if($LASTEXITCODE){throw 'Capture fixture build failed'}
 & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 @includes "$PSScriptRoot/CadenceProbe.cpp" "/Fo:$OutputDirectory/CadenceProbe.obj" "/Fe:$OutputDirectory/CadenceProbe.exe" /link @libraries
 if($LASTEXITCODE){throw 'Cadence probe build failed'}
 }
 & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 /DNOMINMAX @validationDefines @includes "$PSScriptRoot/CaptureWorker.cpp" "/Fo:$OutputDirectory/CaptureWorker.obj" "/Fe:$OutputDirectory/WebRtcCaptureWorker.exe" /link @libraries "$OutputDirectory/WebRtcCapturePrototype.lib" windowsapp.lib ole32.lib
 if($LASTEXITCODE){throw 'Capture worker build failed'}
 & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 /DNOMINMAX /LD @validationDefines @includes "/I$webViewPackage/build/native/include" "/I$webViewPackage/build/native/include-winrt" "$PSScriptRoot/CaptureWorkerHost.cpp" "/Fo:$OutputDirectory/CaptureWorkerHost.obj" "/Fe:$OutputDirectory/CaptureWorkerHost.dll" /link @libraries windowsapp.lib ole32.lib
 if($LASTEXITCODE){throw 'Capture worker host build failed'}
 if($Validation){
 & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 /DNOMINMAX @validationDefines @includes "$PSScriptRoot/CaptureWorkerProbe.cpp" "/Fo:$OutputDirectory/CaptureWorkerProbe.obj" "/Fe:$OutputDirectory/CaptureWorkerProbe.exe" /link @libraries "$OutputDirectory/CaptureWorkerHost.lib" windowsapp.lib ole32.lib user32.lib
 if($LASTEXITCODE){throw 'Capture worker probe build failed'}
 }
}finally{$env:PATH=$old}
