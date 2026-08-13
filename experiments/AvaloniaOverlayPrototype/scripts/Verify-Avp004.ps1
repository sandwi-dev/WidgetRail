[CmdletBinding()]
param(
    [ValidateRange(30, 600)]
    [int]$TimeoutSeconds = 240
)

$ErrorActionPreference = 'Stop'
$prototypeRoot = Split-Path -Parent $PSScriptRoot
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $prototypeRoot '..\..'))
$artifactRoot = Join-Path $prototypeRoot 'artifacts\avp004'
$focusedProofPath = Join-Path $artifactRoot 'focused-verification.json'
$testProject = Join-Path $prototypeRoot 'tests\AvaloniaOverlayPrototype.Tests.csproj'
$invalidFixture = Join-Path $prototypeRoot 'tests\fixtures\InvalidCompiledBinding\InvalidCompiledBinding.csproj'

function Invoke-BoundedDotnet {
    param(
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$Label
    )
    Write-Host "[$Label] dotnet $($Arguments -join ' ')"
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'dotnet'
    $startInfo.WorkingDirectory = $prototypeRoot
    $startInfo.UseShellExecute = $false
    $startInfo.Arguments = ($Arguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }) -join ' '
    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) { throw "$Label could not start dotnet." }
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            throw "$Label exceeded the bounded $TimeoutSeconds second timeout."
        }
        if ($process.ExitCode -ne 0) { throw "$Label failed with exit code $($process.ExitCode)." }
    }
    finally { $process.Dispose() }
}

function Confirm-InvalidCompiledBindingFails {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'dotnet'
    $startInfo.WorkingDirectory = $prototypeRoot
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.Arguments = 'build "' + $invalidFixture + '" --configuration Release --nologo --no-incremental'
    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) { throw 'Invalid compiled-binding fixture could not start dotnet.' }
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            throw "Invalid compiled-binding fixture exceeded the bounded $TimeoutSeconds second timeout."
        }
        $output = $process.StandardOutput.ReadToEnd() + $process.StandardError.ReadToEnd()
        if ($process.ExitCode -eq 0) { throw 'The intentionally invalid compiled binding unexpectedly built successfully.' }
        if ($output -notmatch 'AVLN2000' -or $output -notmatch 'PropertyThatDoesNotExist') {
            throw 'The invalid fixture failed for an unexpected reason.'
        }
        Write-Host '[AVP-004 invalid binding fixture] expected AVLN2000 confirmed.'
    }
    finally { $process.Dispose() }
}

Invoke-BoundedDotnet -Label 'AVP-004 Release build' -Arguments @(
    'build', $testProject, '--configuration', 'Release', '--nologo')
Confirm-InvalidCompiledBindingFails
Invoke-BoundedDotnet -Label 'AVP-004 focused Release tests' -Arguments @(
    'test', '--project', $testProject,
    '--configuration', 'Release', '--no-build', '--no-ansi', '--progress', 'off',
    '--output', 'Detailed', '--minimum-expected-tests', '17')

$worktreeState = (& git -C $repositoryRoot status --porcelain)
if ($worktreeState.Count -ne 0) {
    throw 'Final focused verification proof requires a clean exact commit.'
}
$sourceCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
[ordered]@{
    assignment = 'AVP-004-INTEGRATION'
    sourceCommit = $sourceCommit
    focusedSuitePassed = $true
    genericMappingTest = 'Generic_renderer_maps_every_current_node_kind_to_standard_Avalonia_controls_and_UIA'
    currentNodeKinds = @(
        'Stack', 'Row', 'Scroll', 'Text', 'Button', 'Progress', 'Slider',
        'Spacer', 'Image', 'Icon', 'LoadingIndicator', 'ActionSurface', 'Grid', 'TextEntry')
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $focusedProofPath -Encoding utf8
Write-Host "Retained exact-commit focused proof: $focusedProofPath"
