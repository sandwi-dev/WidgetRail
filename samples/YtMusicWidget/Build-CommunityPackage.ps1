[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$OutputDirectory,
    [string]$Catalog,
    [string]$Version,
    [switch]$Install
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$manifest = Get-Content (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
if ($Version) { $manifest.version = ([version]$Version).ToString() }
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else {
    Join-Path $repository ('artifacts/community-addons/ytmusic/' + $manifest.version + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
}
$staging = Join-Path $output 'package-root'
if (Test-Path -LiteralPath $staging) { throw 'Use a fresh output directory; staged packages are never overwritten.' }
$payload = Join-Path $staging 'payload'
$logs = Join-Path $output 'logs'
New-Item -ItemType Directory -Path $payload, $logs -Force | Out-Null
foreach ($project in @('Application/YtMusicApplication.csproj', 'PlaybackHost/YtMusicPlaybackHost.csproj')) {
    $name = [IO.Path]::GetFileNameWithoutExtension($project)
    $publish = Join-Path $output $name
    & dotnet publish (Join-Path $PSScriptRoot $project) -c $Configuration --no-self-contained --nologo --output $publish "-bl:$logs/$name-{}.binlog"
    if ($LASTEXITCODE -ne 0) { throw "$name publish failed." }
    foreach ($file in Get-ChildItem -LiteralPath $publish -File -Recurse) {
        if ($file.Extension -in @('.pdb', '.xml') -or $file.Name -eq 'Microsoft.Windows.SDK.NET.dll') { continue }
        # Source manifest/styles are copied explicitly below, outside executable payload.
        if ($file.Name -eq 'manifest.json' -or $file.Extension -eq '.wrss') { continue }
        $relative = [IO.Path]::GetRelativePath($publish, $file.FullName)
        $target = Join-Path $payload $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        if (Test-Path -LiteralPath $target) {
            if ((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) { throw "Publish graphs disagree on $relative" }
        } else { Copy-Item -LiteralPath $file.FullName -Destination $target }
    }
}
& (Join-Path $PSScriptRoot 'Service/Prepare-Runtime.ps1') -Destination (Join-Path $payload 'python') -DownloadCache (Join-Path $repository 'artifacts/ytmusic/downloads')
New-Item -ItemType Directory -Path (Join-Path $payload 'service'), (Join-Path $staging 'styles'), (Join-Path $staging 'assets/icons') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Service/service.py') -Destination (Join-Path $payload 'service/service.py')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Service/runtime-lock.json') -Destination (Join-Path $payload 'service/runtime-lock.json')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NOTICE.md') -Destination (Join-Path $payload 'NOTICE.md')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'styles/default.wrss') -Destination (Join-Path $staging 'styles/default.wrss')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets/icons/yt-music.svg') -Destination (Join-Path $staging 'assets/icons/yt-music.svg')
$manifest | ConvertTo-Json -Depth 16 | Set-Content (Join-Path $staging 'manifest.json') -Encoding utf8
$cli = Join-Path $repository 'tools/WrailCli/WrailCli.csproj'
& dotnet build $cli -c $Configuration --nologo "-bl:$logs/cli-{}.binlog"
if ($LASTEXITCODE -ne 0) { throw 'CLI build failed.' }
$cliDll = Join-Path $repository "tools/WrailCli/bin/$Configuration/net8.0/wrail.dll"
$package = Join-Path $output "$($manifest.id)-$($manifest.version).wrwidget"
& dotnet $cliDll validate $staging
if ($LASTEXITCODE -ne 0) { throw 'Package validation failed.' }
& dotnet $cliDll pack $staging --output $package
if ($LASTEXITCODE -ne 0) { throw 'Package creation failed.' }
if ($Install) {
    $catalogArgs = if ($Catalog) { @('--catalog', [IO.Path]::GetFullPath($Catalog)) } else { @() }
    & dotnet $cliDll install $package --accept-full-trust @catalogArgs
    if ($LASTEXITCODE -ne 0) { throw 'Installation failed.' }
    & dotnet $cliDll version select $manifest.id $manifest.version @catalogArgs
    if ($LASTEXITCODE -ne 0) { throw 'Version selection failed.' }
    & dotnet $cliDll enable $manifest.id --accept-full-trust @catalogArgs
    if ($LASTEXITCODE -ne 0) { throw 'Enable failed.' }
}
Write-Host "Community addon package: $package"
