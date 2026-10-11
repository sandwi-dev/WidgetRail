# Standalone recovery engine. Windows PowerShell 5.1; no WidgetRail runtime needed.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RecoveryProgress = $null
function Report-RecoveryProgress([string]$Phase) {
    if ($null -ne $script:RecoveryProgress) { & $script:RecoveryProgress $Phase }
}
function Get-RecoveryHash([string]$Path) {
    Report-RecoveryProgress 'verify'
    $algorithm = [Security.Cryptography.SHA256]::Create()
    $source = $null
    try {
        $source = [IO.File]::Open($Path, 'Open', 'Read', 'Read')
        $buffer = New-Object byte[] 1048576
        [long]$count = 0
        while (($read = $source.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $null = $algorithm.TransformBlock($buffer, 0, $read, $buffer, 0)
            $count += $read
            if ($count -ge 16MB) { Report-RecoveryProgress 'verify'; $count = 0 }
        }
        $null = $algorithm.TransformFinalBlock($buffer, 0, 0)
        return [BitConverter]::ToString($algorithm.Hash).Replace('-', '')
    } finally { if ($null -ne $source) { $source.Dispose() }; $algorithm.Dispose() }
}
function Wait-RecoveryHostClosed([string]$MutexName = 'Local\WidgetRail.OverlayHost.Running', [double]$TimeoutSeconds = 120) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        [Threading.Mutex]$running = $null
        if (![Threading.Mutex]::TryOpenExisting($MutexName, [ref]$running)) { return $true }
        $running.Dispose()
        if ([DateTime]::UtcNow -ge $deadline) { return $false }
        Start-Sleep -Milliseconds 100
    } while ($true)
}
function Assert-PlainPath([string]$Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw 'Recovery cannot follow symbolic links or directory junctions.'
            }
        }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if ($parent -eq $cursor) { break }
        $cursor = $parent
    }
}

