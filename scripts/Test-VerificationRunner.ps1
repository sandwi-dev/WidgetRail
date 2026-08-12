[CmdletBinding()]
param([switch]$SelfTest)

$ErrorActionPreference = 'Stop'
if (-not $SelfTest) { throw 'Use -SelfTest.' }
Import-Module (Join-Path $PSScriptRoot 'VerificationRunner.psm1') -Force
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $PSScriptRoot 'verification-steps.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifestProjectReferences = @($manifest.steps |
    Where-Object { $_.arguments[0] -in @('run', 'test') } |
    ForEach-Object { $_.arguments } | ForEach-Object { $_ } |
    Where-Object { $_ -is [string] -and $_.EndsWith('.csproj', [StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object { $_.Replace('\', '/') })
$manifestProjects = @($manifestProjectReferences | Sort-Object -Unique)
$testProjects = @(Get-ChildItem (Join-Path $repositoryRoot 'tests') -Recurse -Filter '*.csproj' -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Where-Object { $_.Directory.Name.EndsWith('.Tests', [StringComparison]::Ordinal) } |
    ForEach-Object { [IO.Path]::GetRelativePath($repositoryRoot, $_.FullName).Replace('\', '/') } |
    Sort-Object -Unique)
$missingProjects = @($testProjects | Where-Object { $_ -notin $manifestProjects })
if ($missingProjects.Count -ne 0) {
    throw "Verification manifest omits test project: $($missingProjects[0])"
}
$duplicateProjects = @($manifestProjectReferences | Group-Object |
    Where-Object Count -ne 1)
if ($duplicateProjects.Count -ne 0) {
    throw "Verification manifest repeats project: $($duplicateProjects[0].Name)"
}
$duplicateIds = @($manifest.steps | Group-Object id | Where-Object Count -ne 1)
if ($manifest.schemaVersion -ne 1 -or $duplicateIds.Count -ne 0) {
    throw 'Verification manifest schema or step IDs are invalid.'
}
$launcherPath = Join-Path $PSScriptRoot 'Invoke-VerificationChild.ps1'
if (-not (Test-Path -LiteralPath $launcherPath -PathType Leaf)) {
    throw 'Verification child launcher is missing.'
}
$packageProvenancePath = Join-Path $PSScriptRoot 'Get-PackageProvenance.ps1'
if (-not (Test-Path -LiteralPath $packageProvenancePath -PathType Leaf)) {
    throw 'Package provenance helper is missing.'
}
$nativeProvenancePath = Join-Path $PSScriptRoot 'Get-NativeToolchainProvenance.ps1'
if (-not (Test-Path -LiteralPath $nativeProvenancePath -PathType Leaf)) {
    throw 'Native toolchain provenance helper is missing.'
}
$reducedTimeout = Get-RemainingVerificationTimeout `
    -RequestedSeconds 30 -ElapsedSeconds 45.25 -OverallTimeoutSeconds 60
if ($reducedTimeout -ne 14) { throw 'Shared verification deadline did not reduce a local timeout.' }
$exhausted = $false
try {
    Get-RemainingVerificationTimeout `
        -RequestedSeconds 30 -ElapsedSeconds 60 -OverallTimeoutSeconds 60 | Out-Null
}
catch { $exhausted = $_.Exception.Message -eq 'Verification exceeded its 60-second overall limit.' }
if (-not $exhausted) { throw 'Shared verification deadline did not fail before launch at exhaustion.' }
$stableEligibility = Get-VerificationEvidenceEligibility `
    -VerificationPassed $true -StartingCommit 'abc' -StartingStatus '' `
    -StartingStatusTruncated $false -FinalProvenanceSucceeded $true `
    -FinishedCommit 'abc' -FinishedStatus '' -FinishedStatusTruncated $false
if (-not $stableEligibility.eligible -or -not $stableEligibility.repositoryStateStable -or
    $stableEligibility.reasons.Count -ne 0) {
    throw 'Stable clean repository state was not release eligible.'
}
$dirtyFinishEligibility = Get-VerificationEvidenceEligibility `
    -VerificationPassed $true -StartingCommit 'abc' -StartingStatus '' `
    -StartingStatusTruncated $false -FinalProvenanceSucceeded $true `
    -FinishedCommit 'abc' -FinishedStatus ' M source.cs' -FinishedStatusTruncated $false
if ($dirtyFinishEligibility.eligible -or $dirtyFinishEligibility.repositoryStateStable -or
    'finished_worktree_dirty' -notin $dirtyFinishEligibility.reasons -or
    'repository_status_changed' -notin $dirtyFinishEligibility.reasons) {
    throw 'Start-clean/end-dirty fixture remained release eligible.'
}
$changedCommitEligibility = Get-VerificationEvidenceEligibility `
    -VerificationPassed $true -StartingCommit 'abc' -StartingStatus '' `
    -StartingStatusTruncated $false -FinalProvenanceSucceeded $true `
    -FinishedCommit 'def' -FinishedStatus '' -FinishedStatusTruncated $false
if ($changedCommitEligibility.eligible -or $changedCommitEligibility.repositoryStateStable -or
    'repository_commit_changed' -notin $changedCommitEligibility.reasons) {
    throw 'Start-clean/end-different-commit fixture remained release eligible.'
}
$workflowPath = Join-Path $repositoryRoot '.github\workflows\verify.yml'
if (-not (Test-Path -LiteralPath $workflowPath)) {
    throw 'Windows verification workflow is missing.'
}
$workflow = Get-Content -LiteralPath $workflowPath -Raw
foreach ($required in @('-Lane managed', '-Lane native', 'artifacts/verification')) {
    if (-not $workflow.Contains($required, [StringComparison]::Ordinal)) {
        throw "Windows verification workflow omits '$required'."
    }
}
$actionReferences = [regex]::Matches($workflow, '(?m)^\s*uses:\s*[^@\s]+@([^\s#]+)')
if ($actionReferences.Count -eq 0 -or
    @($actionReferences | Where-Object { $_.Groups[1].Value -notmatch '^[0-9a-f]{40}$' }).Count -ne 0) {
    throw 'Windows verification workflow actions must use immutable 40-character commit IDs.'
}
$temporary = Join-Path ([IO.Path]::GetTempPath()) ("gba-verification-runner-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $leaseRoot = Join-Path $temporary 'lease-repository'
    New-Item -ItemType Directory -Path $leaseRoot | Out-Null
    $leaseFixturePath = Join-Path $temporary 'lease-fixture.ps1'
    [IO.File]::WriteAllText($leaseFixturePath, @'
param(
    [string]$ModulePath,
    [string]$RepositoryRoot,
    [string]$MarkerPath,
    [int]$HoldMilliseconds = 0
)
$ErrorActionPreference = 'Stop'
Import-Module $ModulePath -Force
$lease = Enter-RepositoryVerificationLease -RepositoryRoot $RepositoryRoot `
    -RunId 'fixture-child' -Configuration Release -StartedUtc ([DateTimeOffset]::UtcNow)
try {
    [IO.File]::WriteAllText($MarkerPath, 'acquired')
    if ($HoldMilliseconds -gt 0) { Start-Sleep -Milliseconds $HoldMilliseconds }
    Write-Output 'PASS repository lease acquired'
}
finally { Exit-RepositoryVerificationLease -Lease $lease }
'@, [Text.UTF8Encoding]::new($false))

    $heldLease = Enter-RepositoryVerificationLease -RepositoryRoot $leaseRoot `
        -RunId 'fixture-parent' -Configuration Release -StartedUtc ([DateTimeOffset]::UtcNow)
    $contendedMarker = Join-Path $temporary 'contended.marker'
    try {
        $contended = Invoke-BoundedVerificationProcess -Id lease-contended `
            -Description 'lease contended' -FilePath pwsh `
            -ArgumentList @('-NoProfile', '-File', $leaseFixturePath,
                '-ModulePath', (Join-Path $PSScriptRoot 'VerificationRunner.psm1'),
                '-RepositoryRoot', $leaseRoot, '-MarkerPath', $contendedMarker) `
            -WorkingDirectory $temporary -TimeoutSeconds 10 -OutputDirectory $temporary `
            -SuppressReplay
        $contendedError = [IO.File]::ReadAllText((Join-Path $temporary $contended.stderrLog))
        if ($contended.status -ne 'failed' -or
            -not $contendedError.Contains('verification_lease_busy', [StringComparison]::Ordinal) -or
            -not $contendedError.Contains('fixture-parent', [StringComparison]::Ordinal) -or
            (Test-Path -LiteralPath $contendedMarker)) {
            throw 'Concurrent verification did not fail before touching shared output.'
        }
    }
    finally { Exit-RepositoryVerificationLease -Lease $heldLease }

    $releasedMarker = Join-Path $temporary 'released.marker'
    $released = Invoke-BoundedVerificationProcess -Id lease-released `
        -Description 'lease released' -FilePath pwsh `
        -ArgumentList @('-NoProfile', '-File', $leaseFixturePath,
            '-ModulePath', (Join-Path $PSScriptRoot 'VerificationRunner.psm1'),
            '-RepositoryRoot', $leaseRoot, '-MarkerPath', $releasedMarker) `
        -WorkingDirectory $temporary -TimeoutSeconds 10 -OutputDirectory $temporary `
        -SuppressReplay
    if ($released.status -ne 'passed' -or -not (Test-Path -LiteralPath $releasedMarker)) {
        throw 'Repository lease did not become available after handle release.'
    }

    $crashMarker = Join-Path $temporary 'crash.marker'
    $crashStartInfo = [Diagnostics.ProcessStartInfo]::new()
    $crashStartInfo.FileName = 'pwsh'
    $crashStartInfo.WorkingDirectory = $temporary
    $crashStartInfo.UseShellExecute = $false
    $crashStartInfo.CreateNoWindow = $true
    foreach ($argument in @('-NoProfile', '-File', $leaseFixturePath,
            '-ModulePath', (Join-Path $PSScriptRoot 'VerificationRunner.psm1'),
            '-RepositoryRoot', $leaseRoot, '-MarkerPath', $crashMarker,
            '-HoldMilliseconds', '30000')) {
        [void]$crashStartInfo.ArgumentList.Add($argument)
    }
    $crashHolder = [Diagnostics.Process]::new()
    $crashHolder.StartInfo = $crashStartInfo
    if (-not $crashHolder.Start()) { throw 'Crash-release lease holder did not start.' }
    try {
        $readyStopwatch = [Diagnostics.Stopwatch]::StartNew()
        while (-not (Test-Path -LiteralPath $crashMarker) -and
            $readyStopwatch.ElapsedMilliseconds -lt 5000) {
            Start-Sleep -Milliseconds 25
        }
        if (-not (Test-Path -LiteralPath $crashMarker)) {
            throw 'Crash-release lease holder did not acquire within five seconds.'
        }
        $crashHolder.Kill($true)
        if (-not $crashHolder.WaitForExit(5000)) {
            throw 'Crash-release lease holder did not terminate.'
        }
    }
    finally {
        if (-not $crashHolder.HasExited) {
            try { $crashHolder.Kill($true); [void]$crashHolder.WaitForExit(5000) } catch { }
        }
        $crashHolder.Dispose()
    }
    $afterCrash = Enter-RepositoryVerificationLease -RepositoryRoot $leaseRoot `
        -RunId 'fixture-after-crash' -Configuration Release -StartedUtc ([DateTimeOffset]::UtcNow)
    Exit-RepositoryVerificationLease -Lease $afterCrash

    $jobMembershipCommand = @'
Add-Type -Namespace GbaVerificationFixture -Name NativeJob -MemberDefinition @"
[System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode, SetLastError=true)]
public static extern System.IntPtr OpenJobObject(uint desiredAccess, bool inheritHandle, string name);
[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError=true)]
public static extern bool IsProcessInJob(System.IntPtr process, System.IntPtr job, out bool result);
[System.Runtime.InteropServices.DllImport("kernel32.dll")]
public static extern bool CloseHandle(System.IntPtr handle);
"@
$job = [GbaVerificationFixture.NativeJob]::OpenJobObject(0x0004, $false, $env:GBA_VERIFICATION_JOB)
$isMember = $false
try {
    if ($job -eq [IntPtr]::Zero -or
        -not [GbaVerificationFixture.NativeJob]::IsProcessInJob(
            [Diagnostics.Process]::GetCurrentProcess().Handle, $job, [ref]$isMember) -or
        -not $isMember) {
        throw 'User command executed before verification Job membership.'
    }
}
finally { if ($job -ne [IntPtr]::Zero) { [void][GbaVerificationFixture.NativeJob]::CloseHandle($job) } }
Write-Output 'PASS bounded success'
'@
    $success = Invoke-BoundedVerificationProcess -Id success -Description 'success' `
        -FilePath pwsh -ArgumentList @('-NoProfile', '-Command', $jobMembershipCommand) `
        -WorkingDirectory $temporary -TimeoutSeconds 10 -OutputDirectory $temporary
    if ($success.status -ne 'passed') { throw 'Successful process was not reported as passed.' }

    $failure = Invoke-BoundedVerificationProcess -Id failure -Description 'failure' `
        -FilePath pwsh -ArgumentList @('-NoProfile', '-Command', "Write-Output 'PASS before failure'; exit 7") `
        -WorkingDirectory $temporary -TimeoutSeconds 10 -OutputDirectory $temporary
    if ($failure.status -ne 'failed' -or $failure.exitCode -ne 7) {
        throw 'Nonzero exit was not preserved.'
    }

    $noisy = Invoke-BoundedVerificationProcess -Id noisy -Description 'noisy' `
        -FilePath pwsh -ArgumentList @('-NoProfile', '-Command', `
            "[Console]::Out.Write('x' * 100000); [Console]::Error.Write('y' * 100000)") `
        -WorkingDirectory $temporary -TimeoutSeconds 10 -OutputDirectory $temporary `
        -MaximumOutputBytes 4096 -SuppressReplay
    if ($noisy.status -ne 'passed' -or
        -not $noisy.stdoutTruncated -or $noisy.stdoutBytes -gt 4096 -or
        -not $noisy.stderrTruncated -or $noisy.stderrBytes -gt 4096) {
        throw 'High-volume output was not capped and drained.'
    }

    $packageRoot = Join-Path $temporary 'packages'
    New-Item -ItemType Directory -Path $packageRoot | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $packageRoot 'fixture.gbarwidget'), [byte[]](1, 2, 3, 4))
    $packageEvidence = Invoke-BoundedVerificationProcess -Id package-evidence -Description 'package evidence' `
        -FilePath pwsh -ArgumentList @('-NoProfile', '-File', $packageProvenancePath,
            '-Root', $packageRoot, '-RelativeTo', $temporary,
            '-MaximumPackageBytes', '16', '-MaximumTotalBytes', '32') `
        -WorkingDirectory $temporary -TimeoutSeconds 10 -OutputDirectory $temporary -SuppressReplay
    if ($packageEvidence.status -ne 'passed') { throw 'Bounded package provenance failed.' }
    $packageJson = Get-Content -LiteralPath (Join-Path $temporary $packageEvidence.stdoutLog) -Raw |
        ConvertFrom-Json
    if (@($packageJson).Count -ne 1 -or $packageJson.bytes -ne 4 -or
        $packageJson.sha256 -ne '9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a') {
        throw 'Package provenance did not bind the expected bytes.'
    }
    $oversizeEvidence = Invoke-BoundedVerificationProcess -Id package-oversize -Description 'package oversize' `
        -FilePath pwsh -ArgumentList @('-NoProfile', '-File', $packageProvenancePath,
            '-Root', $packageRoot, '-RelativeTo', $temporary,
            '-MaximumPackageBytes', '3', '-MaximumTotalBytes', '32') `
        -WorkingDirectory $temporary -TimeoutSeconds 10 -OutputDirectory $temporary -SuppressReplay
    if ($oversizeEvidence.status -ne 'failed') {
        throw 'Package provenance did not reject an oversized artifact.'
    }
    [IO.File]::WriteAllBytes((Join-Path $packageRoot 'second.gbarwidget'), [byte[]](5, 6, 7, 8))
    $totalEvidence = Invoke-BoundedVerificationProcess -Id package-total -Description 'package total' `
        -FilePath pwsh -ArgumentList @('-NoProfile', '-File', $packageProvenancePath,
            '-Root', $packageRoot, '-RelativeTo', $temporary,
            '-MaximumPackageBytes', '16', '-MaximumTotalBytes', '7') `
        -WorkingDirectory $temporary -TimeoutSeconds 10 -OutputDirectory $temporary -SuppressReplay
    if ($totalEvidence.status -ne 'failed') {
        throw 'Package provenance did not reject aggregate-byte overflow.'
    }
    $entryEvidence = Invoke-BoundedVerificationProcess -Id package-entries -Description 'package entries' `
        -FilePath pwsh -ArgumentList @('-NoProfile', '-File', $packageProvenancePath,
            '-Root', $packageRoot, '-RelativeTo', $temporary,
            '-MaximumEntries', '1', '-MaximumPackageBytes', '16', '-MaximumTotalBytes', '32') `
        -WorkingDirectory $temporary -TimeoutSeconds 10 -OutputDirectory $temporary -SuppressReplay
    if ($entryEvidence.status -ne 'failed') {
        throw 'Package provenance did not reject traversal-entry overflow.'
    }
    $junctionTarget = Join-Path $temporary 'junction-target'
    $junctionRoot = Join-Path $temporary 'junction-root'
    New-Item -ItemType Directory -Path $junctionTarget | Out-Null
    New-Item -ItemType Junction -Path $junctionRoot -Target $junctionTarget | Out-Null
    $junctionEvidence = Invoke-BoundedVerificationProcess -Id package-junction -Description 'package junction' `
        -FilePath pwsh -ArgumentList @('-NoProfile', '-File', $packageProvenancePath,
            '-Root', $junctionRoot, '-RelativeTo', $temporary) `
        -WorkingDirectory $temporary -TimeoutSeconds 10 -OutputDirectory $temporary -SuppressReplay
    if ($junctionEvidence.status -ne 'failed') {
        throw 'Package provenance did not reject a reparse-point root.'
    }

    $detachedCommand = @'
$child = Start-Process pwsh -ArgumentList '-NoProfile','-Command','Start-Sleep -Seconds 30' -WindowStyle Hidden -PassThru
Write-Output "CHILD=$($child.Id)"
'@
    $detached = Invoke-BoundedVerificationProcess -Id detached -Description 'detached' `
        -FilePath pwsh -ArgumentList @('-NoProfile', '-Command', $detachedCommand) `
        -WorkingDirectory $temporary -TimeoutSeconds 10 -OutputDirectory $temporary
    if ($detached.status -ne 'passed' -or $detached.durationMilliseconds -gt 10000) {
        throw 'Root-exits-first process tree was not bounded.'
    }
    $detachedLine = Get-Content -LiteralPath (Join-Path $temporary 'detached.stdout.log') |
        Where-Object { $_ -match '^CHILD=(\d+)$' } | Select-Object -First 1
    if ($null -eq $detachedLine) { throw 'Detached fixture did not report its child PID.' }
    $detachedChildId = [int]($detachedLine -replace '^CHILD=', '')
    Start-Sleep -Milliseconds 150
    if (Get-Process -Id $detachedChildId -ErrorAction SilentlyContinue) {
        throw 'Successful verification left a descendant process alive.'
    }

    $childCommand = @'
$child = Start-Process pwsh -ArgumentList '-NoProfile','-Command','Start-Sleep -Seconds 30' -WindowStyle Hidden -PassThru
Write-Output "CHILD=$($child.Id)"
Wait-Process -Id $child.Id
'@
    $timed = Invoke-BoundedVerificationProcess -Id timeout -Description 'timeout' `
        -FilePath pwsh -ArgumentList @('-NoProfile', '-Command', $childCommand) `
        -WorkingDirectory $temporary -TimeoutSeconds 1 -OutputDirectory $temporary
    if ($timed.status -ne 'timed_out' -or $timed.durationMilliseconds -gt 10000) {
        throw 'Timed process was not bounded.'
    }
    $childLine = Get-Content -LiteralPath (Join-Path $temporary 'timeout.stdout.log') |
        Where-Object { $_ -match '^CHILD=(\d+)$' } | Select-Object -First 1
    if ($null -eq $childLine) { throw 'Timed fixture did not report its child PID.' }
    $childId = [int]($childLine -replace '^CHILD=', '')
    Start-Sleep -Milliseconds 150
    if (Get-Process -Id $childId -ErrorAction SilentlyContinue) {
        throw 'Timed verification left a descendant process alive.'
    }

    $junit = Join-Path $temporary 'success.junit.xml'
    Write-VerificationJUnit -Result $success `
        -StdoutPath (Join-Path $temporary $success.stdoutLog) `
        -StderrPath (Join-Path $temporary $success.stderrLog) -OutputPath $junit
    [xml]$document = Get-Content -LiteralPath $junit -Raw
    if ($document.testsuite.tests -ne '1' -or $document.testsuite.testcase.name -ne 'bounded success') {
        throw 'JUnit conversion did not retain the custom case.'
    }
    $failureJunit = Join-Path $temporary 'failure.junit.xml'
    Write-VerificationJUnit -Result $failure `
        -StdoutPath (Join-Path $temporary $failure.stdoutLog) `
        -StderrPath (Join-Path $temporary $failure.stderrLog) -OutputPath $failureJunit
    [xml]$failureDocument = Get-Content -LiteralPath $failureJunit -Raw
    if ($failureDocument.testsuite.failures -ne '1') {
        throw 'A failing process with PASS output produced a green JUnit result.'
    }

    $mtpStdout = Join-Path $temporary 'mtp.stdout.log'
    [IO.File]::WriteAllText(
        $mtpStdout,
        "passed CurrentPublicApiMatchesReviewedBaseline (38ms)`n" +
        "passed DiffClassifiesRemovalExactly (0ms)`n")
    $mtpJunit = Join-Path $temporary 'mtp.junit.xml'
    Write-VerificationJUnit -Result $success -StdoutPath $mtpStdout `
        -StderrPath (Join-Path $temporary $success.stderrLog) -OutputPath $mtpJunit
    [xml]$mtpDocument = Get-Content -LiteralPath $mtpJunit -Raw
    $mtpNames = @($mtpDocument.testsuite.testcase | ForEach-Object { [string]$_.name })
    if ($mtpDocument.testsuite.tests -ne '2' -or
        $mtpDocument.testsuite.failures -ne '0' -or
        'CurrentPublicApiMatchesReviewedBaseline' -notin $mtpNames -or
        'DiffClassifiesRemovalExactly' -notin $mtpNames) {
        throw 'JUnit conversion did not retain Microsoft Testing Platform cases.'
    }

    $caseLimitStdout = Join-Path $temporary 'case-limit.stdout.log'
    [IO.File]::WriteAllText($caseLimitStdout, "PASS one`nPASS two`nPASS three`n")
    $caseLimitJunit = Join-Path $temporary 'case-limit.junit.xml'
    Write-VerificationJUnit -Result $success -StdoutPath $caseLimitStdout `
        -StderrPath (Join-Path $temporary $success.stderrLog) `
        -OutputPath $caseLimitJunit -MaximumCases 2
    [xml]$caseLimitDocument = Get-Content -LiteralPath $caseLimitJunit -Raw
    if ($caseLimitDocument.testsuite.tests -ne '3' -or
        $caseLimitDocument.testsuite.failures -ne '1') {
        throw 'JUnit case extraction did not fail closed at its configured limit.'
    }
    Write-Output 'Verification runner self-test passed.'
}
finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
