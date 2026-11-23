# Shared exact-file admission for the diagnostic installer and launcher.
# Dot-source only; these helpers do not modify files or start processes.
function Full([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { throw 'An explicit path is required.' }
    return [IO.Path]::GetFullPath($Value)
}
function Same([string]$Left,[string]$Right) {
    return [string]::Equals($Left,$Right,[StringComparison]::OrdinalIgnoreCase)
}
function Assert-PlainPath([string]$Path) {
    $current=$Path
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and
            ((Get-Item -LiteralPath $current).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Reparse path is not admitted: $current"
        }
        $current=Split-Path -Parent $current
    }
}
function Hash([string]$Path) {
    Assert-PlainPath $Path
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Expected file is missing: $Path" }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}
function Assert-Hash([string]$Path,[string]$Expected) {
    if ($Expected -notmatch '^[A-Fa-f0-9]{64}$' -or !(Same (Hash $Path) $Expected)) { throw "File hash differs from reviewed bytes: $Path" }
}
function Read-PinnedJson([string]$Path,[string]$Expected) {
    Assert-Hash $Path $Expected
    if ((Get-Item -LiteralPath $Path).Length -gt 262144) { throw 'Metadata exceeds the bounded size.' }
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -Depth 20
}
function Child([string]$Root,[string]$Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or
        $Relative.Contains(':') -or ($Relative -split '[\\/]') -contains '..') { throw 'Invalid relative artifact path.' }
    $path=Full (Join-Path $Root $Relative)
    if (!$path.StartsWith($Root.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Artifact path escapes its root.' }
    Assert-PlainPath $path
    return $path
}
function Read-QualifiedDesktopProbeStage([string]$Stage,[string]$ExpectedProvenanceSha256) {
    $stage=Full $Stage
    $provenancePath=Join-Path $stage 'provenance.json'
    $provenance=Read-PinnedJson $provenancePath $ExpectedProvenanceSha256
    if ($provenance.schemaVersion -ne 1 -or !$provenance.buildCompleted -or !$provenance.bundleScopeVerified -or !$provenance.typecheckPassed) { throw 'Staged build has not passed required qualification.' }
    # The bootstrap embeds absolute runtime paths. Identical copied artifacts
    # do not prove that the original location it will load has the same bytes.
    if (!(Same (Full $provenance.outputDirectory) $stage)) { throw 'Staged build was relocated; regenerate and review its absolute bootstrap references.' }
    if (!(Same (Full $provenance.bootstrapProposal.dataDirectory) (Join-Path $stage 'vencord-data'))) { throw 'Vencord data directory is not the reviewed stage-owned directory.' }
    Assert-PlainPath $provenance.bootstrapProposal.dataDirectory
    $bootstrap=Child $stage $provenance.bootstrapProposal.archivePath
    Assert-Hash $bootstrap $provenance.bootstrapProposal.archiveSha256
    $artifactPaths=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    if (@($provenance.artifacts).Count -lt 4 -or @($provenance.artifacts).Count -gt 16) { throw 'Unexpected build artifact inventory.' }
    foreach ($artifact in $provenance.artifacts) {
        if ($artifact.path -notmatch '^source/dist/(patcher|preload|renderer)\.[a-z.]+$' -or !$artifactPaths.Add($artifact.path)) { throw 'Unexpected or duplicate desktop build artifact.' }
        $path=Child $stage $artifact.path
        Assert-Hash $path $artifact.sha256
        if ((Get-Item -LiteralPath $path).Length -ne $artifact.bytes) { throw 'Artifact size changed.' }
    }
    foreach ($required in @('source/dist/patcher.js','source/dist/preload.js','source/dist/renderer.js','source/dist/renderer.css')) {
        if (!$artifactPaths.Contains($required)) { throw "Required artifact missing: $required" }
    }
    return [pscustomobject]@{directory=$stage;provenance=$provenance;provenancePath=$provenancePath;bootstrap=$bootstrap}
}
