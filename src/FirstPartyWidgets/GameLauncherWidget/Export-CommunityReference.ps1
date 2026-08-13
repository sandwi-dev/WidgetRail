[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Gbar,

    [Parameter(Mandatory = $true)]
    [string]$Output
)

$ErrorActionPreference = 'Stop'

$gbarPath = [System.IO.Path]::GetFullPath($Gbar)
$outputPath = [System.IO.Path]::GetFullPath($Output)
if (-not [System.IO.File]::Exists($gbarPath)) {
    throw "gbar executable was not found: $gbarPath"
}
if ([System.IO.Directory]::Exists($outputPath) -or [System.IO.File]::Exists($outputPath)) {
    throw "Output already exists: $outputPath"
}

& $gbarPath new widget GameLauncherCommunity `
    --output $outputPath `
    --id org.gbar.community.reference.game-launcher `
    --publisher org.gbar.community.reference `
    --template multipage
if ($LASTEXITCODE -ne 0) {
    throw "gbar new failed with exit code $LASTEXITCODE."
}

$sourceRoot = Join-Path $outputPath 'src'
Remove-Item -LiteralPath (Join-Path $sourceRoot 'GameLauncherCommunity.cs')
$applicationRoot = Join-Path $sourceRoot 'Application'
$backendRoot = Join-Path $sourceRoot 'Backend'
New-Item -ItemType Directory -Force -Path $applicationRoot, $backendRoot | Out-Null
$windowsSdkVersion = '10.0.19041.57'
$windowsSdkPackage = Join-Path (
    [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) `
    ".nuget\packages\microsoft.windows.sdk.net.ref\$windowsSdkVersion\microsoft.windows.sdk.net.ref.$windowsSdkVersion.nupkg"
if (-not (Test-Path -LiteralPath $windowsSdkPackage -PathType Leaf)) {
    throw "The Windows SDK reference package $windowsSdkVersion is unavailable in the local NuGet cache. Restore the Game Launcher application once, then retry."
}
Copy-Item -LiteralPath $windowsSdkPackage `
    -Destination (Join-Path $outputPath '.gbar\packages')
$nugetConfiguration = Join-Path $outputPath 'NuGet.Config'
$nugetText = Get-Content -LiteralPath $nugetConfiguration -Raw
$nugetText = $nugetText.Replace(
    '<package pattern="GameBarAlternative.WidgetSdk" />',
    "<package pattern=`"GameBarAlternative.WidgetSdk`" />`r`n      <package pattern=`"Microsoft.Windows.SDK.NET.Ref`" />")
$nugetText | Set-Content -LiteralPath $nugetConfiguration -Encoding utf8
Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' |
    Where-Object { $_.Name -ne 'AssemblyInfo.cs' } |
    Copy-Item -Destination $sourceRoot
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Application') -Filter '*.cs' |
    Where-Object { $_.Name -ne 'AssemblyInfo.cs' } |
    Copy-Item -Destination $applicationRoot
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '..\..\WindowsAppLibraryProvider') `
    -Filter '*.cs' |
    Where-Object { $_.Name -ne 'AssemblyInfo.cs' } |
    Copy-Item -Destination $backendRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'styles\default.gbss') `
    -Destination (Join-Path $outputPath 'styles\default.gbss') -Force

$projectPath = Join-Path $outputPath 'GameLauncherCommunity.csproj'
$projectText = Get-Content -LiteralPath $projectPath -Raw
$sdkVersion = [regex]::Match($projectText, 'Version="([^"]+)"')
if (-not $sdkVersion.Success) {
    throw 'Generated scaffold omitted its exact Widget SDK package reference.'
}
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <UseAppHost>true</UseAppHost>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AssemblyName>GameLauncherApplication</AssemblyName>
    <DefineConstants>`$(DefineConstants);GAME_LAUNCHER_COMMUNITY_CORE</DefineConstants>
    <ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>
    <PathMap>`$(MSBuildProjectDirectory)=/_/source</PathMap>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="GameBarAlternative.WidgetSdk" Version="$($sdkVersion.Groups[1].Value)" />
  </ItemGroup>
  <ItemGroup>
    <Compile Remove="tests\**\*.cs" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $projectPath -Encoding utf8

@'
{
  "manifestVersion": 1,
  "id": "org.gbar.community.reference.game-launcher",
  "publisher": "org.gbar.community.reference",
  "name": "Game Launcher Community",
  "version": "0.2.0",
  "hostApi": { "minimum": "1.0", "maximumMajor": 1 },
  "entrypoint": {
    "runtime": "full-trust-application-v1",
    "executable": "payload/GameLauncherApplication.exe"
  },
  "presentation": { "icon": "play" },
  "advancedPresentation": {
    "schemaVersion": 1,
    "kind": "launcherExperience"
  },
  "permissions": [],
  "optionalPermissions": [],
  "residencyPolicy": {
    "schemaVersion": 1,
    "mode": "unload-after-idle",
    "idleSeconds": 120
  },
  "resourceRequest": { "memoryMb": 48, "updateHz": 4 },
  "architectures": [ "x64" ]
}
'@ | Set-Content -LiteralPath (Join-Path $outputPath 'manifest.json') -Encoding utf8

@'
# Game Launcher Community Reference

This self-contained full-trust Community application consumes only the packaged
public Widget SDK. Windows/Xbox and opt-in installed-store discovery, SavedId
issuance, organization state, source health, and exact launch revalidation live
inside the package. The host supplies generic lifecycle and presentation IPC;
it grants no Game Launcher capability or product-specific API.
'@ | Set-Content -LiteralPath (Join-Path $outputPath 'README.md') -Encoding utf8

Write-Host "Exported the self-contained Game Launcher Community reference to $outputPath"
