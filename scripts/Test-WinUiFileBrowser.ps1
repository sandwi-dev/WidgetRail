[CmdletBinding()]
param([Parameter(Mandatory)][string]$Frontend,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$exe=(Resolve-Path -LiteralPath $Frontend).Path
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a new output directory so an old report cannot satisfy this run.'}
$null=New-Item -ItemType Directory -Path $output
foreach($mode in @('controller','compact','mouse','keyboard')){
    $dir=Join-Path $output $mode
    $interactive=$mode -in 'mouse','keyboard'
    $option=if($interactive){'--file-picker-interaction='}else{'--file-picker-check='}
    $launch=@('--validate-dialog-input','--validation-platform-activation',($option+'"'+$dir+'"'))
    if($mode -eq 'compact'){$launch+='--file-picker-compact'}
    $p=Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -ArgumentList $launch -PassThru
    try{
        function Ui([string[]]$Words){
            $value=& winapp ui @Words -a $p.Id --json
            if($LASTEXITCODE){throw "File browser $mode check failed at $($Words[0]) $($Words[1]): $value"}
        }
        if($interactive){
            Ui @('wait-for','Host.Files.Entry.1','-t','10000')
            if($mode -eq 'mouse'){
                Ui @('click','Host.Files.Entry.1')
                Ui @('click','Host.Files.Entry.0','--double')
                Ui @('wait-for','Second.txt','-t','3000')
                Ui @('click','Host.Files.Entry.0')
                Ui @('click','Host.Files.Commit')
            } else {
                Ui @('focus','Host.Files.Entry.1')
                Ui @('send-keys','space','--via','send-input')
                Ui @('send-keys','ctrl+l','--via','send-input')
                Ui @('wait-for','Host.Files.Location','-t','3000')
                # Input the path into the same native edit control as a paste, without touching the user's clipboard.
                Ui @('set-value','Host.Files.Location',(Get-Content "$dir/source.txt" -Raw))
                Ui @('send-keys','enter','--via','send-input')
                Ui @('wait-for','Second.txt','-t','3000')
                Ui @('send-keys','ctrl+f','--via','send-input')
                Ui @('set-value','Host.Files.Search','Second')
                Ui @('wait-for','Second.txt','-t','3000')
                Ui @('focus','Host.Files.Entry.0')
                Ui @('send-keys','space','--via','send-input')
                Ui @('focus','Host.Files.Commit')
                Ui @('send-keys','enter','--via','send-input')
            }
        }
        $end=[DateTime]::UtcNow.AddSeconds(45)
        do{Start-Sleep -Milliseconds 200}while(!(Test-Path "$dir/result.json") -and !$p.HasExited -and [DateTime]::UtcNow -lt $end)
        $report=Get-Content "$dir/result.json" -Raw|ConvertFrom-Json
        if($interactive){
            if(($report|Sort-Object) -join ',' -ne 'First.txt,Second.txt'){throw "$mode returned the wrong selection."}
        }elseif(!$report.passed){throw $report.failure}
        Write-Output "$mode passed"
    }finally{
        if(!$p.HasExited){
            & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $p.Id
            if(!$p.WaitForExit(10000)){throw 'The isolated file-browser fixture did not close.'}
        }
    }
}
