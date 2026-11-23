[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleaseRoot,
    [Parameter(Mandatory)][string]$PackagesDirectory,
    [Parameter(Mandatory)][string]$FrontendDirectory,
    [Parameter(Mandatory)][string]$TestBridgeDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot
$out = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $out) { throw 'Use a fresh isolated profile.' }
$release = (Resolve-Path -LiteralPath $ReleaseRoot).Path
$fixture = (Resolve-Path -LiteralPath $TestBridgeDirectory).Path
$frontend = (Resolve-Path -LiteralPath (Join-Path $FrontendDirectory 'OverlayFrontend.WinUI.exe')).Path
$packages = (Resolve-Path -LiteralPath $PackagesDirectory).Path
$profile = "$out/profile"
$indexes = "$out/indexes"
$install = "$out/installation"
$bridge = "$install/runtime/Bridge"
$null = New-Item -ItemType Directory -Path $profile,$indexes,$bridge -Force
Copy-Item -Path "$fixture/*" -Destination $bridge -Recurse
Copy-Item -LiteralPath "$fixture/WidgetBridge.Tests.exe" -Destination "$bridge/WidgetBridge.exe"
Copy-Item -LiteralPath "$release/widget-catalog.json" -Destination "$install/widget-catalog.json"
@{ ReleaseRoot = $release; ProfileRoot = $profile; PackagesDirectory = $packages; ControlPath = "$out/control.json"
   RequestLog = "$out/requests.jsonl"; ThemeIndexDirectory = $indexes } |
    ConvertTo-Json | Set-Content -LiteralPath "$bridge/catalogue-native-fixture.json"
@{ InitialWidgetId = 'marketplace'; SettingsRoot = $profile; InstalledCatalogRoot = "$profile/widgets"
   InstallationRoot = $install; LayoutDiagnosticsPath = "$out/layout.json" } |
    ConvertTo-Json | Set-Content -LiteralPath "$out/shell.json"
# Reuse bundled preview identity so this test needs no public image hosting.
$seed = Get-Content -LiteralPath "$repository/src/WidgetBridge/Marketplace/themes/index.json" -Raw | ConvertFrom-Json -AsHashtable
$template = $seed.themes[0]
$rows = @(for ($n=1; $n -le 12; $n++) {
    $row = $template.Clone()
    $row.id = "local.widgetrail.evening-$('{0:D2}' -f $n)"
    $row.name = "Evening $('{0:D2}' -f $n)"
    $row
})
$unmatched = @(for ($n=1; $n -le 5; $n++) {
    $row = $template.Clone()
    $row.id = "local.widgetrail.morning-$('{0:D2}' -f $n)"
    $row.name = "Morning $('{0:D2}' -f $n)"
    $row
})
$publication = @{ schemaVersion = 1; featured = $rows[0].id; themes = $rows + $unmatched; collections = @() }
$publication | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath "$indexes/1.0.0.json"
$rows[8].description = 'Returned from an updated catalogue after reconnect.'
$publication | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath "$indexes/2.0.0.json"
function Control([string]$Version, [int]$Advance, [bool]$Offline) {
    @{ Version = $Version; AdvanceSeconds = $Advance; ThemeFeedOffline = $Offline } |
        ConvertTo-Json | Set-Content -LiteralPath "$out/pending.json"
    [IO.File]::Move("$out/pending.json", "$out/control.json", $true)
}
Control '1.0.0' 0 $false
$app = Start-Process -FilePath $frontend -PassThru -WindowStyle Hidden -ArgumentList '--show','--shell-no-controller','--validation-platform-activation',
    ('"--shell-config={0}/shell.json"' -f $out)
