[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$CloseAfter
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$results = [Collections.Generic.List[object]]::new()
$collection = 'Widget.playnite-library.library.scroll'
$playnite = 'widgetrail.samples.playnite-library'
$music = 'widgetrail.samples.ytmusic'
function Ui([string[]]$Arguments) {
    $raw = winapp ui @Arguments -a $AppPid --json
    if ($LASTEXITCODE -ne 0) { throw "UI operation failed: $raw" }
    $result = ($raw -join "`n") | ConvertFrom-Json
    if ($Arguments[0] -eq 'wait-for' -and -not $result.found) { throw "UI wait timed out: $($Arguments[1])" }
    return $result
}
function Check([string]$Name, [scriptblock]$Action) {
    & $Action
    $results.Add(@{name=$Name; passed=$true})
}
function WaitState([string]$Id, [bool]$Interactive) {
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $state = (Ui @('get-property','Overlay.Status','-p','HelpText')).properties.HelpText | ConvertFrom-Json
        if ($state.activeWidget -eq $Id -and $state.interactive -eq $Interactive -and -not $state.switching) { return $state }
        Start-Sleep -Milliseconds 50
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Shell did not settle on $Id with interactive=$Interactive."
}
function ReadyRow([string]$Id) {
    $null = Ui @('wait-for',$Id,'-t','10000')
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $row = (Ui @('get-property',$Id)).element
        if ($row.isEnabled -and $row.name -and $row.name -notlike 'Loading item *') { return $row }
        Start-Sleep -Milliseconds 50
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'The retained row did not reacquire current provider content.'
}
function Screenshot([string]$Name) {
    $windows = Ui @('list-windows')
    $main = @($windows | Where-Object title -Like 'WidgetRail*WinUI frontend')
    if ($main.Count -ne 1) { throw 'Could not identify the owned shell window.' }
    $raw = winapp ui screenshot -w $main[0].hwnd --capture-screen -o (Join-Path $OutputDirectory $Name) --json
    if ($LASTEXITCODE -ne 0) { throw "Screenshot failed: $raw" }
}
# Read-only browsing and details only. Never invoke Play/Install or song actions.
try {
    Check 'Open the real Playnite Library and scroll well beyond the initial viewport' {
        $null = Ui @('wait-for','Overlay.Status','--value','Playnite Library','-t','20000')
        $null = Ui @('invoke','Widget.playnite-library.destinations.compact-b718f1354f7247312eca086d')
        $null = ReadyRow "$collection.Item.0"
        # Take explicit native navigation ownership; End cancels any pending
        # authored group entry just like user keyboard navigation does.
        $null = Ui @('focus',"$collection.Item.0")
        $null = Ui @('send-keys','end','--via','send-input')
        Start-Sleep -Milliseconds 800
        $tree = Ui @('inspect',"$collection.Items",'--interactive','--depth','3','--hide-offscreen')
        $rows = @($tree.windows.elements | Where-Object {
            $_.automationId -like "$collection.Item.*" -and -not $_.isOffscreen -and $_.height -gt 0
        } | Sort-Object y,x)
        if ($rows.Count -eq 0) { throw 'No native rows are visible at the deep position.' }
        $script:rowId = $rows[[int][Math]::Floor($rows.Count / 2)].automationId
        if ([int]($rowId.Split('.')[-1]) -lt 20) { throw "Test never left the initial rows: $rowId" }
        $null = ReadyRow $rowId
        $null = Ui @('focus',$rowId)
        Start-Sleep -Milliseconds 300
        $script:before = ReadyRow $rowId
        Screenshot 'before-switch.png'
    }
    Check 'Switch to Music while retaining tray ownership' {
        $null = Ui @('focus',"Overlay.Widget.$music")
        $null = WaitState $music $false
        $null = Ui @('wait-for',"Overlay.Widget.$music",'-p','HasKeyboardFocus','--value','True','-t','3000')
    }
    Check 'Returning preview preserves the deep viewport and reacquires the same game' {
        $null = Ui @('focus',"Overlay.Widget.$playnite")
        $state = WaitState $playnite $false
        $after = ReadyRow $rowId
        if ($after.name -ne $before.name -or $after.isOffscreen) { throw 'Return changed the game identity or viewport.' }
        if ($state.retainedSurfaceCount -lt 2 -or $state.retainedSurfaceCount -gt 3) { throw 'Retained surface count is invalid.' }
        $null = Ui @('wait-for',"Overlay.Widget.$playnite",'-p','HasKeyboardFocus','--value','True','-t','3000')
        Screenshot 'returned-preview.png'
    }
    Check 'Explicit reentry restores the same game focus and admits its current action' {
        $null = Ui @('invoke',"Overlay.Widget.$playnite")
        $null = WaitState $playnite $true
        $null = Ui @('wait-for',$rowId,'-p','HasKeyboardFocus','--value','True','-t','5000')
        # Compare the same focused/settled style: authored focus scale changes the
        # physical tile bounds while browsing its unfocused preview in the tray.
        Start-Sleep -Milliseconds 500
        $after = ReadyRow $rowId
        if ([Math]::Abs($after.y - $before.y) -gt 3) { throw "Viewport moved: before=$($before.y), after=$($after.y)." }
        @{before=$before; after=$after} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory 'retained-row.json')
        $null = Ui @('invoke',$rowId)
        $null = Ui @('wait-for','Widget.playnite-library.details.play','-p','HasKeyboardFocus','--value','True','-t','10000')
    }
    Check 'A retained modal stays associated with its widget after another switch' {
        $null = Ui @('focus',"Overlay.Widget.$music")
        $null = WaitState $music $false
        $null = Ui @('invoke',"Overlay.Widget.$playnite")
        $null = WaitState $playnite $true
        $null = Ui @('wait-for','Widget.playnite-library.details.play','-p','HasKeyboardFocus','--value','True','-t','10000')
        Start-Sleep -Milliseconds 500
        Screenshot 'returned-modal.png'
    }
} catch {
    $results.Add(@{name='Widget retention integration'; passed=$false; error=$_.ToString()})
    try { Screenshot 'failure.png' } catch { }
    throw
} finally {
    $results | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory 'results.json')
    if ($CloseAfter) { $null = Ui @('invoke','Shell.Close') }
}
"Widget retention: $($results.Count) checks passed."
