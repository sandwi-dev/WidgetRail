[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Root,
    [Parameter(Mandatory)][string]$RelativeTo,
    [ValidateRange(1, 256)][int]$MaximumFiles = 256,
    [ValidateRange(1, 32768)][int]$MaximumEntries = 4096,
    [ValidateRange(1, 1073741824)][long]$MaximumPackageBytes = 75497472,
    [ValidateRange(1, 8589934592)][long]$MaximumTotalBytes = 2147483648
)

$ErrorActionPreference = 'Stop'
$rootPath = [IO.Path]::GetFullPath($Root)
$relativeRoot = [IO.Path]::GetFullPath($RelativeTo)
$relativePath = [IO.Path]::GetRelativePath($relativeRoot, $rootPath)
if ([IO.Path]::IsPathRooted($relativePath) -or $relativePath -eq '..' -or
    $relativePath.StartsWith('..' + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
    throw 'Package provenance root must remain below the evidence root.'
}
if (-not (Test-Path -LiteralPath $rootPath -PathType Container)) {
    ConvertTo-Json -InputObject @() -Compress
    exit 0
}
if (([IO.File]::GetAttributes($rootPath) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'Package provenance root cannot be a reparse point.'
}

$directories = [Collections.Generic.Stack[string]]::new()
$directories.Push($rootPath)
$packages = [Collections.Generic.List[IO.FileInfo]]::new()
$visitedEntries = 0
while ($directories.Count -ne 0) {
    $directory = $directories.Pop()
    foreach ($entryPath in [IO.Directory]::EnumerateFileSystemEntries($directory)) {
        $visitedEntries++
        if ($visitedEntries -gt $MaximumEntries) {
            throw "Package provenance exceeds the $MaximumEntries-entry traversal limit."
        }
        $attributes = [IO.File]::GetAttributes($entryPath)
        if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
        if (($attributes -band [IO.FileAttributes]::Directory) -ne 0) {
            $directories.Push($entryPath)
            continue
        }
        if ([IO.Path]::GetExtension($entryPath).Equals('.wrwidget', [StringComparison]::OrdinalIgnoreCase)) {
            if ($packages.Count -eq $MaximumFiles) {
                throw "Package provenance exceeds the $MaximumFiles-file evidence limit."
            }
            $packages.Add([IO.FileInfo]::new($entryPath))
        }
    }
}

$totalBytes = 0L
$digests = @($packages | Sort-Object FullName | ForEach-Object {
    $stream = [IO.File]::Open($_.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $bytes = $stream.Length
        if ($bytes -gt $MaximumPackageBytes) {
            throw "Package provenance file exceeds the $MaximumPackageBytes-byte limit."
        }
        if ($bytes -gt $MaximumTotalBytes - $totalBytes) {
            throw "Package provenance exceeds the $MaximumTotalBytes-byte aggregate limit."
        }
        $totalBytes += $bytes
        $hash = [Security.Cryptography.SHA256]::HashData($stream)
        [pscustomobject]@{
            path = [IO.Path]::GetRelativePath($relativeRoot, $_.FullName).Replace('\', '/')
            sha256 = [Convert]::ToHexString($hash).ToLowerInvariant()
            bytes = $bytes
        }
    }
    finally { $stream.Dispose() }
})

ConvertTo-Json -InputObject $digests -Depth 4 -Compress
