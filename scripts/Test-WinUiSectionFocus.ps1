param([Parameter(Mandatory)][int]$AppPid, [Parameter(Mandatory)][string]$OutputDirectory, [switch]$CloseAfter)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$results = [Collections.Generic.List[object]]::new()
function Ui([string[]]$Arguments) {
    $raw = winapp ui @Arguments -a $AppPid --json
    if ($LASTEXITCODE -ne 0) { throw "UI command failed ($($Arguments -join ' ')): $raw" }
    $value = ($raw -join "`n") | ConvertFrom-Json
    if ($Arguments[0] -eq 'wait-for' -and -not $value.found) { throw "Missing UI: $($Arguments[1])" }
    return $value
}
function State { return (Ui @('get-property','Overlay.Status','-p','HelpText')).properties.HelpText | ConvertFrom-Json }
function Ready([string]$Id) {
    $null = Ui @('wait-for',$Id,'-t','10000')
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $item = (Ui @('get-property',$Id)).element
        if ($item.isEnabled -and -not $item.isOffscreen -and $item.name -notlike 'Loading item *') { return }
        Start-Sleep -Milliseconds 60
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Control did not become ready: $Id"
}
function WidgetOwns([string]$Name) {
    $deadline = [DateTime]::UtcNow.AddSeconds(6)
    do {
        $state = State
        if (-not $state.interactive) { throw "$Name transferred interaction to the tray." }
        $focused = (Ui @('get-focused')).element
        if ($focused.automationId -like 'Widget.playnite-library.*') { break }
        Start-Sleep -Milliseconds 60
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($focused.automationId -notlike 'Widget.playnite-library.*') { throw "$Name did not restore widget focus." }
    # Outgoing transition and native flyout completion must not steal it later.
    Start-Sleep -Milliseconds 900
    $state = State
    $focused = (Ui @('get-focused')).element
    if (-not $state.interactive -or $focused.automationId -notlike 'Widget.playnite-library.*') { throw "$Name lost focus after completion." }
    $results.Add(@{name=$Name; passed=$true; focused=$focused.automationId; publication=$state.publication})
}
function Key([string]$Value) { $null = Ui @('send-keys',$Value,'--via','send-input') }
$homeRow='Widget.playnite-library.library.grid.Item.0'
$library='Widget.playnite-library.library.scroll.Item.0'
try {
    $null = Ui @('wait-for','Overlay.Widget.widgetrail.samples.playnite-library','-t','20000')
    $null = Ui @('invoke','Overlay.Widget.widgetrail.samples.playnite-library')
    Ready $homeRow
    $null = Ui @('focus',$homeRow)
    for ($round=1; $round -le 3; $round++) {
        Key 'f8'
        Ready $library
        WidgetOwns "Home to Library $round"
        $null = Ui @('focus',$library)
        Key 'f7'
        Ready $homeRow
        WidgetOwns "Library to Home $round"
        $null = Ui @('focus',$homeRow)
    }
    foreach ($page in @('Home','Library')) {
        if ($page -eq 'Library') { $null=Ui @('invoke','Widget.playnite-library.destinations.compact-b718f1354f7247312eca086d'); Ready $library; $null=Ui @('focus',$library) }
        Key 'f5'
        $category='Widget.playnite-library.library.menu.Context.playnite-library.categories.open'
        $null = Ui @('wait-for',$category,'-t','5000')
        $null = Ui @('invoke',$category)
        Ready 'Widget.playnite-library.categories.back'
        WidgetOwns "$page menu to Categories"
        $null = Ui @('invoke','Widget.playnite-library.categories.back')
        WidgetOwns "Categories back to $page"
    }
    $null=Ui @('invoke','Widget.playnite-library.destinations.compact-4ea140588150773ce3aace78'); Ready $homeRow; $null = Ui @('focus',$homeRow)
    Key 'f6'
    $tray='Overlay.Widget.widgetrail.samples.playnite-library'
    $null = Ui @('wait-for',$tray,'-p','HasKeyboardFocus','--value','True','-t','5000')
    if ((State).interactive) { throw 'Explicit B failed to return ownership to the tray.' }
    $results.Add(@{name='Explicit B still enters tray';passed=$true})
    Key 'f9'
    $null = Ui @('wait-for',$homeRow,'-p','HasKeyboardFocus','--value','True','-t','8000')
    WidgetOwns 'A reenters widget from tray'
    $null = Ui @('focus',$tray)
    if ((State).interactive) { throw 'Explicit UIA tray focus was rejected.' }
    $results.Add(@{name='Explicit UIA tray entry remains available';passed=$true})
} catch {
    $results.Add(@{name='Section focus regression';passed=$false;error=$_.ToString()})
    throw
} finally {
    $results | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $OutputDirectory 'results.json')
    if ($CloseAfter) { $null=Ui @('invoke','Shell.Close') }
}
"Passed $($results.Count) real-widget focus checks."
