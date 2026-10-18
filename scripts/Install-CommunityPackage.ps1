[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CliDll,
    [Parameter(Mandatory)][string]$Package,
    [string]$Catalog,
    [switch]$AcceptFullTrust
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$catalogArguments = if ($Catalog) { @('--catalog', [IO.Path]::GetFullPath($Catalog)) } else { @() }
$approval = if ($AcceptFullTrust) { @('--accept-full-trust') } else { @() }

function Invoke-PackageCli([string[]]$Arguments) {
    $result = & dotnet $CliDll @Arguments
    if ($LASTEXITCODE -ne 0) { throw "wrail $($Arguments[0]) failed (exit $LASTEXITCODE). See the diagnostic above." }
    $result
}

# Validate and hash before touching enabled state. Production packages remain
# immutable; use wrail dev for repeated same-version development builds.
$inspection = (Invoke-PackageCli @('inspect', $Package, '--json') | Out-String) | ConvertFrom-Json
$inventory = (Invoke-PackageCli (@('list', '--json') + $catalogArguments) | Out-String) | ConvertFrom-Json
$previous = @($inventory.widgets | Where-Object id -eq $inspection.id) | Select-Object -First 1
if ($inspection.executionModel -eq 'full-trust-current-user' -and !$AcceptFullTrust) {
    throw 'This package requires explicit -AcceptFullTrust approval.'
}
if ($previous -and ([version]$inspection.version -le [version]$previous.selectedVersion)) {
    throw 'Choose a newer package version. For repeated same-version development builds, use wrail dev.'
}

$disabledPrevious = $false
try {
    if ($previous) {
        # CLI update keeps settings and refuses a live launch lease. Disabling
        # alone never authorizes copying a running worker's state.
        if ($previous.enabled) {
            Invoke-PackageCli (@('disable', $inspection.id) + $catalogArguments)
            $disabledPrevious = $true
        }
        Invoke-PackageCli (@('update', $inspection.id, '--file', $Package, '--sha256', $inspection.sha256, '--apply') + $approval + $catalogArguments)
    } else {
        Invoke-PackageCli (@('install', $Package, '--sha256', $inspection.sha256) + $approval + $catalogArguments)
    }
    Invoke-PackageCli (@('enable', $inspection.id) + $approval + $catalogArguments)
} catch {
    $originalError = $_
    if ($disabledPrevious) {
        try {
            $currentInventory = (Invoke-PackageCli (@('list', '--json') + $catalogArguments) | Out-String) | ConvertFrom-Json
            $current = @($currentInventory.widgets | Where-Object id -eq $inspection.id) | Select-Object -First 1
            if ($current -and !$current.enabled -and $current.selectedVersion -eq $previous.selectedVersion) {
                Invoke-PackageCli (@('enable', $inspection.id) + $approval + $catalogArguments)
            }
        } catch {
            Write-Warning "Could not restore the previous enabled state: $_"
        }
    }
    throw $originalError
}
