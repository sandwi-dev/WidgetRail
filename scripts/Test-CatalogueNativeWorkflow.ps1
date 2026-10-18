[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleaseRoot,
    [Parameter(Mandatory)][string]$PackagesDirectory,
    [Parameter(Mandatory)][string]$FrontendDirectory,
    [Parameter(Mandatory)][string]$TestBridgeDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('Debug','Release')][string]$Configuration='Release'
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a new output directory; acceptance never resets an existing profile.'}
foreach($file in @((Join-Path $ReleaseRoot 'widget-catalog.json'),(Join-Path $FrontendDirectory 'OverlayFrontend.WinUI.exe'),
    (Join-Path $TestBridgeDirectory 'WidgetBridge.Tests.exe'))){if(!(Test-Path -LiteralPath $file)){throw "Missing $file"}}
New-Item -ItemType Directory -Path "$output/installation/runtime/Bridge","$output/profile" -Force|Out-Null
Copy-Item -Path "$TestBridgeDirectory/*" -Destination "$output/installation/runtime/Bridge" -Recurse
Copy-Item -LiteralPath "$TestBridgeDirectory/WidgetBridge.Tests.exe" -Destination "$output/installation/runtime/Bridge/WidgetBridge.exe"
'{}'|Set-Content "$output/installation/widget-catalog.json"
$controlPath="$output/control.json"
$requests="$output/requests.jsonl"
@{ReleaseRoot=[IO.Path]::GetFullPath($ReleaseRoot);ProfileRoot="$output/profile";PackagesDirectory=[IO.Path]::GetFullPath($PackagesDirectory);
  ControlPath=$controlPath;RequestLog=$requests}|ConvertTo-Json|Set-Content "$output/installation/runtime/Bridge/catalogue-native-fixture.json"
@{InitialWidgetId='marketplace';SettingsRoot="$output/profile";InstalledCatalogRoot="$output/profile/widgets";
  InstallationRoot="$output/installation";SwitchDiagnosticsPath="$output/switches.log"}|ConvertTo-Json|Set-Content "$output/shell.json"
