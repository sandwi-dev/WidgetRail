<#
.SYNOPSIS
Observe cold hidden and repeated warmed-hide resource use on an already registered candidate.
.DESCRIPTION
Does not build, register, install, or execute a packaged EXE directly. Uses Windows
package activation, native tray UIA activation, production Escape dismissal, and
the existing process-tree sampler. No widget actions such as Play or Install run.
Choose a fresh output directory. Run without another frontend using this candidate.
BuildLabel describes the actual staged configuration; executable hashes identify it.
No resource ceiling or leak acceptance is inferred from this bounded observation.
.EXAMPLE
./scripts/Measure-WinUiWarmedHide.ps1 -Aumid 'package!App' `
  -ExpectedExecutable 'C:/staged/OverlayFrontend.WinUI.exe' `
  -BridgeInstallation 'C:/isolated/installation' -BuildLabel 'Release trimmed R2R' `
  -WidgetIds 'widgetrail.samples.playnite-library','widgetrail.samples.ytmusic' `
  -OutputDirectory 'C:/evidence/warmed-hide-01'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Aumid,
    [Parameter(Mandatory)][string]$ExpectedExecutable,
    [Parameter(Mandatory)][string]$BridgeInstallation,
    [Parameter(Mandatory)][string]$BuildLabel,
    [Parameter(Mandatory)][ValidateCount(1,16)][string[]]$WidgetIds,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(2,100)][int]$Cycles = 3,
    [ValidateRange(2,3600)][int]$ColdSeconds = 10,
    [ValidateRange(2,3600)][int]$VisibleSeconds = 5,
    [ValidateRange(2,3600)][int]$HiddenSeconds = 30,
    [ValidateRange(0,60)][int]$WarmupSeconds = 3,
    [ValidateRange(500,10000)][int]$IntervalMilliseconds = 1000
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$installation = (Resolve-Path -LiteralPath $BridgeInstallation).Path
$expected = (Resolve-Path -LiteralPath $ExpectedExecutable).Path
$expectedHash = (Get-FileHash -LiteralPath $expected -Algorithm SHA256).Hash
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh output directory; evidence and profile are never overwritten.' }
if (@($WidgetIds | Select-Object -Unique).Count -ne $WidgetIds.Count -or
    @($WidgetIds | Where-Object { $_ -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$' }).Count) { throw 'Supply distinct valid widget IDs.' }
if ($installation.Contains('"') -or $output.Contains('"')) { throw 'Launch paths cannot contain a quote.' }
if (-not (Test-Path -LiteralPath (Join-Path $installation 'runtime/Bridge/WidgetBridge.exe')) -or
    -not (Test-Path -LiteralPath (Join-Path $installation 'widget-catalog.json'))) { throw 'Installation is missing Bridge or widget catalog.' }
# Refuse to adopt an existing process: a redirected activation must never confer cleanup ownership.
if (@(Get-Process -Name 'OverlayFrontend.WinUI' -ErrorAction SilentlyContinue).Count) {
    throw 'A frontend is already running. This driver requires an exclusive native observation slot.'
}
$null = New-Item -ItemType Directory -Path $output
$profile = Join-Path $output 'profile'
$phases = [Collections.Generic.List[object]]::new()
$events = [Collections.Generic.List[object]]::new()
$known = @{}
$resident = $null
$ownedApp = 0
$failure = $null
$shutdownFailure = $null
$started = [DateTime]::UtcNow

if (-not ('WinUiWarmedHideActivation' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class WinUiWarmedHideActivation {
    [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivation {
        [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string id,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint flags, out uint process);
    }
    public static uint Activate(string id, string arguments) {
        var manager = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45ba127d-10a8-46ea-8ab7-56ea9078943c")));
        try { var hr = ((IActivation)manager).ActivateApplication(id, arguments, 0, out var process);
            Marshal.ThrowExceptionForHR(hr); return process; }
        finally { Marshal.FinalReleaseComObject(manager); }
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool WindowCallback(IntPtr window, IntPtr ignored);
    [DllImport("user32.dll", ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(WindowCallback callback, IntPtr ignored);
    [DllImport("user32.dll", ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
    public static int VisibleWindows(uint process) {
        int count = 0;
        WindowCallback callback = (window, ignored) => {
            GetWindowThreadProcessId(window, out var owner);
            if (owner == process && IsWindowVisible(window)) ++count;
            return true;
        };
        EnumWindows(callback, IntPtr.Zero); GC.KeepAlive(callback); return count;
    }
}
'@
}

function SaveJson([string]$Name, $Value) {
    $Value | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $output $Name)
}
function AssertResident {
    $resident.Refresh()
    if ($resident.HasExited) { throw 'The owned frontend exited during observation.' }
}
function Ui([string[]]$Arguments) {
    AssertResident
    $raw = winapp ui @Arguments -a $ownedApp --json
    if ($LASTEXITCODE -ne 0) { throw "UI operation failed: $($Arguments[0]): $raw" }
    $value = ($raw -join "`n") | ConvertFrom-Json
    if ($Arguments[0] -eq 'wait-for' -and -not $value.found) { throw "UI wait timed out: $($Arguments[1])" }
    return $value
}
function State([string]$Widget, [bool]$Interactive) {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $value = (Ui @('get-property','Overlay.Shell','-p','HelpText')).properties.HelpText | ConvertFrom-Json
        if ($value.activeWidget -eq $Widget -and $value.interactive -eq $Interactive -and $value.visible -and
            $value.foreground -and -not $value.switching -and $value.publication -gt 0) { return $value }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    SaveJson 'unsettled-shell.json' $value
    throw "Shell did not settle on $Widget, interactive=$Interactive, with foreground ownership."
}
function WaitVisibility([bool]$Visible) {
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        AssertResident
        $count = [WinUiWarmedHideActivation]::VisibleWindows([uint32]$ownedApp)
        if (($count -gt 0) -eq $Visible) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Owned window visibility did not become $Visible."
}
function ShowResident {
    AssertResident
    $clientId = [WinUiWarmedHideActivation]::Activate($Aumid, $launchArguments)
    $events.Add(@{kind='show-activation'; utc=[DateTime]::UtcNow.ToString('O'); clientPid=$clientId})
    if ($clientId -ne $ownedApp) {
        $client = Get-Process -Id $clientId -ErrorAction SilentlyContinue
        if ($null -ne $client) {
            try { if (-not $client.WaitForExit(10000)) { throw 'Show activation client did not exit.' } }
            finally { $client.Dispose() }
        }
    }
    WaitVisibility $true
    $null = Ui @('wait-for','Overlay.Shell','-t','30000')
}
function Observe([string]$Name, [int]$Seconds, [bool]$Visible, $ShellState) {
    WaitVisibility $Visible
    $path = Join-Path $output ($Name + '.json')
    $phaseStart = [DateTime]::UtcNow
    & (Join-Path $PSScriptRoot 'Measure-WinUiProcessResources.ps1') -AppPid $ownedApp -OutputPath $path `
        -SampleSeconds $Seconds -IntervalMilliseconds $IntervalMilliseconds -Scenario "$BuildLabel; $Name"
    $report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($report.ending -ne 'duration-complete' -or $report.executableSha256 -ne $expectedHash) {
        throw "Observation $Name ended early or sampled the wrong payload."
    }
    foreach ($sample in $report.samples) {
        foreach ($process in $sample.processes) { $known[$process.identity] = $process }
    }
    WaitVisibility $Visible
    $after = if ($Visible) { State $ShellState.activeWidget ([bool]$ShellState.interactive) } else { $null }
    $cpu = @($report.samples | Where-Object { $null -ne $_.cpuPercentOfOneCore } | ForEach-Object { $_.cpuPercentOfOneCore })
    $last = $report.samples[-1]
    $phases.Add([ordered]@{name=$Name; startUtc=$phaseStart.ToString('O'); endUtc=[DateTime]::UtcNow.ToString('O');
        visibleAtBoundaries=$Visible; shellBefore=$ShellState; shellAfter=$after; report=[IO.Path]::GetFileName($path);
        medianPrivateBytes=$report.medianSampledPrivateBytes; peakPrivateBytes=$report.peakSampledPrivateBytes;
        lastPrivateBytes=$last.privateBytes; lastHandles=($last.processes | Measure-Object handles -Sum).Sum;
        lastProcessCount=$last.processCount; meanCpuPercentOfOneCore=($cpu | Measure-Object -Average).Average})
    SaveJson 'phases.json' $phases.ToArray()
}

$launchArguments = "--installation-root=`"$installation`" --settings-root=`"$profile`" --widget=$($WidgetIds[0])"
try {
    $head = git -C (Split-Path $PSScriptRoot) rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Could not record checkout identity.' }
    $status = @(git -C (Split-Path $PSScriptRoot) status --short)
    # The EXE can be a stable apphost while managed/native payloads change.
    # Preserve adjacent build-file hashes, without reading profile/provider data.
    $payload = @(Get-ChildItem -LiteralPath ([IO.Path]::GetDirectoryName($expected)) -File |
        Where-Object { $_.Extension -in '.exe','.dll','.json','.pri','.winmd' } |
        Sort-Object Name | ForEach-Object {
            [ordered]@{name=$_.Name; bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
        })
    SaveJson 'candidate-files.json' $payload
    SaveJson 'run.json' ([ordered]@{schemaVersion=1; startedUtc=$started.ToString('O'); aumid=$Aumid;
        executable=$expected; executableSha256=$expectedHash; executableBytes=(Get-Item -LiteralPath $expected).Length;
        executableLastWriteUtc=(Get-Item -LiteralPath $expected).LastWriteTimeUtc.ToString('O');
        buildLabel=$BuildLabel; checkoutHead=$head; checkoutStatus=$status;
        bridgeInstallation=$installation; bridgeSha256=(Get-FileHash -LiteralPath (Join-Path $installation 'runtime/Bridge/WidgetBridge.exe')).Hash;
        catalogSha256=(Get-FileHash -LiteralPath (Join-Path $installation 'widget-catalog.json')).Hash;
        profile=$profile; widgetIds=$WidgetIds; cycles=$Cycles; coldSeconds=$ColdSeconds; visibleSeconds=$VisibleSeconds;
        hiddenSeconds=$HiddenSeconds; warmupSeconds=$WarmupSeconds; intervalMilliseconds=$IntervalMilliseconds;
        driverSha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash;
        samplerSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'Measure-WinUiProcessResources.ps1')).Hash})
    $ownedApp = [int][WinUiWarmedHideActivation]::Activate($Aumid, "--hidden $launchArguments")
    $resident = Get-Process -Id $ownedApp -ErrorAction Stop
    if ($resident.StartTime.ToUniversalTime() -lt $started.AddSeconds(-1) -or
        -not [StringComparer]::OrdinalIgnoreCase.Equals($resident.Path, $expected)) {
        $ownedApp = 0
        throw 'Activation returned an existing or unexpected process; cleanup ownership refused.'
    }
    SaveJson 'launch.json' @{pid=$ownedApp; startedUtc=$resident.StartTime.ToUniversalTime().ToString('O'); aumid=$Aumid}
    Observe '00-cold-hidden' $ColdSeconds $false $null
    $cold = Get-Content -LiteralPath (Join-Path $output '00-cold-hidden.json') -Raw | ConvertFrom-Json
    if (@($cold.samples | Where-Object processCount -ne 1).Count -or (Test-Path -LiteralPath $profile)) {
        throw 'Cold hidden startup initialized descendants or wrote the isolated profile.'
    }
    for ($cycle = 1; $cycle -le $Cycles; ++$cycle) {
        ShowResident
        $index = 0
        foreach ($widget in $WidgetIds) {
            ++$index
            $null = Ui @('wait-for',"Overlay.Widget.$widget",'-t','30000')
            $null = Ui @('invoke',"Overlay.Widget.$widget")
            $shell = State $widget $true
            if ($WarmupSeconds) { Start-Sleep -Seconds $WarmupSeconds }
            $shell = State $widget $true
            if ($shell.retainedSurfaceCount -gt 3 -or $shell.bridgePid -le 0) { throw 'Invalid retained-surface or Bridge ownership diagnostics.' }
            Observe ('{0:D2}-visible-{1:D2}' -f $cycle,$index) $VisibleSeconds $true $shell
        }
        # Native tray focus exits widget interaction without invoking widget actions.
        $null = Ui @('focus',"Overlay.Widget.$($WidgetIds[-1])")
        $shell = State $WidgetIds[-1] $false
        $null = Ui @('send-keys','esc','--via','send-input')
        Observe ('{0:D2}-warmed-hidden' -f $cycle) $HiddenSeconds $false $shell
    }
    # The final hidden interval also has a verified reopening, not just a terminal close.
    ShowResident
    $null = Ui @('invoke',"Overlay.Widget.$($WidgetIds[0])")
    SaveJson 'final-reopen.json' (State $WidgetIds[0] $true)
} catch { $failure = $_.Exception.Message }
finally {
    if ($ownedApp -gt 0 -and $null -ne $resident) {
        try {
            $resident.Refresh()
            if (-not $resident.HasExited) {
                & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $ownedApp
                if (-not $resident.WaitForExit(15000)) { throw 'Frontend did not finish normal WM_CLOSE shutdown; process left for inspection.' }
            }
            $deadline = [DateTime]::UtcNow.AddSeconds(10)
            do {
                $survivors = @()
                foreach ($identity in $known.Keys) {
                    $row = $known[$identity]
                    $process = Get-Process -Id $row.pid -ErrorAction SilentlyContinue
                    if ($null -eq $process) { continue }
                    try {
                        $ticks = [long]($identity.Split(':')[1])
                        if ([Math]::Abs($process.StartTime.ToUniversalTime().Ticks - $ticks) -lt 10) { $survivors += $row }
                    } catch [InvalidOperationException] { }
                    finally { $process.Dispose() }
                }
                if ($survivors.Count -eq 0) { break }
                Start-Sleep -Milliseconds 100
            } while ([DateTime]::UtcNow -lt $deadline)
            SaveJson 'cleanup.json' @{frontendExited=$resident.HasExited; sampledSurvivors=$survivors; checkedUtc=[DateTime]::UtcNow.ToString('O')}
            if ($survivors.Count) { throw 'Sampled owned descendants survived normal frontend shutdown; left for inspection.' }
        } catch { $shutdownFailure = $_.Exception.Message }
    }
    if ($null -ne $resident) { $resident.Dispose() }
    $hidden = @($phases | Where-Object { $_.name -like '*-warmed-hidden' })
    SaveJson 'result.json' ([ordered]@{schemaVersion=1; completed=($null -eq $failure -and $null -eq $shutdownFailure);
        error=$failure; shutdownError=$shutdownFailure; phases=$phases.ToArray(); events=$events.ToArray();
        warmedHiddenMedianDeltaBytes=$(if ($hidden.Count -gt 1) { $hidden[-1].medianPrivateBytes-$hidden[0].medianPrivateBytes } else { $null });
        warmedHiddenLastHandleDelta=$(if ($hidden.Count -gt 1) { $hidden[-1].lastHandles-$hidden[0].lastHandles } else { $null });
        limits='Observation only. Cold and warmed samples are distinct. Finite-run changes are not proof of a leak or its absence. No GPU memory, frame presentation, lease-drain counters, continuous visibility/foreground tracing, or physical controller acceptance. Short-lived descendants can escape sampling. Visible replay may perform provider reads; no widget content action is invoked.'})
}
if ($failure -or $shutdownFailure) { throw "Warmed-hide observation incomplete. $failure $shutdownFailure Evidence: $output" }
"Completed $Cycles warmed-hide/reopen cycles. Evidence: $output"
