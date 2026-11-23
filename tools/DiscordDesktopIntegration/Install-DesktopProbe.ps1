[CmdletBinding()]
param(
    [ValidateSet('Install','Restore')][string]$Mode='Install',
    [string]$Executable,
    [string]$ResourcesDirectory,
    [string]$ReviewedTarget,
    [string]$ExpectedReviewSha256,
    [string]$StagedBuild,
    [string]$ExpectedProvenanceSha256,
    [Parameter(Mandatory)][string]$JournalDirectory,
    [string]$ExpectedJournalSha256,
    [switch]$Apply
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$journalRoot=Join-Path $repo 'artifacts/discord-desktop'

. (Join-Path $repo 'tools/DiscordDesktopIntegration/DesktopProbeFiles.ps1')
function Assert-Target([string]$Exe,[string]$Resources,[string]$ExeHash) {
    Assert-PlainPath $Exe; Assert-PlainPath $Resources
    $directory=Split-Path -Parent $Exe
    if ((Split-Path -Leaf $Exe) -notin @('Discord.exe','DiscordPTB.exe','DiscordCanary.exe') -or
        (Split-Path -Leaf $directory) -notmatch '^app-\d+(?:\.\d+){1,3}$' -or
        !(Same $Resources (Join-Path $directory 'resources')) -or
        !(Test-Path -LiteralPath $Resources -PathType Container)) { throw 'Executable and resources must identify one exact Discord app-version directory.' }
    Assert-Hash $Exe $ExeHash
}
function Read-RunningTarget([string]$Exe) {
    $directory=(Split-Path -Parent $Exe)+[IO.Path]::DirectorySeparatorChar
    $matches=[Collections.Generic.List[object]]::new()
    foreach ($process in @(Get-CimInstance Win32_Process)) {
        $path=$process.ExecutablePath
        if (!$path -and $process.Name -in @('Discord.exe','DiscordPTB.exe','DiscordCanary.exe')) {
            throw 'A Discord process path cannot be inspected; client closure cannot be established.'
        }
        if ($path -and ([string]$path).StartsWith($directory,[StringComparison]::OrdinalIgnoreCase)) {
            $matches.Add([pscustomobject]@{processId=$process.ProcessId;executable=$path})
        }
    }
    return $matches.ToArray()
}
function Assert-Closed([string]$Exe) {
    if (@(Read-RunningTarget $Exe).Count) { throw 'The exact selected Discord installation must be closed. This script never kills or restarts processes.' }
}
function Assert-NoOtherLoader([string]$Resources,[bool]$AllowOriginalBackup=$false) {
    $names=@('app','app.asar.bak','app.asar.original','app.asar.orig','_app.asar.original','app.asar.unpacked')
    if (!$AllowOriginalBackup) { $names += '_app.asar' }
    foreach ($name in $names) {
        if (Test-Path -LiteralPath (Join-Path $Resources $name)) { throw "Unclassified loader or backup collision: $name" }
    }
}
function Save-Journal($Value,[string]$Directory) {
    Assert-PlainPath $Directory
    $temporary=Join-Path $Directory ('journal-'+[guid]::NewGuid().ToString('N')+'.tmp')
    [IO.File]::WriteAllText($temporary,($Value|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporary,(Join-Path $Directory 'journal.json'),$true)
}

$journalDirectory=Full $JournalDirectory
if (!$journalDirectory.StartsWith($journalRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'The diagnostic transaction journal must be below this checkout artifacts/discord-desktop.'
}
Assert-PlainPath $journalDirectory

if ($Mode -eq 'Restore') {
    $journalPath=Join-Path $journalDirectory 'journal.json'
    $journal=Read-PinnedJson $journalPath $ExpectedJournalSha256
    if ($journal.schemaVersion -ne 1 -or $journal.transactionId -notmatch '^[a-f0-9]{32}$') { throw 'Unsupported journal.' }
    $exe=Full $journal.executable; $resources=Full $journal.resourcesDirectory
    Assert-Target $exe $resources $journal.executableSha256
    Assert-NoOtherLoader $resources $true
    $app=Join-Path $resources 'app.asar'; $original=Join-Path $resources '_app.asar'
    $archive=Join-Path $resources ('.widgetrail-probe-'+$journal.transactionId+'.removed.asar')
    $running=@(Read-RunningTarget $exe)
    $appExists=Test-Path -LiteralPath $app
    $backupExists=Test-Path -LiteralPath $original
    if ($appExists) { $appHash=Hash $app } else { $appHash=$null }
    if ($backupExists) { Assert-Hash $original $journal.originalAppAsarSha256 }
    $alreadyOriginal=$appExists -and (Same $appHash $journal.originalAppAsarSha256) -and !$backupExists
    if (!$alreadyOriginal) {
        if (!$backupExists -or ($appExists -and !(Same $appHash $journal.bootstrapSha256))) { throw 'Restore conflicts with an update or unknown current loader. No files changed.' }
        if (Test-Path -LiteralPath $archive) {
            # A previous restore can stop after preserving the bootstrap but
            # before returning _app.asar. Resume only that exact known state.
            if ($appExists) { throw 'Restore archive collision; inspect the existing recovery files.' }
            Assert-Hash $archive $journal.bootstrapSha256
        }
    }
    $report=[ordered]@{mode='Restore';applyRequested=[bool]$Apply;executable=$exe;resourcesDirectory=$resources;journalPath=$journalPath;
        runningProcesses=$running;alreadyOriginal=[bool]$alreadyOriginal;originalSha256=$journal.originalAppAsarSha256;
        bootstrapSha256=$journal.bootstrapSha256;preservedBootstrap=$archive;performed=$false}
    if (!$Apply) { $report|ConvertTo-Json -Depth 7; return }
    Assert-Closed $exe
    Assert-Target $exe $resources $journal.executableSha256
    if (!$alreadyOriginal) {
        Assert-Hash $original $journal.originalAppAsarSha256
        if ($appExists) {
            Assert-Hash $app $journal.bootstrapSha256
            [IO.File]::Move($app,$archive)
        } elseif (Test-Path -LiteralPath $app) { throw 'Application entrypoint appeared during restore.' }
        # Original bytes remain in _app.asar if this move fails. Never replace an
        # unexpected app.asar, and never restore an obsolete external backup.
        [IO.File]::Move($original,$app)
        Assert-Hash $app $journal.originalAppAsarSha256
    }
    $journal.state='restored'; $journal.updatedUtc=[DateTimeOffset]::UtcNow.ToString('O')
    Save-Journal $journal $journalDirectory
    $report.performed=$true; $report|ConvertTo-Json -Depth 7; return
}

$exe=Full $Executable; $resources=Full $ResourcesDirectory
if (!$ReviewedTarget) {
    if ($Apply) { throw 'A hash-pinned reviewed target is required before applying.' }
    [pscustomobject]@{mode='Install';executable=$exe;resourcesDirectory=$resources;classification='unclassified';performed=$false;
        reason='Supply parser-observed entrypoint review and its separately reviewed SHA256 before preparing an applicable transaction.'}|ConvertTo-Json
    return
}
$reviewPath=Full $ReviewedTarget
$review=Read-PinnedJson $reviewPath $ExpectedReviewSha256
if ($review.schemaVersion -ne 1 -or $review.loaderClassification -ne 'reviewed-discord-entrypoint' -or
    !(Same (Full $review.executable) $exe) -or !(Same (Full $review.resourcesDirectory) $resources) -or
    [string]::IsNullOrWhiteSpace($review.package.name) -or [string]::IsNullOrWhiteSpace($review.package.main) -or
    @($review.rootEntries).Count -lt 1 -or @($review.rootEntries).Count -gt 64) { throw 'Target is not classified by the exact reviewed entrypoint record.' }
if ((@($review.package.name,$review.package.main)+@($review.rootEntries) -join '|') -match '(?i)vencord|equicord|openasar|betterdiscord|_app\.asar') { throw 'Known custom loader markers are not admitted by this reviewed-entrypoint transaction.' }
Assert-Target $exe $resources $review.executableSha256
Assert-NoOtherLoader $resources
$app=Join-Path $resources 'app.asar'; $original=Join-Path $resources '_app.asar'
Assert-Hash $app $review.appAsarSha256
if (Test-Path -LiteralPath $journalDirectory) { throw 'Install requires a fresh journal directory; use Restore to recover an existing transaction.' }
$stage=Full $StagedBuild
$qualified=Read-QualifiedDesktopProbeStage $stage $ExpectedProvenanceSha256
$provenance=$qualified.provenance; $provenancePath=$qualified.provenancePath; $bootstrap=$qualified.bootstrap
$transaction=[guid]::NewGuid().ToString('N')
$staged=Join-Path $resources ('.widgetrail-probe-'+$transaction+'.staged.asar')
$recovered=Join-Path $resources ('.widgetrail-probe-'+$transaction+'.rollback.asar')
$running=@(Read-RunningTarget $exe)
$journal=[ordered]@{schemaVersion=1;transactionId=$transaction;state='prepared';executable=$exe;resourcesDirectory=$resources;
    executableSha256=$review.executableSha256;originalAppAsarSha256=$review.appAsarSha256;bootstrapSha256=$provenance.bootstrapProposal.archiveSha256;
    reviewPath=$reviewPath;reviewSha256=$ExpectedReviewSha256;provenancePath=$provenancePath;provenanceSha256=$ExpectedProvenanceSha256;
    stagedBootstrap=$staged;rollbackBootstrap=$recovered;updatedUtc=[DateTimeOffset]::UtcNow.ToString('O')}
$report=[ordered]@{mode='Install';applyRequested=[bool]$Apply;executable=$exe;resourcesDirectory=$resources;
    classification=$review.loaderClassification;vendorAuthenticityEstablished=$false;runningProcesses=$running;
    sourceBootstrap=$bootstrap;bootstrapSha256=$journal.bootstrapSha256;originalSha256=$journal.originalAppAsarSha256;
    backupPath=$original;journalPath=(Join-Path $journalDirectory 'journal.json');performed=$false;
    scope='Exact reviewed bytes only. No process termination, launch, profile migration or automatic future repatching.'}
if (!$Apply) { $report|ConvertTo-Json -Depth 7; return }

Assert-Closed $exe
Assert-Target $exe $resources $review.executableSha256
Assert-NoOtherLoader $resources
Assert-Hash $app $review.appAsarSha256
Assert-Hash $bootstrap $provenance.bootstrapProposal.archiveSha256
New-Item -ItemType Directory -Path $journalDirectory | Out-Null
try {
    # An external exact-byte backup supplements the original retained in place.
    # CreateNew semantics prohibit overwriting any preexisting evidence/file.
    [IO.File]::Copy($app,(Join-Path $journalDirectory 'original-app.asar'),$false)
    Assert-Hash (Join-Path $journalDirectory 'original-app.asar') $review.appAsarSha256
    [IO.File]::Copy($bootstrap,$staged,$false)
    Assert-Hash $staged $journal.bootstrapSha256
    Save-Journal $journal $journalDirectory
    Assert-Closed $exe
    Assert-Hash $app $review.appAsarSha256
    [IO.File]::Move($app,$original)
    $journal.state='original-renamed'; Save-Journal $journal $journalDirectory
    [IO.File]::Move($staged,$app)
    Assert-Hash $app $journal.bootstrapSha256
    $journal.state='installed'; $journal.updatedUtc=[DateTimeOffset]::UtcNow.ToString('O')
    Save-Journal $journal $journalDirectory
} catch {
    $failure=$_.Exception.Message
    try {
        Assert-Closed $exe
        if (Test-Path -LiteralPath $original) {
            Assert-Hash $original $review.appAsarSha256
            if (Test-Path -LiteralPath $app) {
                Assert-Hash $app $journal.bootstrapSha256
                [IO.File]::Move($app,$recovered)
            }
            [IO.File]::Move($original,$app)
        }
        Assert-Hash $app $review.appAsarSha256
        $journal.state='rolled-back'; Save-Journal $journal $journalDirectory
    } catch {
        throw "Installation failed; recovery requires inspection. Files are retained. Journal: $journalDirectory. Initial failure: $failure. Recovery: $($_.Exception.Message)"
    }
    throw "Installation failed and original entrypoint was restored. Journal: $journalDirectory. Failure: $failure"
}
$report.performed=$true; $report|ConvertTo-Json -Depth 7
