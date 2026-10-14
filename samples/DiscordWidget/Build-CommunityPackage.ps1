param([ValidateSet('Debug','Release')][string]$Configuration='Release', [string]$OutputDirectory)
& "$PSScriptRoot/../../scripts/Build-MarketplacePackage.ps1" -Widget Discord -Configuration $Configuration -OutputDirectory $OutputDirectory
