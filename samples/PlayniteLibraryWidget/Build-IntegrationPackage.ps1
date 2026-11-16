[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output=[IO.Path]::GetFullPath($OutputDirectory)
$published=Join-Path $output 'published'
$buildGraph=Join-Path $output 'build-graph'
$payload=Join-Path $output 'payload'
New-Item -ItemType Directory -Path $output,$payload -Force|Out-Null
$project=Join-Path $repository 'integrations/Playnite/WidgetRail.Playnite/WidgetRail.Playnite.csproj'
& dotnet build $project -c Release -o $published --artifacts-path $buildGraph -p:ContinuousIntegrationBuild=true `
    "-p:PathMap=$output=/_/integration-output%2C$repository=/_/widgetrail" "-bl:$output/build-{}.binlog"
if($LASTEXITCODE){throw 'Local Playnite integration build failed'}
$files=@('WidgetRail.Playnite.dll','WidgetRail.Playnite.Core.dll','WidgetRail.Playnite.Contracts.dll','extension.yaml','LICENSE')
foreach($name in $files){Copy-Item -LiteralPath (Join-Path $published $name) -Destination (Join-Path $payload $name) -Force}
$manifest=Get-Content (Join-Path $payload 'extension.yaml') -Raw
if($manifest -notmatch '(?m)^Version:\s*([^\r\n]+)'){throw 'Missing integration version'}
$version=$Matches[1].Trim()
$extensionId='WidgetRail_Playnite_950de770'
$pluginId='950de770-1c2f-4a48-ac2a-b62d866f7503'
if($manifest -notmatch ('(?m)^Id:\s*'+[regex]::Escape($extensionId)+'\s*$')){throw 'Unexpected integration identity'}
$sourceFiles=Get-ChildItem -LiteralPath (Join-Path $repository 'integrations/Playnite') -File -Recurse|Where-Object {$_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Extension -in @('.cs','.csproj','.yaml','') }|Sort-Object FullName
$sourceText=($sourceFiles|ForEach-Object {[IO.Path]::GetRelativePath($repository,$_.FullName).Replace('\','/')+'='+(Get-FileHash -LiteralPath $_.FullName).Hash}) -join "`n"
$sourceDigest=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($sourceText)))
$binaryHashes=[ordered]@{}
foreach($name in $files|Where-Object {$_ -like '*.dll'}){$binaryHashes[$name]=(Get-FileHash -LiteralPath (Join-Path $payload $name)).Hash}
$build=[ordered]@{pluginId=$pluginId;extensionId=$extensionId;version=$version;sourceCommit=(& git -C $repository rev-parse HEAD);sourceDigest=$sourceDigest;files=$binaryHashes}
[IO.File]::WriteAllText((Join-Path $payload 'WidgetRail.Build.json'),($build|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
$files+='WidgetRail.Build.json'
$archivePath=Join-Path $output 'WidgetRail.Playnite.pext'
Add-Type -AssemblyName System.IO.Compression
$stream=[IO.File]::Open($archivePath,[IO.FileMode]::Create)
try{
 $zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
 try{foreach($name in $files){$entry=$zip.CreateEntry($name,[IO.Compression.CompressionLevel]::Optimal);$entry.LastWriteTime=[DateTimeOffset]::new(2026,1,1,0,0,0,[TimeSpan]::Zero);$destination=$entry.Open();try{$bytes=[IO.File]::ReadAllBytes((Join-Path $payload $name));$destination.Write($bytes,0,$bytes.Length)}finally{$destination.Dispose()}}}finally{$zip.Dispose()}
}finally{$stream.Dispose()}
$hashes=[ordered]@{}
foreach($name in $files){$hashes[$name]=(Get-FileHash -LiteralPath (Join-Path $payload $name)).Hash}
$metadata=[ordered]@{pluginId=$pluginId;extensionId=$extensionId;version=$version;sha256=(Get-FileHash -LiteralPath $archivePath).Hash;dllSha256=$hashes['WidgetRail.Playnite.dll'];files=$hashes;sourceDigest=$sourceDigest}
[IO.File]::WriteAllText((Join-Path $output 'integration-package.json'),($metadata|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
Write-Host "Prepared WidgetRail for Playnite $version (local API only)."
