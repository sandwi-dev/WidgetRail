<#
.SYNOPSIS
Builds only the native Windows platform boundary used by the WinUI frontend.
.DESCRIPTION
Does not build Taffy, OverlayHost, managed workers, or packages, and never loads
or initializes the resulting DLL unless a focused test switch is supplied.
Process-lifecycle tests load only the ownership exports; they never initialize
controller hardware. Restore uses the existing native dependency
manifest. GameInput's static loader uses the separately installed runtime; its
redistributable is copied but never installed by this script.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('x64')]
    [string]$Architecture = 'x64',
    [string]$OutputDirectory,
    [switch]$NoRestore,
    [switch]$TestForeground,
    [switch]$TestProcessLifecycle
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hostDirectory = Join-Path $repositoryRoot 'src\OverlayHost'
$platformDirectory = Join-Path $repositoryRoot 'src\OverlayPlatformInterop'
$viGEmDirectory = Join-Path $repositoryRoot 'third_party\ViGEmClient'
$dependencyProject = Join-Path $hostDirectory 'NativeDependencies.csproj'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\winui-platform\$Configuration"
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$objectDirectory = Join-Path $OutputDirectory 'obj'
$logDirectory = Join-Path $OutputDirectory 'logs'
New-Item -ItemType Directory -Force -Path $OutputDirectory, $objectDirectory, $logDirectory | Out-Null

[xml]$dependencies = Get-Content -LiteralPath $dependencyProject -Raw
$gameInputReference = @($dependencies.Project.ItemGroup.PackageReference | Where-Object Include -EQ 'Microsoft.GameInput')
if ($gameInputReference.Count -ne 1) { throw 'Expected exactly one Microsoft.GameInput dependency in NativeDependencies.csproj.' }
$gameInputVersion = [string]$gameInputReference[0].Version
$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$gameInputDirectory = Join-Path $nugetRoot "microsoft.gameinput\$gameInputVersion"
if (-not $NoRestore) {
    $restoreLog = Join-Path $logDirectory ("restore-" + [guid]::NewGuid().ToString('N') + '.binlog')
    & dotnet restore $dependencyProject --nologo "-bl:$restoreLog"
    if ($LASTEXITCODE -ne 0) { throw "Native dependency restore failed with exit code $LASTEXITCODE. See $restoreLog." }
}
foreach ($relative in @('native\include\GameInput.h', "native\lib\$Architecture\GameInput.lib", 'redist\GameInputRedist.msi')) {
    if (-not (Test-Path -LiteralPath (Join-Path $gameInputDirectory $relative))) {
        throw "Missing Microsoft.GameInput $gameInputVersion asset: $relative. Restore dependencies before using -NoRestore."
    }
}

$vsWhere = @(
    (Join-Path ${env:ProgramFiles} 'Microsoft Visual Studio\Installer\vswhere.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe')
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $vsWhere) { throw 'Visual Studio Installer vswhere.exe was not found.' }
$vsRoot = & $vsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsRoot) { throw 'Visual Studio Desktop development with C++ tools were not found.' }
$vcTools = Get-ChildItem -LiteralPath (Join-Path $vsRoot 'VC\Tools\MSVC') -Directory |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $vcTools) { throw 'No MSVC toolset was found.' }
$compilerDirectory = Join-Path $vcTools.FullName "bin\Host$Architecture\$Architecture"
$compiler = Join-Path $compilerDirectory 'cl.exe'
$dumpbin = Join-Path $compilerDirectory 'dumpbin.exe'
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$sdk = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Include') -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'um\Windows.h') } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $sdk) { throw 'Windows SDK headers were not found.' }
$sdkBin = Join-Path $sdkRoot "bin\$($sdk.Name)\$Architecture"
foreach ($tool in @($compiler, $dumpbin, (Join-Path $sdkBin 'mt.exe'))) {
    if (-not (Test-Path -LiteralPath $tool)) { throw "Required native build tool is missing: $tool" }
}

