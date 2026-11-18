[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackagePath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression

# Validate the actual archive, including development candidates made outside the
# canonical builder. General wrail validation cannot infer these provider assets.
function Read-Entry($Archive, [string]$Name, [long]$Maximum) {
    $entries = @($Archive.Entries | Where-Object FullName -CEQ $Name)
    if ($entries.Count -ne 1 -or $entries[0].Length -lt 1 -or $entries[0].Length -gt $Maximum) {
        throw "Missing, duplicate or oversized required Playnite package file: $Name"
    }
    $inputStream = $entries[0].Open()
    $output = [IO.MemoryStream]::new()
    try {
        $buffer = [byte[]]::new(8192)
        while (($count = $inputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            if ($output.Length + $count -gt $Maximum) { throw "Oversized Playnite package file: $Name" }
            $output.Write($buffer, 0, $count)
        }
        return ,$output.ToArray()
    } finally { $inputStream.Dispose(); $output.Dispose() }
}
function Hash([byte[]]$Bytes) { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)) }
$archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($PackagePath))
try {
    $manifest = [Text.Encoding]::UTF8.GetString((Read-Entry $archive 'manifest.json' 262144)) | ConvertFrom-Json
    if ($manifest.id -cne 'widgetrail.samples.playnite-library' -or
        $manifest.entrypoint.executable -cne 'payload/PlayniteLibraryApplication.exe') { throw 'Not a Playnite Library application package.' }
    $metadata = [Text.Encoding]::UTF8.GetString((Read-Entry $archive 'payload/integration/integration-package.json' 16384)) | ConvertFrom-Json -AsHashtable
    $bytes = Read-Entry $archive 'payload/integration/WidgetRail.Playnite.pext' 25165824
    if ($metadata.pluginId -cne '950de770-1c2f-4a48-ac2a-b62d866f7503' -or
        $metadata.extensionId -cne 'WidgetRail_Playnite_950de770' -or (Hash $bytes) -ine $metadata.sha256) {
        throw 'The bundled Playnite integration identity or archive hash is invalid.'
    }
    $stream = [IO.MemoryStream]::new($bytes, $false)
    $bundle = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read)
    try {
        $expected = @('WidgetRail.Playnite.dll','WidgetRail.Playnite.Core.dll','WidgetRail.Playnite.Contracts.dll','extension.yaml','LICENSE','WidgetRail.Build.json')
        if ($bundle.Entries.Count -ne $expected.Count -or $metadata.files.Count -ne $expected.Count) { throw 'Unexpected Playnite integration file inventory.' }
        foreach ($name in $expected) {
            if (!$metadata.files.ContainsKey($name) -or (Hash (Read-Entry $bundle $name 4194304)) -ine $metadata.files[$name]) {
                throw "Playnite integration file failed its integrity check: $name"
            }
        }
    } finally { $bundle.Dispose(); $stream.Dispose() }
    "Verified Playnite Library $($manifest.version): complete integration bundle and all six payload hashes."
} finally { $archive.Dispose() }
