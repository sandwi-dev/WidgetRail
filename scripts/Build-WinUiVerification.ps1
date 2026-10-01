[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$OutputDirectory,
    [switch]$NoRestore
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = Split-Path $PSScriptRoot
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repository ('artifacts/winui-verification/' + [guid]::NewGuid().ToString('N')) }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh WinUI verification output directory.' }
$logs = Join-Path $output 'logs'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$restored = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
function Invoke-NuGetAuditedRestore([string]$Project, [string[]]$Properties = @()) {
    if ($NoRestore) { return }
    $resolvedProject = [IO.Path]::GetFullPath((Join-Path $repository $Project))
    $key = $resolvedProject + '|' + ($Properties -join '|')
    if (!$restored.Add($key)) { return }
    $name = [IO.Path]::GetFileNameWithoutExtension($Project)
    & dotnet restore $resolvedProject --nologo @Properties "-bl:$logs/$name-restore.binlog"
    if ($LASTEXITCODE -ne 0) { throw "Audited restore failed: $Project" }
}
Invoke-NuGetAuditedRestore -Project 'src/OverlayPlatformInterop/NativeDependencies.csproj'
$platform = Join-Path $output 'platform'
$preview = Join-Path $output 'preview'
& (Join-Path $PSScriptRoot 'Build-OverlayPlatform.ps1') -Configuration $Configuration -OutputDirectory $platform -NoRestore -TestPolicy -TestForeground -TestProcessLifecycle
& (Join-Path $PSScriptRoot 'Build-WinUiWindowPreview.ps1') -Configuration $Configuration -OutputDirectory $preview -TestPolicy
$Project = 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj'
# Always qualify the shipped trimmed Release frontend, even when native policy checks use Debug.
$properties = @('-p:Configuration=Release','-p:RuntimeIdentifier=win-x64','-p:SelfContained=true',
    '-p:Platform=x64','-p:EnableWidgetValidation=false','-p:ContinuousIntegrationBuild=true',
    "-p:OverlayPlatformInteropPath=$platform/OverlayPlatformInterop.dll", "-p:WindowPreviewNativePath=$preview/WinUiWindowPreviewNative.dll")
Invoke-NuGetAuditedRestore -Project $Project -Properties $properties
$published = Join-Path $output 'published'
Push-Location $repository
try {
    & dotnet publish $Project --no-restore @properties -o $published -m:1 -nr:false "-bl:$logs/frontend-publish.binlog"
    if ($LASTEXITCODE -ne 0) { throw 'Trimmed WinUI publication failed.' }
} finally { Pop-Location }
foreach ($native in @(@('OverlayPlatformInterop.dll',$platform),@('WinUiWindowPreviewNative.dll',$preview))) {
    if ((Get-FileHash (Join-Path $published $native[0])).Hash -cne (Get-FileHash (Join-Path $native[1] $native[0])).Hash) { throw "Stale native publication: $($native[0])" }
}
foreach ($required in @('OverlayFrontend.WinUI.exe','OverlayFrontend.WinUI.dll','OverlayFrontend.WinUI.pri','coreclr.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $published $required))) { throw "Missing WinUI runtime asset: $required" }
}
$runtime = Get-Content (Join-Path $published 'OverlayFrontend.WinUI.runtimeconfig.json') -Raw | ConvertFrom-Json
if (!($runtime.runtimeOptions.includedFrameworks | Where-Object { $_.name -ceq 'Microsoft.NETCore.App' -and $_.version -like '10.*' })) { throw 'Frontend must carry its .NET 10 runtime.' }
$retired = @(Get-ChildItem -LiteralPath $published -Recurse -File | Where-Object { $_.Name -in @('OverlayHost.exe','ArtworkDecoderHost.exe') -or $_.Name -match '(?i)taffy' })
if ($retired.Count) { throw "Retired renderer artifact: $($retired[0].FullName)" }
Invoke-NuGetAuditedRestore -Project 'tools/ReleaseVerifier/ReleaseVerifier.csproj' -Properties @('-p:Configuration=Release','-p:RuntimeIdentifier=win-x64','-p:SelfContained=false')
$verifier = Join-Path $output 'verifier'
& dotnet publish (Join-Path $repository 'tools/ReleaseVerifier/ReleaseVerifier.csproj') --no-restore -c Release -r win-x64 --self-contained false -o $verifier -m:1 -nr:false '-p:ContinuousIntegrationBuild=true' "-bl:$logs/verifier-publish.binlog"
if ($LASTEXITCODE -ne 0) { throw 'Publication verifier build failed.' }
& (Join-Path $verifier 'ReleaseVerifier.exe') --frontend $published *> (Join-Path $logs 'frontend-cleanup-verification.txt')
if ($LASTEXITCODE -ne 0) { throw "Published frontend cleanup verification failed. See $logs/frontend-cleanup-verification.txt" }
[ordered]@{ schemaVersion=1; frontendConfiguration='Release'; nativeConfiguration=$Configuration; published=$published; publishedAsyncCleanupVerified=$true; registered=$false; launched=$false; retiredArtifacts=0 } |
    ConvertTo-Json | Set-Content (Join-Path $output 'verification.json')
Write-Output "WinUI native policies and trimmed publication verified: $output"
