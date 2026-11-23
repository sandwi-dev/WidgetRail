[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$FixtureDirectory,
    [Parameter(Mandatory)][string]$FrontendDirectory,
    [Parameter(Mandatory)][string]$NativeGeometryDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$AllowForeground
)
$ErrorActionPreference='Stop'
if(!$AllowForeground){throw 'This UI fixture opens generated windows. Coordinate foreground use, then pass -AllowForeground.'}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$root=Join-Path $repo 'artifacts/discord-desktop'
$fixture=[IO.Path]::GetFullPath($FixtureDirectory)
$frontend=[IO.Path]::GetFullPath($FrontendDirectory)
$output=[IO.Path]::GetFullPath($OutputDirectory)
$geometry=[IO.Path]::GetFullPath($NativeGeometryDirectory)
$geometryBinary=Join-Path $geometry 'geometry_lease.node'
if(!$geometry.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use a generated native geometry build under this checkout artifact root.'
}
$geometryProvenance=Get-Content -LiteralPath (Join-Path $geometry 'provenance.json') -Raw | ConvertFrom-Json
if($geometryProvenance.binary -cne $geometryBinary -or
    (Get-FileHash -LiteralPath $geometryBinary -Algorithm SHA256).Hash -cne $geometryProvenance.binarySha256) {
    throw 'Native geometry build provenance mismatch.'
}
if(!$output.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)){
    throw 'Choose a fresh output child of artifacts/discord-desktop.'
}
$runtime=Join-Path $fixture 'node_modules/electron/dist/electron.exe'
$hostExecutable=Join-Path $frontend 'OverlayFrontend.WinUI.exe'
foreach($path in @($runtime,$hostExecutable,(Join-Path $fixture 'main.cjs'))){if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing fixture prerequisite: $path"}}
New-Item -ItemType Directory -Path $output | Out-Null
$secret=[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
$pipe='wrail-desktop-surface-'+[guid]::NewGuid().ToString('N')
$helperConfiguration=Join-Path $output 'helper-config.json'
@{pipeName=$pipe;token=$secret;nativeGeometryPath=$geometryBinary}|ConvertTo-Json|Set-Content -LiteralPath $helperConfiguration
$helper=$null;$hostProcess=$null
try {
    $start=[Diagnostics.ProcessStartInfo]::new($runtime)
    # Detach GUI lifetime/stdio from the tool's short-lived pipe handles. The
    # helper itself creates its window hidden until the explicit fixture action.
    $start.UseShellExecute=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden;$start.WorkingDirectory=$fixture
    $start.ArgumentList.Add((Join-Path $fixture 'main.cjs'));$start.ArgumentList.Add('--fixture-config='+$helperConfiguration)
    $helper=[Diagnostics.Process]::Start($start)
    $deadline=[DateTimeOffset]::UtcNow.AddSeconds(20)
    while(!(Test-Path -LiteralPath (Join-Path $output 'helper-ready.json'))){
        if($helper.HasExited){throw 'Generated Electron helper exited before readiness.'}
        if([DateTimeOffset]::UtcNow -gt $deadline){throw 'Generated Electron helper readiness timed out.'}
        Start-Sleep -Milliseconds 100
    }
    $configuration=Join-Path $output 'host-config.json'
    @{pipeName=$pipe;token=$secret;helperProcessId=$helper.Id;helperStartTicks=$helper.StartTime.ToUniversalTime().Ticks;helperExecutable=$runtime}|
        ConvertTo-Json|Set-Content -LiteralPath $configuration
    $hostStart=[Diagnostics.ProcessStartInfo]::new($hostExecutable)
    $hostStart.UseShellExecute=$true;$hostStart.WorkingDirectory=$frontend
    $hostStart.ArgumentList.Add('--validate-desktop-surface='+$configuration)
    $hostStart.ArgumentList.Add('--validation-window-observation='+(Join-Path $output 'window-observations.json'))
    $hostProcess=[Diagnostics.Process]::Start($hostStart)
    [pscustomobject]@{helperProcessId=$helper.Id;hostProcessId=$hostProcess.Id;result=(Join-Path $output 'result.json');
        instruction='Click Run generated-window check in the fixture. No Discord or user content is involved.'}|ConvertTo-Json
    # Return immediately for Computer Use inspection. Helper has a two-minute
    # watchdog and restores its own window on the host pipe disconnect.
} catch {
    if($helper -and !$helper.HasExited){$helper.Kill($true)}
    throw
} finally {
    if($helper){$helper.Dispose()}
    if($hostProcess){$hostProcess.Dispose()}
}
