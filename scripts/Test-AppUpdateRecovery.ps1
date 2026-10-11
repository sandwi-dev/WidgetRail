# All mutations use an isolated temporary profile. No app or installer is launched.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '../src/AppUpdateRecovery/RecoveryEngine.ps1')
$root = Join-Path ([IO.Path]::GetTempPath()) ('wrail-recovery-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$passed = 0
function Check($condition, $message) { if (!$condition) { throw $message } }
function New-Fixture([string]$Name) {
    $base = Join-Path $root $Name
    $profile = Join-Path $base 'WidgetRail'
    $cache = Join-Path $base 'recovery'
    New-Item -ItemType Directory -Path $profile,$cache | Out-Null
    [IO.File]::WriteAllText((Join-Path $profile 'settings.json'), 'before')
    [IO.File]::WriteAllText((Join-Path $cache 'previous.exe'), 'old fixture; never launched')
    [IO.File]::WriteAllText((Join-Path $cache 'update.exe'), 'new fixture; never launched')
    return @{ Profile=$profile; Cache=$cache; Plan=@{ previousSha256=(Get-FileHash (Join-Path $cache 'previous.exe')).Hash; updateSha256=(Get-FileHash (Join-Path $cache 'update.exe')).Hash } }
}
try {
    $f = New-Fixture 'success'
    Invoke-RecoveryInstall $f.Cache $f.Profile $f.Plan { param($path) return 0 }
    Check ((Get-Content (Join-Path $f.Cache 'state.json') -Raw | ConvertFrom-Json).phase -eq 'installed') 'Install phase missing'
    [IO.File]::WriteAllText((Join-Path $f.Profile 'settings.json'), 'after')
    Invoke-RecoveryRestore $f.Cache $f.Profile $f.Plan { param($path) return 0 }
    Check ((Get-Content (Join-Path $f.Profile 'settings.json') -Raw) -eq 'before') 'Old profile not restored'
    Check ((Get-Content (Join-Path $f.Cache 'profile-after/settings.json') -Raw) -eq 'after') 'New data was lost'
    Invoke-RecoveryRestore $f.Cache $f.Profile $f.Plan { throw 'Completed recovery must not reinstall' }
    $passed++; Write-Output 'PASS install, verified backup, profile preservation and idempotent restore'

    $f = New-Fixture 'corrupt'
    [IO.File]::WriteAllText((Join-Path $f.Cache 'update.exe'), 'corrupt')
    $rejected = $false
    try { Invoke-RecoveryInstall $f.Cache $f.Profile $f.Plan { throw 'must not launch' } } catch { $rejected = $_.Exception.Message -like '*integrity*' }
    Check $rejected 'Corrupt installer admitted'
    Check (!(Test-Path (Join-Path $f.Cache 'profile-before'))) 'Backup began before checksum validation'
    $passed++; Write-Output 'PASS corrupt installer cannot begin transaction'

    $f = New-Fixture 'failed'
    try { Invoke-RecoveryInstall $f.Cache $f.Profile $f.Plan { return 2 } } catch { }
    Check ((Get-Content (Join-Path $f.Cache 'state.json') -Raw | ConvertFrom-Json).phase -eq 'install-failed') 'Failed setup lost recovery state'
    Invoke-RecoveryRestore $f.Cache $f.Profile $f.Plan { return 0 }
    Check ((Get-Content (Join-Path $f.Profile 'settings.json') -Raw) -eq 'before') 'Failed setup not recoverable'
    $passed++; Write-Output 'PASS failed setup remains recoverable'

    $f = New-Fixture 'interrupted'
    Invoke-RecoveryInstall $f.Cache $f.Profile $f.Plan { return 0 }
    Copy-RecoveryProfile (Join-Path $f.Cache 'profile-before') (Join-Path $f.Cache 'profile-restored')
    [IO.Directory]::Move($f.Profile, (Join-Path $f.Cache 'profile-after'))
    Write-RecoveryState $f.Cache 'profile-preserved'
    Invoke-RecoveryRestore $f.Cache $f.Profile $f.Plan { throw 'Installer must not run after profile was preserved' }
    Check ((Get-Content (Join-Path $f.Profile 'settings.json') -Raw) -eq 'before') 'Interrupted move did not resume'
    $passed++; Write-Output 'PASS interrupted profile swap resumes'

    $f = New-Fixture 'link'
    $outside = Join-Path $root 'outside'
    New-Item -ItemType Directory -Path $outside | Out-Null
    New-Item -ItemType Junction -Path (Join-Path $f.Profile 'junction') -Target $outside | Out-Null
    $rejected = $false
    try { Invoke-RecoveryInstall $f.Cache $f.Profile $f.Plan { throw 'must not launch' } } catch { $rejected = $_.Exception.Message -like '*link*' }
    Check $rejected 'Profile junction was followed'
    # Remove only the junction itself, never its target.
    [IO.Directory]::Delete((Join-Path $f.Profile 'junction'))
    $passed++; Write-Output 'PASS linked profile refuses backup without traversing'

    $f = New-Fixture 'locked'
    $lease = [IO.File]::Open((Join-Path $f.Profile 'settings.json'), 'Open', 'Write', 'None')
    try {
        $rejected = $false
        try { Invoke-RecoveryInstall $f.Cache $f.Profile $f.Plan { throw 'must not launch' } } catch { $rejected = $true }
        Check $rejected 'Locked settings admitted'
    } finally { $lease.Dispose() }
    Check ((Get-Content (Join-Path $f.Cache 'state.json') -Raw | ConvertFrom-Json).phase -eq 'backing-up') 'Incomplete backup treated as usable'
    $passed++; Write-Output 'PASS in-use profile aborts before setup'

    $f = New-Fixture 'backup-corrupt'
    Invoke-RecoveryInstall $f.Cache $f.Profile $f.Plan { return 0 }
    [IO.File]::WriteAllText((Join-Path $f.Cache 'profile-before/settings.json'), 'damaged')
    $rejected = $false
    try { Invoke-RecoveryRestore $f.Cache $f.Profile $f.Plan { throw 'must not launch' } } catch { $rejected = $_.Exception.Message -like '*integrity*' }
    Check $rejected 'Damaged backup accepted'
    Check ((Get-Content (Join-Path $f.Profile 'settings.json') -Raw) -eq 'before') 'Damaged backup changed the profile'
    $passed++; Write-Output 'PASS damaged backup is rejected before mutation'

    $f = New-Fixture 'nested'
    New-Item -ItemType Directory -Path (Join-Path $f.Profile 'widgets/one/empty') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $f.Profile 'widgets/one/data.json'), 'widget-data')
    Invoke-RecoveryInstall $f.Cache $f.Profile $f.Plan { return 0 }
    Invoke-RecoveryRestore $f.Cache $f.Profile $f.Plan { return 0 }
    Check ((Get-Content (Join-Path $f.Profile 'widgets/one/data.json') -Raw) -eq 'widget-data') 'Nested data lost'
    Check (Test-Path -LiteralPath (Join-Path $f.Profile 'widgets/one/empty')) 'Empty directory lost'
    $passed++; Write-Output 'PASS nested data and empty directories survive recovery'

    $f = New-Fixture 'partial-copy'
    Invoke-RecoveryInstall $f.Cache $f.Profile $f.Plan { return 0 }
    New-Item -ItemType Directory -Path (Join-Path $f.Cache 'profile-restored') | Out-Null
    [IO.File]::WriteAllText((Join-Path $f.Cache 'profile-restored/settings.json'), 'partial')
    Invoke-RecoveryRestore $f.Cache $f.Profile $f.Plan { return 0 }
    Check ((Get-Content (Join-Path $f.Profile 'settings.json') -Raw) -eq 'before') 'Partial copy not rebuilt'
    Check (@(Get-ChildItem -LiteralPath $f.Cache -Directory -Filter 'profile-partial-*').Count -eq 1) 'Partial copy was not preserved'
    $passed++; Write-Output 'PASS interrupted restore copy is preserved and rebuilt'

    Write-Output "$passed/9 recovery checks passed; no real profile or installer touched."
} finally {
    $full = [IO.Path]::GetFullPath($root)
    $expected = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($full.StartsWith($expected, [StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($full).StartsWith('wrail-recovery-test-')) {
        Remove-Item -LiteralPath $full -Recurse -Force
    }
}
