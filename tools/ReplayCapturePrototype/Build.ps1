param([string]$OutputDirectory,[string]$AcquisitionDirectory,[switch]$MediaPlayerMoveOnly)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$OutputDirectory) { $OutputDirectory=Join-Path $repo 'artifacts/replay-prototype' }
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$vsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs=& $vsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'Visual Studio C++ tools are required' }
$vc=Get-ChildItem "$vs/VC/Tools/MSVC" -Directory | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
$sdkRoot=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10'
$sdk=Get-ChildItem "$sdkRoot/Include" -Directory | Where-Object {Test-Path "$($_.FullName)/um/Windows.h"} | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
$includes=@("/I$($vc.FullName)/include")+@('ucrt','shared','um','winrt','cppwinrt'|ForEach-Object{"/I$sdkRoot/Include/$($sdk.Name)/$_"})
$libraries=@("/LIBPATH:$($vc.FullName)/lib/x64","/LIBPATH:$sdkRoot/Lib/$($sdk.Name)/ucrt/x64","/LIBPATH:$sdkRoot/Lib/$($sdk.Name)/um/x64")
$savedPath=$env:PATH
try {
    $env:PATH="$($vc.FullName)/bin/Hostx64/x64;$sdkRoot/bin/$($sdk.Name)/x64;$savedPath"
    & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 /DNOMINMAX @includes "$PSScriptRoot/MediaPlayerFileMoveProbe.cpp" "/Fo:$OutputDirectory/MediaPlayerFileMoveProbe.obj" "/Fe:$OutputDirectory/MediaPlayerFileMoveProbe.exe" /link @libraries windowsapp.lib ole32.lib
    if ($LASTEXITCODE) { throw 'Native MediaPlayer file-move probe build failed' }
    if ($MediaPlayerMoveOnly) { return }
    & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 /DNOMINMAX @includes "$PSScriptRoot/EncodedOutputProbe.cpp" "/Fo:$OutputDirectory/EncodedOutputProbe.obj" "/Fe:$OutputDirectory/EncodedOutputProbe.exe" /link @libraries d3d11.lib dxgi.lib mf.lib mfplat.lib mfreadwrite.lib mfuuid.lib ole32.lib oleaut32.lib windowsapp.lib
    if ($LASTEXITCODE) { throw 'Replay encoded-output probe build failed' }
    & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 /DNOMINMAX @includes "$PSScriptRoot/ReplayBufferTests.cpp" "/Fo:$OutputDirectory/ReplayBufferTests.obj" "/Fe:$OutputDirectory/ReplayBufferTests.exe" /link @libraries
    if ($LASTEXITCODE) { throw 'Replay buffer policy test build failed' }
    & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 /DNOMINMAX @includes "$PSScriptRoot/CaptureFrameTimingTests.cpp" "/Fo:$OutputDirectory/CaptureFrameTimingTests.obj" "/Fe:$OutputDirectory/CaptureFrameTimingTests.exe" /link @libraries
    if ($LASTEXITCODE) { throw 'Capture frame timing test build failed' }
    & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 /DNOMINMAX @includes "$PSScriptRoot/InspectReplay.cpp" "/Fo:$OutputDirectory/InspectReplay.obj" "/Fe:$OutputDirectory/InspectReplay.exe" /link @libraries d3d11.lib mf.lib mfplat.lib mfreadwrite.lib mfuuid.lib ole32.lib oleaut32.lib windowsapp.lib
    if ($LASTEXITCODE) { throw 'Replay media inspection build failed' }
    & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 /DNOMINMAX @includes "$PSScriptRoot/EncoderInventoryProbe.cpp" "/Fo:$OutputDirectory/EncoderInventoryProbe.obj" "/Fe:$OutputDirectory/EncoderInventoryProbe.exe" /link @libraries d3d11.lib dxgi.lib mfplat.lib mfuuid.lib ole32.lib oleaut32.lib windowsapp.lib
    if ($LASTEXITCODE) { throw 'Hardware encoder inventory build failed' }
    & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 /DNOMINMAX @includes "$PSScriptRoot/EncoderCapabilityProbe.cpp" "/Fo:$OutputDirectory/EncoderCapabilityProbe.obj" "/Fe:$OutputDirectory/EncoderCapabilityProbe.exe" /link @libraries windowsapp.lib ole32.lib
    if ($LASTEXITCODE) { throw 'Hardware encoder capability ABI probe build failed' }
    & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 /DNOMINMAX @includes "$PSScriptRoot/EncodingQualityProbe.cpp" "/Fo:$OutputDirectory/EncodingQualityProbe.obj" "/Fe:$OutputDirectory/EncodingQualityProbe.exe" /link @libraries d3d11.lib dxgi.lib mf.lib mfplat.lib mfreadwrite.lib mfuuid.lib ole32.lib oleaut32.lib windowsapp.lib windowscodecs.lib gdi32.lib
    if ($LASTEXITCODE) { throw 'Matched encoding quality probe build failed' }
    & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 /DNOMINMAX @includes "$PSScriptRoot/CapturePixelProbe.cpp" "/Fo:$OutputDirectory/CapturePixelProbe.obj" "/Fe:$OutputDirectory/CapturePixelProbe.exe" /link @libraries d3d11.lib dxgi.lib mf.lib mfplat.lib mfreadwrite.lib mfuuid.lib ole32.lib oleaut32.lib windowsapp.lib windowscodecs.lib gdi32.lib user32.lib dwmapi.lib
    if ($LASTEXITCODE) { throw 'Native capture pixel geometry probe build failed' }
    if($AcquisitionDirectory) {
        $AcquisitionDirectory=[IO.Path]::GetFullPath($AcquisitionDirectory)
        & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 /DNOMINMAX @includes "$PSScriptRoot/WgcReplayProbe.cpp" "/Fo:$OutputDirectory/WgcReplayProbe.obj" "/Fe:$OutputDirectory/WgcReplayProbe.exe" /link @libraries "$AcquisitionDirectory/WebRtcCapturePrototype.lib" d3d11.lib dxgi.lib mf.lib mfplat.lib mfreadwrite.lib mfuuid.lib ole32.lib oleaut32.lib windowsapp.lib user32.lib
        if($LASTEXITCODE){throw 'Replay WGC acquisition probe build failed'}
        Copy-Item -LiteralPath "$AcquisitionDirectory/WebRtcCapturePrototype.dll" -Destination $OutputDirectory -Force
        if(Test-Path -LiteralPath "$AcquisitionDirectory/ReplayWorkerHost.lib") {
            & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 /DNOMINMAX @includes "$PSScriptRoot/WgcReplayWorkerProbe.cpp" "/Fo:$OutputDirectory/WgcReplayWorkerProbe.obj" "/Fe:$OutputDirectory/WgcReplayWorkerProbe.exe" /link @libraries "$AcquisitionDirectory/ReplayWorkerHost.lib" "$AcquisitionDirectory/CaptureWorkerHost.lib" d3d11.lib dxgi.lib user32.lib windowsapp.lib ole32.lib
            if($LASTEXITCODE){throw 'Replay production-worker probe build failed'}
            & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 /DNOMINMAX @includes "$PSScriptRoot/EncodingContentionProbe.cpp" "/Fo:$OutputDirectory/EncodingContentionProbe.obj" "/Fe:$OutputDirectory/EncodingContentionProbe.exe" /link @libraries "$AcquisitionDirectory/ReplayWorkerHost.lib" d3d11.lib dxgi.lib d3dcompiler.lib mfplat.lib mfuuid.lib user32.lib gdi32.lib windowsapp.lib ole32.lib
            if($LASTEXITCODE){throw 'Encoding GPU-contention probe build failed'}
            & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 /DNOMINMAX @includes "$PSScriptRoot/WgcEncodingAdmissionProbe.cpp" "/Fo:$OutputDirectory/WgcEncodingAdmissionProbe.obj" "/Fe:$OutputDirectory/WgcEncodingAdmissionProbe.exe" /link @libraries "$AcquisitionDirectory/ReplayWorkerHost.lib" d3d11.lib dxgi.lib user32.lib windowsapp.lib ole32.lib
            if($LASTEXITCODE){throw 'Advanced source admission probe build failed'}
            & "$($vc.FullName)/bin/Hostx64/x64/cl.exe" /nologo /MT /std:c++20 /utf-8 /EHsc /W4 /WX /O2 /DNOMINMAX @includes "$PSScriptRoot/LibraryRecordingProbe.cpp" "/Fo:$OutputDirectory/LibraryRecordingProbe.obj" "/Fe:$OutputDirectory/LibraryRecordingProbe.exe" /link @libraries d3d11.lib dxgi.lib user32.lib windowsapp.lib ole32.lib
            if ($LASTEXITCODE) { throw 'Native library recording probe build failed' }
            Copy-Item -LiteralPath "$AcquisitionDirectory/ReplayWorkerHost.dll" -Destination $OutputDirectory -Force
            Copy-Item -LiteralPath "$AcquisitionDirectory/CaptureWorkerHost.dll" -Destination $OutputDirectory -Force
        }
    }
} finally { $env:PATH=$savedPath }