$checks = [Collections.Generic.List[string]]::new()
$errorText = $null
function Ui([string[]]$ArgsList) {
    $raw = & winapp ui @ArgsList -a $app.Id --json 2>$null
    $result = $raw | ConvertFrom-Json
    if ($result.error) { throw ($result.error | ConvertTo-Json -Compress) }
    return $result
}
function WaitFor([string]$Id, [string]$Name = '', [int]$Seconds = 12) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $result = Ui @('wait-for',$Id,'-t','900')
        if ($result.found -and (!$Name -or $result.element.name -like $Name)) { return $result.element }
        Start-Sleep -Milliseconds 200
    } while ($timer.Elapsed.TotalSeconds -lt $Seconds)
    throw "Missing $Id with name '$Name'. Last value: '$($result.element.name)'"
}
function Invoke([string]$Id) {
    $null = WaitFor $Id
    $null = Ui @('invoke',$Id)
}
function Card([int]$Number,[string]$Shelf = 'releases') {
    $id = "local.widgetrail.evening-$('{0:D2}' -f $Number)"
    $key = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($id)))
    return "Widget.theme.$Shelf.$key"
}
function Check([bool]$Condition,[string]$Label) {
    if (!$Condition) { throw $Label }
    $checks.Add($Label)
    Write-Output "PASS $Label"
}
try {
    Invoke 'Widget.marketplace.tab.themes'
    $null = WaitFor (Card 1 'feature') 'Evening 01,*'
    $null = WaitFor 'Widget.theme.pages' 'Page 1 of 3'
    Check (Test-Path -LiteralPath "$profile/theme-catalogue/catalogue.json") 'The native storefront accepts and persists a first index response'
    Invoke 'Widget.theme.search'
    $null = WaitFor 'Widget.TextEntry.Editor'
    $null = Ui @('set-value','Widget.TextEntry.Editor','Evening')
    Invoke 'Widget.TextEntry.Commit'
    $null = WaitFor 'Widget.theme.heading.releases' 'Search results'
    $null = WaitFor 'Widget.theme.pages' 'Page 1 of 2'
    Control '1.0.0' 910 $true
    $null = WaitFor 'Widget.theme.next'
    $null = Ui @('scroll-into-view','Widget.theme.next')
    Invoke 'Widget.theme.next'
    $null = WaitFor 'Widget.theme.pages' 'Page 2 of 2'
    $null = WaitFor 'Widget.theme.offline' 'Catalogue offline'
    $card = Card 9
    $null = WaitFor $card 'Evening 09,*'
    $null = Ui @('scroll-into-view',$card)
    $null = Ui @('focus',$card)
    $oldFocus = (Ui @('get-focused')).element.automationId
    Check ($oldFocus -eq $card) 'The last good search page remains focusable while offline'
    $null = Ui @('screenshot','Overlay.ProductionRoot','-o',"$out/offline.png")
    Control '2.0.0' 972 $false
    $known = WaitFor $card '*Returned from an updated catalogue after reconnect.*' 43
    Check ($known.name -like 'Evening 09,*') 'The already-open native page renders the reconnected listing'
    $page = WaitFor 'Widget.theme.pages' 'Page 2 of 2'
    $search = WaitFor 'Widget.theme.heading.releases' 'Search results'
    Check ($page.name -eq 'Page 2 of 2' -and $search.name -eq 'Search results') 'Reconnect preserves the selected search and page'
    $newFocus = (Ui @('get-focused')).element.automationId
    Check ($newFocus -eq $oldFocus) 'Reconnect preserves focus on the same listing'
    $offline = Ui @('wait-for','Widget.theme.offline','-t','250')
    Check (!$offline.found) 'The offline message clears after a successful refresh'
    $null = Ui @('screenshot','Overlay.ProductionRoot','-o',"$out/reconnected.png")
    Invoke $card
    $null = WaitFor 'Widget.theme.name' 'Evening 09'
    Invoke 'Widget.theme.back'
    $null = WaitFor $card
    Start-Sleep -Milliseconds 350
    Check ((Ui @('get-focused')).element.automationId -eq $card) 'Open and Back still restore the same listing after reconnect'
    $requests = @(Get-Content -LiteralPath "$out/requests.jsonl" | ConvertFrom-Json | Where-Object path -Like '*/themes/v1/index.json')
    Check ($requests.Count -eq 3 -and $requests[-1].version -eq '2.0.0') 'Exactly three requests serve entry, disconnection, and recovery'
} catch {
    $errorText = $_.Exception.ToString()
    try { Ui @('inspect','--interactive') | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath "$out/ui-tree.json" } catch { }
} finally {
    @{ passed = !$errorText; checks = $checks; error = $errorText } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$out/result.json"
    if (!$app.HasExited) {
        & "$PSScriptRoot/Close-WinUiTestShell.ps1" -AppPid $app.Id
        $null = $app.WaitForExit(10000)
    }
    $app.Dispose()
}
if ($errorText) { throw $errorText }
