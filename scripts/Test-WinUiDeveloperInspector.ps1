[CmdletBinding()]
param([Parameter(Mandatory)][int]$AppPid, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$checks = [Collections.Generic.List[string]]::new()
function Ui([string[]]$Arguments) {
    $result = & winapp ui @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($result -join "`n") }
    return $result
}
try {
    $windows = Ui @('list-windows', '-a', "$AppPid", '--json') | ConvertFrom-Json
    $inspector = @($windows | Where-Object title -Like '*Widget Inspector*')
    if ($inspector.Count -ne 1) { throw 'Expected one inspector window for the owned development process.' }
    $window = "$($inspector[0].hwnd)"
    foreach ($id in @('Inspector.Search','Inspector.FollowFocus','Inspector.Pause','Inspector.Refresh','Inspector.Tree','Inspector.Map','Inspector.Details')) {
        $null = Ui @('wait-for', $id, '-w', $window, '-t', '3000')
        $checks.Add("Native inspector control: $id")
    }
    $null = Ui @('wait-for', 'Inspector.Details', '-w', $window, '--value', 'computedStyle', '--contains', '-t', '3000')
    $checks.Add('Selected declaration exposes computed native styles')
    $null = Ui @('invoke','Inspector.Pause','-w',$window,'--action','toggle-on')
    $null = Ui @('wait-for','Inspector.Status','-w',$window,'--value','PAUSED','--contains','-t','3000')
    $checks.Add('Pause stops live capture and labels its state')
    $null = Ui @('invoke','Inspector.Refresh','-w',$window)
    $null = Ui @('wait-for','Inspector.Status','-w',$window,'--value','snapshot','--contains','-t','3000')
    $checks.Add('Explicit refresh captures a frame while paused')
    $null = Ui @('invoke','Inspector.Pause','-w',$window,'--action','toggle-off')
    $null = Ui @('set-value','Inspector.Search','__no_such_element__','-w',$window)
    $null = Ui @('wait-for','Inspector.Details','-w',$window,'--value','Select an element','--contains','-t','3000')
    $null = Ui @('set-value','Inspector.Search','','-w',$window)
    $null = Ui @('wait-for','Inspector.Details','-w',$window,'--value','computedStyle','--contains','-t','3000')
    $checks.Add('Filtering and clearing the native tree restores selectable details')
    $null = Ui @('screenshot','-w',$window,'-o',(Join-Path $outputRoot 'inspector.png'))
    $null = Ui @('inspect','-w',$window,'--json') | Set-Content -LiteralPath (Join-Path $outputRoot 'inspector-tree.json')
    $overlay = @($windows | Where-Object title -Like '*WinUI frontend*')
    if ($overlay.Count -ne 1) { throw 'Expected one development overlay for F12 validation.' }
    $null = Ui @('invoke','Close','-w',$window)
    $null = Ui @('send-keys','f12','-w',"$($overlay[0].hwnd)",'--via','send-input')
    $null = Ui @('wait-for','Inspector.Search','-w',$window,'-t','3000')
    $checks.Add('Inspector closes independently and F12 reopens it from the development overlay')
    [pscustomobject]@{passed=$true;checks=$checks} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputRoot 'result.json')
    Write-Output "Passed: $($checks.Count), Failed: 0"
} catch {
    [pscustomobject]@{passed=$false;checks=$checks;error="$_"} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputRoot 'result.json')
    throw
}
