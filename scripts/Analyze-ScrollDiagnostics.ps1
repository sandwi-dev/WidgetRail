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
    }
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
    RenderStages = $stages
    MostRecreatedAssets = $assets
    RefreshCallers = @($records | Where-Object { $_['event'] -eq 'refresh-state' } |
        Group-Object { $_['caller-line'] + ':' + $_['deferred'] } | ForEach-Object {
            [ordered]@{ CallerLineAndDeferred = $_.Name; Transitions = $_.Count }
        })
    Notes = @(
        'Timings are nested: do not add layout/text, draw/image/upload, or log totals together.'
        'Counts cover retained trace records only. Inspect dropped and failures before interpreting them.'
        'Asset/variant values are opaque process-local correlation identifiers, not game IDs.'
        'Geometry records are sampled; correlate sequence and scroll offset before interpreting movement.'
    )
} | ConvertTo-Json -Depth 8
