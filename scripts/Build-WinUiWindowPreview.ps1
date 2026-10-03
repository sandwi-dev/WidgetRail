[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release', [string]$OutputDirectory, [switch]$TestPolicy, [switch]$TestCapture)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repositoryRoot "artifacts\winui-preview\$Configuration" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$objectDirectory = Join-Path $OutputDirectory 'obj'
New-Item -ItemType Directory -Force $OutputDirectory,$objectDirectory | Out-Null
$vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsRoot = & $vsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsRoot) { throw 'Visual Studio C++ tools are required.' }
$vc = Get-ChildItem -LiteralPath (Join-Path $vsRoot 'VC\Tools\MSVC') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$sdk = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Include') -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'um\Windows.h') } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$compilerDirectory = Join-Path $vc.FullName 'bin\Hostx64\x64'
$compiler = Join-Path $compilerDirectory 'cl.exe'
$includes = @("/I$($vc.FullName)\include") + @('ucrt','shared','um','winrt','cppwinrt' | ForEach-Object { "/I$sdkRoot\Include\$($sdk.Name)\$_" })
$libraries = @("/LIBPATH:$($vc.FullName)\lib\x64", "/LIBPATH:$sdkRoot\Lib\$($sdk.Name)\ucrt\x64", "/LIBPATH:$sdkRoot\Lib\$($sdk.Name)\um\x64")
$optimization = if ($Configuration -eq 'Release') { @('/O2','/DNDEBUG') } else { @('/Od','/Zi') }
$captureSources = if ($TestCapture) { @('/DWRAIL_CAPTURE_VALIDATION', (Join-Path $repositoryRoot 'src\WinUiWindowPreviewNative\WindowCaptureFixture.cpp')) } else { @() }
$arguments = @('/nologo','/MT','/std:c++20','/utf-8','/EHsc','/W4','/permissive-','/DUNICODE','/D_UNICODE','/DWIN32_LEAN_AND_MEAN','/DNOMINMAX','/DWRAIL_PREVIEW_EXPORTS','/LD') + $optimization + $includes + $captureSources + @(
    (Join-Path $repositoryRoot 'src\WinUiWindowPreviewNative\WindowPreviewNative.cpp'), (Join-Path $repositoryRoot 'src\WinUiWindowPreviewNative\WindowContextCapture.cpp'), "/Fo:$objectDirectory\", "/Fe:$OutputDirectory\WinUiWindowPreviewNative.dll", '/link',
    "/IMPLIB:$OutputDirectory\WinUiWindowPreviewNative.lib", "/PDB:$OutputDirectory\WinUiWindowPreviewNative.pdb") + $libraries + @('d3d11.lib','dxgi.lib','d2d1.lib','dwmapi.lib','user32.lib','ole32.lib','windowsapp.lib','gdi32.lib','windowscodecs.lib','mfplat.lib','mfreadwrite.lib','mfuuid.lib')
$oldPath = $env:PATH
try {
    $env:PATH = "$compilerDirectory;$sdkRoot\bin\$($sdk.Name)\x64;$oldPath"
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw "Window preview adapter build failed: $LASTEXITCODE" }
    if ($TestPolicy) {
        $policy = @('/nologo','/MT','/std:c++20','/utf-8','/EHsc','/W4','/permissive-','/DNOMINMAX') + $includes + @(
            (Join-Path $repositoryRoot 'src\WinUiWindowPreviewNative\WindowPreviewPolicyTests.cpp'), "/Fo:$objectDirectory\PolicyTests.obj", "/Fe:$OutputDirectory\WindowPreviewPolicyTests.exe", '/link') + $libraries
        & $compiler @policy
        if ($LASTEXITCODE -ne 0) { throw 'Preview policy test build failed.' }
        & (Join-Path $OutputDirectory 'WindowPreviewPolicyTests.exe')
        if ($LASTEXITCODE -ne 0) { throw 'Preview policy test failed.' }
        $diagnostics = @('/nologo','/MT','/std:c++20','/utf-8','/EHsc','/W4','/permissive-','/DNOMINMAX') + $includes + @(
            (Join-Path $repositoryRoot 'src\WinUiWindowPreviewNative\WindowPreviewDiagnosticsTests.cpp'), "/Fo:$objectDirectory\DiagnosticsTests.obj", "/Fe:$OutputDirectory\WindowPreviewDiagnosticsTests.exe", '/link',
            (Join-Path $OutputDirectory 'WinUiWindowPreviewNative.lib')) + $libraries
        & $compiler @diagnostics
        if ($LASTEXITCODE -ne 0) { throw 'Preview diagnostics test build failed.' }
        & (Join-Path $OutputDirectory 'WindowPreviewDiagnosticsTests.exe')
        if ($LASTEXITCODE -ne 0) { throw 'Preview diagnostics test failed.' }
    }
    if ($TestCapture) {
        $fixture = @('/nologo','/MT','/std:c++20','/EHsc','/W4','/DNOMINMAX','/DUNICODE','/D_UNICODE') + $includes + @(
            (Join-Path $repositoryRoot 'src\WinUiWindowPreviewNative\WindowCaptureFixture.cpp'), "/Fo:$objectDirectory\CaptureFixture.obj", "/Fe:$OutputDirectory\WindowCaptureFixture.exe", '/link') + $libraries + @('user32.lib','gdi32.lib')
        & $compiler @fixture
        if ($LASTEXITCODE -ne 0) { throw 'Capture fixture build failed.' }
    }
    & (Join-Path $compilerDirectory 'dumpbin.exe') /nologo /exports (Join-Path $OutputDirectory 'WinUiWindowPreviewNative.dll') | Set-Content (Join-Path $OutputDirectory 'exports.txt')
} finally { $env:PATH = $oldPath }
