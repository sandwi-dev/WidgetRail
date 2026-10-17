[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet(1,2)][int]$Game,
    [Parameter(Mandatory)][string]$EngineCheckout,
    [Parameter(Mandatory)][string]$RuntimeDirectory,
    [Parameter(Mandatory)][string]$EngineLockPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$Python='python',
    [switch]$BuildPackage,
    [ValidateRange(60,3600)][int]$TestTimeoutSeconds=1800
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot ../..))
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'Choose a fresh verification directory.'}
New-Item -ItemType Directory -Path $out|Out-Null
$report=[ordered]@{schemaVersion=1;game=$Game;status='running';releaseEligible=$false;startedUtc=[DateTime]::UtcNow.ToString('o')}
$sourcePaths=@('samples/PobCompanion','src','tools/WrailCli','eng','.github/workflows/pob-engine-candidates.yml','Directory.Build.props','Directory.Build.targets','Directory.Packages.props','global.json')
function Get-SourceDigest {
    $files=@(& git -C $repo ls-files --cached --others --exclude-standard -- @sourcePaths)
    if($LASTEXITCODE -ne 0 -or $files.Count -eq 0){throw 'Cannot inventory candidate source files.'}
    $entries=foreach($file in ($files|Sort-Object -Unique)){
        $path=Join-Path $repo $file
        if(Test-Path -LiteralPath $path -PathType Leaf){$file+':'+(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}else{$file+':deleted'}
    }
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($entries -join "`n"))))
}
function Assert-InputsUnchanged {
    if((Get-SourceDigest) -ne $report.sourceSha256){throw 'Widget source changed during verification. Test the final source again before packaging.'}
    if((Get-FileHash -LiteralPath $EngineLockPath -Algorithm SHA256).Hash -ne $report.lockSha256){throw 'Candidate lock changed during verification.'}
    $head=& git -C $EngineCheckout rev-parse HEAD
    if($LASTEXITCODE -ne 0 -or $head.Trim() -ne $expected){throw 'Engine revision changed during verification.'}
    & git -C $EngineCheckout diff --quiet HEAD -- src runtime/lua runtime/zlib1.dll manifest.xml LICENSE.md
    if($LASTEXITCODE -ne 0){throw 'Engine sources changed during verification.'}
    $extra=@(& git -C $EngineCheckout ls-files --others --exclude-standard -- src runtime/lua)
    if($LASTEXITCODE -ne 0 -or $extra.Count -ne 0){throw 'Engine source inventory includes untracked files.'}
    foreach($file in $report.runtime){
        if((Get-FileHash -LiteralPath (Join-Path $RuntimeDirectory $file.name) -Algorithm SHA256).Hash -ne $file.sha256){throw 'Runtime binaries changed during verification.'}
    }
}
function Build-Checked([string[]]$BuildArguments,[string]$Name){
    & dotnet @BuildArguments "-bl:$out/$Name.binlog" --nologo -v:q 2>&1|Tee-Object (Join-Path $out "$Name.log")
    if($LASTEXITCODE -ne 0){throw "$Name failed. See $Name.log and $Name.binlog."}
}
try {
    $locks=Get-Content -LiteralPath $EngineLockPath -Raw|ConvertFrom-Json
    $expected=$locks."poe$Game".commit
    if($expected -notmatch '^[a-f0-9]{40}$'){throw 'Candidate requires an exact locked engine commit.'}
    $actual=(& git -C $EngineCheckout rev-parse HEAD).Trim()
    if($LASTEXITCODE -ne 0 -or $actual -ne $expected){throw 'Engine checkout does not match the candidate lock.'}
    & git -C $EngineCheckout diff --quiet HEAD -- src runtime/lua runtime/zlib1.dll manifest.xml LICENSE.md
    if($LASTEXITCODE -ne 0){throw 'Upstream engine sources are modified.'}
    $report.engineCommit=$actual;$report.engineRelease=$locks."poe$Game".release
    $report.widgetCommit=(& git -C $repo rev-parse HEAD).Trim()
    $report.sourceDirty=[bool](& git -C $repo status --porcelain -- @sourcePaths)
    if($LASTEXITCODE -ne 0){throw 'Cannot determine candidate source status.'}
    $report.sourceSha256=Get-SourceDigest
    $report.lockSha256=(Get-FileHash -LiteralPath $EngineLockPath -Algorithm SHA256).Hash
    $report.runtime=@(foreach($name in @('luajit.exe','lua51.dll','lua-utf8.dll')){
        $path=Join-Path $RuntimeDirectory $name
        @{name=$name;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
    })
    Assert-InputsUnchanged
    $testOutput=Join-Path $out test-bin
    Build-Checked @('build',(Join-Path $PSScriptRoot Tests/PobCompanion.Tests.csproj),'-c','Release','--output',$testOutput) 'tests-build'
    $test=Join-Path $testOutput PobCompanion.Tests.exe
    $start=[Diagnostics.ProcessStartInfo]::new($test)
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    foreach($argument in @("$Game",[IO.Path]::GetFullPath($EngineCheckout),[IO.Path]::GetFullPath($RuntimeDirectory),(Join-Path $out fixtures),'--engine-update-only')){$start.ArgumentList.Add($argument)}
    $start.Environment['POB_ADAPTER']=Join-Path $PSScriptRoot Engine/bridge.lua
    $process=[Diagnostics.Process]::Start($start)
    $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
    try {
        $completed=$process.WaitForExit($TestTimeoutSeconds*1000)
        if(-not $completed){$process.Kill($true);$process.WaitForExit()}
        $text=$stdout.GetAwaiter().GetResult();$errors=$stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $out tests.log),$text)
        [IO.File]::WriteAllText((Join-Path $out tests-errors.log),$errors)
        if(-not $completed){throw "Engine validation exceeded $TestTimeoutSeconds seconds."}
        if($process.ExitCode -ne 0){throw "Engine validation failed with exit code $($process.ExitCode). See tests.log and tests-errors.log."}
        $summaryLine=$text.Split("`n")|Where-Object{$_.TrimStart().StartsWith('{"game":')}|Select-Object -Last 1
        if(-not $summaryLine){throw 'Test executable returned no structured result.'}
        $summary=$summaryLine|ConvertFrom-Json
        if($summary.game -ne $Game -or $summary.failed -ne 0 -or $summary.passed -lt 30 -or @($summary.results|Where-Object{-not $_.passed}).Count -ne 0){throw 'Engine validation did not execute and pass the required checks.'}
        $summary|ConvertTo-Json -Depth 6|Set-Content (Join-Path $out tests-result.json) -Encoding utf8
        $report.passed=$summary.passed;$report.failed=$summary.failed
    } finally {$process.Dispose()}
    Assert-InputsUnchanged
    if($BuildPackage){
        Build-Checked @('build',(Join-Path $repo tools/WrailCli/WrailCli.csproj),'-c','Release') 'cli-build'
        & (Join-Path $PSScriptRoot Build-CommunityPackage.ps1) -Game $Game -EngineCheckout $EngineCheckout -RuntimeDirectory $RuntimeDirectory -EngineLockPath $EngineLockPath -Python $Python -Cli (Join-Path $repo tools/WrailCli/bin/Release/net8.0/wrail.exe) -OutputDirectory (Join-Path $out package)
        $packages=@(Get-ChildItem (Join-Path $out package) -Filter '*.wrwidget' -File)
        if($packages.Count -ne 1){throw 'Expected exactly one candidate package.'}
        $package=$packages[0]
        $report.package=@{name=$package.Name;sha256=(Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash}
        $report.releaseEligible=-not $report.sourceDirty
    }
    Assert-InputsUnchanged
    $report.status='passed'
} catch {
    $report.status='failed';$report.releaseEligible=$false;$report.error=$_.Exception.Message
    throw
} finally {
    $report.finishedUtc=[DateTime]::UtcNow.ToString('o')
    $report|ConvertTo-Json -Depth 6|Set-Content (Join-Path $out result.json) -Encoding utf8
    if($env:GITHUB_STEP_SUMMARY){
        @("## PoE$Game engine validation","Status: $($report.status)","Engine: $($report.engineCommit)","Checks passed: $($report.passed)","Release eligible: $($report.releaseEligible)","$($report.error)")|Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
    }
}
