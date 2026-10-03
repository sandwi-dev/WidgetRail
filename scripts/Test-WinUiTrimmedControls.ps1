[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory, [string]$CaptureFixtureDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh evidence directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
$published = Join-Path $output 'published'
$project = Join-Path $repository 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj'
# Unlike the full debug fixture suite, this small fixture is trim-safe and uses
# synthetic control values and intercepted browser pages. It never creates a
# Bridge, changes audio levels or contacts a real website.
& dotnet publish $project -c Release -r win-x64 --self-contained true -p:Platform=x64 `
    -p:EnableWidgetValidation=false -p:EnableTrimmedControlValidation=true `
    -o $published -m:1 -nr:false "-bl:$output/publish.binlog" *> (Join-Path $output 'publish.log')
if ($LASTEXITCODE -ne 0) { throw "Trimmed publication failed: $output/publish.log" }
$result = Join-Path $output 'result.txt'
$ownedPid = 0
try {
    # This is the actual unpackaged trimmed publication, not Debug build output.
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $published 'OverlayFrontend.WinUI.exe'))
    $start.UseShellExecute = $false
    $start.WorkingDirectory = $published
    $start.ArgumentList.Add('--validate-trimmed-slider')
    $start.ArgumentList.Add('--result=' + $result)
    if ($CaptureFixtureDirectory) { $start.ArgumentList.Add('--capture-fixtures=' + [IO.Path]::GetFullPath($CaptureFixtureDirectory)) }
    $process = [Diagnostics.Process]::Start($start)
    $ownedPid = $process.Id
    @{ ProcessId=$ownedPid; executable=$start.FileName; packaged=$false } | ConvertTo-Json | Set-Content (Join-Path $output 'launch.json')
    if ($ownedPid -le 0) { throw 'Activation returned no owned process.' }
    $deadline = [DateTime]::UtcNow.AddSeconds(70)
    while (!(Test-Path -LiteralPath $result) -and [DateTime]::UtcNow -lt $deadline) {
        if (!(Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)) { break }
        Start-Sleep -Milliseconds 100
    }
    if (!(Test-Path -LiteralPath $result)) { throw 'Trimmed control probe produced no result.' }
    $text = Get-Content -LiteralPath $result -Raw
    if (!$text.StartsWith('PASS:')) { throw $text }
    Write-Output $text
}
finally {
    # The fixture writes its result immediately before Close; allow its normal
    # process shutdown to finish rather than racing the already-destroyed HWND.
    if ($ownedPid -and (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)) {
        Wait-Process -Id $ownedPid -Timeout 5 -ErrorAction SilentlyContinue
    }
    if ($ownedPid -and (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)) {
        & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $ownedPid
        Wait-Process -Id $ownedPid -Timeout 15 -ErrorAction SilentlyContinue
        if (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue) { throw 'Probe did not close.' }
    }
    if ($process) { $process.Dispose() }
}
