$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixture=Join-Path $repo ('artifacts/discord-desktop/launch-tests-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$launcher=Join-Path $PSScriptRoot 'Start-DesktopProbe.ps1'
$checks=0
function Check([bool]$Condition,[string]$Message) { if (!$Condition) {throw $Message}; $script:checks++ }
function Reject([scriptblock]$Action,[string]$Message) { $failed=$false;try{& $Action|Out-Null}catch{$failed=$true};Check $failed $Message }
function Digest([string]$Path) { (Get-FileHash -LiteralPath $Path).Hash }
function WriteJson([string]$Path,$Value) { $Value|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $Path }
function NewFixture([string]$Name) {
    $root=Join-Path $fixture $Name; $version=Join-Path $root 'Discord/app-1.0.1'; $resources=Join-Path $version 'resources'
    $stage=Join-Path $root 'stage'; $journalDirectory=Join-Path $root 'journal'
    New-Item -ItemType Directory -Path $resources,(Join-Path $stage 'source/dist'),$journalDirectory -Force|Out-Null
    $exe=Join-Path $version 'Discord.exe'
    [IO.File]::WriteAllText($exe,'Non-executable synthetic fixture')
    [IO.File]::WriteAllText((Join-Path $resources 'app.asar'),'Synthetic installed bootstrap')
    [IO.File]::WriteAllText((Join-Path $resources '_app.asar'),'Synthetic original archive')
    Copy-Item -LiteralPath (Join-Path $resources 'app.asar') -Destination (Join-Path $stage 'bootstrap.asar')
    $artifacts=@('patcher.js','preload.js','renderer.js','renderer.css')|ForEach-Object{
        $path=Join-Path $stage ('source/dist/'+$_);[IO.File]::WriteAllText($path,'Synthetic runtime '+$_)
        [pscustomobject]@{path='source/dist/'+$_;sha256=(Digest $path);bytes=(Get-Item -LiteralPath $path).Length}
    }
    $provenance=[pscustomobject]@{schemaVersion=1;outputDirectory=$stage;buildCompleted=$true;bundleScopeVerified=$true;typecheckPassed=$true;
        artifacts=$artifacts;bootstrapProposal=[pscustomobject]@{archivePath='bootstrap.asar';archiveSha256=(Digest (Join-Path $stage 'bootstrap.asar'));dataDirectory=(Join-Path $stage 'vencord-data')}}
    WriteJson (Join-Path $stage 'provenance.json') $provenance
    $journal=[pscustomobject]@{schemaVersion=1;transactionId=[guid]::NewGuid().ToString('N');state='installed';executable=$exe;resourcesDirectory=$resources;
        executableSha256=(Digest $exe);bootstrapSha256=(Digest (Join-Path $resources 'app.asar'));originalAppAsarSha256=(Digest (Join-Path $resources '_app.asar'));
        provenancePath=(Join-Path $stage 'provenance.json');provenanceSha256=(Digest (Join-Path $stage 'provenance.json'))}
    $journalPath=Join-Path $journalDirectory 'journal.json'; WriteJson $journalPath $journal
    return [pscustomobject]@{root=$root;exe=$exe;resources=$resources;stage=$stage;journalPath=$journalPath;journal=$journal;provenance=$provenance;
        args=@{JournalDirectory=$journalDirectory;ExpectedJournalSha256=(Digest $journalPath)}}
}

# Replace the one creation expression in an artifact-only copy. The production
# command has no bypass flag/factory hook; no fixture invokes Process.Start.
$source=Get-Content -LiteralPath $launcher -Raw
$anchor='$started=[Diagnostics.Process]::Start($startInfo)'
Check ([regex]::Matches($source,[regex]::Escape($anchor)).Count -eq 1) 'Launch expression changed; review fixture interception.'
$repoAssignment='$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot ''../..''))'
$source=$source.Replace($repoAssignment,('$repo='''+$repo.Replace("'","''")+''''))
$intercepted=Join-Path $fixture 'intercepted-launch.ps1'
[IO.File]::WriteAllText($intercepted,$source.Replace($anchor,'$started=Invoke-GeneratedProbeStart $startInfo'))
$global:ProbeLaunchFixture=[pscustomobject]@{running=@();startCalls=0;lastStartInfo=$null;outcome='success';expectedImage='';nonFilteredCalls=0;startOnSecondRead=$false;disposed=0}
function global:Get-CimInstance {
    param($ClassName,$Filter)
    if ($Filter) {
        $image=if($global:ProbeLaunchFixture.outcome -eq 'different-image'){'C:\fixture\Update.exe'}else{$global:ProbeLaunchFixture.expectedImage}
        return [pscustomobject]@{Name='Discord.exe';ProcessId=8765;ExecutablePath=$image}
    }
    $global:ProbeLaunchFixture.nonFilteredCalls++
    if ($global:ProbeLaunchFixture.startOnSecondRead -and $global:ProbeLaunchFixture.nonFilteredCalls -eq 2) {
        return [pscustomobject]@{Name='Discord.exe';ProcessId=8764;ExecutablePath=$global:ProbeLaunchFixture.expectedImage}
    }
    return $global:ProbeLaunchFixture.running
}
function global:Invoke-GeneratedProbeStart {
    param($StartInfo)
    $global:ProbeLaunchFixture.startCalls++;$global:ProbeLaunchFixture.lastStartInfo=$StartInfo
    if($global:ProbeLaunchFixture.outcome -eq 'null-start'){return $null}
    $value=[pscustomobject]@{Id=8765;HasExited=($global:ProbeLaunchFixture.outcome -eq 'exited')}
    $value|Add-Member -MemberType ScriptMethod -Name Dispose -Value {$global:ProbeLaunchFixture.disposed++}
    return $value
}
function ResetFixture($Fixture) {
    $global:ProbeLaunchFixture.running=@();$global:ProbeLaunchFixture.startCalls=0;$global:ProbeLaunchFixture.lastStartInfo=$null
    $global:ProbeLaunchFixture.outcome='success';$global:ProbeLaunchFixture.expectedImage=$Fixture.exe
    $global:ProbeLaunchFixture.nonFilteredCalls=0;$global:ProbeLaunchFixture.startOnSecondRead=$false;$global:ProbeLaunchFixture.disposed=0
}
function LaunchFixture($Fixture,[bool]$Launch=$false) {
    $args=$Fixture.args
    & $intercepted @args -Launch:$Launch|ConvertFrom-Json
}
try {
    $f=NewFixture 'default';ResetFixture $f
    $args=$f.args;$plan=& $launcher @args|ConvertFrom-Json
    Check (!$plan.performed -and !$plan.launchRequested -and $plan.status -eq 'planned') 'Default invocation must be read-only.'
    Check ($global:ProbeLaunchFixture.startCalls -eq 0) 'Plan-only invocation started a process.'
    $launched=LaunchFixture $f $true
    $info=$global:ProbeLaunchFixture.lastStartInfo
    Check ($launched.performed -and $launched.status -eq 'launched-image-verified' -and $launched.verifiedProcessId -eq 8765) 'Exact image confirmation failed.'
    Check ($info.FileName -eq $f.exe -and $info.WorkingDirectory -eq (Split-Path $f.exe) -and $info.UseShellExecute) 'Launch must use the exact executable with ShellExecute, not an app id/updater.'
    Check (!$info.RedirectStandardOutput -and !$info.RedirectStandardError -and !$info.RedirectStandardInput) 'Discord must not inherit redirected tool streams.'
    Check ($info.ArgumentList.Count -eq 0 -and !$info.Arguments -and $info.WindowStyle -eq [Diagnostics.ProcessWindowStyle]::Hidden) 'Unexpected launch arguments or visible startup preference.'
    Check ($global:ProbeLaunchFixture.startCalls -eq 1 -and $global:ProbeLaunchFixture.disposed -eq 1) 'Process creation or handle disposal is not exactly once.'
    foreach($outcome in @('different-image','null-start','exited')) {
        $f=NewFixture $outcome;ResetFixture $f;$global:ProbeLaunchFixture.outcome=$outcome
        Reject {LaunchFixture $f $true} 'Unexpected launch outcome should fail without fallback.'
        Check ($global:ProbeLaunchFixture.startCalls -eq 1) 'A launch failure was retried.'
    }
    foreach($changed in @('exe','bootstrap','original','journal','provenance','runtime')) {
        $f=NewFixture ('changed-'+$changed);ResetFixture $f
        $path=switch($changed){exe{$f.exe};bootstrap{Join-Path $f.resources 'app.asar'};original{Join-Path $f.resources '_app.asar'};journal{$f.journalPath};provenance{Join-Path $f.stage 'provenance.json'};runtime{Join-Path $f.stage 'source/dist/renderer.js'}}
        [IO.File]::AppendAllText($path,' changed')
        Reject {LaunchFixture $f $true} 'Changed reviewed bytes were launched.'
        Check ($global:ProbeLaunchFixture.startCalls -eq 0) 'Validation failure still invoked launch.'
    }
    foreach($state in @('prepared','original-renamed','restored','rolled-back')) {
        $f=NewFixture ('state-'+$state);ResetFixture $f;$f.journal.state=$state;WriteJson $f.journalPath $f.journal;$f.args.ExpectedJournalSha256=Digest $f.journalPath
        Reject {LaunchFixture $f $true} 'Non-installed transaction state should not launch the probe.'
    }
    $f=NewFixture 'already-running';ResetFixture $f
    $global:ProbeLaunchFixture.running=@([pscustomobject]@{Name='Discord.exe';ExecutablePath=$f.exe;ProcessId=11})
    Check ((LaunchFixture $f $true).status -eq 'already-running' -and $global:ProbeLaunchFixture.startCalls -eq 0) 'Existing exact client must not be launched again.'
    $f=NewFixture 'other-version';ResetFixture $f
    $global:ProbeLaunchFixture.running=@([pscustomobject]@{Name='Discord.exe';ExecutablePath=(Join-Path $f.root 'Discord/app-1.0.2/Discord.exe');ProcessId=12})
    Reject {LaunchFixture $f $true} 'Another running version must not redirect the selected launch.'
    Check ($global:ProbeLaunchFixture.startCalls -eq 0) 'Other version launched a new process.'
    $f=NewFixture 'unknown-process';ResetFixture $f
    $global:ProbeLaunchFixture.running=@([pscustomobject]@{Name='Discord.exe';ExecutablePath=$null;ProcessId=13})
    Reject {LaunchFixture $f $true} 'Unknown process identity must fail closed.'
    $f=NewFixture 'process-race';ResetFixture $f;$global:ProbeLaunchFixture.startOnSecondRead=$true
    Reject {LaunchFixture $f $true} 'A client starting during preparation must cancel launch.'
    Check ($global:ProbeLaunchFixture.startCalls -eq 0) 'Process-state change still invoked launch.'
    $f=NewFixture 'relocated-stage';ResetFixture $f;$copied=Join-Path $f.root 'copied-stage'
    Copy-Item -LiteralPath $f.stage -Destination $copied -Recurse
    $f.journal.provenancePath=Join-Path $copied 'provenance.json';WriteJson $f.journalPath $f.journal;$f.args.ExpectedJournalSha256=Digest $f.journalPath
    Reject {LaunchFixture $f $true} 'Relocated stage with stale embedded paths was accepted.'
    $result=[pscustomobject]@{passed=$true;checks=$checks;fixture=$fixture;scope='Generated non-executable files and intercepted process creation only. No installed-client reads, launch, kill, restart or modification.'}
    WriteJson (Join-Path $fixture 'result.json') $result;$result|ConvertTo-Json
} finally {
    Remove-Item Function:\Get-CimInstance,Function:\Invoke-GeneratedProbeStart -ErrorAction SilentlyContinue
    Remove-Variable ProbeLaunchFixture -Scope Global -ErrorAction SilentlyContinue
}
