# Drives only the explicitly launched local sharing fixture. Native control
# geometry is read through UIA; actions use analog controller frames, never UIA Invoke or direct cursor writes.
param([Parameter(Mandatory)][string]$Result, [switch]$Cancel, [switch]$CancelWithB, [ValidateRange(1, 3)][int]$Round = 1)
$ErrorActionPreference = 'Stop'
if ($CancelWithB -and !$Cancel) { throw '-CancelWithB requires -Cancel.' }
$deadline = [DateTime]::UtcNow.AddSeconds(40)
while (!(Test-Path -LiteralPath ($Result + '.sharing-ready')) -or [int](Get-Content -LiteralPath ($Result + '.sharing-ready') -Raw | ConvertFrom-Json).round -lt $Round) {
    if (Test-Path -LiteralPath $Result) { throw (Get-Content -LiteralPath $Result -Raw) }
    if ([DateTime]::UtcNow -gt $deadline) { throw 'Sharing fixture did not become ready.' }
    Start-Sleep -Milliseconds 100
}
$fixture = Get-Content -LiteralPath ($Result + '.sharing-ready') -Raw | ConvertFrom-Json
$fixturePid = [int]$fixture.pid
if ($fixturePid -le 0 -or $fixture.target -ne "WidgetRail synthetic sharing target $fixturePid") { throw 'Invalid synthetic fixture identity.' }
$ownerProcess = Get-CimInstance Win32_Process -Filter "ProcessId=$fixturePid"
$expectedArgument = '--validate-webapp-shell=' + ([IO.Path]::GetFullPath($Result).Replace('\','/'))
if ($ownerProcess.Name -ne 'OverlayFrontend.WinUI.exe' -or !$ownerProcess.CommandLine.Replace('\','/').Contains($expectedArgument) -or !$ownerProcess.CommandLine.Contains('--shell-no-controller') -or !$ownerProcess.CommandLine.Contains('--validation-platform-activation')) {
    throw 'Controller driver requires the exact isolated native validation process.'
}
$browser = @(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" | Where-Object { $_.ParentProcessId -eq $fixture.pid -and $_.CommandLine -notlike '*--type=*' })
if ($browser.Count -ne 1) { throw 'Expected one owned browser process.' }
$script:window = 0
$script:virtualCursor = [pscustomobject]@{ X=0; Y=0 }
$script:sequence = if (Test-Path -LiteralPath ($Result + '.picker-ack')) { [int](Get-Content -LiteralPath ($Result + '.picker-ack') -Raw | ConvertFrom-Json).sequence } else { 0 }
$script:sequence = [Math]::Max($Round * 10000, $script:sequence)
$evidence = [Collections.Generic.List[object]]::new()
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SharingPointerProbe {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    public static Point Read() {
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try { if (!GetCursorPos(out var point)) throw new InvalidOperationException("Cannot read pointer"); return point; }
        finally { SetThreadDpiAwarenessContext(previous); }
    }
}
'@
function Cursor { $script:virtualCursor }
function Find-Control([string]$Name, [string]$Type = '') {
    $match = @( (winapp ui search $Name -w $window --json | ConvertFrom-Json).matches | Where-Object { $_.name -eq $Name -and !$_.isOffscreen -and (!$Type -or $_.type -eq $Type) } )
    if ($match.Count -ne 1) { throw "Expected one visible native control: $Name" }
    return $match[0]
}
function Send-Controller([string]$Command) {
    $script:sequence++
    [IO.File]::WriteAllText($Result + '.picker-command.tmp', "$script:sequence $Command")
    Move-Item -LiteralPath ($Result + '.picker-command.tmp') -Destination ($Result + '.picker-command') -Force
    $until = [DateTime]::UtcNow.AddSeconds(3)
    do {
        Start-Sleep -Milliseconds 100
        $ack = if (Test-Path -LiteralPath ($Result + '.picker-ack')) { Get-Content -LiteralPath ($Result + '.picker-ack') -Raw | ConvertFrom-Json } else { $null }
    } while (($null -eq $ack -or $ack.sequence -ne $script:sequence -or $ack.round -ne $Round) -and [DateTime]::UtcNow -lt $until)
    if ($null -eq $ack -or $ack.sequence -ne $script:sequence -or $ack.round -ne $Round) { throw 'Native controller command was not acknowledged.' }
    $script:virtualCursor = [pscustomobject]@{ X=$ack.x; Y=$ack.y }
    if ($ack.window -ne 0) { $script:window = $ack.window }
    $cursor = Cursor
    $evidence.Add(@{command=$Command;nativeForeground=$ack.nativeReady;circle=$ack.circleVisible;x=$cursor.X;y=$cursor.Y})
    $evidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath ($Result + ".round-$Round.controller-evidence.json") -Encoding utf8NoBOM
    return $ack.nativeReady
}
function Move-To($Control) {
    for ($i=0; $i -lt 80; $i++) {
        # Chromium can reorder sources as thumbnails arrive. Follow current
        # geometry, not a tile's position from the initial asynchronous scan.
        if ($i % 4 -eq 0 -and $Control.name) { $Control = Find-Control $Control.name $Control.type }
        $x = $Control.x + $Control.width / 2; $y = $Control.y + [Math]::Min(80, $Control.height) / 2
        $cursor = Cursor
        $dx = $x - $cursor.X; $dy = $y - $cursor.Y
        if ([Math]::Abs($dx) -le 8 -and [Math]::Abs($dy) -le 8) { return }
        $axisX = if ([Math]::Abs($dx) -le 8) { 0 } else { [Math]::Sign($dx) * [Math]::Clamp([Math]::Abs($dx) * 350, 9000, 26000) }
        $axisY = if ([Math]::Abs($dy) -le 8) { 0 } else { -[Math]::Sign($dy) * [Math]::Clamp([Math]::Abs($dy) * 350, 9000, 26000) }
        $null = Send-Controller "move $([int]$axisX) $([int]$axisY) 1"
    }
    throw 'Analog pointer did not reach the native control.'
}
function Press-WithKeyboardActivity {
    $null = Send-Controller press
    # GetLastInputInfo changes even though the mouse never moved. This must not
    # steal the controller gesture or hide its circle before the release.
    winapp ui send-keys shift -w $window --via send-input --json | Out-Null
    winapp ui send-keys 'vk=0xC3' -w $window --via send-input --json | Out-Null
    $null = Send-Controller observe
    if (!$evidence[$evidence.Count-1].circle) { throw 'Unrelated keyboard activity hid the controller circle.' }
}
for ($i=0; $i -lt 12; $i++) { if ((Send-Controller neutral) -and $evidence[$evidence.Count-1].circle) { break }; Start-Sleep -Milliseconds 100 }
if (!$evidence[$evidence.Count-1].nativeForeground) { throw 'Native dialog did not receive focus.' }
$circleWindow = @(winapp ui list-windows -a $fixturePid --json | ConvertFrom-Json | Where-Object { $_.className -eq 'WidgetRail.ControllerPointer.v1' })
if ($circleWindow.Count -ne 1 -or $circleWindow[0].isForeground) { throw 'Expected one nonactivating controller-circle window.' }
winapp ui screenshot -w $circleWindow[0].hwnd -o ($Result + ".round-$Round.circle.png") --json | Out-Null
if ($Round -eq 1) {
    winapp ui hover Cancel -w $window --json | Out-Null
    $null = Send-Controller neutral
    if ($evidence[$evidence.Count-1].circle) { throw 'Desktop mouse input did not hide the circle.' }
    $null = Send-Controller 'move 9000 0 3'
    if (!$evidence[$evidence.Count-1].circle) { throw 'Controller input did not restore the circle.' }
}
$physicalStart = [SharingPointerProbe]::Read()
if (!$evidence[$evidence.Count-1].circle) { throw 'Controller circle was not visible on entry.' }
if ($Cancel) {
    if ($Round -eq 2) {
        $audio = Find-Control 'Share with system audio' 'Button'
        if ($audio.toggleState -ne 'off') { throw 'Audio must initially be disabled.' }
        Move-To $audio
        Press-WithKeyboardActivity
        $null = Send-Controller release
        if ((Find-Control 'Share with system audio' 'Button').toggleState -ne 'on') { throw 'A did not enable the native audio switch.' }
        Press-WithKeyboardActivity
        $null = Send-Controller release
        if ((Find-Control 'Share with system audio' 'Button').toggleState -ne 'off') { throw 'A did not disable the native audio switch.' }
    }
    Move-To (Find-Control 'Cancel')
    if ($CancelWithB) { $null = Send-Controller cancel } else { $null = Send-Controller cancel-click }
}
else {
    $grid = Find-Control 'Window' 'Tab'
    Move-To ([pscustomobject]@{x=$grid.x; y=$grid.y+100; width=$grid.width; height=60})
    $source = Find-Control $fixture.target
    # UIA reports full tile bounds even when the lower half is clipped by the viewport.
    # Start inside the visible thumbnail and exercise right-stick scrolling before selecting.
    $originalY = $source.y
    $null = Send-Controller 'scroll 0 -22000 10'
    $source = Find-Control $fixture.target
    if ($source.y -ge $originalY) { throw 'Right-stick scrolling did not move the source grid.' }
    $null = Send-Controller 'scroll 0 22000 30'
    $source = Find-Control $fixture.target
    for ($i=0; $i -lt 10 -and $source.y + 80 -gt $grid.y + $grid.height; $i++) {
        $null = Send-Controller 'scroll 0 -22000 5'
        $source = Find-Control $fixture.target
    }
    Move-To $source
    Press-WithKeyboardActivity
    $null = Send-Controller 'move 9000 0 3'
    $null = Send-Controller release
    $share = Find-Control 'Share'
    if (!$share.isEnabled) { throw 'Native Share control is not enabled.' }
    Move-To $share
    Press-WithKeyboardActivity
    $null = Send-Controller release
}
$physicalEnd = [SharingPointerProbe]::Read()
if ($physicalStart.X -ne $physicalEnd.X -or $physicalStart.Y -ne $physicalEnd.Y) { throw 'Controller navigation moved the physical mouse.' }
$evidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath ($Result + ".round-$Round.controller-evidence.json") -Encoding utf8NoBOM
Write-Output 'Controller-only native picker commands completed; no UIA activation or direct test-driver clicks were used.'