$script:appPid=0
$checks=[Collections.Generic.List[string]]::new()
$pm='widgetrail.firstparty.performance-monitor';$audio='widgetrail.firstparty.audio-mixer'
$pmCard='Widget.marketplace.item.4D7DBBCA83C1962D22DB4A1FABA180913C184ABC26E59C5D8A5BD78E62F5DD60'
$audioCard='Widget.marketplace.item.DFA1CD1F87AD5641B23B72BC8B2105085E86D9329C0B29A5239FD0E6B2E8C5D1'
function Control([string]$Version='1.0.0',[string]$FailPackage='', [int]$Delay=0,[int]$Advance=0){
    # Atomic replacement prevents the fixture transport observing a partial JSON write.
    @{Version=$Version;FailPackage=$FailPackage;DownloadDelayMs=$Delay;AdvanceSeconds=$Advance}|ConvertTo-Json|Set-Content "$controlPath.tmp"
    Move-Item -LiteralPath "$controlPath.tmp" -Destination $controlPath -Force
}
function Command([string[]]$Arguments){
    $info=[Diagnostics.ProcessStartInfo]::new((Get-Command winapp).Source)
    $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    foreach($argument in $Arguments){$info.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::Start($info);$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
    try{
        if(!$process.WaitForExit(25000)){$process.Kill()}
        $raw=$stdout.GetAwaiter().GetResult();$value=$raw|ConvertFrom-Json
        if($value.error){throw ($value.error|ConvertTo-Json -Compress)}
        if(!$value){throw "WinApp returned no result: $($stderr.GetAwaiter().GetResult())"}
        return $value
    }finally{$process.Dispose()}
}
function Ui([string[]]$Arguments){Command (@('ui')+$Arguments+@('-a',"$script:appPid",'--json'))}
function Launch {
    Command @('run',"$repo/src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj",'--no-build','--detach','--json','--arch','x64',
        '-c',$Configuration,"-p:OutDir=$([IO.Path]::GetFullPath($FrontendDirectory))/",'--args',"--shell-no-controller --shell-config=`"$output/shell.json`"")
}
function Ready([string]$Id,[string]$Name=''){
    $deadline=[DateTime]::UtcNow.AddSeconds(25);$last='';$stable=0
    do{
        $result=Ui @('wait-for',$Id,'-t','1500')
        if($result.found){
            $e=$result.element;$signature="$($e.x),$($e.y),$($e.width),$($e.height),$($e.name)"
            if(!$e.isOffscreen -and $e.width -gt 0 -and $e.height -gt 0 -and (!$Name -or $e.name -eq $Name) -and $signature -ceq $last){$stable++}else{$stable=0}
            if($stable -ge 2){return $e};$last=$signature
        }
        Start-Sleep -Milliseconds 150
    }while([DateTime]::UtcNow -lt $deadline)
    throw "UI did not settle: $Id expected '$Name', last '$last'"
}
function Invoke([string]$Id){$null=Ready $Id;$null=Ui @('invoke',$Id)}
function Check([bool]$Condition,[string]$Message){if(!$Condition){throw $Message};$checks.Add($Message);Write-Host "PASS $Message"}
function Catalog {
    $path="$output/profile/widgets/catalog-state.json"
    if(Test-Path -LiteralPath $path){return Get-Content -LiteralPath $path -Raw|ConvertFrom-Json}
    return @{widgets=@()}
}
function Installed([string]$Id,[string]$Version,[bool]$Enabled=$true){
    $deadline=[DateTime]::UtcNow.AddSeconds(25)
    do{
        $entry=@((Catalog).widgets|Where-Object id -eq $Id)
        if($entry.Count -eq 1 -and $entry[0].activeVersion -eq $Version -and $entry[0].enabled -eq $Enabled){return $entry[0]}
        Start-Sleep -Milliseconds 200
    }while([DateTime]::UtcNow -lt $deadline)
    throw "Installed state mismatch for $Id ($Version enabled=$Enabled)"
}
function Focus([string]$Id){$f=Ui @('get-focused');Check ($f.hasFocus -and $f.element.automationId -eq $Id) "Focus stays on $Id"}
function Pixels([string]$Id,[string]$Name){
    $null=Ready $Id
    $capture=Ui @('screenshot',$Id,'-o',"$output/$Name.png")
    Add-Type -AssemblyName System.Drawing
    $bitmap=[Drawing.Bitmap]::FromFile($capture.filePath)
    try{
        $colors=[Collections.Generic.HashSet[int]]::new();$opaque=0
        for($y=0;$y -lt $bitmap.Height;$y+=2){for($x=0;$x -lt $bitmap.Width;$x+=2){
            $pixel=$bitmap.GetPixel($x,$y);if($pixel.A -gt 0){$opaque++;$null=$colors.Add($pixel.ToArgb())}
        }}
        Check ($opaque -gt 100 -and $colors.Count -gt 8) "$Name contains rendered pixels, not only UIA bounds"
    }finally{$bitmap.Dispose()}
}
function Reopen([string]$Id,[string]$Name,[int]$HiddenMilliseconds=500){
    $tree=Ui @('inspect','--interactive');$close=@($tree.windows.elements|Where-Object name -like 'Guide button*')
    if($close.Count -ne 1){throw 'Expected one semantic Guide Close action'}
    Invoke $close[0].automationId
    Start-Sleep -Milliseconds $HiddenMilliseconds
    $null=Launch
    # Activation must reuse the original process; this also exercises the real election/message path.
    Check (!$((Get-Process -Id $script:appPid).HasExited)) 'Second launch keeps the elected frontend'
    Pixels $Id $Name
}
Control
try{
    $script:appPid=(Launch).ProcessId
    Pixels 'Widget.marketplace.tab.browse' 'initial'
    Invoke $pmCard;Invoke 'Widget.marketplace.primary';$null=Ready 'Widget.marketplace.primary' 'Review installation';Invoke 'Widget.marketplace.primary'
    $null=Ready 'Widget.marketplace.review.cancel';Focus 'Widget.marketplace.review.cancel';Invoke 'Widget.marketplace.review.cancel'
    Check (@((Catalog).widgets).Count -eq 0) 'Review Cancel installs nothing'
    Invoke 'Widget.marketplace.primary';Invoke 'Widget.marketplace.review.install';$null=Installed $pm '1.0.0'
    $null=Ready 'Widget.marketplace.primary' 'Manage in Settings';Invoke 'Widget.marketplace.primary'
    Invoke 'Widget.installed.details.toggle';$null=Installed $pm '1.0.0' $false;Focus 'Widget.installed.details.toggle'
    Invoke 'Overlay.Widget.marketplace';Invoke 'Widget.marketplace.back';Invoke $audioCard
    Control -Delay 10000
    Invoke 'Widget.marketplace.primary';Focus 'Widget.marketplace.primary'
    Reopen 'Widget.marketplace.primary' 'download-reopen'
    $null=Ready 'Widget.marketplace.primary' 'Review installation'
    $audioDownloads=@(Get-Content $requests|ConvertFrom-Json|Where-Object path -like "*/$audio-1.0.0.wrwidget")
    Check ($audioDownloads.Count -eq 1) 'Hide/reopen does not duplicate the pending download'
    Invoke 'Widget.marketplace.primary';Invoke 'Widget.marketplace.review.install';$null=Installed $audio '1.0.0'
    Invoke "Overlay.Widget.$audio";Invoke 'Widget.audio.retry';Invoke 'Host.Choice.Secondary'
    $null=Ready 'Widget.audio.retry';Invoke 'Widget.audio.retry';Invoke 'Host.Choice.Primary'
    $null=Ready 'Widget.audio.master.volume.slider';$checks.Add('Permission denial remains retryable; approval opens the simulated mixer')
    Invoke 'Overlay.Widget.marketplace';Invoke 'Widget.marketplace.back'
    Control -Version '2.0.0' -FailPackage $audio -Advance 1200
    Invoke 'Widget.marketplace.refresh';Invoke 'Widget.marketplace.tab.updates';Invoke 'Widget.marketplace.update-all'
    Invoke 'Widget.marketplace.batch.review.open';Invoke 'Widget.marketplace.batch.review.cancel'
    $null=Installed $pm '1.0.0' $false;$null=Installed $audio '1.0.0';$checks.Add('Batch Cancel changes neither installed version')
    Invoke 'Widget.marketplace.batch.review.open';Pixels 'Widget.marketplace.batch.review.cancel' 'partial-review'
    Invoke 'Widget.marketplace.batch.review.install';$null=Installed $pm '2.0.0' $false;$null=Installed $audio '1.0.0'
    Pixels 'Widget.marketplace.retry-updates' 'partial-failure'
    $null=Ui @('screenshot','Widget.marketplace.page','-o',"$output/partial-update-page.png")
    Control -Version '2.0.0' -Advance 1200
    Invoke 'Widget.marketplace.retry-updates';Invoke 'Widget.marketplace.batch.review.open';Invoke 'Widget.marketplace.batch.review.install'
    $null=Installed $audio '2.0.0';$checks.Add('Retry completes the failed package and preserves the disabled successful package')
    Invoke 'Widget.marketplace.tab.installed';Invoke $pmCard;Invoke 'Widget.marketplace.primary'
    # The technical link is below the fold in the compact Settings details view.
    # Exercise real scrolling before activation; UIA focus alone need not reveal
    # a clipped button and invoking it offscreen would conceal that workflow step.
    $null=Ui @('scroll','Widget.installed.details','--to','bottom')
    Invoke 'Widget.installed.details.technical';Invoke 'Widget.installed.details.revert'
    $null=Installed $pm '1.0.0' $false
    Invoke 'Overlay.Widget.marketplace';$null=Ready 'Widget.marketplace.primary' 'Download update for review'
    Invoke 'Widget.marketplace.primary';$null=Ready 'Widget.marketplace.primary' 'Review installation';Invoke 'Widget.marketplace.primary';Invoke 'Widget.marketplace.review.install'
    $entry=Installed $pm '2.0.0' $false
    $versions=@(Get-ChildItem -LiteralPath "$output/profile/widgets/packages/$pm" -Directory)
    Check ($versions.Count -eq 2) 'Ordinary re-update selects the retained version without duplicate history and keeps disabled state'
    foreach($i in 1..3){Reopen 'Widget.marketplace.primary' "completed-reopen-$i"}
    Reopen 'Overlay.Widget.marketplace' 'idle-reopen' 125000
    (Catalog)|ConvertTo-Json -Depth 8|Set-Content "$output/final-catalog.json"
    @{passed=$true;checks=@($checks);scope='Native isolated real bridge/core workers/package admission; simulated HTTP/clock/OS audio; no external requests'}|ConvertTo-Json -Depth 5|Set-Content "$output/result.json"
    "PASS: $($checks.Count) native Catalogue workflow checks"
}catch{
    @{passed=$false;checks=@($checks);error=$_.Exception.ToString();appPid=$script:appPid}|ConvertTo-Json -Depth 5|Set-Content "$output/result.json"
    throw
}finally{
    if($script:appPid -and ($process=Get-Process -Id $script:appPid -ErrorAction SilentlyContinue)){
        $null=$process.CloseMainWindow()
        if(!$process.WaitForExit(15000)){Write-Warning "Fixture PID $script:appPid did not finish cleanup; it was not force killed."}
    }
}
