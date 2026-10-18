[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Discord','GameHelp','DisplayProfiles','Twitch','MapGenie')][string]$Widget,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = Join-Path $repository "samples/${Widget}Widget"
$manifest = Get-Content -LiteralPath (Join-Path $source 'manifest.json') -Raw | ConvertFrom-Json
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repository "artifacts/community-addons/$Widget" }
# Each invocation owns a fresh staging directory; never erase a caller's output tree.
$work = Join-Path $output ([guid]::NewGuid().ToString('N'))
$stage = Join-Path $work 'package-root'
$published = Join-Path $work 'published'
$logs = Join-Path $work 'logs'
New-Item -ItemType Directory -Path "$stage/payload",$logs -Force | Out-Null
$application = $manifest.entrypoint.runtime -eq 'full-trust-application-v1'
$entry = if ($application) { $manifest.entrypoint.executable } else { $manifest.entrypoint.assembly }
$assembly = [IO.Path]::GetFileNameWithoutExtension($entry)
$project = if ($application) { Join-Path $source "Application/$assembly.csproj" } else { Join-Path $source "$assembly.csproj" }
& dotnet publish $project -c $Configuration --no-self-contained -o $published "-bl:$logs/publish.binlog" -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw "$Widget publication failed." }
foreach ($file in Get-ChildItem -LiteralPath $published -File -Recurse) {
    $relative = [IO.Path]::GetRelativePath($published, $file.FullName)
    if ($file.Extension -in @('.pdb','.xml') -or $relative -match '^(assets|styles)[\\/]' -or $relative -eq 'manifest.json') { continue }
    $target = Join-Path "$stage/payload" $relative
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
}
Copy-Item -LiteralPath (Join-Path $source 'manifest.json') -Destination $stage
foreach ($directory in @('styles','assets')) {
    $path = Join-Path $source $directory
    if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $stage -Recurse }
}
$cliProject = Join-Path $repository 'tools/WrailCli/WrailCli.csproj'
& dotnet publish $cliProject -c $Configuration --no-self-contained -o "$work/cli" "-bl:$logs/cli.binlog" -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'CLI publication failed.' }
$cli = Join-Path $work 'cli/wrail.exe'
& $cli validate $stage
if ($LASTEXITCODE -ne 0) { throw 'Package validation failed.' }
$archive = Join-Path $output "$($manifest.id)-$($manifest.version).wrwidget"
if (Test-Path -LiteralPath $archive) { throw "Package already exists: $archive. Use a fresh output directory or bump the package version." }
& $cli pack $stage --output $archive
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $archive)) { throw 'Package creation failed.' }
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath ($archive + '.sha256') -Encoding utf8NoBOM
Get-ChildItem -LiteralPath $output -File -Filter '*.wrwidget' | Sort-Object Name | ForEach-Object {
    "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)"
} | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding utf8NoBOM
Write-Output "Community package: $archive"
Write-Output 'No installation, enabling, permission grant, or publication was performed.'
