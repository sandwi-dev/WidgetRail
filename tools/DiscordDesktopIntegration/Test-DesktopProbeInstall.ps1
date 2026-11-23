$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixture=Join-Path $repo ('artifacts/discord-desktop/install-tests-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$installer=Join-Path $PSScriptRoot 'Install-DesktopProbe.ps1'
$checks=0
function Check([bool]$Condition,[string]$Message) { if (!$Condition) { throw $Message }; $script:checks++ }
function Reject([scriptblock]$Action,[string]$Message) {
    $rejected=$false; try { & $Action | Out-Null } catch { $rejected=$true }
    Check $rejected $Message
}
function Digest([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function WriteJson([string]$Path,$Value) { $Value|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $Path }
function NewFixture([string]$Name) {
    $root=Join-Path $fixture $Name
    $resources=Join-Path $root 'Discord/app-1.0.1/resources'
    $exe=Join-Path (Split-Path $resources) 'Discord.exe'
    $stage=Join-Path $root 'stage'
    New-Item -ItemType Directory -Path $resources,(Join-Path $stage 'source/dist') -Force | Out-Null
    [IO.File]::WriteAllText($exe,'Generated non-executable fixture')
    [IO.File]::WriteAllText((Join-Path $resources 'app.asar'),'Generated original fixture, not executable archive')
    [IO.File]::WriteAllText((Join-Path $stage 'bootstrap.asar'),'Generated bootstrap fixture, not executable archive')
    $artifacts=@('patcher.js','preload.js','renderer.js','renderer.css') | ForEach-Object {
        $relative='source/dist/'+$_; $path=Join-Path $stage $relative
        [IO.File]::WriteAllText($path,'Generated build fixture '+$_)
        [pscustomobject]@{path=$relative;sha256=(Digest $path);bytes=(Get-Item -LiteralPath $path).Length}
    }
    $provenance=[pscustomobject]@{schemaVersion=1;outputDirectory=$stage;buildCompleted=$true;typecheckPassed=$true;bundleScopeVerified=$true;
        artifacts=$artifacts;bootstrapProposal=[pscustomobject]@{archivePath='bootstrap.asar';archiveSha256=(Digest (Join-Path $stage 'bootstrap.asar'));dataDirectory=(Join-Path $stage 'vencord-data')}}
    WriteJson (Join-Path $stage 'provenance.json') $provenance
    $review=[pscustomobject]@{schemaVersion=1;executable=$exe;resourcesDirectory=$resources;
        executableSha256=(Digest $exe);appAsarSha256=(Digest (Join-Path $resources 'app.asar'));
        loaderClassification='reviewed-discord-entrypoint';package=[pscustomobject]@{name='discord';main='app_bootstrap/index.js'};rootEntries=@('package.json','app_bootstrap')}
    $reviewPath=Join-Path $root 'review.json'; WriteJson $reviewPath $review
    $arguments=@{Executable=$exe;ResourcesDirectory=$resources;ReviewedTarget=$reviewPath;ExpectedReviewSha256=(Digest $reviewPath);
        StagedBuild=$stage;ExpectedProvenanceSha256=(Digest (Join-Path $stage 'provenance.json'));JournalDirectory=(Join-Path $root 'journal')}
    return [pscustomobject]@{root=$root;resources=$resources;exe=$exe;stage=$stage;review=$review;provenance=$provenance;arguments=$arguments}
}
function RunInstall($Fixture,[bool]$Apply=$false) {
    $arguments=$Fixture.arguments
    & $installer @arguments -Apply:$Apply | ConvertFrom-Json
}
function RunRestore($Fixture,[bool]$Apply=$false) {
    $journal=Join-Path $Fixture.arguments.JournalDirectory 'journal.json'
    & $installer -Mode Restore -JournalDirectory $Fixture.arguments.JournalDirectory -ExpectedJournalSha256 (Digest $journal) -Apply:$Apply | ConvertFrom-Json
}

# Test-only command substitution. The production script exposes no bypass or
# synthetic-process argument. No process is started/stopped by these fixtures.
function global:Get-CimInstance { param($ClassName); return $global:WidgetRailDesktopFixtureProcesses }
$global:WidgetRailDesktopFixtureProcesses=@()
try {
    $f=NewFixture 'roundtrip'
    $original=Digest (Join-Path $f.resources 'app.asar')
    $plan=RunInstall $f
    Check (!$plan.performed -and !(Test-Path -LiteralPath $f.arguments.JournalDirectory)) 'Default mode must not create journal or modify installation.'
    Check ((Digest (Join-Path $f.resources 'app.asar')) -eq $original) 'Plan changed original bytes.'
    $applied=RunInstall $f $true
    Check ($applied.performed -and (Digest (Join-Path $f.resources '_app.asar')) -eq $original) 'Install did not retain the exact original.'
    Check ((Digest (Join-Path $f.resources 'app.asar')) -eq $f.provenance.bootstrapProposal.archiveSha256) 'Installed bootstrap differs.'
    Check ((Digest (Join-Path $f.arguments.JournalDirectory 'original-app.asar')) -eq $original) 'External journal backup differs.'
    $restorePlan=RunRestore $f
    Check (!$restorePlan.performed -and (Test-Path -LiteralPath (Join-Path $f.resources '_app.asar'))) 'Restore plan must not mutate.'
    $restored=RunRestore $f $true
    Check ($restored.performed -and (Digest (Join-Path $f.resources 'app.asar')) -eq $original) 'Restore must return exact original bytes.'
    Check (!(Test-Path -LiteralPath (Join-Path $f.resources '_app.asar')) -and (Test-Path -LiteralPath $restored.preservedBootstrap)) 'Restore must preserve removed bootstrap evidence.'
    Check ((RunRestore $f).alreadyOriginal) 'Restored journal must support safe idempotent inspection.'

    $f=NewFixture 'unknown'
    $unclassified=& $installer -Executable $f.exe -ResourcesDirectory $f.resources -JournalDirectory $f.arguments.JournalDirectory | ConvertFrom-Json
    Check ($unclassified.classification -eq 'unclassified' -and !$unclassified.performed) 'No review must remain unclassified.'
    Reject { & $installer -Executable $f.exe -ResourcesDirectory $f.resources -JournalDirectory $f.arguments.JournalDirectory -Apply } 'Missing review must reject apply.'
    foreach ($marker in @('_app.asar','app','app.asar.original','app.asar.unpacked')) {
        $f=NewFixture ('marker-'+($marker -replace '\.','-'))
        [IO.File]::WriteAllText((Join-Path $f.resources $marker),'unknown loader marker')
        Reject { RunInstall $f $true } "Unknown marker $marker was admitted."
        Check (!(Test-Path -LiteralPath $f.arguments.JournalDirectory)) 'Collision must fail before journal/write.'
    }
    foreach ($tamper in @('review','app','exe','provenance','artifact','bootstrap')) {
        $f=NewFixture ('tamper-'+$tamper)
        $path=switch($tamper) {
            review {$f.arguments.ReviewedTarget}; app {Join-Path $f.resources 'app.asar'}; exe {$f.exe}
            provenance {Join-Path $f.stage 'provenance.json'}; artifact {Join-Path $f.stage 'source/dist/renderer.js'}
            bootstrap {Join-Path $f.stage 'bootstrap.asar'}
        }
        [IO.File]::AppendAllText($path,'changed')
        Reject { RunInstall $f $true } "$tamper byte changes must reject."
        Check (!(Test-Path -LiteralPath $f.arguments.JournalDirectory)) 'Hash failure wrote journal.'
    }
    $f=NewFixture 'wrong-resources'; $f.arguments.ResourcesDirectory=Join-Path $f.root 'other/resources'
    Reject { RunInstall $f $true } 'Exact resources path mismatch admitted.'
    $f=NewFixture 'unknown-classification'; $f.review.loaderClassification='asar-present-unclassified'
    WriteJson $f.arguments.ReviewedTarget $f.review; $f.arguments.ExpectedReviewSha256=Digest $f.arguments.ReviewedTarget
    Reject { RunInstall $f $true } 'An unclassified reviewed hash must not be treated as source review.'
    $f=NewFixture 'custom-package'; $f.review.package.name='Vencord'
    WriteJson $f.arguments.ReviewedTarget $f.review; $f.arguments.ExpectedReviewSha256=Digest $f.arguments.ReviewedTarget
    Reject { RunInstall $f $true } 'Known custom package markers admitted.'
    $f=NewFixture 'path-escape'; $f.provenance.bootstrapProposal.archivePath='../outside.asar'
    WriteJson (Join-Path $f.stage 'provenance.json') $f.provenance; $f.arguments.ExpectedProvenanceSha256=Digest (Join-Path $f.stage 'provenance.json')
    Reject { RunInstall $f $true } 'Bootstrap path traversal admitted.'
    $f=NewFixture 'duplicate-artifact'; $f.provenance.artifacts+=@($f.provenance.artifacts[0])
    WriteJson (Join-Path $f.stage 'provenance.json') $f.provenance; $f.arguments.ExpectedProvenanceSha256=Digest (Join-Path $f.stage 'provenance.json')
    Reject { RunInstall $f $true } 'Duplicate artifact inventory admitted.'
    $f=NewFixture 'relocated-stage'; $copiedStage=Join-Path $f.root 'copied-stage'
    Copy-Item -LiteralPath $f.stage -Destination $copiedStage -Recurse
    $f.arguments.StagedBuild=$copiedStage
    Reject { RunInstall $f $true } 'A copied stage must not validate bytes at a different location from its embedded bootstrap.'
    Check (!(Test-Path -LiteralPath $f.arguments.JournalDirectory)) 'Relocated stage rejection created a journal.'
    $f=NewFixture 'shared-data-directory'; $f.provenance.bootstrapProposal.dataDirectory=Join-Path $f.root 'existing-user-data'
    WriteJson (Join-Path $f.stage 'provenance.json') $f.provenance; $f.arguments.ExpectedProvenanceSha256=Digest (Join-Path $f.stage 'provenance.json')
    Reject { RunInstall $f $true } 'A different Vencord data directory must not be accepted as an isolated build.'

    $f=NewFixture 'running'
    $global:WidgetRailDesktopFixtureProcesses=@([pscustomobject]@{Name='Discord.exe';ExecutablePath=$f.exe;ProcessId=123})
    Check ((RunInstall $f).runningProcesses.Count -eq 1) 'Plan omitted exact target process.'
    Reject { RunInstall $f $true } 'Running target was modified.'
    $global:WidgetRailDesktopFixtureProcesses=@([pscustomobject]@{Name='Discord.exe';ExecutablePath=$null;ProcessId=123})
    Reject { RunInstall $f $true } 'Unknown Discord process identity must fail closed.'
    $global:WidgetRailDesktopFixtureProcesses=@([pscustomobject]@{Name='Discord.exe';ExecutablePath=(Join-Path $f.root 'Discord/app-1.0.2/Discord.exe');ProcessId=124})
    Check ((RunInstall $f).runningProcesses.Count -eq 0) 'Other version must not be mistaken for exact target.'
    $global:WidgetRailDesktopFixtureProcesses=@()

    foreach ($conflict in @('current','backup','exe','journal')) {
        $f=NewFixture ('restore-conflict-'+$conflict); RunInstall $f $true | Out-Null
        if ($conflict -eq 'journal') {
            $journal=Join-Path $f.arguments.JournalDirectory 'journal.json'; $oldDigest=Digest $journal
            [IO.File]::AppendAllText($journal,' ')
            Reject { & $installer -Mode Restore -JournalDirectory $f.arguments.JournalDirectory -ExpectedJournalSha256 $oldDigest -Apply } 'Unreviewed journal change admitted.'
        } else {
            $path=switch($conflict) {current {Join-Path $f.resources 'app.asar'};backup {Join-Path $f.resources '_app.asar'};exe {$f.exe}}
            [IO.File]::AppendAllText($path,'external update')
            Reject { RunRestore $f $true } 'Restore overwrote externally changed bytes.'
        }
        Check (Test-Path -LiteralPath (Join-Path $f.resources '_app.asar')) 'Conflicted restore lost original backup.'
    }
    $f=NewFixture 'restore-running'; RunInstall $f $true | Out-Null
    $global:WidgetRailDesktopFixtureProcesses=@([pscustomobject]@{Name='Discord.exe';ExecutablePath=$f.exe;ProcessId=125})
    Reject { RunRestore $f $true } 'Restore while running was admitted.'
    $global:WidgetRailDesktopFixtureProcesses=@()

    # Simulate crash states with only generated files; recovery uses the actual
    # unmodified production script and a separately pinned journal each time.
    $f=NewFixture 'partial-restore'; RunInstall $f $true | Out-Null
    $journal=Get-Content (Join-Path $f.arguments.JournalDirectory 'journal.json') -Raw | ConvertFrom-Json
    $archive=Join-Path $f.resources ('.widgetrail-probe-'+$journal.transactionId+'.removed.asar')
    [IO.File]::Move((Join-Path $f.resources 'app.asar'),$archive)
    Check ((RunRestore $f $true).performed) 'Restore cannot recover after preserving bootstrap but before original rename.'
    Check ((Digest (Join-Path $f.resources 'app.asar')) -eq $f.review.appAsarSha256) 'Partial restore recovery changed original.'

    # Fault-inject the precise rename boundary into a generated script copy, not
    # a production bypass flag. All target paths remain this generated fixture.
    $source=Get-Content -LiteralPath $installer -Raw
    $repoAssignment='$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot ''../..''))'
    $source=$source.Replace($repoAssignment,('$repo='''+$repo.Replace("'","''")+''''))
    $anchor='$journal.state=''original-renamed''; Save-Journal $journal $journalDirectory'
    Check ($source.Contains($anchor)) 'Fault-injection anchor changed; review test.'
    $fault=Join-Path $fixture 'fault-install.ps1'
    [IO.File]::WriteAllText($fault,$source.Replace($anchor,($anchor+"; throw 'Injected failure after original rename'")))
    $f=NewFixture 'rollback'; $arguments=$f.arguments
    Reject { & $fault @arguments -Apply } 'Injected transaction fault was not reached.'
    Check ((Digest (Join-Path $f.resources 'app.asar')) -eq $f.review.appAsarSha256) 'Rollback failed to preserve original after rename.'
    Check (!(Test-Path -LiteralPath (Join-Path $f.resources '_app.asar'))) 'Rollback left the original displaced.'
    $rolled=Get-Content (Join-Path $f.arguments.JournalDirectory 'journal.json') -Raw | ConvertFrom-Json
    Check ($rolled.state -eq 'rolled-back') 'Rollback journal did not record recovery.'

    $f=NewFixture 'reparse'
    $junction=Join-Path $f.root 'linked-stage'
    New-Item -ItemType Junction -Path $junction -Target $f.stage | Out-Null
    $f.arguments.StagedBuild=$junction
    Reject { RunInstall $f $true } 'Reparse stage path admitted.'
    $result=[pscustomobject]@{passed=$true;checks=$checks;fixture=$fixture;
        scope='Generated non-executable fixture installations only; mocked process inventory, injected rename fault and actual filesystem rollback. No installed Discord/profile modification, process launch or termination.'}
    WriteJson (Join-Path $fixture 'result.json') $result
    $result|ConvertTo-Json -Depth 4
} finally {
    Remove-Item Function:\Get-CimInstance -ErrorAction SilentlyContinue
    Remove-Variable WidgetRailDesktopFixtureProcesses -Scope Global -ErrorAction SilentlyContinue
}
