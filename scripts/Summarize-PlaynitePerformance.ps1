[CmdletBinding()]
param([Parameter(Mandatory)][string]$RunDirectory)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $RunDirectory).Path
$result=Get-Content "$root/result.json" -Raw|ConvertFrom-Json
$provenance=Get-Content "$root/provenance.json" -Raw|ConvertFrom-Json
function Distribution($Values){
    $sorted=@($Values|Sort-Object)
    if(!$sorted.Count){return $null}
    return [pscustomobject]@{Count=$sorted.Count;MedianMs=$sorted[[int]($sorted.Count*.5)];
        P95Ms=$sorted[[int]($sorted.Count*.95)];P99Ms=$sorted[[int]($sorted.Count*.99)];
        MaximumMs=$sorted[-1];Over50Ms=@($sorted|Where-Object{$_ -gt 50}).Count;
        Over50Percent=[math]::Round(100*@($sorted|Where-Object{$_ -gt 50}).Count/$sorted.Count,1)}
}
$passes=@(Get-ChildItem $root -Filter 'scroll-*.json'|Sort-Object Name|ForEach-Object{
    $data=Get-Content $_.FullName -Raw|ConvertFrom-Json
    [pscustomobject]@{Phase=$_.BaseName;CompositionCallbackGaps=(Distribution $data.frames);DispatcherTicks=(Distribution $data.ticks)}
})
$processes=@(Import-Csv "$root/process-memory.csv"|Group-Object Pid|ForEach-Object{
    $rows=$_.Group
    [pscustomobject]@{Pid=[int]$_.Name;Name=$rows[0].Name;
        PeakPrivateWorkingSetMiB=[math]::Round((($rows.PrivateWorkingSet|ForEach-Object{[long]$_}|Measure-Object -Maximum).Maximum)/1MB,1);
        PeakPrivateBytesMiB=[math]::Round((($rows.PrivateBytes|ForEach-Object{[long]$_}|Measure-Object -Maximum).Maximum)/1MB,1);
        PeakHandles=($rows.Handles|ForEach-Object{[long]$_}|Measure-Object -Maximum).Maximum}
})
$milestones=@($result.samples|Where-Object{$_.phase -eq 'ready' -or $_.phase -match 'end|settled|diagnostic|background|disposed'}|ForEach-Object{
    [pscustomobject]@{Phase=$_.phase;PrivateBytesMiB=[math]::Round($_.privateBytes/1MB,1);
        ManagedMiB=[math]::Round($_.managedBytes/1MB,1);AllocatedMiB=[math]::Round($_.allocated/1MB,1);
        Realized=$_.realized;Images=$_.images;Gen2=$_.gen2}
})
$scrollSamples=@($result.samples|Where-Object{$_.phase -match '^scroll-[0-2]$'})
$visible=($scrollSamples.visibleTiles|Measure-Object -Sum).Sum
$withArtwork=($scrollSamples.visibleTilesWithArtwork|Measure-Object -Sum).Sum
$coverage=if($visible -gt 0){[math]::Round(100*$withArtwork/$visible,1)}else{$null}
$summary=[pscustomobject]@{Passed=$result.passed;Error=$result.error;Provenance=$provenance;Processes=$processes;
    Milestones=$milestones;Passes=$passes;SampledVisibleArtworkPercent=$coverage}
$summary|ConvertTo-Json -Depth 12|Set-Content "$root/summary.json"
$processes|Format-Table
$milestones|Format-Table
$passes|ForEach-Object{[pscustomobject]@{Pass=$_.Phase;CallbackP95Ms=$_.CompositionCallbackGaps.P95Ms;
    CallbackP99Ms=$_.CompositionCallbackGaps.P99Ms;CallbackOver50=$_.CompositionCallbackGaps.Over50Ms;
    CallbackOver50Percent=$_.CompositionCallbackGaps.Over50Percent;
    TickP95Ms=$_.DispatcherTicks.P95Ms}}|Format-Table
Write-Output "Sampled visible tiles with artwork during scrolling: $coverage%"