$includeArguments = @(
    "/I$gameInputDirectory\native\include", "/I$viGEmDirectory\include",
    "/I$($vcTools.FullName)\include",
    "/I$sdkRoot\Include\$($sdk.Name)\ucrt", "/I$sdkRoot\Include\$($sdk.Name)\shared",
    "/I$sdkRoot\Include\$($sdk.Name)\um", "/I$sdkRoot\Include\$($sdk.Name)\winrt",
    "/I$sdkRoot\Include\$($sdk.Name)\cppwinrt"
)
$libraryArguments = @(
    "/LIBPATH:$gameInputDirectory\native\lib\$Architecture",
    "/LIBPATH:$($vcTools.FullName)\lib\$Architecture",
    "/LIBPATH:$sdkRoot\Lib\$($sdk.Name)\ucrt\$Architecture",
    "/LIBPATH:$sdkRoot\Lib\$($sdk.Name)\um\$Architecture"
)
# Keep this list aligned with Invoke-OverlayPlatformInteropBuild in OverlayHost/build.ps1.
$sources = @(
    'OverlayPlatformInterop.cpp', 'ControllerIsolationCore.cpp', 'ControllerIsolationReader.cpp',
    'ControllerIsolationRoutingSession.cpp', 'GameInputSelectedControllerReader.cpp',
    'DualSenseHidReader.cpp', 'ControllerIsolationHostSession.cpp', 'LocalControllerPolicy.cpp',
    'HidHideConfigurationAdapter.cpp', 'ViGEmOutputAdapter.cpp', 'OverlayPlatformPolicy.cpp',
    'OverlayPlatformPlacement.cpp', 'OverlayPlatformTargeting.cpp', 'OverlayProcessInterop.cpp'
) | ForEach-Object { Join-Path $platformDirectory $_ }
$sources += (Join-Path $viGEmDirectory 'src\ViGEmClient.cpp'), (Join-Path $hostDirectory 'GuideInputCompatibility.cpp')
$sources += (Join-Path $hostDirectory 'OverlayProcessOwner.cpp')
$optimization = if ($Configuration -eq 'Release') { @('/O2', '/DNDEBUG') } else { @('/Od', '/Zi') }
# Explicit static CRT matches the legacy build's cl default and avoids adding a
# redistributable DLL dependency solely for the WinUI platform boundary.
$arguments = @('/nologo', '/MP2', '/FS', '/MT', '/std:c++20', '/utf-8', '/EHsc', '/W4', '/permissive-',
    '/DUSING_GAMEINPUT', '/DUNICODE', '/D_UNICODE', '/DWIN32_LEAN_AND_MEAN', '/DNOMINMAX',
    '/DWRAIL_OVERLAY_PLATFORM_EXPORTS', '/DWRAIL_VIGEM_NATIVE_BACKEND', '/DWRAIL_GAMEINPUT_ISOLATION_READER',
    '/LD', "/Fd:$objectDirectory\platform-compile.pdb") + $optimization + $includeArguments + $sources + @(
    "/Fo:$objectDirectory\", "/Fe:$OutputDirectory\OverlayPlatformInterop.dll", '/link',
    "/IMPLIB:$OutputDirectory\OverlayPlatformInterop.lib", "/PDB:$OutputDirectory\OverlayPlatformInterop.pdb",
    '/SUBSYSTEM:WINDOWS'
) + $libraryArguments + @('gameinput.lib', 'user32.lib', 'hid.lib', 'xinput9_1_0.lib', 'bcrypt.lib',
    'advapi32.lib', 'shell32.lib', 'setupapi.lib', 'cfgmgr32.lib', 'ole32.lib',
    'ntdll.lib', 'userenv.lib', 'ws2_32.lib')
