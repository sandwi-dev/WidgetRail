[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$root=Join-Path $repo 'artifacts/discord-desktop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(!$output.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)) {
    throw 'Choose a fresh child of artifacts/discord-desktop.'
}
$ancestor=$output
while($ancestor){
    if((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Fixture staging does not accept reparse-point ancestors.'}
    $ancestor=Split-Path -Parent $ancestor
}
New-Item -ItemType Directory -Path $output | Out-Null
foreach($file in @('package.json','package-lock.json','main.cjs','protocol.cjs','surface.html')){
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $output $file)
}
Push-Location $output
try {
    & npm ci --ignore-scripts --no-audit --no-fund *> (Join-Path $output 'restore.log')
    if($LASTEXITCODE){throw 'Fixture npm restore failed; inspect restore.log.'}
    # Electron's pinned downloader validates the release archive checksum. This
    # obtains a generated test runtime only; it does not install or start Discord.
    & node node_modules/electron/install.js *> (Join-Path $output 'electron-download.log')
    if($LASTEXITCODE){throw 'Pinned Electron download failed; inspect electron-download.log.'}
} finally {Pop-Location}
$executable=Join-Path $output 'node_modules/electron/dist/electron.exe'
if(!(Test-Path -LiteralPath $executable)){throw 'Pinned Electron executable is missing.'}
$package=Get-Content -LiteralPath (Join-Path $output 'package.json') -Raw | ConvertFrom-Json
[pscustomobject]@{schemaVersion=1;electronVersion=$package.devDependencies.electron;runtime=$executable;
    executableSha256=(Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash;
    sourceFiles=@('package.json','package-lock.json','main.cjs','protocol.cjs','surface.html')|ForEach-Object{
        [pscustomobject]@{name=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $output $_) -Algorithm SHA256).Hash}};
    launched=$false;scope='Generated Electron fixture only. No Discord profile, capture, credentials or installation.'} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'provenance.json')
Write-Output $output
