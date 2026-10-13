# Drives only the explicitly launched local sharing fixture. Native control
# geometry is read through UIA; actions use analog controller frames, never UIA Invoke or direct cursor writes.
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
if ($ownerProcess.Name -ne 'OverlayFrontend.WinUI.exe' -or !$ownerProcess.CommandLine.Replace('\','/').Contains($expectedArgument) -or !$ownerProcess.CommandLine.Contains('--shell-no-controller') -or !$ownerProcess.CommandLine.Contains('--validation-platform-activation')) {
    throw 'Controller driver requires the exact isolated native validation process.'
}
$browser = @(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" | Where-Object { $_.ParentProcessId -eq $fixture.pid -and $_.CommandLine -notlike '*--type=*' })
if ($browser.Count -ne 1) { throw 'Expected one owned browser process.' }
$native = @(winapp ui list-windows -a $browser[0].ProcessId --json | ConvertFrom-Json)
if ($native.Count -ne 1) { throw 'Expected one native sharing window.' }
$window = $native[0].hwnd
$script:sequence = if (Test-Path -LiteralPath ($Result + '.picker-ack')) { [int](Get-Content -LiteralPath ($Result + '.picker-ack') -Raw | ConvertFrom-Json).sequence } else { 0 }
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
function Cursor { [SharingPointerProbe]::Read() }
function Find-Control([string]$Name) {
    $match = @( (winapp ui search $Name -w $window --json | ConvertFrom-Json).matches | Where-Object { $_.name -eq $Name -and !$_.isOffscreen } )
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
    } while (($null -eq $ack -or $ack.sequence -ne $script:sequence) -and [DateTime]::UtcNow -lt $until)
    if ($null -eq $ack -or $ack.sequence -ne $script:sequence) { throw 'Native controller command was not acknowledged.' }
    $cursor = Cursor
    $evidence.Add(@{command=$Command;nativeForeground=$ack.nativeReady;x=$cursor.X;y=$cursor.Y})
    $evidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath ($Result + '.controller-evidence.json') -Encoding utf8NoBOM
    return $ack.nativeReady
}
function Move-To($Control) {
    $x = $Control.x + $Control.width / 2; $y = $Control.y + $Control.height / 2
    for ($i=0; $i -lt 80; $i++) {
        $cursor = Cursor
        $dx = $x - $cursor.X; $dy = $y - $cursor.Y
        if ([Math]::Abs($dx) -le 8 -and [Math]::Abs($dy) -le 8) { return }
        $axisX = if ([Math]::Abs($dx) -le 8) { 0 } else { [Math]::Sign($dx) * [Math]::Clamp([Math]::Abs($dx) * 350, 9000, 26000) }
        $axisY = if ([Math]::Abs($dy) -le 8) { 0 } else { -[Math]::Sign($dy) * [Math]::Clamp([Math]::Abs($dy) * 350, 9000, 26000) }
        $null = Send-Controller "move $([int]$axisX) $([int]$axisY) 1"
    }
    throw 'Analog pointer did not reach the native control.'
}
for ($i=0; $i -lt 12; $i++) { if (Send-Controller neutral) { break }; Start-Sleep -Milliseconds 100 }
if (!$evidence[$evidence.Count-1].nativeForeground) { throw 'Native dialog did not receive focus.' }
if ($Cancel) {
    Move-To (Find-Control 'Cancel')
    $null = Send-Controller cancel
}
else {
    $source = Find-Control $fixture.target
    # UIA reports full tile bounds even when the lower half is clipped by the viewport.
    # Start inside the visible thumbnail and exercise right-stick scrolling before selecting.
    $originalY = $source.y
    $source.height = [Math]::Min(80, $source.height)
    Move-To $source
    $null = Send-Controller 'scroll 0 -22000 10'
    $source = Find-Control $fixture.target
    if ($source.y -ge $originalY) { throw 'Right-stick scrolling did not move the source grid.' }
    $null = Send-Controller 'scroll 0 22000 30'
    $source = Find-Control $fixture.target
    $source.height = [Math]::Min(80, $source.height)
    Move-To $source
    $null = Send-Controller select
    $share = Find-Control 'Share'
    if (!$share.isEnabled) { throw 'Native Share control is not enabled.' }
    Move-To $share
    $null = Send-Controller select
}
$evidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath ($Result + '.controller-evidence.json') -Encoding utf8NoBOM
Write-Output 'Controller-only native picker commands completed; no UIA activation or direct test-driver clicks were used.'
