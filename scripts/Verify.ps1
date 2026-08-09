[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipNative,
    [ValidateSet('all', 'managed', 'native')]
    [string]$Lane = 'all',
    [string[]]$StepId = @(),
    [ValidateRange(60, 7200)]
    [int]$OverallTimeoutSeconds = 1800,
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$stepManifestPath = Join-Path $PSScriptRoot 'verification-steps.json'
Import-Module (Join-Path $PSScriptRoot 'VerificationRunner.psm1') -Force

function Get-CommandText([string]$file, [string[]]$arguments) {
    (@($file) + $arguments | ForEach-Object {
        if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
    }) -join ' '
}

function Invoke-ProvenanceCommand(
    [string]$id,
    [string]$file,
    [string[]]$arguments,
    [int]$maximumOutputBytes,
    [string]$workingDirectory,
    [string]$outputDirectory,
    [ValidateRange(1, 120)][int]$timeoutSeconds = 10
) {
    $result = Invoke-BoundedVerificationProcess -Id "provenance-$id" -Description "Capture $id provenance" `
        -FilePath $file -ArgumentList $arguments -WorkingDirectory $workingDirectory `
        -TimeoutSeconds $timeoutSeconds -OutputDirectory $outputDirectory `
        -MaximumOutputBytes $maximumOutputBytes -SuppressReplay
    if ($result.status -ne 'passed') {
        throw "Unable to capture $id provenance; inspect $($result.stderrLog)."
    }
    [pscustomobject]@{
        text = ([IO.File]::ReadAllText((Join-Path $outputDirectory $result.stdoutLog))).Trim()
        truncated = $result.stdoutTruncated
        stdoutLog = $result.stdoutLog
        stderrLog = $result.stderrLog
    }
}

Push-Location $repositoryRoot
$runStopwatch = [Diagnostics.Stopwatch]::StartNew()
$startedUtc = [DateTimeOffset]::UtcNow
$results = [Collections.Generic.List[object]]::new()
$runStatus = 'passed'
$failureMessage = $null
$runDirectory = $null
$provenance = $null
try {
    if ($SkipNative -and $Lane -eq 'native') {
        throw '-SkipNative cannot be combined with -Lane native.'
    }
    if ($SkipNative) { $Lane = 'managed' }
    $manifestFile = Get-Item -LiteralPath $stepManifestPath
    if ($manifestFile.Length -gt 262144) {
        throw 'Verification step manifest exceeds 256 KiB.'
    }
    $manifestBytes = [IO.File]::ReadAllBytes($stepManifestPath)
    $manifest = [Text.Encoding]::UTF8.GetString($manifestBytes) | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $null -eq $manifest.steps) {
        throw 'Verification step manifest schema is invalid.'
    }
    if ($manifest.maximumOutputBytesPerStream -lt 4096 -or
        $manifest.maximumOutputBytesPerStream -gt 67108864 -or
        $manifest.maximumCasesPerStep -lt 1 -or $manifest.maximumCasesPerStep -gt 10000) {
        throw 'Verification output limits are invalid.'
    }
    $allSteps = @($manifest.steps)
    if ($allSteps.Count -lt 1 -or $allSteps.Count -gt 128) {
        throw 'Verification step manifest must contain between 1 and 128 steps.'
    }
    $ids = @($allSteps | ForEach-Object { $_.id })
    if ($ids.Count -ne @($ids | Sort-Object -Unique).Count) {
        throw 'Verification step IDs must be unique.'
    }
    foreach ($step in $allSteps) {
        if ([string]::IsNullOrWhiteSpace($step.id) -or $step.id -notmatch '^[a-z0-9-]{1,80}$' -or
            [string]::IsNullOrWhiteSpace($step.description) -or
            $step.description.Length -gt 256 -or
            $step.lane -notin @('managed', 'native') -or
            [string]::IsNullOrWhiteSpace($step.file) -or
            $step.file.Length -gt 1024 -or
            @($step.arguments).Count -gt 32 -or
            @($step.arguments | Where-Object { ([string]$_).Length -gt 4096 }).Count -ne 0 -or
            $step.timeoutSeconds -lt 1 -or $step.timeoutSeconds -gt 1800) {
            throw "Verification step '$($step.id)' is invalid."
        }
    }
    if ($StepId.Count -ne 0) {
        $unknown = @($StepId | Where-Object { $_ -notin $ids })
        if ($unknown.Count -ne 0) { throw "Unknown verification step ID: $($unknown[0])" }
    }
    $eligibleSteps = @($allSteps | Where-Object {
        ($StepId.Count -eq 0 -or $_.id -in $StepId) -and
        ($Lane -eq 'all' -or $_.lane -eq $Lane)
    })
    if ($eligibleSteps.Count -eq 0) {
        throw 'No verification steps match the selected IDs and lane.'
    }

    $runId = $startedUtc.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
        $OutputRoot = Join-Path $repositoryRoot 'artifacts\verification'
    }
    $runDirectory = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) $runId
    New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null

    $gitStatus = Invoke-ProvenanceCommand -Id git-status -File git `
        -Arguments @('status', '--porcelain=v1', '--untracked-files=all') `
        -MaximumOutputBytes $manifest.maximumOutputBytesPerStream `
        -WorkingDirectory $repositoryRoot -OutputDirectory $runDirectory `
        -TimeoutSeconds (Get-RemainingVerificationTimeout 10 $runStopwatch.Elapsed.TotalSeconds $OverallTimeoutSeconds)
    $gitCommit = Invoke-ProvenanceCommand -Id git-commit -File git `
        -Arguments @('rev-parse', 'HEAD') -MaximumOutputBytes 4096 `
        -WorkingDirectory $repositoryRoot -OutputDirectory $runDirectory `
        -TimeoutSeconds (Get-RemainingVerificationTimeout 10 $runStopwatch.Elapsed.TotalSeconds $OverallTimeoutSeconds)
    $dotnetVersion = Invoke-ProvenanceCommand -Id dotnet-version -File dotnet `
        -Arguments @('--version') -MaximumOutputBytes 4096 `
        -WorkingDirectory $repositoryRoot -OutputDirectory $runDirectory `
        -TimeoutSeconds (Get-RemainingVerificationTimeout 10 $runStopwatch.Elapsed.TotalSeconds $OverallTimeoutSeconds)
    $dirtyText = $gitStatus.text
    $dirtyBytes = [Text.Encoding]::UTF8.GetBytes($dirtyText)
    $packageRoot = Join-Path $repositoryRoot 'artifacts\community-addons'
    $packageProvenance = Invoke-ProvenanceCommand -Id package-artifacts -File pwsh `
        -Arguments @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Get-PackageProvenance.ps1'),
            '-Root', $packageRoot, '-RelativeTo', $repositoryRoot) `
        -MaximumOutputBytes $manifest.maximumOutputBytesPerStream `
        -WorkingDirectory $repositoryRoot -OutputDirectory $runDirectory `
        -TimeoutSeconds (Get-RemainingVerificationTimeout 30 $runStopwatch.Elapsed.TotalSeconds $OverallTimeoutSeconds)
    if ($packageProvenance.truncated) {
        throw 'Package provenance output exceeded its configured limit.'
    }
    $artifactDigests = if ([string]::IsNullOrWhiteSpace($packageProvenance.text)) { @() } else {
        @($packageProvenance.text | ConvertFrom-Json)
    }
    $nativeToolchainResult = Invoke-ProvenanceCommand -Id native-toolchain -File pwsh `
        -Arguments @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Get-NativeToolchainProvenance.ps1')) `
        -MaximumOutputBytes 65536 -WorkingDirectory $repositoryRoot -OutputDirectory $runDirectory `
        -TimeoutSeconds (Get-RemainingVerificationTimeout 10 $runStopwatch.Elapsed.TotalSeconds $OverallTimeoutSeconds)
    if ($nativeToolchainResult.truncated) {
        throw 'Native toolchain provenance output exceeded its configured limit.'
    }
    $nativeToolchain = $nativeToolchainResult.text | ConvertFrom-Json
    $provenance = [ordered]@{
        schemaVersion = 1
        runId = $runId
        configuration = $Configuration
        lane = $Lane
        selectedStepIds = @($StepId)
        startedUtc = $startedUtc.ToString('O')
        repositoryCommit = $gitCommit.text
        repositoryDirty = -not [string]::IsNullOrEmpty($dirtyText)
        dirtyStatusSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($dirtyBytes)).ToLowerInvariant()
        dirtyStatusTruncated = $gitStatus.truncated
        releaseEvidenceEligible = [string]::IsNullOrEmpty($dirtyText) -and -not $gitStatus.truncated
        stepManifestSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($manifestBytes)).ToLowerInvariant()
        os = [Environment]::OSVersion.VersionString
        processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        githubRunnerImage = [ordered]@{ os = $env:ImageOS; version = $env:ImageVersion }
        powershellVersion = $PSVersionTable.PSVersion.ToString()
        dotnetVersion = $dotnetVersion.text
        nativeToolchain = $nativeToolchain
        provenanceCommandLogs = @(@(
            $gitStatus.stdoutLog, $gitStatus.stderrLog,
            $gitCommit.stdoutLog, $gitCommit.stderrLog,
            $dotnetVersion.stdoutLog, $dotnetVersion.stderrLog,
            $packageProvenance.stdoutLog, $packageProvenance.stderrLog,
            $nativeToolchainResult.stdoutLog, $nativeToolchainResult.stderrLog))
        artifactDigests = $artifactDigests
    }

    foreach ($step in $allSteps) {
        if ($StepId.Count -ne 0 -and $step.id -notin $StepId) { continue }
        if ($Lane -ne 'all' -and $step.lane -ne $Lane) { continue }
        if ($null -ne $step.PSObject.Properties['optionalPath'] -and
            -not (Test-Path -LiteralPath (Join-Path $repositoryRoot $step.optionalPath))) { continue }
        $timeout = Get-RemainingVerificationTimeout ([int]$step.timeoutSeconds) `
            $runStopwatch.Elapsed.TotalSeconds $OverallTimeoutSeconds
        $file = ([string]$step.file).Replace('{configuration}', $Configuration)
        $arguments = @($step.arguments | ForEach-Object { ([string]$_).Replace('{configuration}', $Configuration) })
        Write-Host "`n== $($step.description) [$($step.id), ${timeout}s] =="
        Write-Host (Get-CommandText $file $arguments)
        $result = Invoke-BoundedVerificationProcess -Id $step.id -Description $step.description `
            -FilePath $file -ArgumentList $arguments -WorkingDirectory $repositoryRoot `
            -TimeoutSeconds $timeout -OutputDirectory $runDirectory `
            -MaximumOutputBytes $manifest.maximumOutputBytesPerStream
        $junitName = "$($step.id).junit.xml"
        Write-VerificationJUnit -Result $result `
            -StdoutPath (Join-Path $runDirectory $result.stdoutLog) `
            -StderrPath (Join-Path $runDirectory $result.stderrLog) `
            -OutputPath (Join-Path $runDirectory $junitName) `
            -MaximumCases $manifest.maximumCasesPerStep
        $result | Add-Member -NotePropertyName junit -NotePropertyValue $junitName
        $results.Add($result)
        if ($result.status -ne 'passed') {
            throw "Verification step '$($step.id)' $($result.status)."
        }
    }
}
catch {
    $runStatus = 'failed'
    $failureMessage = $_.Exception.Message
    throw
}
finally {
    $runStopwatch.Stop()
    if ($null -ne $runDirectory) {
        $summary = [ordered]@{
            provenance = $provenance
            status = $runStatus
            failure = $failureMessage
            finishedUtc = [DateTimeOffset]::UtcNow.ToString('O')
            durationMilliseconds = [Math]::Round($runStopwatch.Elapsed.TotalMilliseconds, 3)
            steps = @($results)
        }
        $resultPath = Join-Path $runDirectory 'verification-result.json'
        [IO.File]::WriteAllText(
            $resultPath,
            ($summary | ConvertTo-Json -Depth 12),
            [Text.UTF8Encoding]::new($false))
        Write-Host "`nVerification result: $resultPath"
    }
    Pop-Location
}
