[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string]$FirstWidgetId,
    [Parameter(Mandatory)][string]$SecondWidgetId,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$CloseAfter
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$results = [Collections.Generic.List[object]]::new()
function Ui([string[]]$Arguments) {
    $target = @('-a', $AppPid)
    if ($Arguments[0] -eq 'screenshot') {
        $windows = winapp ui list-windows -a $AppPid --json | ConvertFrom-Json
        $main = @($windows | Where-Object title -Like 'WidgetRail*WinUI frontend')
        if ($main.Count -ne 1) { throw 'Could not identify the owned shell window.' }
        $target = @('-w', $main[0].hwnd)
    }
    $raw = winapp ui @Arguments @target --json
    if ($LASTEXITCODE -ne 0) { throw "UI operation failed: $raw" }
    $value = ($raw -join "`n") | ConvertFrom-Json
    if ($Arguments[0] -eq 'wait-for' -and -not $value.found) { throw 'UI target did not appear.' }
    return $value
}
function Check([string]$Name, [scriptblock]$Action) {
    & $Action
    $results.Add(@{ name=$Name; passed=$true })
}
function State([string]$Id, [bool]$Interactive) {
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $state = (Ui @('get-property','Overlay.Status','-p','HelpText')).properties.HelpText | ConvertFrom-Json
        if ($state.activeWidget -eq $Id -and $state.interactive -eq $Interactive) { return }
        Start-Sleep -Milliseconds 50
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Shell did not reach requested widget/interaction state: $Id, $Interactive"
}
function TrayFocus([string]$Id) {
    $null = Ui @('wait-for',"Overlay.Widget.$Id",'-p','HasKeyboardFocus','--value','True','-t','3000')
}
try {
    $null = Ui @('wait-for',"Overlay.Widget.$FirstWidgetId",'-t','20000')
    Check 'Native tray focus previews the first widget without entering it' {
        $null = Ui @('focus',"Overlay.Widget.$FirstWidgetId")
        State $FirstWidgetId $false
        TrayFocus $FirstWidgetId
    }
    Check 'Browsing another widget retains tray focus through its publication' {
        $null = Ui @('focus',"Overlay.Widget.$SecondWidgetId")
        State $SecondWidgetId $false
        Start-Sleep -Milliseconds 500
        TrayFocus $SecondWidgetId
    }
    Check 'Latest tray selection wins rapid direction reversal' {
        $null = Ui @('focus',"Overlay.Widget.$FirstWidgetId")
        $null = Ui @('focus',"Overlay.Widget.$SecondWidgetId")
        State $SecondWidgetId $false
        Start-Sleep -Milliseconds 300
        TrayFocus $SecondWidgetId
    }
    Check 'Explicit native activation enters the selected widget' {
        $null = Ui @('invoke',"Overlay.Widget.$SecondWidgetId")
        State $SecondWidgetId $true
        $deadline = [DateTime]::UtcNow.AddSeconds(5)
        do {
            $focus = (Ui @('get-focused')) | ConvertTo-Json -Depth 12 -Compress
            if ($focus -match 'Widget\.' -and $focus -notmatch [regex]::Escape("Overlay.Widget.$SecondWidgetId")) { break }
            Start-Sleep -Milliseconds 50
        } while ([DateTime]::UtcNow -lt $deadline)
        if ($focus -notmatch 'Widget\.' -or $focus -match [regex]::Escape("Overlay.Widget.$SecondWidgetId")) { throw 'Widget did not acquire native focus.' }
    }
    Ui @('get-property','Overlay.Status','-p','HelpText') | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $OutputDirectory 'state.json')
    $null = Ui @('screenshot','--capture-screen','-o',(Join-Path $OutputDirectory 'tray-browsing.png'))
} catch {
    $results.Add(@{ name='Tray browse integration'; passed=$false; error=$_.ToString() })
    throw
} finally {
    if ($CloseAfter) { $null = Ui @('invoke','Shell.Close') }
    $results | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory 'results.json')
}
"Tray browsing: $($results.Count) checks passed."
