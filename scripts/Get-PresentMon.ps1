[CmdletBinding()]
param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dependency = (Get-Content -LiteralPath (Join-Path $repository 'eng/runtime-dependencies.json') -Raw | ConvertFrom-Json).presentMon
$destinationPath = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $destinationPath) {
    if ((Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash -cne $dependency.sha256) { throw 'PresentMon checksum mismatch.' }
    Write-Output $destinationPath
    return
}
New-Item -ItemType Directory -Path (Split-Path $destinationPath) -Force | Out-Null
$partial = $destinationPath + '.' + [guid]::NewGuid().ToString('N') + '.partial'
try {
    Invoke-WebRequest -Uri $dependency.url -OutFile $partial -TimeoutSec 120
    if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -cne $dependency.sha256) { throw 'PresentMon checksum mismatch.' }
    Move-Item -LiteralPath $partial -Destination $destinationPath
} finally {
    if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial }
}
Write-Output $destinationPath
