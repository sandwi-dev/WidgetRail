[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'VerificationRunner.psm1') -Force
$manifest = Get-Content (Join-Path $PSScriptRoot 'verification-steps.json') -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) { throw 'Unsupported verification manifest.' }
$shards = Get-ManagedVerificationShardMap -Steps @($manifest.steps)
@{ shard = @($shards.Keys) } | ConvertTo-Json -Compress
