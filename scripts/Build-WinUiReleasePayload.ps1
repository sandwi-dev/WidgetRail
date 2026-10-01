[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'WinUiPublication.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'ReleasePackaging.psm1') -Force -DisableNameChecking
$publication=New-WinUiPublishedPayload -OutputDirectory $OutputDirectory
Write-ReleaseJson (Join-Path $OutputDirectory 'payload.json') $publication
Write-Output (Join-Path $OutputDirectory 'payload.json')
