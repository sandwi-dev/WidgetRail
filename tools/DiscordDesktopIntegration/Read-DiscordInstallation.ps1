[CmdletBinding()]
param(
    [string[]]$InstallationRoot = @(),
    [switch]$IncludeFileHashes,
    [switch]$SkipRunningProcesses
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Read only application executable/resource metadata. Do not enumerate user-data,
# module caches, settings, cookies, Local Storage, or any account profile.
if (!$InstallationRoot.Count) {
    $InstallationRoot = @('Discord', 'DiscordPTB', 'DiscordCanary') | ForEach-Object {
        Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) $_
    }
}
$running = @()
if (!$SkipRunningProcesses) {
    $running = @(Get-CimInstance Win32_Process -Filter "Name='Discord.exe' OR Name='DiscordPTB.exe' OR Name='DiscordCanary.exe'" |
        Where-Object ExecutablePath | Select-Object ProcessId, ExecutablePath, CreationDate)
}

function Read-ResourceFile([string]$Path) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $item = Get-Item -LiteralPath $Path
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        return [pscustomobject]@{ name=$item.Name; bytes=$null; sha256=$null; state='reparse-point-not-read' }
    }
    [pscustomobject]@{
        name=$item.Name
        bytes=$item.Length
        sha256=if ($IncludeFileHashes) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash } else { $null }
        state='observed'
    }
}

$installations = [Collections.Generic.List[object]]::new()
foreach ($candidate in ($InstallationRoot | Sort-Object -Unique)) {
    $root = [IO.Path]::GetFullPath($candidate)
    if (!(Test-Path -LiteralPath $root -PathType Container)) { continue }
    if ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Installation root is a reparse point; choose its explicit target: $root"
    }
    $versions = @(Get-ChildItem -LiteralPath $root -Directory | Where-Object Name -Match '^app-\d+(?:\.\d+){1,3}$')
    if ($versions.Count -gt 64) { throw 'Installation inventory exceeds the bounded version limit.' }
    foreach ($version in ($versions | Sort-Object Name)) {
        if ($version.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
        $executables = @('Discord.exe', 'DiscordPTB.exe', 'DiscordCanary.exe') |
            ForEach-Object { Join-Path $version.FullName $_ } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
        foreach ($executable in $executables) {
            if ((Get-Item -LiteralPath $executable).Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
            $resources = Join-Path $version.FullName 'resources'
            if ((Test-Path -LiteralPath $resources) -and
                ((Get-Item -LiteralPath $resources).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "Resource directory is a reparse point: $resources"
            }
            $asar = Read-ResourceFile (Join-Path $resources 'app.asar')
            $backup = Read-ResourceFile (Join-Path $resources '_app.asar')
            $directoryBootstrap = Test-Path -LiteralPath (Join-Path $resources 'app') -PathType Container
            $processIds = @($running | Where-Object { [string]::Equals($_.ExecutablePath, $executable, [StringComparison]::OrdinalIgnoreCase) } |
                Select-Object -ExpandProperty ProcessId)
            $processIdentities = @($running | Where-Object { [string]::Equals($_.ExecutablePath, $executable, [StringComparison]::OrdinalIgnoreCase) } |
                ForEach-Object { [pscustomobject]@{processId=$_.ProcessId;createdUtc=if ($_.CreationDate) {$_.CreationDate.ToUniversalTime().ToString('O')}else{$null}} })
            $installations.Add([pscustomobject]@{
                installationRoot=$root
                versionDirectory=$version.Name
                executable=$executable
                resources=$resources
                runningProcessIds=$processIds
                runningProcesses=$processIdentities
                running=($processIds.Count -gt 0)
                appAsar=$asar
                originalBackup=$backup
                directoryBootstrapPresent=$directoryBootstrap
                loaderState=if ($backup -or $directoryBootstrap) { 'modified-layout-requires-review' }
                    elseif ($asar) { 'asar-present-unclassified' } else { 'unsupported-layout' }
            })
        }
    }
}
$active = @($installations | Where-Object running)
[pscustomobject]@{
    schemaVersion=1
    capturedUtc=[DateTimeOffset]::UtcNow.ToString('O')
    installations=$installations.ToArray()
    runningInstallationCount=$active.Count
    suggestedRunningExecutable=if ($active.Count -eq 1) { $active[0].executable } else { $null }
    scope='Read-only executable/resource inventory. Loader identity, account state, installation compatibility and permission to modify are not established.'
} | ConvertTo-Json -Depth 7
