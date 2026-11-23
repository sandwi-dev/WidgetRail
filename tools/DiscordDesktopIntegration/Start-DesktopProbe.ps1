[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$JournalDirectory,
    [Parameter(Mandatory)][string]$ExpectedJournalSha256,
    [switch]$Launch
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $repo 'tools/DiscordDesktopIntegration/DesktopProbeFiles.ps1')
$journalRoot=Join-Path $repo 'artifacts/discord-desktop'
$directory=Full $JournalDirectory
if (!$directory.StartsWith($journalRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Journal must be inside this checkout diagnostic artifacts.' }
$journalPath=Join-Path $directory 'journal.json'

function Read-InstalledProbe {
    $record=Read-PinnedJson $journalPath $ExpectedJournalSha256
    if ($record.schemaVersion -ne 1 -or $record.state -ne 'installed' -or $record.transactionId -notmatch '^[a-f0-9]{32}$') { throw 'Only a hash-pinned installed probe journal may be launched.' }
    $exe=Full $record.executable; $resources=Full $record.resourcesDirectory
    if ((Split-Path -Leaf $exe) -notin @('Discord.exe','DiscordPTB.exe','DiscordCanary.exe') -or
        (Split-Path -Leaf (Split-Path -Parent $exe)) -notmatch '^app-\d+(?:\.\d+){1,3}$' -or
        !(Same $resources (Join-Path (Split-Path -Parent $exe) 'resources'))) { throw 'Journal does not identify one exact Discord application version.' }
    Assert-Hash $exe $record.executableSha256
    Assert-Hash (Join-Path $resources 'app.asar') $record.bootstrapSha256
    Assert-Hash (Join-Path $resources '_app.asar') $record.originalAppAsarSha256
    foreach ($marker in @('app','app.asar.bak','app.asar.original','app.asar.orig','_app.asar.original','app.asar.unpacked')) {
        if (Test-Path -LiteralPath (Join-Path $resources $marker)) { throw 'The installed loader layout changed; inspect it before launch.' }
    }
    $provenancePath=Full $record.provenancePath
    if ((Split-Path -Leaf $provenancePath) -ne 'provenance.json') { throw 'Unexpected provenance path.' }
    $qualified=Read-QualifiedDesktopProbeStage (Split-Path -Parent $provenancePath) $record.provenanceSha256
    if (!(Same $record.bootstrapSha256 $qualified.provenance.bootstrapProposal.archiveSha256)) { throw 'Installed bootstrap and staged build disagree.' }
    return [pscustomobject]@{journal=$record;executable=$exe;resources=$resources;stage=$qualified.directory}
}
function Read-ClientProcesses([string]$Executable) {
    $versionDirectory=(Split-Path -Parent $Executable)+[IO.Path]::DirectorySeparatorChar
    $installationDirectory=(Split-Path -Parent (Split-Path -Parent $Executable))+[IO.Path]::DirectorySeparatorChar
    $matching=[Collections.Generic.List[object]]::new();$other=[Collections.Generic.List[object]]::new()
    foreach ($process in @(Get-CimInstance Win32_Process)) {
        $path=$process.ExecutablePath
        if (!$path -and $process.Name -in @('Discord.exe','DiscordPTB.exe','DiscordCanary.exe')) { throw 'A Discord process identity is unavailable; refusing an ambiguous launch.' }
        if (!$path) { continue }
        $item=[pscustomobject]@{processId=$process.ProcessId;executable=$path}
        if (([string]$path).StartsWith($versionDirectory,[StringComparison]::OrdinalIgnoreCase)) { $matching.Add($item) }
        elseif (([string]$path).StartsWith($installationDirectory,[StringComparison]::OrdinalIgnoreCase) -and
            $process.Name -in @('Discord.exe','DiscordPTB.exe','DiscordCanary.exe','Update.exe')) { $other.Add($item) }
    }
    return [pscustomobject]@{matching=$matching.ToArray();otherVersion=$other.ToArray()}
}

$installed=Read-InstalledProbe
$processes=Read-ClientProcesses $installed.executable
$report=[ordered]@{schemaVersion=1;launchRequested=[bool]$Launch;performed=$false;status='planned';
    journalPath=$journalPath;executable=$installed.executable;stagedBuild=$installed.stage;
    useShellExecute=$true;registeredApplicationLookup=$false;redirectStandardStreams=$false;
    runningTargetProcesses=$processes.matching;otherVersionProcesses=$processes.otherVersion;
    launchedProcessId=$null;verifiedProcessId=$null;
    scope='Exact executable path only. No updater fallback, process termination, installation change or automatic retry. Hidden window style is advisory, not a guarantee that Discord stays hidden.'}
if (!$Launch) { $report|ConvertTo-Json -Depth 7; return }
if ($processes.otherVersion.Count) { throw 'Another version/updater of this installation is running; no launch was attempted.' }
if ($processes.matching.Count) { $report.status='already-running';$report|ConvertTo-Json -Depth 7;return }

# Revalidate immediately before the one permitted process creation. No generic
# app-id lookup: that path routed through Squirrel/Update.exe during live testing.
$installed=Read-InstalledProbe
$processes=Read-ClientProcesses $installed.executable
if ($processes.matching.Count -or $processes.otherVersion.Count) { throw 'Client process state changed during launch preparation; no launch was attempted.' }
$startInfo=[Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName=$installed.executable
$startInfo.WorkingDirectory=Split-Path -Parent $installed.executable
$startInfo.UseShellExecute=$true
$startInfo.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
# ShellExecute avoids inheriting the tool's stdout/stderr pipes, which produced
# EPIPE in Discord's console logger when the tool invocation ended.
$started=[Diagnostics.Process]::Start($startInfo)
if ($null -eq $started) { throw 'The exact launch returned no process identity. No automatic retry was performed.' }
try {
    $report.launchedProcessId=$started.Id
    if ($started.HasExited) { throw 'The launched process already exited. No automatic retry or updater fallback was performed.' }
    $clock=[Diagnostics.Stopwatch]::StartNew()
    $observed=$null
    do {
        $candidates=@(Get-CimInstance Win32_Process -Filter ('ProcessId='+$started.Id))
        if ($candidates.Count -eq 1) {
            $observed=$candidates[0]
            if ($observed.ExecutablePath) { break }
        }
        Start-Sleep -Milliseconds 100
    } while ($clock.ElapsedMilliseconds -lt 3000)
    if ($started.HasExited -or $null -eq $observed -or !$observed.ExecutablePath -or
        $observed.ProcessId -ne $started.Id -or !(Same $observed.ExecutablePath $installed.executable)) {
        throw 'Launch outcome is unconfirmed or ran a different image. No process was killed and no updater/retry fallback was attempted.'
    }
    # Successful process creation is not a claim of adapter readiness, sign-in,
    # native media compatibility or a completed user flow.
    $report.verifiedProcessId=$observed.ProcessId
    $report.status='launched-image-verified';$report.performed=$true
    $report|ConvertTo-Json -Depth 7
} finally { $started.Dispose() }
