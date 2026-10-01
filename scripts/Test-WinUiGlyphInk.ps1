[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh glyph evidence directory.' }
$null = New-Item -ItemType Directory -Path $output
$started = [DateTime]::UtcNow
$launch = winapp run (Join-Path $root 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') `
    --no-build --arch x64 -p Platform=x64 --detach --json --args '--validate-glyphs --validation-platform-activation' | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $launch.ProcessId -le 0) { throw 'Glyph fixture did not launch.' }
$ownedApp = [int]$launch.ProcessId
$launch | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'launch.json')
try {
    $path = Join-Path $env:LOCALAPPDATA 'WidgetRail/WinUI/diagnostics/glyph-result.json'
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    $result = $null
    do {
        $file = Get-Item -LiteralPath $path -ErrorAction SilentlyContinue
        if ($file -and $file.LastWriteTimeUtc -ge $started) {
            try { $result = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json } catch { $result = $null }
            if ($result -and $result.pid -eq $ownedApp) { break }
        }
        if (-not (Get-Process -Id $ownedApp -ErrorAction SilentlyContinue)) { throw 'Glyph fixture exited before publishing evidence.' }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($result) { $result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'result.json') }
    if (-not $result -or $result.pid -ne $ownedApp -or -not $result.passed -or $result.samples.Count -ne 24) {
        throw "Native glyph ink checks failed: $($result.error)"
    }
    foreach ($family in @('Xbox','PlayStation')) {
        $raw = winapp ui invoke "Glyph.$family" -a $ownedApp --json
        if ($LASTEXITCODE -ne 0) { throw "Could not select $family glyph specimens: $raw" }
        $ready = winapp ui wait-for Glyph.Specimens -a $ownedApp -p Name --value "$family optical glyph matrix" -t 3000 --json | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0 -or -not $ready.found) { throw "The $family specimen matrix did not settle." }
        winapp ui screenshot Glyph.Specimens -a $ownedApp -o (Join-Path $output "$family.png") --json | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not capture the $family specimen matrix." }
    }
    $process = Get-Process -Id $ownedApp
    $payload = Join-Path ([IO.Path]::GetDirectoryName($process.Path)) 'OverlayFrontend.WinUI.dll'
    Get-FileHash -LiteralPath $payload -Algorithm SHA256 | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'payload-hash.json')
    $process.Dispose()
    "Passed $($result.checks.Count) native glyph checks with 24 optical raster samples."
} finally {
    if (Get-Process -Id $ownedApp -ErrorAction SilentlyContinue) {
        & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $ownedApp
        Wait-Process -Id $ownedApp -Timeout 15 -ErrorAction SilentlyContinue
        if (Get-Process -Id $ownedApp -ErrorAction SilentlyContinue) { throw 'Glyph fixture did not complete normal shutdown.' }
    }
}
