[CmdletBinding()]
param([Parameter(Mandatory)][string]$Package)
$ErrorActionPreference = 'Stop'
# Inspect the delivered archive, not the staging directory. Package validation
# cannot infer runtime web assets from a full-trust executable's source code.
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Package).Path)
try {
    function Read-PackageText([string]$path) {
        $entry = $archive.GetEntry($path)
        if ($null -eq $entry -or $entry.Length -eq 0) { throw "Missing required package asset: $path" }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    $manifest = (Read-PackageText 'manifest.json') | ConvertFrom-Json
    if ($manifest.id -ne 'widgetrail.samples.ytmusic') { throw 'Expected a YouTube Music package.' }
    $required = @('web/player.html', 'web/web-session.js', 'payload/service/service.py',
        'payload/service/runtime-lock.json', 'payload/python/python.exe', $manifest.entrypoint.executable)
    $required += @($manifest.iconAssets.PSObject.Properties | ForEach-Object { $_.Value.path })
    foreach ($path in $required) {
        $entry = $archive.GetEntry($path)
        if ($null -eq $entry -or $entry.Length -eq 0) { throw "Missing required package asset: $path" }
    }
    if ((Read-PackageText 'web/player.html') -notmatch 'web-session\.js') { throw 'Player bridge script is not referenced.' }
    [pscustomobject]@{ version=$manifest.version; requiredAssets=$required.Count; passed=$true }
} finally { $archive.Dispose() }
