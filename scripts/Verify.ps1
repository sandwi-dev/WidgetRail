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
    Write-Host "`n== Validate bounded performance harness helpers =="
    & 'scripts\Measure-OverlayPerformance.ps1' -SelfTest

    Invoke-Checked -Description 'Build widget protocol, SDK, sample, and tests' -Command {
        dotnet build 'tests\WidgetSdk.Tests\WidgetSdk.Tests.csproj' --configuration $Configuration --nologo
    }
    Invoke-Checked -Description 'Run widget SDK contract tests' -Command {
        dotnet run --project 'tests\WidgetSdk.Tests\WidgetSdk.Tests.csproj' --configuration $Configuration --no-build
    }
    Invoke-Checked -Description 'Build and test YT Music widget' -Command {
        dotnet run --project 'tests\YtMusicWidget.Tests\YtMusicWidget.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test isolated widget runtime' -Command {
        dotnet run --project 'tests\WidgetRuntime.Tests\WidgetRuntime.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test generic installed-widget worker host' -Command {
        dotnet run --project 'tests\WidgetWorkerHost.Tests\WidgetWorkerHost.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test widget developer CLI' -Command {
        dotnet run --project 'tests\GbarCli.Tests\GbarCli.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test shared GBSS engine' -Command {
        dotnet run --project 'tests\WidgetStyling.Tests\WidgetStyling.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test platform settings and themes' -Command {
        dotnet run --project 'tests\PlatformSettings.Tests\PlatformSettings.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test package-scoped widget configuration' -Command {
        dotnet run --project 'tests\WidgetConfiguration.Tests\WidgetConfiguration.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test capability broker contracts' -Command {
        dotnet run --project 'tests\PlatformBroker.Tests\PlatformBroker.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test Windows Core Audio provider' -Command {
        dotnet run --project 'tests\WindowsAudioProvider.Tests\WindowsAudioProvider.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test Windows network provider' -Command {
        dotnet run --project 'tests\WindowsNetworkProvider.Tests\WindowsNetworkProvider.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test Windows Bluetooth provider' -Command {
        dotnet run --project 'tests\WindowsBluetoothProvider.Tests\WindowsBluetoothProvider.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test Windows foreground activity provider' -Command {
        dotnet run --project 'tests\WindowsActivityProvider.Tests\WindowsActivityProvider.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test Windows app-library provider' -Command {
        dotnet run --project 'tests\WindowsAppLibraryProvider.Tests\WindowsAppLibraryProvider.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test constrained loopback and private-secret provider' -Command {
        dotnet run --project 'tests\WindowsCommunityProvider.Tests\WindowsCommunityProvider.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test Windows media-session provider' -Command {
        dotnet run --project 'tests\WindowsMediaProvider.Tests\WindowsMediaProvider.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test trusted Spotify OAuth and Web API provider' -Command {
        dotnet run --project 'tests\WindowsSpotifyProvider.Tests\WindowsSpotifyProvider.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test trusted Spotify Web Playback host' -Command {
        dotnet run --project 'tests\SpotifyPlaybackHost.Tests\SpotifyPlaybackHost.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test trusted Spotify playback process client' -Command {
        dotnet run --project 'tests\SpotifyPlaybackClient.Tests\SpotifyPlaybackClient.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test Spotify community widget' -Command {
        dotnet run --project 'tests\SpotifyWidget.Tests\SpotifyWidget.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test public SDK Gallery community widget' -Command {
        dotnet run --project 'tests\SdkGalleryWidget.Tests\SdkGalleryWidget.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test first-party Audio Mixer widget' -Command {
        dotnet run --project 'tests\AudioMixerWidget.Tests\AudioMixerWidget.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test first-party Network Controls widget' -Command {
        dotnet run --project 'tests\NetworkControlsWidget.Tests\NetworkControlsWidget.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test retired Recent Apps reference widget' -Command {
        dotnet run --project 'tests\RecentAppsWidget.Tests\RecentAppsWidget.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test first-party Games & Apps widget' -Command {
        dotnet run --project 'tests\GamesAppsWidget.Tests\GamesAppsWidget.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test first-party Now Playing widget' -Command {
        dotnet run --project 'tests\MediaSessionsWidget.Tests\MediaSessionsWidget.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test first-party Settings widget' -Command {
        dotnet run --project 'tests\SettingsWidget.Tests\SettingsWidget.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build and test private platform diagnostics transport' -Command {
        dotnet run --project 'tests\PlatformDiagnostics.Tests\PlatformDiagnostics.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Build first-party Settings isolated worker' -Command {
        dotnet build 'src\FirstPartyWidgets\SettingsWidget.Worker\SettingsWidget.Worker.csproj' --configuration $Configuration --nologo
    }
    Invoke-Checked -Description 'Build and test widget package catalog' -Command {
        dotnet run --project 'tests\WidgetCatalog.Tests\WidgetCatalog.Tests.csproj' --configuration $Configuration
    }
    Invoke-Checked -Description 'Validate developer documentation contracts and local links' -Command {
        dotnet run --project 'tests\Documentation.Tests\Documentation.Tests.csproj' --configuration $Configuration
    }

    if (Test-Path -LiteralPath 'tests\WidgetBridge.Tests\WidgetBridge.Tests.csproj') {
        Invoke-Checked -Description 'Build and test native widget bridge' -Command {
            dotnet run --project 'tests\WidgetBridge.Tests\WidgetBridge.Tests.csproj' --configuration $Configuration
        }
    }
    Invoke-Checked -Description 'Prove first-party widgets use the community AppContainer path' -Command {
        dotnet run --project 'tests\FirstPartyWidgetConformance.Tests\FirstPartyWidgetConformance.Tests.csproj' --configuration $Configuration
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
