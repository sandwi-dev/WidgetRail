[CmdletBinding()]
param(
    [ValidateRange(30, 600)]
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
$prototypeRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $prototypeRoot 'AvaloniaOverlayPrototype.sln'

function Invoke-BoundedDotnet {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,
        [Parameter(Mandatory)]
        [string]$Label
    )

    Write-Host "[$Label] dotnet $($Arguments -join ' ')"
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = 'dotnet'
    $startInfo.WorkingDirectory = $prototypeRoot
    $startInfo.UseShellExecute = $false
    $startInfo.Arguments = ($Arguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }) -join ' '
    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "$Label could not start dotnet."
    }
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            throw "$Label exceeded the bounded $TimeoutSeconds second timeout."
        }

        $exitCode = $process.ExitCode
        if ($exitCode -ne 0) {
            throw "$Label failed with exit code $exitCode."
        }
    }
    finally {
        $process.Dispose()
    }
}

Invoke-BoundedDotnet -Label 'Release build' -Arguments @('build', $solution, '--configuration', 'Release', '--nologo')
Invoke-BoundedDotnet -Label 'Focused Release tests' -Arguments @(
    'test', '--project', (Join-Path $prototypeRoot 'tests\AvaloniaOverlayPrototype.Tests.csproj'),
    '--configuration', 'Release', '--no-build', '--no-ansi', '--progress', 'off',
    '--output', 'Detailed', '--minimum-expected-tests', '12'
)
