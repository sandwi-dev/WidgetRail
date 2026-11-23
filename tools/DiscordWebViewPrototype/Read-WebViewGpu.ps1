param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo ('artifacts/discord-webview/gpu-capabilities-'+[guid]::NewGuid().ToString('N'))}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Choose a fresh output directory; existing profiles are never reused.'}
if(@(Get-ChildItem Env:WEBVIEW2*).Count){throw 'WebView2 environment overrides are present; default-runtime capability probe refused.'}
[xml]$dependencies=Get-Content (Join-Path $repo 'src/OverlayPlatformInterop/NativeDependencies.csproj')
$version=($dependencies.Project.ItemGroup.PackageReference | Where-Object Include -eq 'Microsoft.Web.WebView2').Version
if(!$version){throw 'The repository WebView2 SDK version was not found.'}
$packages=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages'}
$sdk=Join-Path $packages "microsoft.web.webview2/$version"
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$files=@((Join-Path $sdk 'lib/net462/Microsoft.Web.WebView2.Core.dll'),(Join-Path $sdk 'lib/net462/Microsoft.Web.WebView2.WinForms.dll'),(Join-Path $sdk 'runtimes/win-x64/native/WebView2Loader.dll'))
foreach($path in @($compiler)+$files){if(!(Test-Path -LiteralPath $path)){throw 'An existing .NET Framework compiler or pinned WebView2 SDK dependency is unavailable. No install was attempted.'}}
New-Item -ItemType Directory -Path $output | Out-Null
foreach($path in $files){Copy-Item -LiteralPath $path -Destination $output}
$exe=Join-Path $output 'WebViewGpuProbe.exe'
& $compiler /nologo /target:winexe /platform:x64 "/out:$exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "/r:$($files[0])" "/r:$($files[1])" (Join-Path $PSScriptRoot 'WebViewGpuProbe.cs') *> (Join-Path $output 'compile.log')
if($LASTEXITCODE){throw "GPU capability probe compilation failed; see $output/compile.log"}
$readScript=Join-Path $PSScriptRoot 'webview-gpu-read.js'
$encodeScript=Join-Path $PSScriptRoot 'webview-encode-capabilities.js'
$process=Start-Process -FilePath $exe -ArgumentList @(('"'+$output+'"'),('"'+$readScript+'"'),('"'+$encodeScript+'"')) -WindowStyle Hidden -PassThru
if(!$process.WaitForExit(50000)){
    $process.Kill($true)
    [void]$process.WaitForExit(5000)
    throw 'GPU capability probe timed out; the runner-owned process tree was stopped.'
}
$resultPath=Join-Path $output 'gpu-capabilities.json'
if(!(Test-Path -LiteralPath $resultPath)){throw 'GPU capability probe did not produce a report.'}
$result=Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
$result | ConvertTo-Json -Depth 12
if(!$result.success){throw 'The runtime did not expose GPU diagnostics; no flags or system settings were changed.'}
