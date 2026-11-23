[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Cli,
    [Parameter(Mandatory)][string]$Renderer,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$Sources = (Join-Path $PSScriptRoot '../themes'),
    [switch]$ReuseVerifiedPackages
)
$ErrorActionPreference = 'Stop'
$Cli = (Resolve-Path -LiteralPath $Cli).Path
$Renderer = (Resolve-Path -LiteralPath $Renderer).Path
$Sources = (Resolve-Path -LiteralPath $Sources).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$null = [IO.Directory]::CreateDirectory($outputRoot)
$rendererRoot = Split-Path -Parent $Renderer
$sceneFiles = @((Join-Path $rendererRoot 'OverlayFrontend.WinUI.dll')) +
    @(Get-ChildItem -LiteralPath (Join-Path $rendererRoot 'Assets/ThemePreview') -File | Sort-Object Name | ForEach-Object FullName)
$sceneRevision = '1'
$sceneHashes = ($sceneFiles | ForEach-Object { (Get-FileHash -LiteralPath $_).Hash }) -join '/'
$previewKey = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
    [Text.Encoding]::UTF8.GetBytes("$sceneRevision/$sceneHashes"))).ToLowerInvariant()
$source = Get-Content -LiteralPath (Join-Path $Sources 'catalogue.json') -Raw | ConvertFrom-Json -AsHashtable
$indexPath = Join-Path $outputRoot 'index.json'
$publishedByScene = @{}
$publishedVersions = @{}
if ([IO.File]::Exists($indexPath)) {
    if ((Get-Item -LiteralPath $indexPath).Length -gt 2MB) { throw 'The previous catalogue index exceeds the client limit.' }
    $previous = Get-Content -LiteralPath $indexPath -Raw | ConvertFrom-Json -AsHashtable
    foreach ($theme in $previous.themes) {
        if ($theme.previewHashes) { $publishedByScene["$($theme.sha256)/$($theme.previewKey)"] = $theme.previewHashes }
        $publishedVersions["$($theme.id)/$($theme.version)"] = $theme.sha256
    }
}
$themes = [Collections.Generic.List[object]]::new()
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$scratchRoot = [IO.Path]::GetFullPath([IO.Path]::Combine([IO.Path]::GetTempPath(), 'WidgetRailThemePublisher'))
$scratch = [IO.Directory]::CreateDirectory([IO.Path]::Combine($scratchRoot, [Guid]::NewGuid().ToString('N'))).FullName
$assetNames = @('tray-480.png', 'tray-960.png', 'tray.png', 'posters.png', 'notification.png')

