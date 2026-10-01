[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$buildPath = Join-Path $repositoryRoot 'scripts\Build-WinUiVerification.ps1'
$guidePath = Join-Path $repositoryRoot 'docs\maintainers\build-execution.md'
$readmePath = Join-Path $repositoryRoot 'CONTRIBUTING.md'
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
    Require-Text -Source $build -Expected $requiredBuildContract -Owner 'WinUI verification build'
}

$restoreCommandCount = [regex]::Matches(
    $build,
    '& dotnet restore\b',
    [Text.RegularExpressions.RegexOptions]::IgnoreCase).Count
if ($restoreCommandCount -ne 1) {
    throw "WinUI verification build has $restoreCommandCount explicit NuGet restore command owners; expected 1."
}
$restoreBeforePublish = $build.IndexOf(
    'Invoke-NuGetAuditedRestore -Project $Project', [StringComparison]::Ordinal)
$publishNoRestore = $build.IndexOf(
    'dotnet publish $Project --no-restore', [StringComparison]::Ordinal)
if ($restoreBeforePublish -lt 0 -or $restoreBeforePublish -gt $publishNoRestore) {
    throw 'WinUI verification managed publication can precede its explicit audited restore owner.'
}

foreach ($forbiddenAuditBypass in @(
    'NuGetAudit=false',
    'NuGetAuditMode=',
    'WarningsNotAsErrors',
    'NoWarn=NU1900'
)) {
    if ($build.Contains($forbiddenAuditBypass, [StringComparison]::OrdinalIgnoreCase)) {
        throw "WinUI verification build contains forbidden audit bypass '$forbiddenAuditBypass'."
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
    -Expected '](docs/maintainers/build-execution.md)' -Owner 'Contributing guide'
Require-Text -Source (Get-Content -LiteralPath $docsReadmePath -Raw) `
    -Expected '](maintainers/build-execution.md)' -Owner 'Documentation index'

# Exercise the imported production guard without restoring/compiling WinUI or
# requiring native binaries. This catches publish --no-build and early-restore
# regressions that checking source text alone cannot establish.
$frontend = [xml](Get-Content (Join-Path $repositoryRoot 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') -Raw)
if (!(@($frontend.Project.Import) | Where-Object { $_.Project -eq '..\..\eng\WinUiNativeInputs.targets' })) {
    throw 'The frontend must import its native-input build guard.'
}
$evidence = Join-Path $repositoryRoot ('artifacts/native-input-contract/' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $evidence -Force
$guard = [Security.SecurityElement]::Escape((Join-Path $repositoryRoot 'eng/WinUiNativeInputs.targets'))
$fixture = Join-Path $evidence 'guard.proj'
@"
<Project>
  <Import Project="$guard" />
  <Target Name="PrepareForBuild" />
  <Target Name="ComputeFilesToPublish" />
  <Target Name="Build" DependsOnTargets="PrepareForBuild" />
  <Target Name="Publish" DependsOnTargets="ComputeFilesToPublish" />
  <Target Name="Restore" />
</Project>
"@ | Set-Content -LiteralPath $fixture
$present = Join-Path $evidence 'present.dll'
[IO.File]::WriteAllBytes($present, [byte[]]@(0))
$absent = Join-Path $evidence 'absent.dll'
$cases = @(
    @{ Name='missing-platform'; Target='Build'; Platform=$absent; Preview=$present; Error='Missing OverlayPlatformInterop.dll' },
    @{ Name='missing-preview'; Target='Build'; Platform=$present; Preview=$absent; Error='Missing WinUiWindowPreviewNative.dll' },
    @{ Name='publish-no-build'; Target='Publish'; Platform=$absent; Preview=$absent; Error='Missing OverlayPlatformInterop.dll' },
    @{ Name='explicit-inputs'; Target='Build'; Platform=$present; Preview=$present },
    @{ Name='explicit-publish-inputs'; Target='Publish'; Platform=$present; Preview=$present },
    @{ Name='restore-before-native-build'; Target='Restore'; Platform=$absent; Preview=$absent },
    @{ Name='design-time-before-native-build'; Target='Build'; Platform=$absent; Preview=$absent; Extra='-p:DesignTimeBuild=true' }
)
foreach ($case in $cases) {
    $arguments = @('msbuild', $fixture, '-nologo', '-v:quiet', "-t:$($case.Target)",
        "-p:OverlayPlatformInteropPath=$($case.Platform)", "-p:WindowPreviewNativePath=$($case.Preview)",
        "-bl:$evidence/$($case.Name).binlog")
    if ($case.ContainsKey('Extra')) { $arguments += $case.Extra }
    $result = & dotnet @arguments 2>&1
    $exitCode = $LASTEXITCODE
    $result | Set-Content (Join-Path $evidence "$($case.Name).log")
    if ($case.ContainsKey('Error')) {
        if ($exitCode -eq 0 -or ($result -join "`n") -notlike "*$($case.Error)*") {
            throw "Native-input guard did not reject $($case.Name) with the expected diagnostic. See $evidence."
        }
    } elseif ($exitCode -ne 0) {
        throw "Native-input guard rejected $($case.Name). See $evidence."
    }
}
Write-Output "NuGet build execution contract passed (1 restore owner, audited guidance, 7 native-input build/publish/restore checks). Evidence: $evidence"
