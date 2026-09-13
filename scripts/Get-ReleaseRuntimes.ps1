[CmdletBinding()]
param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$lock = Get-Content (Join-Path $repository 'eng/runtime-dependencies.json') -Raw | ConvertFrom-Json
$native = [xml](Get-Content (Join-Path $repository 'src/OverlayHost/NativeDependencies.csproj') -Raw)
$nativeGameInput = @($native.Project.ItemGroup.PackageReference | Where-Object Include -EQ 'Microsoft.GameInput')[0]
if ($nativeGameInput.Version -ne $lock.gameInput.version) { throw 'GameInput build and redistributable versions differ.' }
$Destination = [IO.Path]::GetFullPath($Destination)
$runtime = Join-Path $Destination 'dotnet'
$redist = Join-Path $Destination 'prerequisites'
if ((Test-Path $runtime) -or (Test-Path $redist)) { throw 'Runtime destination already exists.' }
New-Item -ItemType Directory -Path $redist -Force | Out-Null
$archive = Join-Path $Destination 'dotnet-runtime.zip'
Invoke-WebRequest $lock.dotnet.url -OutFile $archive
if ((Get-FileHash $archive -Algorithm SHA512).Hash -ine $lock.dotnet.sha512) { throw '.NET archive checksum mismatch.' }
Expand-Archive -LiteralPath $archive -DestinationPath $runtime
Remove-Item -LiteralPath $archive
$nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
$gameInput = Join-Path $nuget "microsoft.gameinput/$($lock.gameInput.version)/redist/GameInputRedist.msi"
if ((Get-FileHash $gameInput).Hash -ine $lock.gameInput.sha256) { throw 'GameInput redistributable checksum mismatch.' }
Copy-Item -LiteralPath $gameInput -Destination $redist
$webView = Join-Path $redist 'MicrosoftEdgeWebview2Setup.exe'
Invoke-WebRequest $lock.webView2.url -OutFile $webView
if ((Get-FileHash $webView).Hash -ine $lock.webView2.sha256) { throw 'WebView2 bootstrapper checksum mismatch.' }
foreach ($file in @((Join-Path $runtime 'dotnet.exe'), (Join-Path $redist 'GameInputRedist.msi'), $webView)) {
    $signature = Get-AuthenticodeSignature -LiteralPath $file
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
        throw "Microsoft runtime signature is invalid: $file"
    }
}
Copy-Item -LiteralPath (Join-Path $repository 'eng/runtime-dependencies.json') -Destination (Join-Path $redist 'runtime-dependencies.json')
Write-Output "Verified private .NET $($lock.dotnet.version), GameInput $($lock.gameInput.version), and WebView2 bootstrapper. Nothing was installed."
