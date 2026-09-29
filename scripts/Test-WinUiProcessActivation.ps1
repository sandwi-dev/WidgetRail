[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BridgeInstallation,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$installation = (Resolve-Path -LiteralPath $BridgeInstallation).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh output directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
if (-not ('WinUiProcessActivationProbe' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class WinUiProcessActivationProbe {
    [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager {
        [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string id,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint process);
    }
    public static uint Activate(string id, string arguments) {
        var value = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45ba127d-10a8-46ea-8ab7-56ea9078943c")));
        try {
            var result = ((IApplicationActivationManager)value).ActivateApplication(id, arguments, 0, out var process);
            Marshal.ThrowExceptionForHR(result); return process;
        } finally { Marshal.FinalReleaseComObject(value); }
    }
    private delegate bool Callback(IntPtr window, IntPtr ignored);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback, IntPtr ignored);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    public static int VisibleWindows(uint process) {
        int count = 0;
        EnumWindows((window, ignored) => { GetWindowThreadProcessId(window, out var owner);
            if(owner == process && IsWindowVisible(window)) ++count; return true; }, IntPtr.Zero);
        return count;
    }
}
'@
}
$arguments = "--installation-root=`"$installation`" --settings-root=`"$(Join-Path $output 'profile')`""
$launch = winapp run (Join-Path $root 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') `
    --no-build --arch x64 -p Platform=x64 --detach --json --args "--hidden $arguments" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $launch.ProcessId -le 0) { throw 'Initial packaged launch failed.' }
$appPid = [int]$launch.ProcessId
$resident = Get-Process -Id $appPid
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$Condition, [string]$Name) { if (-not $Condition) { throw $Name }; $checks.Add($Name) }
function Children { @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $appPid" | Where-Object Name -eq 'WidgetBridge.exe') }
function Redirect([bool]$Hidden) {
    $request = $(if ($Hidden) { '--hidden ' }) + $arguments
    # Activate the registered identity through Windows without re-registering or
    # replacing the live development payload. Never execute a packaged EXE directly.
    $client = [WinUiProcessActivationProbe]::Activate($launch.AUMID, $request)
    Check ($client -ne $appPid) 'Windows starts a separate activation client'
    $process = Get-Process -Id $client -ErrorAction SilentlyContinue
    if ($process) {
        Check ($process.WaitForExit(10000)) 'Activation client exits after process election'
        $process.Dispose()
    }
    $resident.Refresh()
    Check (-not $resident.HasExited) 'Original resident survives duplicate activation'
}
try {
    Start-Sleep -Milliseconds 750
    Check ([WinUiProcessActivationProbe]::VisibleWindows($appPid) -eq 0) 'Initial hidden launch has no visible window'
    Check (@(Children).Count -eq 0) 'Initial hidden launch starts no Bridge'
    Redirect $true
    Check ([WinUiProcessActivationProbe]::VisibleWindows($appPid) -eq 0 -and @(Children).Count -eq 0) 'Hidden duplicate neither shows nor starts workers'
    Redirect $false
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $children = @(Children)
        if ($children.Count -eq 1 -and [WinUiProcessActivationProbe]::VisibleWindows($appPid) -gt 0) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    Check ($children.Count -eq 1 -and [WinUiProcessActivationProbe]::VisibleWindows($appPid) -gt 0) 'Normal duplicate opens the resident with one Bridge'
    $bridge = Get-Process -Id $children[0].ProcessId
    Redirect $false
    Check ([WinUiProcessActivationProbe]::VisibleWindows($appPid) -gt 0) 'Repeated normal activation shows instead of toggling closed'
    Check (@(Children).Count -eq 1 -and @(Children)[0].ProcessId -eq $bridge.Id) 'Repeated activation preserves the same Bridge'
    & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $appPid
    Check ($resident.WaitForExit(15000)) 'Resident exits after orderly cleanup'
    Check ($bridge.WaitForExit(5000)) 'Owned Bridge exits with resident'
    @{passed=$true;pid=$appPid;checks=$checks} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $output 'result.json')
    "Passed $($checks.Count) packaged process-activation checks."
} catch {
    @{passed=$false;pid=$appPid;checks=$checks;error=$_.Exception.Message} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $output 'result.json')
    throw
} finally {
    if (Get-Process -Id $appPid -ErrorAction SilentlyContinue) {
        & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $appPid
        Wait-Process -Id $appPid -Timeout 15 -ErrorAction SilentlyContinue
    }
    $resident.Dispose()
    if ($bridge) { $bridge.Dispose() }
}
