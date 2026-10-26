param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
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
$old=$env:PATH
try{
 $env:PATH="$($vc.FullName)/bin/Hostx64/x64;$sdkRoot/bin/$($sdk.Name)/x64;$old"
 & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 /permissive- /DUNICODE /D_UNICODE /DNOMINMAX /LD @includes "$PSScriptRoot/LiveCapture.cpp" "/Fo:$OutputDirectory/LiveCapture.obj" "/Fe:$OutputDirectory/WebRtcCapturePrototype.dll" /link @libraries d3d11.lib dxgi.lib d2d1.lib dwmapi.lib user32.lib ole32.lib windowsapp.lib mmdevapi.lib
 if($LASTEXITCODE){throw 'Live capture build failed'}
 & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 /DUNICODE /D_UNICODE @includes "$PSScriptRoot/MediaFixture.cpp" "/Fo:$OutputDirectory/CaptureFixture.obj" "/Fe:$OutputDirectory/WindowCaptureFixture.exe" /link @libraries user32.lib gdi32.lib winmm.lib
 if($LASTEXITCODE){throw 'Capture fixture build failed'}
 & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 @includes "$PSScriptRoot/CadenceProbe.cpp" "/Fo:$OutputDirectory/CadenceProbe.obj" "/Fe:$OutputDirectory/CadenceProbe.exe" /link @libraries
 if($LASTEXITCODE){throw 'Cadence probe build failed'}
}finally{$env:PATH=$old}
