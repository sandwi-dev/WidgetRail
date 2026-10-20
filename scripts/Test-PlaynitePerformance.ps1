[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CandidateRoot,
    [Parameter(Mandatory)][string]$FrontendExecutable,
    [Parameter(Mandatory)][string]$Workspace,
    [Parameter(Mandatory)][string]$RunName,
    [ValidateRange(1,20000)][int]$GameCount=2000,
    [switch]$WithoutArtwork,
    [switch]$HeapSnapshot,
    [switch]$NavigationChecks,
    [switch]$Prepare
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$candidate=(Resolve-Path -LiteralPath $CandidateRoot).Path
$frontend=(Resolve-Path -LiteralPath $FrontendExecutable).Path
$workspaceRoot=[IO.Path]::GetFullPath($Workspace)
$cli=Join-Path $candidate 'supplemental/tools/wrail/wrail.dll'
$bridge=Join-Path $candidate 'build/runtime/Bridge/WidgetBridge.exe'
$catalog=Join-Path $workspaceRoot 'catalog'
$fixture=Join-Path $workspaceRoot 'fixture'
$output=Join-Path $workspaceRoot $RunName
if(Test-Path -LiteralPath $output){throw 'Use a fresh run name to preserve comparison evidence.'}
if($Prepare){
    if(Test-Path -LiteralPath $workspaceRoot){throw 'Preparation requires a new isolated workspace.'}
    New-Item -ItemType Directory -Path $workspaceRoot,$fixture,(Join-Path $fixture 'artwork')|Out-Null
    Set-Content -LiteralPath (Join-Path $fixture 'fixture-owned') -Value 'Performance fixture only'
    $stage=Join-Path $workspaceRoot 'stage'
    New-Item -ItemType Directory -Path $stage|Out-Null
    dotnet publish (Join-Path $repository 'tests/PlayniteLibraryApplicationFixture/PlayniteLibraryApplicationFixture.csproj') `
        -c Release -r win-x64 --no-self-contained -o "$stage/payload" "-bl:$workspaceRoot/fixture-{}.binlog" -v:q
    if($LASTEXITCODE -ne 0){throw 'Fixture build failed.'}
    Copy-Item (Join-Path $repository 'samples/PlayniteLibraryWidget/styles') "$stage/styles" -Recurse
    Copy-Item (Join-Path $repository 'samples/PlayniteLibraryWidget/assets') "$stage/assets" -Recurse
    $manifest=Get-Content (Join-Path $repository 'samples/PlayniteLibraryWidget/manifest.json') -Raw|ConvertFrom-Json
    $manifest.entrypoint.executable='payload/PlayniteLibraryApplicationFixture.exe'
    $manifest|ConvertTo-Json -Depth 40|Set-Content "$stage/manifest.json" -Encoding utf8
    dotnet $cli pack $stage --output "$workspaceRoot/playnite-fixture.wrwidget"
    if($LASTEXITCODE -ne 0){throw 'Fixture pack failed.'}
    dotnet $cli install "$workspaceRoot/playnite-fixture.wrwidget" --catalog $catalog --accept-full-trust
    if($LASTEXITCODE -ne 0){throw 'Fixture install failed.'}
    dotnet $cli enable $manifest.id --catalog $catalog --accept-full-trust
    if($LASTEXITCODE -ne 0){throw 'Fixture enable failed.'}
    # Deterministic locally-generated artwork. Unique handles across all games
    # exercise real caches; a small set of files bounds fixture disk usage.
    Add-Type -AssemblyName System.Drawing
    $random=[Random]::new(1729)
    foreach($kind in @('cover','background')){
        $width=if($kind -eq 'cover'){1000}else{1920}
        $height=if($kind -eq 'cover'){1500}else{1080}
        for($imageIndex=0;$imageIndex -lt 8;$imageIndex++){
            $bitmap=[Drawing.Bitmap]::new($width,$height)
            $graphics=[Drawing.Graphics]::FromImage($bitmap)
            try {
                for($y=0;$y -lt $height;$y+=8){
                    for($x=0;$x -lt $width;$x+=8){
                        $brush=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb($random.Next(256),$random.Next(256),$random.Next(256)))
                        try{$graphics.FillRectangle($brush,$x,$y,8,8)}finally{$brush.Dispose()}
                    }
                }
                $bitmap.Save((Join-Path $fixture "artwork/$kind-$imageIndex.jpg"),[Drawing.Imaging.ImageFormat]::Jpeg)
            } finally {$graphics.Dispose();$bitmap.Dispose()}
        }
    }
}
if(!(Test-Path -LiteralPath "$fixture/fixture-owned")){throw 'Missing isolated fixture ownership marker.'}
New-Item -ItemType Directory -Path $output|Out-Null
@{GameCount=$GameCount;Artwork=!$WithoutArtwork;DelayMilliseconds=15}|ConvertTo-Json|Set-Content "$fixture/performance.json"
$pipe='playnite-perf-'+[guid]::NewGuid().ToString('N')
$oldFixture=$env:WRAIL_PLAYNITE_PROCESS_FIXTURE_ROOT
$bridgeProcess=$null
$frontendProcess=$null
$measurements=[Collections.Generic.List[object]]::new()
try {
    $env:WRAIL_PLAYNITE_PROCESS_FIXTURE_ROOT=$fixture
    $bridgeProcess=Start-Process $bridge -ArgumentList @('--host-pipe',$pipe,'--catalog',"`"$candidate/build/widget-catalog.json`"",
        '--installed-catalog-root',"`"$catalog`"",'--settings-root',"`"$workspaceRoot/profile`"",'--accept-timeout-ms','60000',
        '--startup-widget','widgetrail.samples.playnite-library') -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput "$output/bridge.log" -RedirectStandardError "$output/bridge-error.log"
    $frontendArguments=@("--indexed-validation-pipe=$pipe","--playnite-performance-output=`"$output`"")
    if($HeapSnapshot){$frontendArguments+='--playnite-performance-heap-pause'}
    if($NavigationChecks){$frontendArguments+='--playnite-performance-navigation'}
    $frontendProcess=Start-Process $frontend -ArgumentList $frontendArguments `
        -WindowStyle Hidden -PassThru
    $env:WRAIL_PLAYNITE_PROCESS_FIXTURE_ROOT=$oldFixture
    @{SourceCommit=(& git -C $repository rev-parse HEAD);GameCount=$GameCount;Artwork=!$WithoutArtwork;
      Frontend=$frontend;Bridge=$bridge;FrontendPid=$frontendProcess.Id;BridgePid=$bridgeProcess.Id;
      FrontendAssemblySha256=(Get-FileHash (Join-Path (Split-Path $frontend) 'OverlayFrontend.WinUI.dll')).Hash;
      ContractsSha256=(Get-FileHash (Join-Path (Split-Path $frontend) 'WidgetBridge.Contracts.dll')).Hash;
      FixturePackageSha256=(Get-FileHash "$workspaceRoot/playnite-fixture.wrwidget").Hash;
      ArtworkBytes=(Get-ChildItem "$fixture/artwork" -File|Measure-Object Length -Sum).Sum}|ConvertTo-Json|Set-Content "$output/provenance.json"
    $timer=[Diagnostics.Stopwatch]::StartNew()
    while(!(Test-Path "$output/result.json") -and $timer.Elapsed.TotalSeconds -lt 150){
        if($frontendProcess.HasExited){throw 'Native performance fixture exited before reporting.'}
        $processes=Get-CimInstance Win32_Process
        $owned=[Collections.Generic.HashSet[int]]::new()
        [void]$owned.Add($frontendProcess.Id);[void]$owned.Add($bridgeProcess.Id)
        foreach($pass in 1..3){foreach($process in $processes){if($owned.Contains([int]$process.ParentProcessId)){[void]$owned.Add([int]$process.ProcessId)}}}
        $phase=if(Test-Path "$output/progress.json"){
            try{(Get-Content "$output/progress.json" -Raw|ConvertFrom-Json).phase}catch{'transition'}
        }else{'startup'}
        if($HeapSnapshot -and $phase -eq 'heap-diagnostic' -and !(Test-Path "$output/heap-complete")){
            & (Join-Path $repository 'artifacts/performance/tools/dotnet-gcdump.exe') collect -p $frontendProcess.Id `
                -o "$output/frontend.gcdump" -t 25 > "$output/gcdump.log" 2>&1
            & (Join-Path $repository 'artifacts/performance/tools/dotnet-dump.exe') collect -p $frontendProcess.Id `
                --type Heap -o "$output/frontend.dmp" > "$output/dump.log" 2>&1
            Set-Content "$output/heap-complete" -Value "gcdump exit $LASTEXITCODE"
        }
        foreach($process in (Get-CimInstance Win32_PerfFormattedData_PerfProc_Process|Where-Object {$owned.Contains([int]$_.IDProcess)})){
            $measurements.Add([pscustomobject]@{Seconds=$timer.Elapsed.TotalSeconds;Phase=$phase;Pid=$process.IDProcess;Name=$process.Name;
                PrivateWorkingSet=$process.WorkingSetPrivate;WorkingSet=$process.WorkingSet;PrivateBytes=$process.PrivateBytes;
                Cpu=$process.PercentProcessorTime;Handles=$process.HandleCount;Threads=$process.ThreadCount})
        }
        Start-Sleep -Milliseconds 750
    }
    if(!(Test-Path "$output/result.json")){throw 'Performance fixture timed out.'}
    $result=Get-Content "$output/result.json" -Raw|ConvertFrom-Json
    if(!$result.passed){throw $result.error}
    Write-Output "Completed performance run: $output"
} finally {
    $env:WRAIL_PLAYNITE_PROCESS_FIXTURE_ROOT=$oldFixture
    $measurements|Export-Csv "$output/process-memory.csv" -NoTypeInformation
    if($frontendProcess -and !$frontendProcess.HasExited){& (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $frontendProcess.Id}
    if($bridgeProcess -and !$bridgeProcess.HasExited){
        if(!$bridgeProcess.WaitForExit(5000)){Stop-Process -Id $bridgeProcess.Id}
    }
}
