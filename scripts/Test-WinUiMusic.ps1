param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$CloseAfter
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$results=[System.Collections.Generic.List[object]]::new()
function Ui([string[]]$Arguments) {
    $raw=& winapp ui @Arguments -a $AppPid --json 2>&1
    if($LASTEXITCODE -ne 0){throw ($raw -join "`n")}
    ($raw -join "`n") | ConvertFrom-Json
}
function Check([string]$Name,[scriptblock]$Action) {
    try { & $Action; $results.Add(@{name=$Name;status='PASS'}) }
    catch { $results.Add(@{name=$Name;status='FAIL';detail=$_.Exception.Message}) }
}
function ReadyRow([string]$Id) {
    $null=Ui @('wait-for',$Id,'-t','15000')
    $end=[DateTime]::UtcNow.AddSeconds(10)
    do {
        $row=(Ui @('get-property',$Id)).element
        if($row.name -and $row.name -notlike 'Loading item *'){return $row}
        Start-Sleep -Milliseconds 50
    } while([DateTime]::UtcNow -lt $end)
    throw 'Real service row did not replace its loading placeholder.'
}
# Read-only browsing of the real sealed package. No song/playlist activation,
# playback, sign-in, account editing or search submission is performed.
try {
    Check 'Real YouTube Music reached the native shell without Retry' {
        $null=Ui @('wait-for','Overlay.Status','--value','YouTube Music','-t','20000')
    }
    Check 'Home renders grouped real-service posters as authored squares' {
        $row=ReadyRow 'Widget.music.scroll.home.Item.0'
        $null=Ui @('focus','Widget.music.nav.compact-4ea140588150773ce3aace78')
        Start-Sleep -Milliseconds 500
        $row=(Ui @('get-property','Widget.music.scroll.home.Item.0')).element
        if([Math]::Abs($row.width-$row.height) -gt 4){throw "Poster is not square: $($row.width) x $($row.height)."}
        $null=Ui @('screenshot','--capture-screen','-o',(Join-Path $OutputDirectory 'home.png'))
    }
    Check 'Library section opens native indexed list without playing media' {
        $null=Ui @('invoke','Widget.music.nav.compact-b718f1354f7247312eca086d')
        $null=ReadyRow 'Widget.music.scroll.library.playlists.Item.0'
        $null=Ui @('focus','Widget.music.scroll.library.playlists.Item.0')
        $null=Ui @('send-keys','down','--via','send-input')
        $focus=Ui @('get-focused')
        if($focus.element.automationId -ne 'Widget.music.scroll.library.playlists.Item.1'){throw 'Native Down did not select the next Library row.'}
        $null=Ui @('screenshot','--capture-screen','-o',(Join-Path $OutputDirectory 'library.png'))
    }
    Check 'Returning Home retains the real collection' {
        $null=Ui @('invoke','Widget.music.nav.compact-4ea140588150773ce3aace78')
        $null=ReadyRow 'Widget.music.scroll.home.Item.0'
        $null=Ui @('wait-for','Overlay.Status','--value','YouTube Music','-t','5000')
    }
} finally {
    if($CloseAfter){ Check 'Close the owned test shell' {$null=Ui @('invoke','Shell.Close')} }
    $results | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'results.json')
}
$results | Format-Table
if(@($results | Where-Object status -eq 'FAIL').Count -gt 0){exit 1}
