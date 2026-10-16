param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string]$WindowHandle,
    [Parameter(Mandatory)][string]$EvidenceDirectory
)
# Run against the foreground Saved builds page in a disposable profile with
# at least two builds. This changes focus only; no build actions are invoked.
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
    Focus 'Widget.pob.saved-builds.Item.0'
    Focus 'Widget.pob.library.open'
    Test-FocusMove left 'Widget.pob.saved-builds.Item.0'
    Test-FocusMove down 'Widget.pob.saved-builds.Item.1'
    Test-FocusMove right 'Widget.pob.library.open'
    Test-FocusMove down 'Widget.pob.library.rename'
    Test-FocusMove left 'Widget.pob.saved-builds.Item.1'
    Test-FocusMove up 'Widget.pob.saved-builds.Item.0'
    Test-FocusMove right 'Widget.pob.library.rename'
    @{passed=$true;checks=$checks.ToArray()}|ConvertTo-Json|Set-Content (Join-Path $output 'result.json')
    "Passed $($checks.Count) native library navigation checks."
}catch{
    @{passed=$false;checks=$checks.ToArray();error="$($_.Exception.Message)"}|ConvertTo-Json|Set-Content (Join-Path $output 'result.json')
    throw
}