function Write-ImmutableFile([string]$From, [string]$To) {
    $parent = [IO.Path]::GetDirectoryName($To)
    $null = [IO.Directory]::CreateDirectory($parent)
    if ([IO.File]::Exists($To)) {
        if ((Get-FileHash -LiteralPath $From).Hash -ne (Get-FileHash -LiteralPath $To).Hash) {
            throw "Published content at $To has a different digest."
        }
        return
    }
    $pending = [IO.Path]::Combine($parent, [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::Copy($From, $pending)
        [IO.File]::Move($pending, $To)
    } finally {
        if ([IO.File]::Exists($pending)) { [IO.File]::Delete($pending) }
    }
}

function Test-ListingText($Value, [int]$MaximumLength) {
    return $Value -is [string] -and $Value.Length -le $MaximumLength -and
        ![string]::IsNullOrWhiteSpace($Value) -and $Value -cnotmatch '\p{Cc}'
}

function Test-ArchivedSources([string]$PackageRoot, [string]$Archive, [string]$Digest) {
    # Reuse only the previously verified archive with an exact, case-sensitive
    # match for every source path and file. The packer handles any mismatch.
    try {
        for ($directory = [IO.DirectoryInfo]::new($PackageRoot); $directory; $directory = $directory.Parent) {
            if ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) { return $false }
        }
        $archiveFile = Get-Item -LiteralPath $Archive
        if ($archiveFile.Attributes -band [IO.FileAttributes]::ReparsePoint -or $archiveFile.Length -gt 4MB -or
            (Get-FileHash -LiteralPath $Archive).Hash -ine $Digest) { return $false }

        $contents = [IO.Compression.ZipFile]::OpenRead($Archive)
        try {
            $entries = [Collections.Generic.Dictionary[string,IO.Compression.ZipArchiveEntry]]::new([StringComparer]::Ordinal)
            if ($contents.Entries.Count -gt 65 -or ($contents.Entries | Measure-Object Length -Sum).Sum -gt 4MB) { return $false }
            foreach ($entry in $contents.Entries) {
                if (!$entries.TryAdd($entry.FullName, $entry)) { return $false }
            }
            foreach ($path in Get-ChildItem -LiteralPath $PackageRoot -Recurse -Force) {
                if ($path.Attributes -band [IO.FileAttributes]::ReparsePoint) { return $false }
                if ($path.PSIsContainer) { continue }
                $relative = [IO.Path]::GetRelativePath($PackageRoot, $path.FullName).Replace('\', '/')
                $entry = $null
                if (!$entries.Remove($relative, [ref]$entry) -or $path.Length -ne $entry.Length) { return $false }
                $stream = $entry.Open()
                try {
                    if ((Get-FileHash -InputStream $stream).Hash -cne (Get-FileHash -LiteralPath $path.FullName).Hash) { return $false }
                } finally { $stream.Dispose() }
            }
            return $entries.Count -eq 0
        } finally { $contents.Dispose() }
    } catch [IO.IOException], [IO.InvalidDataException], [UnauthorizedAccessException], [Management.Automation.ItemNotFoundException] {
        return $false
    }
}

function Get-PreviewDigest([string]$Path, [string]$Asset) {
    $header = [byte[]]::new(24)
    $stream = [IO.File]::OpenRead($Path)
    try {
        if ($stream.Length -lt $header.Length -or $stream.Length -gt 2MB) {
            throw "Preview is outside the file size limit: $Path"
        }
        $stream.ReadExactly($header)
    } finally { $stream.Dispose() }
    $width = switch ($Asset) { 'tray-480.png' { 480 } 'tray-960.png' { 960 } default { 1920 } }
    if ([Convert]::ToHexString($header, 0, 16) -cne '89504E470D0A1A0A0000000D49484452' -or
        [Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($header, 16)) -ne $width -or
        [Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($header, 20)) -ne $width * 9 / 16) {
        throw "Preview must be a $($width)-pixel-wide 16:9 PNG: $Path"
    }
    return (Get-FileHash -LiteralPath $Path).Hash.ToLowerInvariant()
}

try {
    if ($source.schemaVersion -ne 1 -or $source.themes -isnot [array] -or $source.themes.Count -gt 1000 -or
        $source.collections -isnot [array] -or $source.collections.Count -gt 32) {
        throw 'Invalid catalogue inventory.'
    }
    foreach ($entry in $source.themes) {
        $id = $entry.id
        $version = $entry.version
        if ($id -isnot [string] -or $id.Length -gt 128 -or $id -cnotmatch '^[a-z0-9]+([.-][a-z0-9]+)*$' -or
            $version -isnot [string] -or $version -cnotmatch '^[0-9]+\.[0-9]+\.[0-9]+$' -or !$seen.Add($id)) {
            throw "Invalid catalogue identity $id $version."
        }
        $released = [DateOnly]::MinValue
        if (!(Test-ListingText $entry.creator 80) -or !(Test-ListingText $entry.description 240) -or
            $entry.tags -isnot [array] -or $entry.tags.Count -gt 8 -or
            $entry.released -isnot [string] -or
            ![DateOnly]::TryParseExact($entry.released, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::None, [ref]$released) -or $released -eq [DateOnly]::MinValue) {
            throw "Invalid catalogue metadata for $id."
        }
        foreach ($tag in $entry.tags) {
            if (!(Test-ListingText $tag 40)) { throw "Invalid catalogue tag for $id." }
        }
    }
    if ($source.featured -isnot [string] -or !$seen.Contains($source.featured)) { throw 'The featured theme is absent.' }
    foreach ($collection in $source.collections) {
        if (!(Test-ListingText $collection.name 64) -or $collection.themes -isnot [array] -or $collection.themes.Count -gt 1000) {
            throw 'Invalid catalogue collection.'
        }
        $members = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($id in $collection.themes) {
            if ($id -isnot [string] -or !$seen.Contains($id) -or !$members.Add($id)) { throw "Invalid collection entry $id." }
        }
    }

    foreach ($entry in $source.themes) {
        $id = $entry.id
        $version = $entry.version
        $packageRoot = Join-Path (Join-Path $Sources $id) $version
        $job = [IO.Directory]::CreateDirectory((Join-Path $scratch $id)).FullName
        $archive = Join-Path $job 'package.wrtheme'
        $publishedDigest = $publishedVersions["$id/$version"]
        $reuse = $false
        if ($ReuseVerifiedPackages -and $publishedDigest -cmatch '^[0-9a-f]{64}$') {
            $publishedArchive = Join-Path $outputRoot "packages/$publishedDigest.wrtheme"
            $reuse = Test-ArchivedSources $packageRoot $publishedArchive $publishedDigest
        }
        if ($reuse) {
            $archive = $publishedArchive
            $digest = $publishedDigest
            Write-Verbose "Reused verified package $id $version."
        } else {
            & $Cli theme pack $packageRoot --output $archive
            if ($LASTEXITCODE) { throw "Theme packaging failed for $id." }
            $digest = (Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()
            if ($publishedDigest -and $publishedDigest -cne $digest) {
                throw "Package bytes changed for the published theme $id $version. Increase its version before publishing."
            }
        }
        $contents = [IO.Compression.ZipFile]::OpenRead($archive)
        try {
            $manifestEntry = $contents.GetEntry('theme.json')
            if (!$manifestEntry -or $manifestEntry.Length -gt 64KB) { throw "Invalid archived manifest for $id." }
            $reader = [IO.StreamReader]::new($manifestEntry.Open())
            try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json }
            finally { $reader.Dispose() }
        } finally { $contents.Dispose() }
        if ($manifest.id -cne $id -or $manifest.version -cne $version) { throw "Manifest mismatch for $id." }

        $images = Join-Path $outputRoot "previews/$digest/$previewKey"
        if (@($assetNames | Where-Object { ![IO.File]::Exists((Join-Path $images $_)) }).Count) {
            if ($reuse) { Copy-Item -LiteralPath $archive -Destination (Join-Path $job 'package.wrtheme') }
            @{id=$id; version=$version; sha256=$digest} | ConvertTo-Json | Set-Content -Encoding utf8NoBOM -LiteralPath (Join-Path $job 'job.json')
            $resultPath = Join-Path $job 'result.json'
            $process = Start-Process -FilePath $Renderer -ArgumentList '--validate-styles', '--validation-platform-activation',
                ('"--render-catalogue-theme=' + $job + '"') -WindowStyle Hidden -PassThru
            try {
                $timer = [Diagnostics.Stopwatch]::StartNew()
                while (![IO.File]::Exists($resultPath) -and !$process.HasExited -and $timer.Elapsed.TotalSeconds -lt 60) {
                    Start-Sleep -Milliseconds 100
                }
                if (![IO.File]::Exists($resultPath)) { throw "Renderer did not finish $id." }
                $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
                if (!$result.passed) { throw "Renderer rejected $id. $($result.error)" }
            } finally {
                if (!$process.HasExited) {
                    & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $process.Id
                    if (!$process.WaitForExit(10000)) { throw "Renderer did not close for $id." }
                }
                $process.Dispose()
            }
            foreach ($asset in $assetNames) {
                $image = Join-Path $job $asset
                $null = Get-PreviewDigest $image $asset
                Write-ImmutableFile $image (Join-Path $images $asset)
            }
        }
        $previewHashes = [ordered]@{}
        $previousPreviewHashes = $publishedByScene["$digest/$previewKey"]
        foreach ($asset in $assetNames) {
            $verified = Get-PreviewDigest (Join-Path $images $asset) $asset
            if ($previousPreviewHashes -and $previousPreviewHashes.Contains($asset) -and $previousPreviewHashes[$asset] -cne $verified) {
                throw "Published preview has a different digest for $id/$asset."
            }
            $previewHashes[$asset] = $verified
        }
        if (!$reuse) { Write-ImmutableFile $archive (Join-Path $outputRoot "packages/$digest.wrtheme") }
        $themes.Add([ordered]@{id=$id; version=$version; name=$manifest.name; publisher=$manifest.publisher; creator=$entry.creator
            description=$entry.description; tags=$entry.tags; released=$entry.released; sha256=$digest
            bytes=(Get-Item -LiteralPath $archive).Length; previewKey=$previewKey; previewHashes=$previewHashes})
    }
    $index = [ordered]@{schemaVersion=1; featured=$source.featured; collections=$source.collections; themes=$themes}
    $pendingIndex = Join-Path $outputRoot ([Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $encoding = [Text.UTF8Encoding]::new($false)
        $json = $index | ConvertTo-Json -Depth 7 -Compress
        if ($encoding.GetByteCount($json) -gt 2MB) { throw 'The catalogue index exceeds the client limit.' }
        [IO.File]::WriteAllText($pendingIndex, $json, $encoding)
        [IO.File]::Move($pendingIndex, $indexPath, $true)
    } finally {
        if ([IO.File]::Exists($pendingIndex)) { [IO.File]::Delete($pendingIndex) }
    }
} finally {
    if ([IO.Directory]::Exists($scratch) -and [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($scratch)) -eq $scratchRoot) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
}
