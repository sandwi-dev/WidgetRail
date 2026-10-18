# Synthetic installation metadata and inert installer callbacks only.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '../src/AppUpdateInstaller/UpdateInstaller.ps1')
$root = Join-Path ([IO.Path]::GetTempPath()) ('wrail-update-handoff-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
function Check($value, [string]$message) { if (!$value) { throw $message } }
try {
  foreach ($edition in @('production', 'bare')) {
    $installer = Join-Path $root 'update.exe'
    [IO.File]::WriteAllText($installer, 'Inert fixture, never executed')
    $plan = [pscustomobject]@{ schema=1; edition=$edition; previousVersion='0.1.0-preview.33'; updateVersion='0.1.0-preview.34'; previousRoot=(Join-Path $root 'old-app'); updateSha256=(Get-FileHash $installer).Hash }
    $script:installed = [pscustomobject]@{ ApplicationRoot=$plan.previousRoot; Edition=$edition; version=$plan.previousVersion }
    function Payload($path, $version) {
        New-Item -ItemType Directory -Path $path -Force | Out-Null
        $exe = Join-Path $path 'OverlayFrontend.WinUI.exe'
        [IO.File]::WriteAllText($exe, 'Inert application, never launched')
        [IO.File]::WriteAllText((Join-Path $path 'release.json'), (@{ version=$version; edition=$edition; files=@(@{path='OverlayFrontend.WinUI.exe';bytes=(Get-Item $exe).Length;sha256=(Get-FileHash $exe).Hash}) } | ConvertTo-Json -Depth 4))
    }
    Payload $plan.previousRoot $plan.previousVersion
    Payload (Join-Path $root 'new-app') $plan.updateVersion
    $events = [Collections.Generic.List[string]]::new()
    Invoke-UpdateHandoff $root $plan { $script:installed } { $events.Add('ready') } { $events.Add('closed') } {
        param($path)
        Check (($events -join ',') -eq 'ready,closed') 'Installer ran before acknowledgement and shutdown'
        $writeDenied = $false
        try { $stream = [IO.File]::Open($path, 'Open', 'Write', 'ReadWrite'); $stream.Dispose() } catch [IO.IOException] { $writeDenied = $true }
        Check $writeDenied 'Verified installer was not protected against modification'
        $script:installed = [pscustomobject]@{ ApplicationRoot=(Join-Path $root 'new-app'); Edition=$edition; version=$plan.updateVersion }
        $events.Add('installer'); return 0
    } { param($path) Check ($path -eq $script:installed.ApplicationRoot) 'Wrong app relaunched'; $events.Add('relaunch') }
    Check (($events -join ',') -eq 'ready,closed,installer,relaunch') 'Incorrect handoff order'
    Check (@(Get-ChildItem $root).Count -eq 3) 'Handoff created unexpected files or a profile backup'
    Write-Output 'PASS verification, readiness, graceful-exit ordering, locked installer, successful relaunch; no profile work'

    foreach ($code in @(2,3010,5)) {
        $script:installed = [pscustomobject]@{ ApplicationRoot=$plan.previousRoot; Edition=$edition; version=$plan.previousVersion }
        $events.Clear()
        $script:fixtureCode = $code
        Invoke-UpdateHandoff $root $plan { $script:installed } {} {} { return $script:fixtureCode } { $events.Add('relaunch') } -RecordOutcome { param($outcome) $events.Add($outcome) }
        $expected = switch ($code) { 2 {'cancelled'}; 3010 {'restart-required'}; default {'failed'} }
        Check (($events -join ',') -eq "$expected,relaunch") 'Verified installation did not reopen with outcome'
    }
    Write-Output 'PASS cancellation, restart-needed and failure report outcomes and reopen a verified usable app'

    $events.Clear()
    $result = Invoke-UpdateHandoff $root $plan { $script:installed } { throw 'Must not close host' } {} { throw 'Must not install' } {} -Preflight { 'interactive-required' }
    Check ($result -eq 'interactive-required') 'Prerequisite failure did not request interactive review'
    Write-Output 'PASS interactive fallback leaves working app open before confirmation'

    $silent = @(Get-UpdateSetupArguments $root $false)
    $interactiveArgs = @(Get-UpdateSetupArguments $root $true)
    Check ($silent -contains '/VERYSILENT' -and $silent -contains '/SUPPRESSMSGBOXES' -and $silent -contains '/NORESTART' -and $silent -contains '/RESTARTEXITCODE=3010') 'Unattended/restart arguments missing'
    Check ($interactiveArgs -notcontains '/VERYSILENT' -and $interactiveArgs -notcontains '/SUPPRESSMSGBOXES') 'Interactive setup suppressed UI'
    Write-Output 'PASS deliberate unattended and interactive argument sets'

    $exe = Join-Path $plan.previousRoot 'OverlayFrontend.WinUI.exe'
    [IO.File]::AppendAllText($exe, 'corrupt')
    $rejected=$false
    try { Invoke-UpdateHandoff $root $plan { $script:installed } {} {} { return 5 } { throw 'Must not relaunch corrupt app' } }
    catch { $rejected=$_.Exception.Message -like '*verified usable*' }
    Check $rejected 'Corrupt installed payload relaunched'
    Payload $plan.previousRoot $plan.previousVersion
    Write-Output 'PASS corrupt application inventory prevents relaunch'

    $rejected = $false
    try { Invoke-UpdateHandoff $root $plan { $script:installed } {} { $script:installed.version='0.1.0-preview.35' } { throw 'Must not install' } {} }
    catch { $rejected=$_.Exception.Message -like '*installation changed*' }
    Check $rejected 'Changed installed version was accepted'
    Write-Output 'PASS installation identity is rechecked after shutdown'

    [IO.File]::AppendAllText($installer,'tampered')
    $rejected=$false
    try { Invoke-UpdateHandoff $root $plan { throw 'Must not read' } { throw 'Must not acknowledge' } {} {} {} }
    catch { $rejected=$_.Exception.Message -like '*verification failed*' }
    Check $rejected 'Tampered installer was accepted'
    Write-Output 'PASS checksum failure prevents acknowledgement and launch'

    $mutexName='Local\WidgetRail.TestUpdate.'+[guid]::NewGuid().ToString('N')
    $fake=[Threading.Mutex]::new($false,$mutexName)
    $rejected=$false
    try { try { Wait-UpdateHostClosed $mutexName 0.1 } catch { $rejected=$_.Exception.Message -like '*could not close*' } }
    finally { $fake.Dispose() }
    Check $rejected 'Host-close deadline was not enforced'
    Wait-UpdateHostClosed $mutexName 0.1
    Write-Output "PASS bounded wait blocks running host and proceeds after exit ($edition)"
  }
} finally {
    $full=[IO.Path]::GetFullPath($root)
    $parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if ($full.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($full).StartsWith('wrail-update-handoff-test-')) { Remove-Item -LiteralPath $full -Recurse -Force }
}
