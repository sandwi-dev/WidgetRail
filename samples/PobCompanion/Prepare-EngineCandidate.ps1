[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet(1,2)][int]$Game,
    [ValidateSet('pinned','stable','beta')][string]$Channel='pinned',
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'Choose a fresh candidate directory.'}
New-Item -ItemType Directory -Path $out | Out-Null
$locks=Get-Content (Join-Path $PSScriptRoot engine-lock.json) -Raw | ConvertFrom-Json
$engine=$locks."poe$Game"
$expected=if($Game -eq 1){'https://github.com/PathOfBuildingCommunity/PathOfBuilding.git'}else{'https://github.com/PathOfBuildingCommunity/PathOfBuilding-PoE2.git'}
if($engine.repository -ne $expected){throw 'Unexpected upstream repository.'}
$checkout=Join-Path $out engine
try {
    $ref=$engine.commit
    if($Channel -eq 'stable'){
        $repo=if($Game -eq 1){'PathOfBuilding'}else{'PathOfBuilding-PoE2'}
        $release=Invoke-RestMethod -Uri "https://api.github.com/repos/PathOfBuildingCommunity/$repo/releases/latest" -Headers @{'User-Agent'='WidgetRail-engine-validation'} -TimeoutSec 30
        if($release.draft -or $release.prerelease -or $release.tag_name -notmatch '^[A-Za-z0-9][A-Za-z0-9._/-]*$'){throw 'Latest stable release has an invalid tag.'}
        $ref='refs/tags/'+$release.tag_name;$engine.release=$release.tag_name
    } elseif($Channel -eq 'beta'){$ref='refs/heads/beta';$engine.release='beta candidate'}
    New-Item -ItemType Directory -Path $checkout | Out-Null
    & git -C $checkout init --quiet
    if($LASTEXITCODE -ne 0){throw 'Git initialization failed.'}
    & git -C $checkout fetch --depth=1 $expected $ref 2>&1 | Tee-Object (Join-Path $out fetch.log)
    if($LASTEXITCODE -ne 0){throw 'Upstream fetch failed.'}
    & git -C $checkout checkout --detach FETCH_HEAD
    if($LASTEXITCODE -ne 0){throw 'Upstream checkout failed.'}
    $commit=(& git -C $checkout rev-parse HEAD).Trim()
    if($commit -notmatch '^[a-f0-9]{40}$'){throw 'Could not resolve an exact upstream commit.'}
    if($Channel -eq 'pinned' -and $commit -ne $engine.commit){throw 'Fetched revision differs from the lock.'}
    $engine.commit=$commit
    $lockPath=Join-Path $out engine-lock.candidate.json
    $locks | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $lockPath -Encoding utf8
    @{game=$Game;channel=$Channel;repository=$expected;requestedRef=$ref;commit=$commit;engine=$checkout;engineLock=$lockPath;status='prepared'} |
        ConvertTo-Json | Set-Content (Join-Path $out candidate.json) -Encoding utf8
    Write-Output "Prepared PoE$Game $Channel at $commit. No installed engine or repository lock was changed."
} catch {
    @{game=$Game;channel=$Channel;status='failed';error=$_.Exception.Message}|ConvertTo-Json|Set-Content (Join-Path $out preparation-failure.json)
    throw
}
