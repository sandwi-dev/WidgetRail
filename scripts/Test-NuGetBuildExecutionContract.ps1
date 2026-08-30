[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$buildPath = Join-Path $repositoryRoot 'src\OverlayHost\build.ps1'
$guidePath = Join-Path $repositoryRoot 'docs\build-execution.md'
$readmePath = Join-Path $repositoryRoot 'README.md'
$docsReadmePath = Join-Path $repositoryRoot 'docs\README.md'
$build = Get-Content -LiteralPath $buildPath -Raw
$guide = Get-Content -LiteralPath $guidePath -Raw
$normalizedGuide = [regex]::Replace($guide, '\s+', ' ')

function Require-Text {
    param(
        [Parameter(Mandatory = $true)] [string]$Source,
        [Parameter(Mandatory = $true)] [string]$Expected,
        [Parameter(Mandatory = $true)] [string]$Owner
    )
    if (-not $Source.Contains($Expected, [StringComparison]::Ordinal)) {
        throw "$Owner omits '$Expected'."
    }
}

foreach ($requiredBuildContract in @(
    '[switch]$NoRestore',
    'function Invoke-NuGetAuditedRestore',
    '[System.Collections.Generic.HashSet[string]]::new',
    'dotnet restore $resolvedProject --nologo',
    'Invoke-NuGetAuditedRestore -Project $Project',
    'dotnet publish $Project --no-restore'
)) {
    Require-Text -Source $build -Expected $requiredBuildContract -Owner 'OverlayHost build'
}

$restoreCommandCount = [regex]::Matches(
    $build,
    '& dotnet restore\b',
    [Text.RegularExpressions.RegexOptions]::IgnoreCase).Count
if ($restoreCommandCount -ne 1) {
    throw "OverlayHost build has $restoreCommandCount explicit NuGet restore command owners; expected 1."
}
$restoreBeforePublish = $build.IndexOf(
    'Invoke-NuGetAuditedRestore -Project $Project', [StringComparison]::Ordinal)
$publishNoRestore = $build.IndexOf(
    'dotnet publish $Project --no-restore', [StringComparison]::Ordinal)
if ($restoreBeforePublish -lt 0 -or $restoreBeforePublish -gt $publishNoRestore) {
    throw 'OverlayHost managed publication can precede its explicit audited restore owner.'
}

foreach ($forbiddenAuditBypass in @(
    'NuGetAudit=false',
    'NuGetAuditMode=',
    'WarningsNotAsErrors',
    'NoWarn=NU1900'
)) {
    if ($build.Contains($forbiddenAuditBypass, [StringComparison]::OrdinalIgnoreCase)) {
        throw "OverlayHost build contains forbidden audit bypass '$forbiddenAuditBypass'."
    }
}

foreach ($requiredGuidance in @(
    'supported network access on its first attempt',
    'an `NU1900` failure is a failed build',
    'pass `NuGetAudit=false`',
    'There is no silent retry',
    '-NoRestore',
    '--no-restore',
    'already-restored commands may remain restricted'
)) {
    Require-Text -Source $normalizedGuide -Expected $requiredGuidance -Owner 'Build execution guide'
}
Require-Text -Source (Get-Content -LiteralPath $readmePath -Raw) `
    -Expected '](docs/build-execution.md)' -Owner 'README'
Require-Text -Source (Get-Content -LiteralPath $docsReadmePath -Raw) `
    -Expected '](build-execution.md)' -Owner 'Documentation index'

Write-Output 'NuGet build execution contract passed (1 restore owner, audited fail-closed guidance, explicit no-restore path).'
