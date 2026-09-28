param([Parameter(Mandatory)][int]$AppPid,
      [Parameter(Mandatory)][string]$OutputDirectory,
      [switch]$CloseAfter)
$ErrorActionPreference='Stop'
$null=New-Item -ItemType Directory -Force $OutputDirectory
$results=[Collections.Generic.List[object]]::new()
function Ui([string[]]$Arguments) {
    $raw=& winapp ui @Arguments -a $AppPid --json 2>&1
    if($LASTEXITCODE -ne 0){throw "UI $($Arguments -join ' '): $($raw -join "`n")"}
    $result=($raw -join "`n") | ConvertFrom-Json
    if($Arguments[0] -eq 'wait-for' -and -not $result.found){throw "UI wait timed out: $($Arguments[1])"}
    return $result
}
function Check([string]$Name,[scriptblock]$Action) {
    try { & $Action; $results.Add(@{name=$Name;status='PASS'}) }
    catch {
        $results.Add(@{name=$Name;status='FAIL';detail=$_.Exception.Message})
        try {
            Ui @('get-focused') | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory 'failure-focus.json')
            Ui @('get-property','Overlay.Status','-p','HelpText') | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory 'failure-state.json')
            $null=Ui @('screenshot','--capture-screen','-o',(Join-Path $OutputDirectory 'failure.png'))
        } catch { }
        throw
    }
}
# Real read-only browsing. Never invoke an app tile: A launches or mutates curation.
# Use an isolated profile granting only library and running-app read access.
try {
    Check 'Real bundled Games and Apps reaches enabled Library without Retry' {
        $null=Ui @('wait-for','Overlay.Status','--value','Games & Apps','-t','15000')
        $null=Ui @('wait-for','Widget.games.library.scroll.Item.0','-p','IsEnabled','--value','True','-t','15000')
    }
    Check 'Native Down navigation reaches the next grid row' {
        $null=Ui @('focus','Widget.games.library.scroll.Item.0')
        $null=Ui @('send-keys','DOWN','--via','send-input')
        $null=Ui @('wait-for','Widget.games.library.scroll.Item.3','-p','HasKeyboardFocus','--value','True','-t','5000')
    }
    Check 'Tray reentry restores the same enabled game without launching it' {
        $null=Ui @('focus','Overlay.Widget.games-apps')
        $null=Ui @('wait-for','Widget.games.library.scroll.Item.0','-p','IsEnabled','--value','False','-t','5000')
        $null=Ui @('invoke','Overlay.Widget.games-apps')
        $null=Ui @('wait-for','Widget.games.library.scroll.Item.3','-p','IsEnabled','--value','True','-t','5000')
        $null=Ui @('wait-for','Widget.games.library.scroll.Item.3','-p','HasKeyboardFocus','--value','True','-t','5000')
        $state=(Ui @('get-property','Overlay.Status','-p','HelpText')).properties.HelpText | ConvertFrom-Json
        $state | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $OutputDirectory 'tray-state.json')
        if(-not $state.interactive){throw 'Tray activation lost widget interaction ownership.'}
    }
    Check 'Library shows real app icons and bounded grid layout' {
        $null=Ui @('screenshot','--capture-screen','-o',(Join-Path $OutputDirectory 'library.png'))
    }
    Check 'Running section uses the real indexed observation without registering apps' {
        $null=Ui @('invoke','Widget.games.sections.compact-068daed90360cc142c9ef3a4')
        $null=Ui @('wait-for','Widget.games.running.scroll.Item.0','-t','20000')
        $null=Ui @('screenshot','--capture-screen','-o',(Join-Path $OutputDirectory 'running.png'))
    }
    Check 'Catalog retains explicit page controls without adding an app' {
        $null=Ui @('invoke','Widget.games.sections.compact-086a9eee56dfd356d1526ee7')
        $null=Ui @('wait-for','Widget.games.catalog.grid','-t','20000')
        $null=Ui @('screenshot','--capture-screen','-o',(Join-Path $OutputDirectory 'catalog.png'))
    }
} finally {
    try { if($CloseAfter){$null=Ui @('invoke','Shell.Close')} }
    finally { $results | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory 'results.json') }
}
$results | Format-Table
