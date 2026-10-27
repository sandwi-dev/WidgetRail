param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo ('artifacts/discord-webview/minified-'+[guid]::NewGuid().ToString('N'))}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a fresh backend test directory.'}
New-Item -ItemType Directory -Path $output | Out-Null
Push-Location $PSScriptRoot
try {
    & npm ci --ignore-scripts --no-audit --no-fund
    if($LASTEXITCODE){throw 'Pinned backend tools could not be restored.'}
} finally {Pop-Location}
& node "$PSScriptRoot/Build-Backend.mjs" "$output/backend.js"
if($LASTEXITCODE){throw 'Backend package composition failed.'}
$previousMode=$env:WRAIL_MINIFIED_TESTS
$previousBackend=$env:WRAIL_PACKAGED_BACKEND
try {
    $env:WRAIL_MINIFIED_TESTS='1'
    $env:WRAIL_PACKAGED_BACKEND=Join-Path $output 'backend.js'
    & node --test "$repo/samples/DiscordNativePrototype/adapter.test.mjs" "$repo/samples/DiscordWebViewWidget/web/session-backend.test.mjs"
    if($LASTEXITCODE){throw 'Minified provider behavior differs from the qualified source.'}
    & node --test "$PSScriptRoot/packaged-backend.check.mjs"
    if($LASTEXITCODE){throw 'Composed sealed backend startup/endpoint verification failed.'}
} finally {
    $env:WRAIL_MINIFIED_TESTS=$previousMode
    $env:WRAIL_PACKAGED_BACKEND=$previousBackend
}
Write-Output "Minified provider behavior and sealed payload bound verified: $output"
