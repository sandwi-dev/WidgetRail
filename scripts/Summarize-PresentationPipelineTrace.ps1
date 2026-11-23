[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Trace,
    [Parameter(Mandatory)][string]$TraceEventAssembly,
    [Parameter(Mandatory)][string]$Output
)
$ErrorActionPreference = 'Stop'
[void][Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $TraceEventAssembly).Path)
$records = [Collections.Generic.List[object]]::new()
$scrollRecords = [Collections.Generic.List[object]]::new()
$errors = [Collections.Generic.List[string]]::new()
$source = [Microsoft.Diagnostics.Tracing.EventPipeEventSource]::new((Resolve-Path -LiteralPath $Trace).Path)
$source.add_AllEvents([Action[Microsoft.Diagnostics.Tracing.TraceEvent]]{
    param($event)
    # TraceEvent may expose EventPipe events without their manifest names. The
    # provider GUID and versioned, numeric-only event layout are an explicit contract.
    if ($event.ProviderGuid -ne [Guid]'d32b0e30-924a-598f-9296-f318d749fa45') { return }
    if ([int]$event.ID -eq 0) { $errors.Add($event.ToString()); return }
    if ([int]$event.ID -eq 2) {
        $bytes = $event.EventData()
        if ($event.Version -ne 0 -or $bytes.Length -ne 64) { throw 'Unsupported scroll event version/layout.' }
        $record = [pscustomobject]@{
            atMilliseconds = $event.TimeStampRelativeMSec
            processId = $event.ProcessID
            gestureId = [BitConverter]::ToInt64($bytes, 0)
            phase = [BitConverter]::ToInt32($bytes, 8)
            requestedX = [BitConverter]::ToDouble($bytes, 12)
            requestedY = [BitConverter]::ToDouble($bytes, 20)
            offsetX = [BitConverter]::ToDouble($bytes, 28)
            offsetY = [BitConverter]::ToDouble($bytes, 36)
            extentX = [BitConverter]::ToDouble($bytes, 44)
            extentY = [BitConverter]::ToDouble($bytes, 52)
            flags = [BitConverter]::ToInt32($bytes, 60)
        }
        if ($record.gestureId -le 0 -or $record.phase -notin @(1, 2) -or $record.flags -lt 0 -or $record.flags -gt 15) {
            throw 'Invalid scroll event identity/flags.'
        }
        foreach ($name in @('requestedX', 'requestedY', 'offsetX', 'offsetY', 'extentX', 'extentY')) {
            if (![double]::IsFinite($record.$name) -or $record.$name -lt 0) { throw 'Invalid scroll geometry.' }
        }
        $scrollRecords.Add($record)
        return
    }
    if ([int]$event.ID -ne 1) { return }
    $bytes = $event.EventData()
    if ($event.Version -ne 0 -or $bytes.Length -ne 28) { throw 'Unsupported presentation timing event version/layout.' }
    $record = [pscustomobject]@{
        atMilliseconds = $event.TimeStampRelativeMSec
        processId = $event.ProcessID
        operationId = [BitConverter]::ToInt64($bytes, 0)
        stage = [BitConverter]::ToInt32($bytes, 8)
        milliseconds = [BitConverter]::ToDouble($bytes, 12)
        count = [BitConverter]::ToInt32($bytes, 20)
        outcome = [BitConverter]::ToInt32($bytes, 24)
    }
    if ($record.operationId -le 0 -or $record.stage -lt 1 -or $record.stage -gt 12 -or
        $record.outcome -lt 0 -or $record.outcome -gt 3 -or $record.count -lt 0 -or
        ![double]::IsFinite($record.milliseconds) -or $record.milliseconds -lt 0) {
        throw 'Invalid presentation timing event values.'
    }
    $records.Add($record)
})
try { [void]$source.GetType().GetMethod('Process', [Type[]]@()).Invoke($source, @()) }
finally { $source.Dispose() }
if ($errors.Count) { throw ($errors -join [Environment]::NewLine) }
if (!$records.Count -and !$scrollRecords.Count) { throw 'No presentation pipeline or scroll events were captured. Enable WidgetRail-PresentationPipeline:0xffffffffffffffff:5 while reproducing the issue; an empty capture is inconclusive.' }
$names = @('unused', 'range-data', 'range-ui-queue', 'range-batch', 'range-total',
    'artwork-resolve', 'artwork-decode', 'artwork-publish', 'artwork-total', 'range-normal-probe', 'range-batch-queue',
    'indexed-row-apply', 'indexed-row-retire')
$summary = foreach ($group in ($records | Group-Object stage | Sort-Object { [int]$_.Name })) {
    $all = @($group.Group)
    $completed = @($all | Where-Object outcome -eq 0 | Sort-Object milliseconds)
    $stage = [int]$group.Name
    [pscustomobject]@{
        stage = $names[$stage]
        total = $all.Count
        completed = $completed.Count
        cancelled = @($all | Where-Object outcome -eq 1).Count
        failed = @($all | Where-Object outcome -eq 2).Count
        retired = @($all | Where-Object outcome -eq 3).Count
        medianMs = if ($completed.Count) { $completed[[int][Math]::Floor(($completed.Count - 1) / 2)].milliseconds } else { $null }
        p95Ms = if ($completed.Count) { $completed[[Math]::Max(0, [int][Math]::Ceiling($completed.Count * .95) - 1)].milliseconds } else { $null }
        maxMs = if ($completed.Count) { $completed[-1].milliseconds } else { $null }
    }
}
$scrollSummary = foreach ($group in ($scrollRecords | Group-Object processId, gestureId)) {
    $all = @($group.Group)
    $x = $all | Measure-Object offsetX -Minimum -Maximum
    $y = $all | Measure-Object offsetY -Minimum -Maximum
    [pscustomobject]@{
        processId = $all[0].processId
        gestureId = $all[0].gestureId
        requests = @($all | Where-Object phase -eq 1).Count
        nativeUpdates = @($all | Where-Object phase -eq 2).Count
        observedTravelX = $x.Maximum - $x.Minimum
        observedTravelY = $y.Maximum - $y.Minimum
    }
}
[pscustomobject]@{ summary = @($summary); events = $records.ToArray(); scrollSummary = @($scrollSummary); scrollEvents = $scrollRecords.ToArray() } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Output -Encoding utf8NoBOM
$summary | Format-Table -AutoSize
$scrollSummary | Format-Table -AutoSize
