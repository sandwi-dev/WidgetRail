[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet(1,2)][int]$Game,
    [Parameter(Mandatory)][string]$EngineCheckout,
    [Parameter(Mandatory)][string]$RuntimeDirectory,
    [Parameter(Mandatory)][string]$Cli,
    [string]$OutputDirectory,
    [string]$EngineLockPath = (Join-Path $PSScriptRoot 'engine-lock.json'),
    [string]$Python = 'python',
    [string]$Version = '0.1.0'
)
$ErrorActionPreference='Stop'
$parsedVersion = $null
if (-not [version]::TryParse($Version, [ref]$parsedVersion) -or $parsedVersion.ToString() -cne $Version) {
    throw 'Version must use canonical dotted numeric notation.'
}
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$locks=Get-Content -LiteralPath $EngineLockPath -Raw | ConvertFrom-Json
$engineLock=$locks."poe$Game"
$actual=(& git -C $EngineCheckout rev-parse HEAD).Trim()
if($LASTEXITCODE -ne 0 -or $actual -ne $engineLock.commit){throw 'Engine checkout does not match engine-lock.json.'}
& git -C $EngineCheckout diff --quiet HEAD -- src runtime/lua runtime/zlib1.dll manifest.xml LICENSE.md
if($LASTEXITCODE -ne 0){throw 'Engine sources have tracked changes; use the unmodified pinned checkout.'}
if(-not $OutputDirectory){$OutputDirectory=Join-Path $root ('artifacts/pob/package-'+$Game+'-'+[Guid]::NewGuid().ToString('N'))}
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path $out){throw 'Use a fresh output directory; package staging is never overlaid.'}
$stage=Join-Path $out 'stage';$payload=Join-Path $stage 'payload'
New-Item -ItemType Directory -Force -Path $payload,(Join-Path $stage 'styles'),(Join-Path $stage 'assets/icons') | Out-Null
dotnet publish (Join-Path $PSScriptRoot "Applications/Poe$Game/Poe${Game}Companion.csproj") -c Release --no-self-contained --nologo -v:q "-bl:$out/publish.binlog" -o $payload
if($LASTEXITCODE -ne 0){throw 'Companion publish failed.'}
foreach($dir in @('Engine','web')){Copy-Item (Join-Path $PSScriptRoot $dir) -Destination $payload -Recurse}
Copy-Item (Join-Path $root 'src/WidgetSdk/WebSessionRuntime.js') (Join-Path $payload 'web/web-session.js')
& $Python (Join-Path $PSScriptRoot 'Build-TreeArtwork.py') --engine $EngineCheckout --runtime $RuntimeDirectory --output (Join-Path $payload 'web/art')
if($LASTEXITCODE -ne 0){throw 'Tree artwork preparation failed. Install requirements-artwork.txt into your build Python environment.'}
Copy-Item (Join-Path $PSScriptRoot 'styles/default.wrss') (Join-Path $stage 'styles/default.wrss')
Copy-Item (Join-Path $PSScriptRoot 'assets/companion.svg') (Join-Path $stage 'assets/icons/companion.svg')
$engineTarget=Join-Path $out 'engine-source';New-Item -ItemType Directory -Force -Path $engineTarget,(Join-Path $payload 'runtime'),(Join-Path $payload 'licenses') | Out-Null
# Preserve upstream code and data. Drawing-only assets and exporter tooling are not needed by headless calculations.
$tracked = & git -C $EngineCheckout ls-files -- src runtime/lua runtime/zlib1.dll
if($LASTEXITCODE -ne 0){throw 'Could not enumerate pinned engine sources.'}
foreach($relative in $tracked){
    $jewelData=$relative -like 'src/Data/TimelessJewelData/*.zip' -or $relative -like 'src/Data/TimelessJewelData/*.zip.part*'
    if(([IO.Path]::GetExtension($relative) -notin @('.lua','.bin','.jsonc') -and -not $jewelData -and $relative -ne 'runtime/zlib1.dll') -or $relative -like 'src/Export/*'){continue}
    $destination=Join-Path $engineTarget $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
    Copy-Item -LiteralPath (Join-Path $EngineCheckout $relative) -Destination $destination
}
Copy-Item (Join-Path $EngineCheckout 'manifest.xml') $engineTarget
# Stable order/timestamps preserve the engine-cache digest across widget-only updates.
$archiveStream=[IO.File]::Open((Join-Path $payload 'pob-engine.zip'),[IO.FileMode]::CreateNew)
$engineArchive=[IO.Compression.ZipArchive]::new($archiveStream,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach($file in Get-ChildItem -LiteralPath $engineTarget -File -Recurse | Sort-Object FullName){
        $relative=[IO.Path]::GetRelativePath($engineTarget,$file.FullName).Replace('\','/')
        $entry=$engineArchive.CreateEntry($relative,[IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime=[DateTimeOffset]::new(2000,1,1,0,0,0,[TimeSpan]::Zero)
        $inputStream=[IO.File]::OpenRead($file.FullName);$entryStream=$entry.Open()
        try {$inputStream.CopyTo($entryStream)}finally{$entryStream.Dispose();$inputStream.Dispose()}
    }
} finally {$engineArchive.Dispose();$archiveStream.Dispose()}
& (Join-Path $PSScriptRoot 'Split-EngineArchive.ps1') -Path (Join-Path $payload 'pob-engine.zip')
Copy-Item (Join-Path $EngineCheckout 'LICENSE.md') (Join-Path $payload 'licenses/PoB-LICENSE.md')
Copy-Item (Join-Path $PSScriptRoot 'licenses/zlib-LICENSE.txt') (Join-Path $payload 'licenses/zlib-LICENSE.txt')
foreach($name in @('luajit.exe','lua51.dll','lua-utf8.dll')){Copy-Item (Join-Path $RuntimeDirectory $name) (Join-Path $payload 'runtime')}
foreach($name in @('LuaJIT-COPYRIGHT','LuaUTF8-LICENSE')){Copy-Item (Join-Path $RuntimeDirectory $name) (Join-Path $payload 'licenses')}
Copy-Item -LiteralPath $EngineLockPath -Destination (Join-Path $payload 'engine-lock.json')
$id="widgetrail.samples.poe$Game-companion"
$widgetName="PoE$Game Path of Building (Beta)"
$manifest=[ordered]@{
    manifestVersion=1;id=$id;publisher='widgetrail.samples';name=$widgetName;version=$Version
    hostApi=@{minimum='1.0';maximumMajor=1}
    entrypoint=@{runtime='full-trust-application-v1';executable="payload/Poe${Game}Companion.exe"}
    presentation=@{icon='connection';packageIcon=@{assetId='companion.mark';colorMode='themeTint'}}
    iconAssets=@{'companion.mark'=@{path='assets/icons/companion.svg'}}
    permissions=@();optionalPermissions=@()
    intents=@{requests=@(
        @{id='widgetrail.web.open';version=1;payloadSchema=@{type='object';additionalProperties=$false;properties=@{url=@{type='string';minLength=1;maxLength=2048}};required=@('url')}},
        @{id='widgetrail.web.open';version=2;payloadSchema=@{type='object';additionalProperties=$false;properties=@{url=@{type='string';minLength=1;maxLength=8192}};required=@('url')}},
        @{id='widgetrail.video.open';version=1;payloadSchema=@{type='object';additionalProperties=$false;properties=@{provider=@{type='string';minLength=1;maxLength=64};videoId=@{type='string';minLength=1;maxLength=256};startTimeSeconds=@{type='number';minimum=0;maximum=604800}};required=@('provider','videoId')}}
    )}
    residencyPolicy=@{schemaVersion=1;mode='unload-after-idle';idleSeconds=120}
    resourceRequest=@{updateHz=4};architectures=@('x64')
}
$manifest|ConvertTo-Json -Depth 8|Set-Content (Join-Path $stage 'manifest.json') -Encoding utf8
Get-ChildItem -LiteralPath $payload -Filter '*.pdb' -File -Recurse | ForEach-Object {Remove-Item -LiteralPath $_.FullName}
& $Cli validate $stage
if($LASTEXITCODE -ne 0){throw 'Package validation failed.'}
$archive=Join-Path $out ($id+'-'+$Version+'.wrwidget')
& $Cli pack $stage --output $archive
if($LASTEXITCODE -ne 0){throw 'Package packing failed.'}
Get-FileHash -LiteralPath $archive -Algorithm SHA256
