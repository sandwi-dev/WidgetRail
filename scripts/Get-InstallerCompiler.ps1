[CmdletBinding()]
param([string]$Destination = (Join-Path $PSScriptRoot '../artifacts/tools/installer-compiler'))
$ErrorActionPreference = 'Stop'
$Destination = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $Destination) { throw 'Compiler destination already exists. Use a fresh directory.' }
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$archive = Join-Path $Destination 'compiler.zip'
Invoke-WebRequest 'https://api.nuget.org/v3-flatcontainer/tools.innosetup/6.7.3/tools.innosetup.6.7.3.nupkg' -OutFile $archive
if ((Get-FileHash -LiteralPath $archive).Hash -ne 'F780898E402FF80612CC8D9FCB8C6E02932BD1CB4C900FFDAA31F9341CFB49F4') {
    throw 'Compiler archive checksum differs. It was not extracted or executed.'
}
Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $Destination 'package')
$compiler = Join-Path $Destination 'package/tools/ISCC.exe'
if ((Get-AuthenticodeSignature -LiteralPath $compiler).Status -ne 'Valid') { throw 'Compiler signature is invalid.' }
Write-Output $compiler
