# Synthetic installation metadata and inert installer callbacks only.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '../src/AppUpdateInstaller/UpdateInstaller.ps1')
$root = Join-Path ([IO.Path]::GetTempPath()) ('wrail-update-handoff-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
function Check($value, [string]$message) { if (!$value) { throw $message } }
try {
    $installer = Join-Path $root 'update.exe'
    [IO.File]::WriteAllText($installer, 'Inert fixture, never executed')
    $plan = [pscustomobject]@{ schema=1; edition='production'; previousVersion='0.1.0-preview.33'; updateVersion='0.1.0-preview.34'; previousRoot=(Join-Path $root 'old-app'); updateSha256=(Get-FileHash $installer).Hash }
    $script:installed = [pscustomobject]@{ ApplicationRoot=$plan.previousRoot; Edition='production'; version=$plan.previousVersion }
    $events = [Collections.Generic.List[string]]::new()
    Invoke-UpdateHandoff $root $plan { $script:installed } { $events.Add('ready') } { $events.Add('closed') } {
        param($path)
        Check (($events -join ',') -eq 'ready,closed') 'Installer ran before acknowledgement and shutdown'
        $writeDenied = $false
        try { $stream = [IO.File]::Open($path, 'Open', 'Write', 'ReadWrite'); $stream.Dispose() } catch [IO.IOException] { $writeDenied = $true }
        Check $writeDenied 'Verified installer was not protected against modification'
        $script:installed = [pscustomobject]@{ ApplicationRoot=(Join-Path $root 'new-app'); Edition='production'; version=$plan.updateVersion }
        $events.Add('installer'); return 0
    } { param($path) Check ($path -eq $script:installed.ApplicationRoot) 'Wrong app relaunched'; $events.Add('relaunch') }
    Check (($events -join ',') -eq 'ready,closed,installer,relaunch') 'Incorrect handoff order'
    Check (@(Get-ChildItem $root).Count -eq 1) 'Handoff created unexpected files or a profile backup'
    Write-Output 'PASS verification, readiness, graceful-exit ordering, locked installer, successful relaunch; no profile work'

    foreach ($code in @(2,3010,5)) {
        $script:installed = [pscustomobject]@{ ApplicationRoot=$plan.previousRoot; Edition='production'; version=$plan.previousVersion }
        $rejected = $false
        try { Invoke-UpdateHandoff $root $plan { $script:installed } {} {} { return $code } { throw 'Must not relaunch' } }
        catch { if ($code -ne 5 -or $_.Exception.Message -notlike '*exit code 5*') { throw }; $rejected=$true }
        Check ($rejected -eq ($code -eq 5)) 'Wrong cancellation/reboot/failure handling'
    }
    Write-Output 'PASS installer cancellation, reboot-needed and failure do not relaunch'

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
    Write-Output 'PASS bounded wait blocks running host and proceeds after exit'
} finally {
    $full=[IO.Path]::GetFullPath($root)
    $parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if ($full.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($full).StartsWith('wrail-update-handoff-test-')) { Remove-Item -LiteralPath $full -Recurse -Force }
}
