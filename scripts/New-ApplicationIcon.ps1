[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$tooling = Join-Path $PSScriptRoot 'branding'
if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    throw 'Install Node.js 18 or later to regenerate branding assets.'
}
if (-not (Test-Path -LiteralPath (Join-Path $tooling 'node_modules/@resvg/resvg-js/package.json'))) {
    throw 'Install the pinned development-only renderer first: npm.cmd ci --prefix scripts/branding'
}
& node (Join-Path $tooling 'render.cjs')
if ($LASTEXITCODE -ne 0) { throw "Icon generation failed (exit $LASTEXITCODE)." }
