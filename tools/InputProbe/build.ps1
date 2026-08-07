[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Architecture = 'x64'
)

$ErrorActionPreference = 'Stop'
$probeRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$vsWhere = Join-Path ${env:ProgramFiles} 'Microsoft Visual Studio\Installer\vswhere.exe'

if (Test-Path -LiteralPath $vsWhere) {
    $vsRoot = & $vsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
}

if (-not $vsRoot) {
    $vsCandidates = Get-ChildItem -LiteralPath (Join-Path ${env:ProgramFiles} 'Microsoft Visual Studio') -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending
    foreach ($version in $vsCandidates) {
        $edition = Get-ChildItem -LiteralPath $version.FullName -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($edition -and (Test-Path -LiteralPath (Join-Path $edition.FullName 'Common7\Tools\VsDevCmd.bat'))) {
            $vsRoot = $edition.FullName
            break
        }
    }
}

if (-not $vsRoot) {
    throw 'Visual Studio C++ build tools were not found.'
}

$buildDir = Join-Path $probeRoot (Join-Path 'build' $Configuration)
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

$vcToolsRoot = Join-Path $vsRoot 'VC\Tools\MSVC'
$vcTools = Get-ChildItem -LiteralPath $vcToolsRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
if (-not $vcTools) {
    throw "No MSVC toolset was found below $vcToolsRoot."
}
if (-not (Test-Path -LiteralPath (Join-Path $vcTools.FullName 'include\excpt.h'))) {
    throw "MSVC compiler binaries exist at $($vcTools.FullName), but the C++ headers are missing. Install the 'Desktop development with C++' workload (including MSVC headers/libraries), then rerun this script."
}

$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$sdkVersions = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Include') -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'um\Windows.h') } |
    Sort-Object Name -Descending
$sdk = $sdkVersions | Select-Object -First 1
if (-not $sdk) {
    throw "No Windows 10/11 SDK was found below $sdkRoot."
}

$cl = Join-Path $vcTools.FullName "bin\Host$Architecture\$Architecture\cl.exe"
if (-not (Test-Path -LiteralPath $cl)) {
    throw "The requested $Architecture compiler was not found at $cl."
}

$sdkBin = Join-Path $sdkRoot "bin\$($sdk.Name)\$Architecture"
$compilerBin = Split-Path -Parent $cl
if (-not (Test-Path -LiteralPath (Join-Path $sdkBin 'mt.exe'))) {
    throw "The Windows SDK manifest tool was not found below $sdkBin."
}
$env:PATH = "$sdkBin;$compilerBin;$env:PATH"

$optimization = if ($Configuration -eq 'Release') { @('/O2', '/DNDEBUG') } else { @('/Od', '/Zi') }
$source = Join-Path $probeRoot 'main.cpp'
$output = Join-Path $buildDir 'InputProbe.exe'
$symbols = Join-Path $buildDir 'InputProbe.pdb'

$includeArguments = @(
    "/I$($vcTools.FullName)\include",
    "/I$sdkRoot\Include\$($sdk.Name)\ucrt",
    "/I$sdkRoot\Include\$($sdk.Name)\shared",
    "/I$sdkRoot\Include\$($sdk.Name)\um",
    "/I$sdkRoot\Include\$($sdk.Name)\winrt"
)
$libraryArguments = @(
    "/LIBPATH:$($vcTools.FullName)\lib\$Architecture",
    "/LIBPATH:$sdkRoot\Lib\$($sdk.Name)\ucrt\$Architecture",
    "/LIBPATH:$sdkRoot\Lib\$($sdk.Name)\um\$Architecture"
)
$object = Join-Path $buildDir 'main.obj'
$arguments = @('/nologo', '/std:c++20', '/EHsc', '/W4', '/permissive-', '/DUNICODE', '/D_UNICODE', '/DWIN32_LEAN_AND_MEAN', '/DNOMINMAX') +
    $optimization + $includeArguments + @($source, "/Fo:$object", "/Fe:$output", "/Fd:$symbols", '/link') +
    $libraryArguments + @('user32.lib', 'shell32.lib', 'gdi32.lib')

& $cl $arguments
if ($LASTEXITCODE -ne 0) {
    throw "Native build failed with exit code $LASTEXITCODE."
}

Write-Host "Built $output"
