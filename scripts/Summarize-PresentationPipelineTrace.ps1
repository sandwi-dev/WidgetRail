[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Trace,
    [Parameter(Mandatory)][string]$TraceEventAssembly,
    [Parameter(Mandatory)][string]$Output
)
$ErrorActionPreference = 'Stop'
[void][Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $TraceEventAssembly).Path)
$records = [Collections.Generic.List[object]]::new()
$errors = [Collections.Generic.List[string]]::new()
$source = [Microsoft.Diagnostics.Tracing.EventPipeEventSource]::new((Resolve-Path -LiteralPath $Trace).Path)
$source.add_AllEvents([Action[Microsoft.Diagnostics.Tracing.TraceEvent]]{
    param($event)
    # TraceEvent may expose EventPipe events without their manifest names. The
    # provider GUID and versioned, numeric-only event layout are an explicit contract.
    if ($event.ProviderGuid -ne [Guid]'d32b0e30-924a-598f-9296-f318d749fa45') { return }
    if ([int]$event.ID -eq 0) { $errors.Add($event.ToString()); return }
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
if (!$records.Count) { throw 'No presentation pipeline events were captured. Enable WidgetRail-PresentationPipeline:0xffffffffffffffff:4 while reproducing the issue.' }
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
[pscustomobject]@{ summary = @($summary); events = $records.ToArray() } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Output -Encoding utf8NoBOM
$summary | Format-Table -AutoSize
