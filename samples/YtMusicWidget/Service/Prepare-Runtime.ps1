[CmdletBinding()]
param([Parameter(Mandatory)][string]$Destination, [Parameter(Mandatory)][string]$DownloadCache)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$destinationPath = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $destinationPath) { throw 'Runtime destination must be new.' }
New-Item -ItemType Directory -Path $destinationPath, $DownloadCache -Force | Out-Null
$library = Join-Path $destinationPath 'Lib'
New-Item -ItemType Directory -Path $library -Force | Out-Null
$lock = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'runtime-lock.json') -Raw | ConvertFrom-Json
foreach ($artifact in $lock) {
    $download = Join-Path $DownloadCache $artifact.name
    if (-not (Test-Path -LiteralPath $download)) {
        Invoke-WebRequest -Uri $artifact.url -OutFile $download
    }
    if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ine $artifact.sha256) {
        throw "Runtime artifact checksum mismatch: $($artifact.name)"
    }
    switch ($artifact.kind) {
        'python' { [IO.Compression.ZipFile]::ExtractToDirectory($download, $destinationPath) }
        'wheel' {
            if ($artifact.name.StartsWith('yt_dlp-')) {
                # zipimport keeps the 1,000+ extractor modules within the package entry bound.
                Copy-Item -LiteralPath $download -Destination (Join-Path $destinationPath 'yt_dlp.zip')
            } else {
                [IO.Compression.ZipFile]::ExtractToDirectory($download, $library)
            }
        }
        'executable' { Copy-Item -LiteralPath $download -Destination (Join-Path $destinationPath $artifact.name) }
        default { throw "Unknown artifact kind: $($artifact.kind)" }
    }
}
@('python313.zip', '.', 'Lib', 'yt_dlp.zip') | Set-Content -LiteralPath (Join-Path $destinationPath 'python313._pth') -Encoding ascii
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'quickjs-LICENSE.txt') -Destination $destinationPath
& (Join-Path $destinationPath 'python.exe') -I -c 'import ytmusicapi, yt_dlp, yt_dlp_ejs, requests, websocket; print("YouTube Music runtime imports passed")'
if ($LASTEXITCODE -ne 0) { throw 'Packaged Python runtime import check failed.' }
