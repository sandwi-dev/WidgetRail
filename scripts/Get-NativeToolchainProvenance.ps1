$ErrorActionPreference = 'Stop'

function Get-BoundedFileEvidence([IO.FileInfo]$file) {
    if ($null -eq $file) { return $null }
    if ($file.Length -gt 134217728) { throw 'Native tool exceeds the 128 MiB evidence limit.' }
    $stream = [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        [pscustomobject]@{
            version = $file.VersionInfo.FileVersion
            sha256 = [Convert]::ToHexString(
                [Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()
        }
    }
    finally { $stream.Dispose() }
}

$discoveryError = $null
$vsRoot = $null
$visualStudioRoot = Join-Path $env:ProgramFiles 'Microsoft Visual Studio'
$vsWhere = Join-Path $visualStudioRoot 'Installer\vswhere.exe'
if (Test-Path -LiteralPath $vsWhere) {
    try {
        $vsRoot = (& $vsWhere -latest -products * -requires `
            Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null |
            Select-Object -First 1)
        if ($LASTEXITCODE -ne 0) { $discoveryError = 'vswhere_failed'; $vsRoot = $null }
    }
    catch { $discoveryError = 'vswhere_failed'; $vsRoot = $null }
}
if (-not $vsRoot -and (Test-Path -LiteralPath $visualStudioRoot)) {
    $vsCandidates = Get-ChildItem -LiteralPath $visualStudioRoot -Directory | Sort-Object Name -Descending
    foreach ($version in $vsCandidates) {
        $edition = Get-ChildItem -LiteralPath $version.FullName -Directory -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($edition -and (Test-Path -LiteralPath (Join-Path $edition.FullName 'VC\Tools\MSVC'))) {
            $vsRoot = $edition.FullName
            break
        }
    }
}

$vcTools = if ($vsRoot) {
    Get-ChildItem -LiteralPath (Join-Path $vsRoot 'VC\Tools\MSVC') -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -First 1
} else { $null }
$compiler = if ($vcTools) {
    Get-Item -LiteralPath (Join-Path $vcTools.FullName 'bin\Hostx64\x64\cl.exe') -ErrorAction SilentlyContinue
} else { $null }

$overlaySdk = $null
$inputProbeSdk = $null
$manifestTool = $null
$programFilesX86 = ${env:ProgramFiles(x86)}
if (-not [string]::IsNullOrWhiteSpace($programFilesX86)) {
    $sdkRoot = Join-Path $programFilesX86 'Windows Kits\10'
    $sdkIncludeRoot = Join-Path $sdkRoot 'Include'
    if (Test-Path -LiteralPath $sdkIncludeRoot) {
        $sdkVersions = Get-ChildItem -LiteralPath $sdkIncludeRoot -Directory | Sort-Object Name -Descending
        $overlaySdk = $sdkVersions |
            Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'um\GameInput.h') } |
            Select-Object -First 1
        $inputProbeSdk = $sdkVersions |
            Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'um\Windows.h') } |
            Select-Object -First 1
        if ($overlaySdk) {
            $manifestTool = Get-Item -LiteralPath `
                (Join-Path $sdkRoot "bin\$($overlaySdk.Name)\x64\mt.exe") -ErrorAction SilentlyContinue
        }
    }
}

$compilerEvidence = Get-BoundedFileEvidence $compiler
$manifestToolEvidence = Get-BoundedFileEvidence $manifestTool
[pscustomobject]@{
    available = $null -ne $compiler -and $null -ne $overlaySdk -and $null -ne $inputProbeSdk
    discoveryError = $discoveryError
    visualStudioInstallation = $vsRoot
    msvcVersion = if ($vcTools) { $vcTools.Name } else { $null }
    compilerVersion = if ($compilerEvidence) { $compilerEvidence.version } else { $null }
    compilerSha256 = if ($compilerEvidence) { $compilerEvidence.sha256 } else { $null }
    overlayWindowsSdkVersion = if ($overlaySdk) { $overlaySdk.Name } else { $null }
    inputProbeWindowsSdkVersion = if ($inputProbeSdk) { $inputProbeSdk.Name } else { $null }
    manifestToolVersion = if ($manifestToolEvidence) { $manifestToolEvidence.version } else { $null }
    manifestToolSha256 = if ($manifestToolEvidence) { $manifestToolEvidence.sha256 } else { $null }
} | ConvertTo-Json -Depth 4 -Compress