function Get-ProfileInventory([string]$Root) {
    Assert-PlainPath $Root
    $result = [Collections.Generic.List[object]]::new()
    $queue = [Collections.Generic.Queue[string]]::new()
    if (Test-Path -LiteralPath $Root) { $queue.Enqueue($Root) }
    [long]$bytes = 0
    while ($queue.Count) {
        Report-RecoveryProgress 'copy'
        foreach ($entry in Get-ChildItem -LiteralPath $queue.Dequeue() -Force) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Profile contains a link. Backup stopped without installing.' }
            $relative = $entry.FullName.Substring($Root.TrimEnd('\').Length + 1)
            $directory = $entry.PSIsContainer
            if (!$directory) { $bytes += $entry.Length }
            if ($bytes -gt 10GB -or $result.Count -ge 100000) { throw 'Profile exceeds the 10 GiB or 100,000 entry recovery limit. No installation started.' }
            $result.Add([pscustomobject]@{ Relative = $relative; Directory = $directory; Bytes = $(if ($directory) { 0 } else { $entry.Length }) })
            if ($directory) { $queue.Enqueue($entry.FullName) }
        }
    }
    return $result.ToArray()
}

function Copy-RecoveryProfile([string]$Source, [string]$Destination) {
    Assert-PlainPath $Source
    Assert-PlainPath $Destination
    if (Test-Path -LiteralPath $Destination) { throw 'Backup destination already exists. Existing data was preserved.' }
    $items = @(Get-ProfileInventory $Source)
    [long]$bytes = 0
    foreach ($item in $items) { $bytes += $item.Bytes }
    $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($Destination))
    if ($drive.AvailableFreeSpace -lt ($bytes + 512MB)) { throw 'Not enough free space for a complete recovery copy.' }
    New-Item -ItemType Directory -Path $Destination | Out-Null
    foreach ($item in $items) {
        Report-RecoveryProgress 'copy'
        $from = Join-Path $Source $item.Relative
        $to = Join-Path $Destination $item.Relative
        Assert-PlainPath $from
        if ($item.Directory) { New-Item -ItemType Directory -Path $to | Out-Null }
        else {
            # Deny concurrent writers; a changed/unreadable file aborts the backup.
            $sourceStream = [IO.File]::Open($from, 'Open', 'Read', 'Read')
            try {
                $output = [IO.File]::Open($to, 'CreateNew', 'Write', 'None')
                try {
                    $buffer = New-Object byte[] 1048576
                    [long]$copied = 0
                    while (($read = $sourceStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                        $output.Write($buffer, 0, $read); $copied += $read
                        if ($copied -ge 16MB) { Report-RecoveryProgress 'copy'; $copied = 0 }
                    }
                    $output.Flush($true)
                } finally { $output.Dispose() }
            } finally { $sourceStream.Dispose() }
            if ((Get-Item -LiteralPath $to).Length -ne $item.Bytes) { throw 'Profile changed during backup. Installation was stopped.' }
            if ((Get-RecoveryHash $from) -ne (Get-RecoveryHash $to)) { throw 'Backup verification failed.' }
        }
    }
    # Apply original ACLs after copying children, preserving sandbox access without
    # letting inherited package ACLs prevent the backup itself from completing.
    foreach ($item in @($items | Sort-Object { $_.Relative.Length } -Descending)) {
        Set-Acl -LiteralPath (Join-Path $Destination $item.Relative) -AclObject (Get-Acl -LiteralPath (Join-Path $Source $item.Relative))
    }
    if (Test-Path -LiteralPath $Source) { Set-Acl -LiteralPath $Destination -AclObject (Get-Acl -LiteralPath $Source) }
}

function Write-ProfileManifest([string]$Root, [string]$Profile) {
    $entries = @(Get-ProfileInventory $Profile | ForEach-Object {
        foreach ($item in $_) {
            [pscustomobject]@{ relative = $item.Relative; directory = $item.Directory; sha256 = $(if ($item.Directory) { '' } else { (Get-RecoveryHash (Join-Path $Profile $item.Relative)) }) }
        }
    })
    [IO.File]::WriteAllText((Join-Path $Root 'profile-manifest.json'), (ConvertTo-Json -InputObject @($entries) -Depth 4))
}

function Assert-ProfileManifest([string]$Root, [string]$Profile) {
    $manifest = Join-Path $Root 'profile-manifest.json'
    if ((Get-Item -LiteralPath $manifest).Length -gt 32MB) { throw 'Recovery manifest is oversized.' }
    $entries = [object[]](Get-Content -LiteralPath $manifest -Raw -Encoding UTF8 | ConvertFrom-Json)
    $inventory = @(Get-ProfileInventory $Profile)
    if ($entries.Count -ne $inventory.Count) { throw 'Backup contents changed. No profile was replaced.' }
    $byPath = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($entry in $entries) {
        if ($byPath.ContainsKey($entry.relative)) { throw 'Recovery manifest repeats a path.' }
        $byPath.Add($entry.relative, $entry)
    }
    foreach ($item in $inventory) {
        if (!$byPath.ContainsKey($item.Relative)) { throw 'Backup manifest does not match its contents.' }
        $entry = $byPath[$item.Relative]
        if ($entry.directory -ne $item.Directory) { throw 'Backup manifest does not match its contents.' }
        if (!$item.Directory -and (Get-RecoveryHash (Join-Path $Profile $item.Relative)) -ne $entry.sha256) { throw 'Backup integrity check failed.' }
    }
}
function Write-RecoveryState([string]$Root, [string]$Phase) {
    Report-RecoveryProgress $Phase
    $path = Join-Path $Root 'state.json'
    $temporary = Join-Path $Root 'state.pending'
    [IO.File]::WriteAllText($temporary, (@{ phase = $Phase; utc = [DateTime]::UtcNow.ToString('o') } | ConvertTo-Json))
    if (Test-Path -LiteralPath $path) { [IO.File]::Replace($temporary, $path, (Join-Path $Root 'state.previous')) }
    else { [IO.File]::Move($temporary, $path) }
}

function Open-VerifiedInstaller([string]$Path, [string]$Hash) {
    Assert-PlainPath $Path
    if ($Hash -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid installer checksum.' }
    $lease = [IO.File]::Open($Path, 'Open', 'Read', 'Read')
    try {
        $actual = Get-RecoveryHash $Path
        if ($actual -ne $Hash) { throw 'Installer integrity check failed. Nothing was launched.' }
        return $lease
    } catch { $lease.Dispose(); throw }
}

function Invoke-RecoveryInstall([string]$Root, [string]$Profile, [object]$Plan, [scriptblock]$RunInstaller) {
    $backup = Join-Path $Root 'profile-before'
    if (Test-Path -LiteralPath (Join-Path $Root 'state.json')) { throw 'This recovery set was already used. Use its recovery option rather than installing twice.' }
    $oldLease = Open-VerifiedInstaller (Join-Path $Root 'previous.exe') $Plan.previousSha256
    try {
        $newLease = Open-VerifiedInstaller (Join-Path $Root 'update.exe') $Plan.updateSha256
        try {
            Write-RecoveryState $Root 'backing-up'
            Copy-RecoveryProfile $Profile $backup
            Write-ProfileManifest $Root $backup
            Write-RecoveryState $Root 'backup-ready'
            Write-RecoveryState $Root 'installing'
            $code = & $RunInstaller (Join-Path $Root 'update.exe')
            if ($code -notin @(0, 3010)) { Write-RecoveryState $Root 'install-failed'; throw 'Setup did not finish. Your recovery copy and previous installer are available.' }
            Write-RecoveryState $Root 'installed'
        } finally { $newLease.Dispose() }
    } finally { $oldLease.Dispose() }
}

function Invoke-RecoveryRestore([string]$Root, [string]$Profile, [object]$Plan, [scriptblock]$RunInstaller) {
    $state = Get-Content -LiteralPath (Join-Path $Root 'state.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($state.phase -eq 'restored') { return }
    if ($state.phase -eq 'backing-up') { throw 'Backup did not finish. Setup was never started; original profile is unchanged.' }
    if ($state.phase -notin @('backup-ready','installing','installed','install-failed','restoring-installer','restoring-profile','profile-preserved')) { throw 'Unknown recovery state.' }
    $lease = Open-VerifiedInstaller (Join-Path $Root 'previous.exe') $Plan.previousSha256
    try {
        Assert-ProfileManifest $Root (Join-Path $Root 'profile-before')
        $staging = Join-Path $Root 'profile-restored'
        $preserved = Join-Path $Root 'profile-after'
        if ($state.phase -ne 'profile-preserved') {
            if (Test-Path -LiteralPath $staging) {
                try { Assert-ProfileManifest $Root $staging }
                catch {
                    # Preserve interrupted copies and rebuild from the verified original.
                    if (@(Get-ChildItem -LiteralPath $Root -Directory -Filter 'profile-partial-*').Count -ge 3) { throw 'Three partial restore copies remain. Free space and retain needed copies before retrying.' }
                    [IO.Directory]::Move($staging, (Join-Path $Root ('profile-partial-' + [guid]::NewGuid().ToString('N'))))
                }
            }
            if (!(Test-Path -LiteralPath $staging)) { Copy-RecoveryProfile (Join-Path $Root 'profile-before') $staging }
            Assert-ProfileManifest $Root $staging
            Write-RecoveryState $Root 'restoring-installer'
            $code = & $RunInstaller (Join-Path $Root 'previous.exe')
            if ($code -notin @(0, 3010)) { throw 'Previous setup did not finish. Current profile was preserved. Run recovery again to retry.' }
            Write-RecoveryState $Root 'restoring-profile'
            if (Test-Path -LiteralPath $Profile) {
                if (Test-Path -LiteralPath $preserved) { throw 'A post-update profile already exists. No data was overwritten.' }
                Assert-PlainPath $Profile
                [IO.Directory]::Move($Profile, $preserved)
            }
            Write-RecoveryState $Root 'profile-preserved'
        }
        if (Test-Path -LiteralPath $staging) {
            Assert-ProfileManifest $Root $staging
            if (Test-Path -LiteralPath $Profile) { throw 'Profile changed while recovery was pending. Existing data was preserved.' }
            [IO.Directory]::Move($staging, $Profile)
        } elseif (!(Test-Path -LiteralPath $Profile)) { throw 'Restored profile is missing.' }
        Write-RecoveryState $Root 'restored'
    } finally { $lease.Dispose() }
}
