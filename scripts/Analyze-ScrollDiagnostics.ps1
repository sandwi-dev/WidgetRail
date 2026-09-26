[CmdletBinding()]
param([Parameter(Mandatory)][string]$Path)

$ErrorActionPreference = 'Stop'
$lines = @(Get-Content -LiteralPath $Path)
if ($lines.Count -eq 0 -or -not $lines[0].StartsWith('scroll-trace-v1 ')) {
    throw 'Expected a WidgetRail scroll-trace-v1 capture.'
}
function ConvertFrom-TraceLine([string]$Line) {
    $record = @{}
    foreach ($match in [regex]::Matches($Line, '([a-z][a-z0-9-]*)=([^ ]+)')) {
        $record[$match.Groups[1].Value] = $match.Groups[2].Value
    }
    return $record
}
function Get-TimingSummary($Records, [string]$Field) {
    $values = @($Records | Where-Object { $_.ContainsKey($Field) } | ForEach-Object {
        [double]::Parse($_[$Field], [System.Globalization.CultureInfo]::InvariantCulture) / 1000
    } | Sort-Object)
    if ($values.Count -eq 0) { return $null }
    return [ordered]@{
        Samples = $values.Count
        MedianMs = $values[[int][Math]::Floor(($values.Count - 1) * .5)]
        P95Ms = $values[[int][Math]::Ceiling(($values.Count - 1) * .95)]
        MaximumMs = $values[-1]
        Over16_67Ms = @($values | Where-Object { $_ -gt (1000.0 / 60) }).Count
        Over33_33Ms = @($values | Where-Object { $_ -gt (1000.0 / 30) }).Count
    }
}
function Get-CounterSummary($Records, [string]$Field) {
    $values = @($Records | Where-Object { $_.ContainsKey($Field) } | ForEach-Object {
        [double]::Parse($_[$Field], [System.Globalization.CultureInfo]::InvariantCulture)
    })
    if ($values.Count -eq 0) { return $null }
    $summary = $values | Measure-Object -Sum -Maximum
    return [ordered]@{ Samples = $values.Count; Total = $summary.Sum; Maximum = $summary.Maximum }
}
$records = @($lines | Select-Object -Skip 1 | ForEach-Object { ConvertFrom-TraceLine $_ })
$frames = @($records | Where-Object { $_['event'] -eq 'frame-commit' })
$renders = @($records | Where-Object { $_['event'] -eq 'render' })
$creates = @($records | Where-Object { $_['event'] -eq 'bitmap-create' })
$stages = [ordered]@{}
foreach ($field in @('total-us','prepare-us','layout-us','text-us','style-us','present-us','draw-us','image-us','upload-us')) {
    $stages[$field] = Get-TimingSummary $renders $field
}
$assets = @($creates | Group-Object { $_['asset'] } | Sort-Object Count -Descending | Select-Object -First 12 | ForEach-Object {
    [ordered]@{
        Asset = $_.Name
        BitmapCreates = $_.Count
        DistinctVariants = @($_.Group | ForEach-Object { $_['variant'] } | Sort-Object -Unique).Count
        RequestedSizes = @($_.Group | ForEach-Object { $_['requested'] } | Sort-Object -Unique)
    }
})
[ordered]@{
    Capture = ConvertFrom-TraceLine $lines[0]
    Events = @($records | Group-Object { $_['event'] } | Sort-Object Count -Descending | ForEach-Object {
        [ordered]@{ Event = $_.Name; Count = $_.Count }
    })
    FrameDrawing = Get-TimingSummary $frames 'draw-us'
    FrameCommit = Get-TimingSummary $frames 'commit-us'
    FrameCpu = Get-TimingSummary $frames 'cpu-us'
    FrameSubmission = Get-TimingSummary $frames 'submit-us'
    PaintRetention = [ordered]@{
        Hits = Get-CounterSummary $renders 'paint-hits'
        Misses = Get-CounterSummary $renders 'paint-misses'
        PaintedBytes = Get-CounterSummary $renders 'painted-bytes'
    }
    GpuTransfers = [ordered]@{
        Uploads = Get-CounterSummary $frames 'gpu-uploads'
        Reuses = Get-CounterSummary $frames 'gpu-reuses'
        UploadedBytes = Get-CounterSummary $frames 'gpu-uploaded-bytes'
        FocusAtlasReuses = Get-CounterSummary $frames 'focus-atlas-reuses'
    }
    RenderPaths = @($renders | Group-Object { $_['instance'] + ':' + $_['work'] + ':' + $_['free-scroll'] } | ForEach-Object {
        [ordered]@{
            InstanceWorkAndFreeScroll = $_.Name
            Timing = Get-TimingSummary $_.Group 'total-us'
            Preparation = Get-TimingSummary $_.Group 'prepare-us'
            Painting = Get-TimingSummary $_.Group 'draw-us'
            PaintHits = Get-CounterSummary $_.Group 'paint-hits'
            PaintMisses = Get-CounterSummary $_.Group 'paint-misses'
        }
    })
    RenderStages = $stages
    MostRecreatedAssets = $assets
    RefreshCallers = @($records | Where-Object { $_['event'] -eq 'refresh-state' } |
        Group-Object { $_['caller-line'] + ':' + $_['deferred'] } | ForEach-Object {
            [ordered]@{ CallerLineAndDeferred = $_.Name; Transitions = $_.Count }
        })
    Notes = @(
        'Timings are nested: do not add layout/text, draw/image/upload, or log totals together.'
        'Submission includes CPU upload and commit calls, not GPU execution or display latency. CPU budget counts are not dropped-frame counts.'
        'Counts cover retained trace records only. Inspect dropped and failures before interpreting them.'
        'Asset/variant values are opaque process-local correlation identifiers, not game IDs.'
        'Geometry records are sampled; correlate sequence and scroll offset before interpreting movement.'
    )
} | ConvertTo-Json -Depth 8
