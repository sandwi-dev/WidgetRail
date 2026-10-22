[CmdletBinding()]
param([Parameter(Mandatory)][int]$AppPid, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$checks=[Collections.Generic.List[object]]::new()
function Ui([string[]]$Arguments) {
    $raw=& winapp ui @Arguments -a $AppPid --json
    if($LASTEXITCODE -ne 0){throw "UI failed: $raw"}
    $raw | ConvertFrom-Json
}
function Observe {
    Ui @('send-keys','F6','--via','send-input') | Out-Null
    (Ui @('get-value','IndexedWidget.Status')).text | ConvertFrom-Json
}
function Until([string]$Name,[scriptblock]$Condition) {
    $deadline=[DateTime]::UtcNow.AddSeconds(8)
    do {
        $state=Observe
        if($state.failure){throw $state.failure}
        if(& $Condition $state){$checks.Add(@{name=$Name;passed=$true;state=$state}); return $state}
        Start-Sleep -Milliseconds 40
    } while([DateTime]::UtcNow -lt $deadline)
    $checks.Add(@{name=$Name;passed=$false;state=$state})
    throw "Failed: $Name"
}
try {
    Ui @('wait-for','Widget.loaded','-t','10000') | Out-Null
    Ui @('invoke','Widget.loaded') | Out-Null
    $before=Until 'exact deep loaded occurrence enters lazily' {param($s) $s.focus -eq 'Widget.items.Item.75' -and $s.realized -lt 40}
    Ui @('send-keys','F8','--via','send-input') | Out-Null
    Until 'content update keeps focus and viewport' {param($s) $s.revision -eq 1 -and $s.focus -eq 'Widget.items.Item.75' -and [math]::Abs($s.offset-$before.offset) -lt 2} | Out-Null
    Ui @('send-keys','F1','--via','send-input') | Out-Null
    Until 'membership reorder preserves the surviving occurrence' {param($s) $s.focus -eq 'Widget.items.Item.24'} | Out-Null
    Ui @('send-keys','F2','--via','send-input') | Out-Null
    Until 'semantic identity replacement does not reuse old occurrence memory' {param($s) $s.focus -eq 'Widget.items.Item.0'} | Out-Null
    Ui @('send-keys','F3','--via','send-input') | Out-Null
    Until 'new exact request resolves in the replacement query' {param($s) $s.focus -eq 'Widget.items.Item.24'} | Out-Null
    Ui @('send-keys','F4','--via','send-input') | Out-Null
    Until 'removed occurrence leaves a valid bounded source' {param($s) $s.count -eq 99 -and $s.realized -lt 40 -and $s.focus -match '^Widget.items.Item.\d+$'} | Out-Null
    "Loaded collection native checks passed: $($checks.Count)"
} finally {
    ConvertTo-Json -InputObject @($checks) -Depth 8 | Set-Content (Join-Path $OutputDirectory 'results.json')
    & winapp ui screenshot -a $AppPid -o (Join-Path $OutputDirectory 'loaded.png') | Out-Null
}
