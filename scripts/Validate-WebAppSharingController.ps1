# Drives only the explicitly launched local sharing fixture. UIA reads focus;
# actions travel through the production native-picker controller adapter.
param([Parameter(Mandatory)][string]$Result, [switch]$Cancel)
$ErrorActionPreference = 'Stop'
$deadline = [DateTime]::UtcNow.AddSeconds(40)
while (!(Test-Path -LiteralPath ($Result + '.sharing-ready'))) {
    if (Test-Path -LiteralPath $Result) { throw (Get-Content -LiteralPath $Result -Raw) }
    if ([DateTime]::UtcNow -gt $deadline) { throw 'Sharing fixture did not become ready.' }
    Start-Sleep -Milliseconds 100
}
$fixture = Get-Content -LiteralPath ($Result + '.sharing-ready') -Raw | ConvertFrom-Json
$fixturePid = [int]$fixture.pid
if ($fixturePid -le 0 -or $fixture.target -ne "WidgetRail synthetic sharing target $fixturePid") { throw 'Invalid synthetic fixture identity.' }
$ownerProcess = Get-CimInstance Win32_Process -Filter "ProcessId=$fixturePid"
$expectedArgument = '--validate-webapp-shell=' + ([IO.Path]::GetFullPath($Result).Replace('\','/'))
if ($ownerProcess.Name -ne 'OverlayFrontend.WinUI.exe' -or !$ownerProcess.CommandLine.Replace('\','/').Contains($expectedArgument) -or !$ownerProcess.CommandLine.Contains('--shell-no-controller')) {
    throw 'Controller driver requires the exact isolated native validation process.'
}
$browser = @(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" | Where-Object { $_.ParentProcessId -eq $fixture.pid -and $_.CommandLine -notlike '*--type=*' })
if ($browser.Count -ne 1) { throw 'Expected one owned browser process.' }
$native = @(winapp ui list-windows -a $browser[0].ProcessId --json | ConvertFrom-Json)
if ($native.Count -ne 1) { throw 'Expected one native sharing window.' }
$window = $native[0].hwnd
$script:sequence = 0
$evidence = [Collections.Generic.List[object]]::new()
function Focused { (winapp ui get-focused -w $window --json | ConvertFrom-Json).element }
function Send-Controller([string]$Command) {
    $script:sequence++
    [IO.File]::WriteAllText($Result + '.picker-command.tmp', "$script:sequence $Command")
    Move-Item -LiteralPath ($Result + '.picker-command.tmp') -Destination ($Result + '.picker-command') -Force
    $until = [DateTime]::UtcNow.AddSeconds(3)
    do {
        Start-Sleep -Milliseconds 100
        $ack = if (Test-Path -LiteralPath ($Result + '.picker-ack')) { Get-Content -LiteralPath ($Result + '.picker-ack') -Raw | ConvertFrom-Json } else { $null }
    } while (($null -eq $ack -or $ack.sequence -ne $script:sequence) -and [DateTime]::UtcNow -lt $until)
    if ($null -eq $ack -or $ack.sequence -ne $script:sequence) { throw 'Native controller command was not acknowledged.' }
    Start-Sleep -Milliseconds 100
    $focused = Focused
    $evidence.Add(@{command=$Command;nativeForeground=$ack.nativeReady;type=$focused.type;syntheticTarget=($focused.name -eq $fixture.target);share=($focused.name -eq 'Share')})
    return $focused
}
if ($Cancel) {
    for ($i=0; $i -lt 8; $i++) {
        $null = Send-Controller next
        if ($evidence[$evidence.Count-1].nativeForeground) { break }
    }
    $null = Send-Controller cancel
}
else {
    $focused = Focused
    for ($i=0; $i -lt 8 -and $focused.className -ne 'DesktopMediaSourceView'; $i++) { $focused = Send-Controller next }
    if ($focused.className -ne 'DesktopMediaSourceView') { throw 'Controller did not enter native source choices.' }
    for ($i=0; $i -lt 40 -and $focused.name -ne $fixture.target; $i++) { $focused = Send-Controller right }
    if ($focused.name -ne $fixture.target) { throw 'Synthetic window is not focused. No selection will be sent.' }
    $focused = Send-Controller select
    for ($i=0; $i -lt 8 -and $focused.name -ne 'Share'; $i++) { $focused = Send-Controller next }
    if ($focused.name -ne 'Share' -or !$focused.isEnabled) { throw 'Native Share control is not focused and enabled.' }
    $null = Send-Controller select
}
$evidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath ($Result + '.controller-evidence.json') -Encoding utf8NoBOM
Write-Output 'Controller-only native picker commands completed; no UIA activation or mouse clicks were used.'
