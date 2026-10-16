param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string]$WindowHandle,
    [Parameter(Mandatory)][string]$EvidenceDirectory
)
# Run against Settings home launched with --shell-no-controller and
# --replay-shell-input in a disposable profile. Only focus/navigation changes.
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($EvidenceDirectory)
if(Test-Path -LiteralPath $output){throw 'Choose a fresh evidence directory.'}
New-Item -ItemType Directory -Path $output | Out-Null
$winapp=(Get-Command winapp -ErrorAction Stop).Source
$script:step=0
$checks=[Collections.Generic.List[string]]::new()
function Probe([string[]]$Arguments){
    $script:step++
    $stdout=Join-Path $output "$script:step.out"
    $stderr=Join-Path $output "$script:step.err"
    $probe=Start-Process $winapp -ArgumentList $Arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    try{
        if(!$probe.WaitForExit(12000)){
            # Some WinApp versions finish their UIA operation but do not exit.
            # Only retire this owned probe; inspect its saved result below.
            $probe.Kill();$probe.WaitForExit()
        }
        $errors=Get-Content -LiteralPath $stderr -Raw
        if($errors){throw $errors}
        Get-Content -LiteralPath $stdout -Raw
    }finally{$probe.Dispose()}
}
function Focus([string]$Id){
    $result=Probe @('ui','focus',$Id,'-a',"$AppPid")
    if($result -notmatch 'Focused '){throw $result}
}
function Test-FocusMove([string]$Key,[string]$Expected){
    $result=Probe @('ui','send-keys',$Key,'-w',$WindowHandle,'--via','send-input')
    if($result -notmatch 'Sent 1 key action'){throw $result}
    $deadline=[DateTime]::UtcNow.AddSeconds(3)
    do{
        $focused=Probe @('ui','get-focused','-a',"$AppPid",'--json') | ConvertFrom-Json
        if($focused.element.automationId -eq $Expected){$checks.Add("$Key -> $Expected");return}
        Start-Sleep -Milliseconds 50
    }while([DateTime]::UtcNow -lt $deadline)
    throw "Expected $Expected after $Key; got $($focused.element.automationId)."
}
try{
    $boundary='Widget.settings.restart'
    foreach($key in @('f2','f3')){
        Focus 'Widget.category.overlay'
        $result=Probe @('ui','send-keys',$key,'-w',$WindowHandle,'--via','send-input')
        if($result -notmatch 'Sent 1 key action'){throw $result}
        $focused=Probe @('ui','get-focused','-a',"$AppPid",'--json') | ConvertFrom-Json
        if($focused.element.automationId -notlike 'Widget.category.*' -or $focused.element.automationId -eq 'Widget.category.overlay'){throw "Controller replay did not navigate: $key"}
        $checks.Add("$key navigates inside the widget")
        Focus $boundary
        1..4|ForEach-Object{Test-FocusMove $key $boundary}
    }
    Focus $boundary
    1..4|ForEach-Object{Test-FocusMove down $boundary}
    $result=Probe @('ui','send-keys','f6','-w',$WindowHandle,'--via','send-input')
    if($result -notmatch 'Sent 1 key action'){throw $result}
    $deadline=[DateTime]::UtcNow.AddSeconds(5)
    do{
        $focused=Probe @('ui','get-focused','-a',"$AppPid",'--json') | ConvertFrom-Json
        if($focused.element.automationId -like 'Overlay.*'){break}
        Start-Sleep -Milliseconds 50
    }while([DateTime]::UtcNow -lt $deadline)
    if($focused.element.automationId -notlike 'Overlay.*'){throw 'Explicit B did not leave the widget'}
    $checks.Add('Explicit B still returns to the widget switcher')
    # B intentionally opens the radial chooser. Arrange the separate rail route
    # explicitly before testing Up; do not mistake radial navigation for rail entry.
    $result=Probe @('ui','send-keys','f4','-w',$WindowHandle,'--via','send-input')
    if($result -notmatch 'Sent 1 key action'){throw $result}
    $result=Probe @('ui','send-keys','up','-w',$WindowHandle,'--via','send-input')
    if($result -notmatch 'Sent 1 key action'){throw $result}
    $deadline=[DateTime]::UtcNow.AddSeconds(5)
    do{
        $focused=Probe @('ui','get-focused','-a',"$AppPid",'--json') | ConvertFrom-Json
        if($focused.element.automationId -like 'Widget.*'){break}
        Start-Sleep -Milliseconds 50
    }while([DateTime]::UtcNow -lt $deadline)
    if($focused.element.automationId -notlike 'Widget.*'){throw 'Up did not re-enter the selected widget'}
    $checks.Add('Up from the rail still enters the selected widget')
    @{passed=$true;checks=$checks.ToArray()}|ConvertTo-Json|Set-Content (Join-Path $output 'result.json')
    "Passed $($checks.Count) widget boundary checks."
}catch{
    @{passed=$false;checks=$checks.ToArray();error="$($_.Exception.Message)"}|ConvertTo-Json|Set-Content (Join-Path $output 'result.json')
    throw
}