$originalPath = $env:PATH
try {
    $env:PATH = "$sdkBin;$compilerDirectory;$originalPath"
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw "OverlayPlatformInterop build failed with exit code $LASTEXITCODE." }
    $exports = & $dumpbin /nologo /exports (Join-Path $OutputDirectory 'OverlayPlatformInterop.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Export inspection failed.' }
    $exports | Set-Content -LiteralPath (Join-Path $logDirectory 'exports.txt') -Encoding utf8
    $imports = & $dumpbin /nologo /dependents (Join-Path $OutputDirectory 'OverlayPlatformInterop.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Dependency inspection failed.' }
    $imports | Set-Content -LiteralPath (Join-Path $logDirectory 'dependencies.txt') -Encoding utf8
    if ($TestProcessLifecycle) {
        $testArguments = @('/nologo', '/MT', '/std:c++20', '/utf-8', '/EHsc', '/W4', '/permissive-',
            '/DUNICODE', '/D_UNICODE', '/DWIN32_LEAN_AND_MEAN', '/DNOMINMAX') + $includeArguments + @(
            (Join-Path $repositoryRoot 'tests\OverlayPlatformInterop.Tests\ProcessLifecycleTests.cpp'),
            (Join-Path $hostDirectory 'OverlayProcessOwner.cpp'),
            "/Fo:$objectDirectory\", "/Fe:$OutputDirectory\ProcessLifecycleTests.exe", '/link', "/LIBPATH:$OutputDirectory"
        ) + $libraryArguments + @('OverlayPlatformInterop.lib', 'user32.lib', 'advapi32.lib')
        & $compiler @testArguments
        if ($LASTEXITCODE -ne 0) { throw "Process lifecycle test build failed with exit code $LASTEXITCODE." }
        $testOutput = Join-Path $logDirectory "process-lifecycle-$([Guid]::NewGuid()).out.log"
        $testErrors = Join-Path $logDirectory "process-lifecycle-$([Guid]::NewGuid()).err.log"
        $test = Start-Process -FilePath (Join-Path $OutputDirectory 'ProcessLifecycleTests.exe') -WindowStyle Hidden -PassThru -RedirectStandardOutput $testOutput -RedirectStandardError $testErrors
        if (!$test.WaitForExit(20000)) { $test.Kill(); throw 'Process lifecycle test exceeded 20 seconds.' }
        Get-Content -LiteralPath $testOutput, $testErrors
        if ($test.ExitCode -ne 0) { throw "Process lifecycle test failed with exit code $($test.ExitCode)." }
    }
    if ($TestForeground) {
        $testArguments = @('/nologo', '/MT', '/std:c++20', '/utf-8', '/EHsc', '/W4', '/permissive-',
            '/DUNICODE', '/D_UNICODE', '/DWIN32_LEAN_AND_MEAN', '/DNOMINMAX') + $includeArguments + @(
            (Join-Path $repositoryRoot 'tests\OverlayPlatformInterop.Tests\ForegroundAcquisitionTests.cpp'),
            "/Fo:$objectDirectory\ForegroundAcquisitionTests.obj", "/Fe:$OutputDirectory\ForegroundAcquisitionTests.exe",
            '/link', "/LIBPATH:$OutputDirectory"
        ) + $libraryArguments + @('OverlayPlatformInterop.lib', 'user32.lib')
        & $compiler @testArguments
        if ($LASTEXITCODE -ne 0) { throw "Foreground boundary test build failed with exit code $LASTEXITCODE." }
        & (Join-Path $OutputDirectory 'ForegroundAcquisitionTests.exe')
        if ($LASTEXITCODE -ne 0) { throw "Foreground boundary test failed with exit code $LASTEXITCODE." }
    }
} finally {
    $env:PATH = $originalPath
}

# The pinned GameInput package supplies a static loader and an MSI, not an
# app-local runtime DLL. Preserve that deployment model; never copy System32 DLLs.
$redistDirectory = Join-Path $OutputDirectory 'redist'
New-Item -ItemType Directory -Force -Path $redistDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $gameInputDirectory 'redist\GameInputRedist.msi') -Destination $redistDirectory -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md') -Destination $OutputDirectory -Force
Write-Output "Built platform-only DLL: $(Join-Path $OutputDirectory 'OverlayPlatformInterop.dll')"
Write-Output "Export and dependency inspection: $logDirectory"
Write-Output 'No hardware runtime initialization, host build/launch, or driver installation was performed.'
