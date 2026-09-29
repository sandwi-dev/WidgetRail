[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Receipt,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$AssetsPath,
    [string]$FrontendManifest
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
# These tests only create isolated artifact directories and inspect signed MSIXs.
# No package registration, runtime installation, frontend activation or trust changes.
$sourceReceipt=[IO.Path]::GetFullPath($Receipt)
$sourceRoot=Split-Path $sourceReceipt
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Choose a fresh adversarial-test directory'}
if([bool]$AssetsPath -ne [bool]$FrontendManifest){throw 'Supply both source inputs for the archive content-hash case'}
$validator=Join-Path $PSScriptRoot 'Test-WinUiRuntimePrerequisites.ps1'
$json=Get-Content -LiteralPath $sourceReceipt -Raw
New-Item -ItemType Directory -Path $output|Out-Null
$results=[Collections.Generic.List[object]]::new()
function NewCase([string]$Name){
    $directory=Join-Path $output $Name
    New-Item -ItemType Directory -Path $directory|Out-Null
    foreach($file in @('WindowsAppSDK-VersionInfo.json','LICENSE.txt')){
        Copy-Item -LiteralPath (Join-Path $sourceRoot $file) -Destination (Join-Path $directory $file)
    }
    $data=$json|ConvertFrom-Json
    foreach($item in $data.packages){
        if([string]::IsNullOrWhiteSpace($item.path) -or [IO.Path]::IsPathRooted($item.path) -or $item.path.Contains(':') -or
            @($item.path -split '[/\\]'|Where-Object {$_ -in @('','.', '..')}).Count){throw 'Source receipt contains an unsafe package path'}
        $target=Join-Path $directory $item.path
        New-Item -ItemType Directory -Path (Split-Path $target) -Force|Out-Null
        Copy-Item -LiteralPath (Join-Path $sourceRoot $item.path) -Destination $target
    }
    return @{directory=$directory;record=$data}
}
function RunCase([string]$Name,[scriptblock]$Mutate,[string]$ExpectedError){
    $case=NewCase $Name
    & $Mutate $case
    $caseReceipt=Join-Path $case.directory 'runtime-prerequisites.json'
    $case.record|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $caseReceipt
    $failure=$null
    try{& $validator -Receipt $caseReceipt|Out-Null}catch{$failure=$_.Exception.Message}
    if($ExpectedError){
        if(-not $failure -or $failure -notlike $ExpectedError){throw "Case '$Name' did not reject as expected. Actual: $failure"}
        if(Test-Path -LiteralPath (Join-Path $case.directory 'validation.json')){throw "Rejected case '$Name' wrote a success result"}
    }elseif($failure){throw "Valid baseline failed: $failure"}
    $results.Add(@{name=$Name;passed=$true;expectedError=$ExpectedError;observedError=$failure})
}
RunCase 'valid' {} ''
RunCase 'role-substitution' {
    param($case)
    ($case.record.packages|Where-Object role -eq 'Main').role='DDLM'
    ($case.record.packages|Where-Object {$_.name -like 'Microsoft.WinAppRuntime.DDLM.*-x6'}).role='Main'
} '*Actual package belongs to the expected runtime role*'
RunCase 'escaped-path' {
    param($case)
    $case.record.packages[0].path=[IO.Path]::GetRelativePath($case.directory,(Join-Path $sourceRoot $case.record.packages[0].path))
} '*contained relative path*'
RunCase 'absolute-path' {
    param($case)
    $case.record.packages[0].path=Join-Path $sourceRoot $case.record.packages[0].path
} '*contained relative path*'
RunCase 'reparse-directory' {
    param($case)
    New-Item -ItemType Junction -Path (Join-Path $case.directory 'redirect') -Target (Join-Path $sourceRoot 'x64')|Out-Null
    $case.record.packages[0].path='redirect/'+[IO.Path]::GetFileName($case.record.packages[0].path)
} '*Reparse point is not allowed*'
RunCase 'duplicate-path' {
    param($case)
    $duplicate=($case.record.packages|Where-Object {$_.role -eq 'DDLM' -and $_.architecture -eq 'x64'})|ConvertTo-Json -Depth 10|ConvertFrom-Json
    $duplicate.role='Main'
    for($i=0;$i -lt $case.record.packages.Count;$i++){if($case.record.packages[$i].role -eq 'Main'){$case.record.packages[$i]=$duplicate;break}}
} '*Distinct runtime package path*'
RunCase 'duplicate-identity' {
    param($case)
    $duplicate=($case.record.packages|Where-Object {$_.role -eq 'DDLM' -and $_.architecture -eq 'x64'})|ConvertTo-Json -Depth 10|ConvertFrom-Json
    Copy-Item -LiteralPath (Join-Path $case.directory $duplicate.path) -Destination (Join-Path $case.directory 'duplicate.msix')
    $duplicate.path='duplicate.msix';$duplicate.role='Main'
    for($i=0;$i -lt $case.record.packages.Count;$i++){if($case.record.packages[$i].role -eq 'Main'){$case.record.packages[$i]=$duplicate;break}}
} '*Actual package belongs to the expected runtime role*'
RunCase 'dependency-omission' {
    param($case)
    ($case.record.packages|Where-Object role -eq 'Main').dependencies=@()
} '*Complete dependency set agrees with manifest*'
RunCase 'dependency-substitution' {
    param($case)
    ($case.record.packages|Where-Object role -eq 'Main').dependencies[0].name='WrongFramework'
} '*Dependency edge agrees with signed manifest*'
RunCase 'license-omission' {
    param($case)
    $case.record.packages[0].embeddedLicenses=@()
} '*Complete license set agrees with signed package*'
RunCase 'license-duplication' {
    param($case)
    $first=$case.record.packages[0].embeddedLicenses[0]
    $case.record.packages[0].embeddedLicenses=@($first,$first)
} '*Complete license set agrees with signed package*'
RunCase 'framework-property' {
    param($case)
    $case.record.packages[0].isFramework=$false
} '*Actual package type matches its role*'
RunCase 'publisher-substitution' {
    param($case)
    $case.record.packages[0].publisher='CN=Untrusted'
} '*Actual manifest publisher is Microsoft*'
if($AssetsPath){
    # Forge both editable metadata claims together while keeping the real signed
    # archive and its raw SHA512 intact. Only computing NuGet content hash catches this.
    $directory=Join-Path $output 'forged-cache-content-hash'
    $cache=Join-Path $directory 'cache'
    $assets=Get-Content -LiteralPath $AssetsPath -Raw|ConvertFrom-Json -AsHashtable
    $key=@($assets.libraries.Keys|Where-Object {$_ -like 'Microsoft.WindowsAppSDK.Runtime/*'})[0]
    $library=$assets.libraries[$key]
    $packageVersion=$key.Split('/')[1]
    $archiveName='microsoft.windowsappsdk.runtime.'+$packageVersion+'.nupkg'
    $sourceArchives=@(foreach($folder in $assets.packageFolders.Keys){
        $candidate=Join-Path (Join-Path $folder $library.path) $archiveName
        if(Test-Path -LiteralPath $candidate){$candidate}
    })
    if($sourceArchives.Count -ne 1){throw 'Expected one restored archive for the content-hash case'}
    $packageDirectory=Join-Path $cache $library.path
    New-Item -ItemType Directory -Path $packageDirectory -Force|Out-Null
    Copy-Item -LiteralPath $sourceArchives[0] -Destination (Join-Path $packageDirectory $archiveName)
    Copy-Item -LiteralPath ($sourceArchives[0]+'.sha512') -Destination (Join-Path $packageDirectory ($archiveName+'.sha512'))
    $forgedHash=[Convert]::ToBase64String([byte[]]::new(64))
    @{version=2;contentHash=$forgedHash}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $packageDirectory '.nupkg.metadata')
    $forgedAssets=Join-Path $directory 'project.assets.json'
    @{libraries=@{$key=@{path=$library.path;sha512=$forgedHash}};packageFolders=@{$cache=@{}}}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $forgedAssets
    $stageOutput=Join-Path $directory 'stage'
    $failure=$null
    try{& (Join-Path $PSScriptRoot 'Stage-WinUiRuntimePrerequisites.ps1') -AssetsPath $forgedAssets -FrontendManifest $FrontendManifest -OutputDirectory $stageOutput|Out-Null}catch{$failure=$_.Exception.Message}
    if($failure -cne 'Computed runtime archive content hash differs from the build graph' -or (Test-Path -LiteralPath $stageOutput)){
        throw "Forged-cache content hash case did not reject before staging. Actual: $failure"
    }
    $results.Add(@{name='forged-cache-content-hash';passed=$true;observedError=$failure})
}
@{passed=$true;sourceReceipt=$sourceReceipt;cases=$results;runtimeInstalled=$false}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $output 'result.json')
"Passed $($results.Count) offline runtime prerequisite adversarial cases."
