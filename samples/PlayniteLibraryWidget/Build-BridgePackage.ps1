[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$spec = Get-Content (Join-Path $PSScriptRoot 'bridge/source.json') -Raw | ConvertFrom-Json
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$archive = Join-Path $output 'source.zip'
if (!(Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive).Hash -ne $spec.archiveSha256) {
    Invoke-WebRequest -Uri $spec.archive -OutFile ($archive + '.download')
    if ((Get-FileHash -LiteralPath ($archive + '.download')).Hash -ne $spec.archiveSha256) { throw 'Bridge source archive digest does not match the pinned build.' }
    Move-Item -LiteralPath ($archive + '.download') -Destination $archive -Force
}
$source = Join-Path $output ('playnite-bridge-' + $spec.commit)
if (!(Test-Path -LiteralPath (Join-Path $source 'src/PlayniteBridge.csproj'))) { Expand-Archive -LiteralPath $archive -DestinationPath $output -Force }
& dotnet build (Join-Path $source 'src/PlayniteBridge.csproj') -c Release -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false `
    -p:ContinuousIntegrationBuild=true -p:Deterministic=true "-p:PathMap=$source=/_/playnite-bridge" "-bl:$output/build-{}.binlog"
if ($LASTEXITCODE -ne 0) { throw 'Bridge build failed.' }
$payload = Join-Path $output 'payload'
New-Item -ItemType Directory -Force -Path $payload | Out-Null
Copy-Item -LiteralPath (Join-Path $source 'src/bin/Release/net462/PlayniteBridge.dll') -Destination $payload -Force
Copy-Item -LiteralPath (Join-Path $source 'LICENSE') -Destination $payload -Force
$manifest = @"
Id: PlayniteBridge_f47ac10b
Name: Playnite Bridge (WidgetRail build)
Author: rollacode; compatibility patch by WidgetRail
Version: $($spec.version)
Module: PlayniteBridge.dll
Type: GenericPlugin
"@
[IO.File]::WriteAllText((Join-Path $payload 'extension.yaml'), $manifest.Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
$build = [ordered]@{ source=$spec.repository; commit=$spec.commit; upstreamVersion=$spec.upstreamVersion; version=$spec.version; pullRequest=$spec.upstreamPullRequest }
[IO.File]::WriteAllText((Join-Path $payload 'WidgetRail.Build.json'), ($build | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Add-Type -AssemblyName System.IO.Compression
$pext = Join-Path $output 'PlayniteBridge.WidgetRail.pext'
$stream = [IO.File]::Open($pext, [IO.FileMode]::Create)
try {
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($name in @('LICENSE','PlayniteBridge.dll','WidgetRail.Build.json','extension.yaml')) {
            $entry = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2026,1,1,0,0,0,[TimeSpan]::Zero)
            $entryStream = $entry.Open()
            try { $bytes = [IO.File]::ReadAllBytes((Join-Path $payload $name)); $entryStream.Write($bytes,0,$bytes.Length) } finally { $entryStream.Dispose() }
        }
    } finally { $zip.Dispose() }
} finally { $stream.Dispose() }
$metadata = [ordered]@{ version=$spec.version; sha256=(Get-FileHash $pext).Hash; dllSha256=(Get-FileHash (Join-Path $payload 'PlayniteBridge.dll')).Hash; knownOriginalDllSha256=$spec.knownOriginalDllSha256; source=$build }
[IO.File]::WriteAllText((Join-Path $output 'bridge-package.json'), ($metadata | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
Write-Host "Prepared pinned Bridge package $($spec.version)."
