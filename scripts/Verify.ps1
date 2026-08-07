[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipNative
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

function Invoke-Checked {
    param(
        [Parameter(Mandatory)]
        [scriptblock]$Command,
        [Parameter(Mandatory)]
        [string]$Description
    )

    Write-Host "`n== $Description =="
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

Push-Location $repositoryRoot
try {
    Invoke-Checked -Description 'Build widget protocol, SDK, sample, and tests' -Command {
        dotnet build 'tests\WidgetSdk.Tests\WidgetSdk.Tests.csproj' --configuration $Configuration --nologo
    }
    Invoke-Checked -Description 'Run widget SDK contract tests' -Command {
        dotnet run --project 'tests\WidgetSdk.Tests\WidgetSdk.Tests.csproj' --configuration $Configuration --no-build
    }
    Invoke-Checked -Description 'Build and test YT Music widget' -Command {
        dotnet run --project 'tests\YtMusicWidget.Tests\YtMusicWidget.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build YT Music isolated worker' -Command {
        dotnet build 'samples\YtMusicWidget.Worker\YtMusicWidget.Worker.csproj' --configuration $Configuration --nologo
    }
    Invoke-Checked -Description 'Build and test isolated widget runtime' -Command {
        dotnet run --project 'tests\WidgetRuntime.Tests\WidgetRuntime.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test widget developer CLI' -Command {
        dotnet run --project 'tests\GbarCli.Tests\GbarCli.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test shared GBSS engine' -Command {
        dotnet run --project 'tests\WidgetStyling.Tests\WidgetStyling.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test widget package catalog' -Command {
        dotnet run --project 'tests\WidgetCatalog.Tests\WidgetCatalog.Tests.csproj' --configuration $Configuration
    }

    if (Test-Path -LiteralPath 'tests\WidgetBridge.Tests\WidgetBridge.Tests.csproj') {
        Invoke-Checked -Description 'Build and test native widget bridge' -Command {
            dotnet run --project 'tests\WidgetBridge.Tests\WidgetBridge.Tests.csproj' --configuration $Configuration
        }
    }

    if ($SkipNative) {
        Write-Host "`nNative verification skipped by request."
        return
    }

    Invoke-Checked -Description 'Build native overlay and run state-machine tests' -Command {
        & 'src\OverlayHost\build.ps1' -Configuration $Configuration
    }
    Invoke-Checked -Description 'Smoke-test hidden overlay initialization' -Command {
        $overlayPath = Resolve-Path "src\OverlayHost\out\$Configuration\OverlayHost.exe"
        $startupError = Join-Path $env:LOCALAPPDATA 'GameBarAlternative\startup-error.log'
        $overlayProcess = Start-Process -FilePath $overlayPath -ArgumentList '--hidden' -WindowStyle Hidden -PassThru
        try {
            $overlayProcess.WaitForExit(1000) | Out-Null
            $overlayProcess.Refresh()
            if ($overlayProcess.HasExited) {
                throw "OverlayHost exited during initialization with code $($overlayProcess.ExitCode)."
            }
            if (Test-Path -LiteralPath $startupError) {
                throw "OverlayHost reported a startup failure: $(Get-Content -LiteralPath $startupError -Raw)"
            }
            Write-Host 'OverlayHost initialized and remained resident while hidden.'
        }
        finally {
            if (-not $overlayProcess.HasExited) {
                Stop-Process -Id $overlayProcess.Id
                $overlayProcess.WaitForExit()
            }
        }
    }
    Invoke-Checked -Description 'Build controller input probe' -Command {
        & 'tools\InputProbe\build.ps1' -Configuration $Configuration
    }
    Invoke-Checked -Description 'Smoke-test controller input probe command line' -Command {
        & "tools\InputProbe\build\$Configuration\InputProbe.exe" --help
    }
}
finally {
    Pop-Location
}
