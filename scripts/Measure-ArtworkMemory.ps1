[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateCount(1, 16)]
    [int[]]$ProcessIds,

    [Parameter(Mandatory)]
    [ValidateCount(1, 16)]
    [string[]]$ProcessRoles,

    [Parameter(Mandatory)]
    [ValidateSet(
        'cold-start', 'stable-home', 'slow-navigation', 'rapid-home',
        'browse', 'focus-thrash', 'hide-reopen', 'idle-5m', 'idle-15m',
        'idle-30m', 'repeat-traversal')]
    [string]$Checkpoint,

    [Parameter(Mandatory)]
    [string]$OutputPath,

    [ValidateRange(1, 120)]
    [int]$Samples = 1,

    [ValidateRange(100, 60000)]
    [int]$IntervalMilliseconds = 1000
)

$ErrorActionPreference = 'Stop'

if ($ProcessIds.Count -ne $ProcessRoles.Count) {
    throw 'ProcessIds and ProcessRoles must have the same bounded count.'
}

foreach ($role in $ProcessRoles) {
    if ($role -notmatch '^[a-z][a-z0-9-]{0,31}$') {
        throw "Process role '$role' is not a bounded opaque token."
    }
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [System.IO.Path]::GetDirectoryName($resolvedOutput)
if ([string]::IsNullOrWhiteSpace($outputDirectory)) {
    throw 'OutputPath must have a parent directory.'
}
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

$rows = [System.Collections.Generic.List[object]]::new()
for ($sample = 1; $sample -le $Samples; $sample++) {
    $capturedAt = [DateTimeOffset]::UtcNow.ToString('O')
    for ($index = 0; $index -lt $ProcessIds.Count; $index++) {
        $process = Get-Process -Id $ProcessIds[$index] -ErrorAction SilentlyContinue
        if ($null -eq $process) {
            $rows.Add([pscustomobject]@{
                schema = 1
                captured_at_utc = $capturedAt
                checkpoint = $Checkpoint
                sample = $sample
                role = $ProcessRoles[$index]
                state = 'absent'
                private_bytes = $null
                working_set_bytes = $null
            })
            continue
        }
        $rows.Add([pscustomobject]@{
            schema = 1
            captured_at_utc = $capturedAt
            checkpoint = $Checkpoint
            sample = $sample
            role = $ProcessRoles[$index]
            state = 'running'
            private_bytes = [long]$process.PrivateMemorySize64
            working_set_bytes = [long]$process.WorkingSet64
        })
    }
    if ($sample -lt $Samples) {
        Start-Sleep -Milliseconds $IntervalMilliseconds
    }
}

$rows | Export-Csv -LiteralPath $resolvedOutput -NoTypeInformation -Encoding utf8
Write-Output "Captured $($rows.Count) bounded process samples at checkpoint '$Checkpoint'."
