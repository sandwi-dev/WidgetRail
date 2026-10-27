param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [string]$BridgeFixtureDirectory,
    [string]$OutputDirectory,
    [ValidateRange(30,600)][int]$TimeoutSeconds = 120
)
# Build WidgetBridge.Tests and a validation-enabled frontend before running.
# This runner stages the existing authenticated worker/broker/bridge fixture;
# it does not install widgets or use an existing account/profile.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$candidate = [IO.Path]::GetFullPath($CandidateDirectory)
$exe = Join-Path $candidate 'OverlayFrontend.WinUI.exe'
if (!$BridgeFixtureDirectory) {
    $BridgeFixtureDirectory = Join-Path $repo 'tests/WidgetBridge.Tests/bin/Debug/net8.0-windows10.0.19041.0/win-x64'
}
$fixture = [IO.Path]::GetFullPath($BridgeFixtureDirectory)
$required = @('WidgetBridge.Tests.exe','WidgetBridge.Tests.dll','WidgetBridge.Tests.deps.json',
    'WidgetBridge.Tests.runtimeconfig.json','WebSessionRuntime.js','YouTubeSessionFixture.html')
foreach ($file in @($exe) + @($required | ForEach-Object { Join-Path $fixture $_ })) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Build the fixture prerequisites first: $file" }
}
if (!$OutputDirectory) {
    $OutputDirectory = Join-Path $repo ('artifacts/discord-webview/hosted-runs/' + [guid]::NewGuid().ToString('N'))
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh evidence directory; existing profiles are never reused.' }
$installation = Join-Path $output 'installation'
$bridge = Join-Path $installation 'runtime/Bridge'
# Keep WebView2's generated named profile below its problematic long-path range.
$profile = Join-Path $output 'p'
$profilePrefix = Join-Path $profile ('WebBrowser/EBWebView/pkg-persistent-' + ('0' * 48))
if ($profilePrefix.Length -gt 230) { throw 'Choose a shorter OutputDirectory for the isolated WebView2 profile.' }
New-Item -ItemType Directory -Force $bridge,$profile,(Join-Path $output 'catalog') | Out-Null

# The tests apphost embeds WidgetBridge.Tests.dll. Renaming only its staged EXE
# supplies the production bridge filename while retaining its DLL/deps/runtimeconfig.
# Ignore markers from unrelated test runs; only this fixture's marker is admitted.
foreach ($item in Get-ChildItem -LiteralPath $fixture -Force) {
    if (!$item.PSIsContainer -and ($item.Extension -eq '.marker' -or $item.Name -eq 'catalogue-native-fixture.json')) { continue }
    Copy-Item -LiteralPath $item.FullName -Destination $bridge -Recurse
}
Copy-Item -LiteralPath (Join-Path $bridge 'WidgetBridge.Tests.exe') -Destination (Join-Path $bridge 'WidgetBridge.exe') -Force
[IO.File]::WriteAllText((Join-Path $bridge 'web-session-fixture.marker'), '')
@{catalogVersion=1;widgets=@();bundledWidgets=@()} | ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $installation 'widget-catalog.json')
@{version=1;step='complete';paused=$false} | ConvertTo-Json |
    Set-Content -LiteralPath (Join-Path $profile 'introduction-progress.json')
$configuration = Join-Path $output 'shell.json'
@{InstallationRoot=$installation;SettingsRoot=$profile;InstalledCatalogRoot=(Join-Path $output 'catalog');
    InitialWidgetId='web-session';LayoutDiagnosticsPath=(Join-Path $output 'layout.json')} | ConvertTo-Json |
    Set-Content -LiteralPath $configuration
@{candidate=$candidate;frontendSha256=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash;
    bridgeFixture=$fixture;bridgeFixtureSha256=(Get-FileHash -LiteralPath (Join-Path $bridge 'WidgetBridge.Tests.dll') -Algorithm SHA256).Hash;
    runtimeSha256=(Get-FileHash -LiteralPath (Join-Path $bridge 'WebSessionRuntime.js') -Algorithm SHA256).Hash;
    startedUtc=[DateTime]::UtcNow.ToString('O')} | ConvertTo-Json |
    Set-Content -LiteralPath (Join-Path $output 'candidate.json')

$result = Join-Path $output 'result.json'
$process = Start-Process -FilePath $exe -ArgumentList @(
    ('"--shell-config=' + $configuration + '"'), '--shell-no-controller',
    ('"--validate-hosted-session=' + $result + '"')) -WindowStyle Hidden -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $data = $null
    while ($null -eq $data) {
        if (Test-Path -LiteralPath $result) {
            # The existing shell fixture writes once with WriteAllText. A reader
            # can observe an incomplete file; only accept a complete JSON report.
            try {
                $parsed = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
                if ($null -ne $parsed -and $null -ne $parsed.passed -and $null -ne $parsed.checks) { $data = $parsed }
            } catch { }
        }
        if ($null -ne $data) { break }
        if ($process.HasExited) { throw "Hosted-session fixture exited with $($process.ExitCode). Evidence: $output" }
        if ([DateTime]::UtcNow -gt $deadline) { throw "Hosted-session fixture timed out. Evidence: $output" }
        Start-Sleep -Milliseconds 250
    }
    if (!$data.passed) { throw "Hosted-session fixture failed at '$($data.step)': $($data.error). Evidence: $result" }
    # Nine existing end-to-end checks plus four actual owner startup checks.
    if ($data.checks.Count -lt 13) { throw "Hosted-session fixture did not complete all 13 expected checks. Evidence: $result" }
    foreach ($check in @(
        "A rejected duplicate Open cannot retire another attempt's live browser",
        'Close during profile registration rejects its late result without creating or retiring another browser',
        'Configuration failure disposes the unregistered native surface and preserves the existing session',
        'Rejected and cancelled startup attempts release their capacity reservations')) {
        if ($data.checks -notcontains $check) { throw "Candidate is missing the startup ownership check: $check" }
    }
    $data.checks | ForEach-Object { Write-Output "PASS $_" }
    Write-Output "Evidence: $result"
} finally {
    # Touch only the frontend launched above. Its owned bridge observes pipe
    # closure and retires its own fixture worker; other running candidates stay open.
    if (!$process.HasExited) {
        [void]$process.CloseMainWindow()
        if (!$process.WaitForExit(5000)) { Stop-Process -Id $process.Id }
    }
    $process.Dispose()
}
