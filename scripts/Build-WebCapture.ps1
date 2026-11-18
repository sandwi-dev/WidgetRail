[CmdletBinding()]
param([string]$OutputDirectory, [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = Split-Path $PSScriptRoot
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repository 'artifacts/winui-webcapture/Release' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $output | Out-Null
$dependencies = Join-Path $repository 'src/OverlayPlatformInterop/NativeDependencies.csproj'
if (!$NoRestore) {
    & dotnet restore $dependencies "-bl:$output/native-dependencies-{}.binlog"
    if ($LASTEXITCODE) { throw 'Native capture dependency restore failed.' }
}
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
$project = [xml](Get-Content -LiteralPath $dependencies -Raw)
$version = [string]$project.SelectSingleNode('//PackageReference[@Include="Microsoft.Web.WebView2"]').Version
$webView = Join-Path $packages "microsoft.web.webview2/$version"
# Same worker sources as the validation build, with test flags and source
# generators compiled out. This entry point never launches a capture process.
& (Join-Path $repository 'tools/DiscordWebViewPrototype/Build-Native.ps1') -OutputDirectory $output -WebViewPackageDirectory $webView
foreach ($file in @('WebRtcCaptureWorker.exe', 'CaptureWorkerHost.dll', 'WebRtcCapturePrototype.dll', 'ReplayCaptureWorker.exe', 'ReplayWorkerHost.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $output $file))) { throw "Native capture build omitted $file" }
}
