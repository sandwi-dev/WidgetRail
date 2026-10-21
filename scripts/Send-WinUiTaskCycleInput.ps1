param([Parameter(Mandatory)][string]$SignalPath, [Parameter(Mandatory)][string]$TargetsPath, [switch]$ReplaceForegroundGrant)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ExternalTaskCycleInput {
    [StructLayout(LayoutKind.Explicit, Size=40)] public struct Input {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(20)] public uint Flags;
    }
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint process);
    [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint count, ref Input value, int size);
    [DllImport("user32.dll", SetLastError=true)] public static extern int AllowSetForegroundWindow(uint process);
}
'@
$targets = @(foreach ($target in (Get-Content -LiteralPath $TargetsPath -Raw | ConvertFrom-Json)) { [uint32]$target.ProcessId })
if ($targets.Count -ne 2 -or $targets -contains 0) { throw 'Two exact target process identities are required.' }
$sequence = 1
$deadline = [DateTime]::UtcNow.AddMinutes(2)
while ([DateTime]::UtcNow -lt $deadline -and !(Test-Path -LiteralPath ($SignalPath + '.stop'))) {
    if (Test-Path -LiteralPath ($SignalPath + '.' + $sequence)) {
        [uint32]$foregroundProcess = 0
        [void][ExternalTaskCycleInput]::GetWindowThreadProcessId([ExternalTaskCycleInput]::GetForegroundWindow(), [ref]$foregroundProcess)
        if ($foregroundProcess -notin $targets) { throw "Only the named live target may receive the neutral input (foreground=$foregroundProcess, expected=$targets)." }
        $inputEvent = [ExternalTaskCycleInput+Input]::new()
        $inputEvent.Flags = 0x2001 # zero-distance relative mouse move; no click/key
        if ([ExternalTaskCycleInput]::SendInput(1, [ref]$inputEvent, 40) -ne 1) { throw 'Neutral input was not delivered.' }
        # An explicit grant to the foreground target deterministically expires
        # the broker's previous grant. Synthetic pointer input alone does not
        # consistently exercise that documented lifetime boundary.
        if ($ReplaceForegroundGrant -and [ExternalTaskCycleInput]::AllowSetForegroundWindow($foregroundProcess) -eq 0) {
            throw "Foreground-grant replacement failed ($([Runtime.InteropServices.Marshal]::GetLastWin32Error()))."
        }
        Start-Sleep -Milliseconds 80
        [IO.File]::WriteAllText($SignalPath + '.done.' + $sequence, '')
        $sequence++
    }
    Start-Sleep -Milliseconds 20
}
