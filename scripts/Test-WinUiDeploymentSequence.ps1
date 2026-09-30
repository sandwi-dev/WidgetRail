[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DeploymentProbe,
    [Parameter(Mandatory)][string]$FirstStage,
    [Parameter(Mandatory)][string]$SecondStage,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$exe = (Resolve-Path -LiteralPath $DeploymentProbe).Path
$first = (Resolve-Path -LiteralPath $FirstStage).Path
$second = (Resolve-Path -LiteralPath $SecondStage).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh deployment-sequence directory.' }
if (Get-Process -Name 'OverlayFrontend.WinUI' -ErrorAction SilentlyContinue) { throw 'The deployment sequence needs an exclusive frontend slot.' }
$v1 = Get-Content -LiteralPath $first -Raw | ConvertFrom-Json
$v2 = Get-Content -LiteralPath $second -Raw | ConvertFrom-Json
if ($v1.packageName -cne $v2.packageName -or [version]$v2.version -le [version]$v1.version) { throw 'Supply two versions of one isolated probe, in increasing order.' }
New-Item -ItemType Directory -Path $output | Out-Null
$checks = [Collections.Generic.List[string]]::new()
$failure = $null; $cleanupFailure = $null; $ownsRegistration = $false
function Check([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw $Name }
    $checks.Add($Name)
}
function RunProbe([string]$Name, [string]$Operation, [string]$Stage, [string[]]$Extra = @(), [switch]$AllowFailure) {
    $resultPath = Join-Path $output ($Name + '.json')
    & $exe $Operation --stage $Stage --output $resultPath @Extra | Out-Null
    $code = $LASTEXITCODE
    $record = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if (-not $AllowFailure -and ($code -ne 0 -or -not $record.Succeeded)) { throw "$Name failed: $($record.Error)" }
    return $record
}
function Matches($Packages, $Stage) {
    return @($Packages).Count -eq 1 -and $Packages[0].IsDevelopmentMode -and
        $Packages[0].Name -ceq $Stage.packageName -and $Packages[0].Version -ceq $Stage.version -and
        [StringComparer]::OrdinalIgnoreCase.Equals($Packages[0].ExternalLocation, $Stage.externalLocation)
}
function ObserveRuntime([string]$Name, [string]$Receipt) {
    $directory = Join-Path $output $Name
    & (Join-Path $PSScriptRoot 'Test-WinUiExternalContentRuntime.ps1') -Receipt $Receipt -OutputDirectory $directory -RegisteredStageProbe $exe | Out-Null
    $result = Get-Content -LiteralPath (Join-Path $directory 'result.json') -Raw | ConvertFrom-Json
    Check ($result.passed -and $result.processExited -and $result.registrationRetained -and -not $result.registrationsRemoved -and
        $result.runtime.workerAppContainer -and $result.runtime.privateRuntimeVerified) "$Name activates the bound payload and sandboxed worker, then exits normally"
}
try {
    $null = RunProbe 'validate-v1' 'validate' $first
    $null = RunProbe 'validate-v2' 'validate' $second
    $initial = RunProbe 'before' 'inspect' $first
    Check (@($initial.Before).Count -eq 0) 'The isolated identity starts absent'
    # Only after validating both owned stages and absence can this driver own
    # compensation. Every removal still verifies the live exact stage/full name.
    $ownsRegistration = $true
    $registered = RunProbe 'register-v1' 'register' $first
    Check (Matches $registered.After $v1) 'First registration binds version one to its exact external directory'
    ObserveRuntime 'runtime-v1' $first
    $updated = RunProbe 'update-v2' 'register' $second @('--expected-current-stage', $first)
    Check (Matches $updated.After $v2) 'Update changes version and external directory together'
    ObserveRuntime 'runtime-v2' $second
    $deniedRemove = RunProbe 'reject-foreign-full-name' 'remove' $second @('--expected-full-name', $registered.After[0].FullName) -AllowFailure
    Check (-not $deniedRemove.Succeeded -and -not $deniedRemove.OperationStarted) 'An old full name cannot unregister the newer identity'
    $downgrade = RunProbe 'reject-default-downgrade' 'register' $first @('--expected-current-stage', $second) -AllowFailure
    Check (-not $downgrade.Succeeded -and $downgrade.HResult -eq '0x80073D06' -and (Matches $downgrade.After $v2)) 'Default downgrade is rejected and leaves version two bound'
    $rolledBack = RunProbe 'rollback-v1' 'register' $first @('--expected-current-stage', $second, '--allow-downgrade')
    Check (Matches $rolledBack.After $v1) 'Explicit rollback restores version and external directory together'
    ObserveRuntime 'runtime-rollback' $first
    $removed = RunProbe 'remove-before-repair' 'remove' $first @('--expected-full-name', $rolledBack.After[0].FullName)
    Check (@($removed.After).Count -eq 0) 'Exact owned removal leaves the retained payload available for repair'
    $repaired = RunProbe 'repair-registration' 'register' $first
    Check (Matches $repaired.After $v1) 'Missing registration is repaired against the original retained payload'
} catch { $failure = $_.Exception.Message }
finally {
    if ($ownsRegistration) {
        try {
            $current = RunProbe 'cleanup-inspect' 'inspect' $first
            if (@($current.Before).Count -gt 0) {
                $currentStage = if (Matches $current.Before $v1) { $first } elseif (Matches $current.Before $v2) { $second } else { throw 'Cleanup refuses a registration outside the two exact owned stages.' }
                $cleaned = RunProbe 'cleanup-remove' 'remove' $currentStage @('--expected-full-name', $current.Before[0].FullName)
                Check (@($cleaned.After).Count -eq 0) 'Normal cleanup removes only the current owned probe registration'
            }
        } catch { $cleanupFailure = $_.Exception.Message }
    }
    @{passed=($null -eq $failure -and $null -eq $cleanupFailure); checks=$checks; error=$failure; cleanupError=$cleanupFailure;
      firstStage=$first; secondStage=$second; signedProductionQualified=$false; cleanMachineQualified=$false;
      limits='Unsigned development registration mechanics only. Uses installed Windows App Runtime; frontend initialization may service Main/Singleton for the current user. Does not modify WidgetRail installation/startup metadata, signing trust, shared runtimes or user profiles. Does not qualify installer journaling, signed deployment, same-version rebinding or data-schema rollback.'} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'result.json')
}
if ($failure -or $cleanupFailure) { throw "Deployment sequence failed. $failure $cleanupFailure" }
"Passed $($checks.Count) native deployment sequence checks."
