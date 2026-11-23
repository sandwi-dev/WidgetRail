[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory,[string]$ReuseBundleDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output=[IO.Path]::GetFullPath($OutputDirectory)
$published=Join-Path $output 'published'
$buildGraph=Join-Path $output 'build-graph'
$payload=Join-Path $output 'payload'
New-Item -ItemType Directory -Path $output,$payload -Force|Out-Null
$sourceFiles=Get-ChildItem -LiteralPath (Join-Path $repository 'integrations/Playnite') -File -Recurse|Where-Object {$_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Extension -in @('.cs','.csproj','.yaml','') }|Sort-Object FullName
$sourceFiles = @($sourceFiles) + (Get-Item -LiteralPath (Join-Path $repository 'src/Shared/CaptureProcessKey.cs'))
$sourceText=($sourceFiles|ForEach-Object {[IO.Path]::GetRelativePath($repository,$_.FullName).Replace('\','/')+'='+(Get-FileHash -LiteralPath $_.FullName).Hash}) -join "`n"
$sourceDigest=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($sourceText)))
if($ReuseBundleDirectory){
    # Explicit reuse of a trusted prior build for widget-only updates. Keep the
    # exact installed-binary verification; do not replace it with a source claim.
    $reuse=(Resolve-Path -LiteralPath $ReuseBundleDirectory).Path
    $archive=Join-Path $reuse 'WidgetRail.Playnite.pext'
    $metadataPath=Join-Path $reuse 'integration-package.json'
    $metadata=Get-Content -LiteralPath $metadataPath -Raw|ConvertFrom-Json
    $sourceManifest=Get-Content (Join-Path $repository 'integrations/Playnite/WidgetRail.Playnite/extension.yaml') -Raw
    if($sourceManifest -notmatch '(?m)^Version:\s*([^\r\n]+)'){throw 'Missing source integration version'}
    if($metadata.sourceDigest -ne $sourceDigest -or $metadata.version -cne $Matches[1].Trim() -or
        $metadata.pluginId -cne '950de770-1c2f-4a48-ac2a-b62d866f7503' -or $metadata.extensionId -cne 'WidgetRail_Playnite_950de770'){
        throw 'The reusable integration bundle does not match the current integration sources and identity.'
    }
    if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $metadata.sha256){throw 'Reusable integration archive checksum mismatch.'}
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip=[IO.Compression.ZipFile]::OpenRead($archive)
    try{
        $expected=@('WidgetRail.Playnite.dll','WidgetRail.Playnite.Core.dll','WidgetRail.Playnite.Contracts.dll','extension.yaml','LICENSE','WidgetRail.Build.json')
        if($zip.Entries.Count -ne $expected.Count -or @($metadata.files.PSObject.Properties).Count -ne $expected.Count){throw 'Unexpected reusable integration inventory.'}
        foreach($name in $expected){
            $entries=@($zip.Entries|Where-Object FullName -CEQ $name)
            if($entries.Count -ne 1 -or $entries[0].Length -gt 32MB){throw 'Invalid reusable integration entry.'}
            $stream=$entries[0].Open()
            try{$actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))}finally{$stream.Dispose()}
            if($actual -ne $metadata.files.$name){throw "Reusable integration entry checksum mismatch: $name"}
        }
        if($metadata.dllSha256 -ne $metadata.files.'WidgetRail.Playnite.dll'){throw 'Reusable integration primary checksum mismatch.'}
    }finally{$zip.Dispose()}
    Copy-Item -LiteralPath $archive,$metadataPath -Destination $output
    @{source=$reuse;sourceDigest=$sourceDigest;archiveSha256=$metadata.sha256;version=$metadata.version}|
        ConvertTo-Json|Set-Content -LiteralPath (Join-Path $output 'reuse-provenance.json')
    Write-Host "Reused unchanged WidgetRail for Playnite $($metadata.version) from the explicitly supplied prior build."
    return
}
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
