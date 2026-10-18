param([ValidateSet('Debug','Release')][string]$Configuration='Release', [string]$OutputDirectory)
& "$PSScriptRoot/../../scripts/Build-MarketplacePackage.ps1" -Widget MapGenie -Configuration $Configuration -OutputDirectory $OutputDirectory
