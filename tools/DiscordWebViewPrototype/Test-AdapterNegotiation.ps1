param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo ('artifacts/discord-webview/adapter-negotiation-'+[guid]::NewGuid().ToString('N'))}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Choose a fresh output directory; no existing browser profile is reused.'}
if(@(Get-ChildItem Env:WEBVIEW2*).Count){throw 'WebView2 environment overrides are present; no default-runtime test was started.'}
[xml]$dependencies=Get-Content (Join-Path $repo 'src/OverlayPlatformInterop/NativeDependencies.csproj')
$version=($dependencies.Project.ItemGroup.PackageReference | Where-Object Include -eq 'Microsoft.Web.WebView2').Version
if(!$version){throw 'The repository WebView2 SDK version was not found.'}
$packages=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages'}
$sdk=Join-Path $packages "microsoft.web.webview2/$version"
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$files=@((Join-Path $sdk 'lib/net462/Microsoft.Web.WebView2.Core.dll'),(Join-Path $sdk 'lib/net462/Microsoft.Web.WebView2.WinForms.dll'),(Join-Path $sdk 'runtimes/win-x64/native/WebView2Loader.dll'))
foreach($path in @($compiler)+$files){if(!(Test-Path -LiteralPath $path)){throw 'An existing compiler or pinned WebView2 SDK dependency is missing; no installation was attempted.'}}
$adapter=Join-Path $repo 'samples/DiscordNativePrototype/adapter.js'
$contract=Get-Content (Join-Path $repo 'samples/DiscordNativePrototype/adapter-contract.json') -Raw|ConvertFrom-Json
if($contract.build -isnot [long] -and $contract.build -isnot [int]){throw 'Invalid adapter build.'}
New-Item -ItemType Directory -Path $output|Out-Null
foreach($path in $files){Copy-Item -LiteralPath $path -Destination $output}
Copy-Item -LiteralPath $adapter -Destination (Join-Path $output 'adapter.js')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'negotiation-probe.js') -Destination $output
[IO.File]::WriteAllText((Join-Path $output 'configuration.js'),('globalThis.negotiationProbeBuild='+$contract.build+';'))
[IO.File]::WriteAllText((Join-Path $output 'probe.html'),@'
<!doctype html><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'self'; script-src 'self'; connect-src 'none'; media-src 'self' blob:"><title>Generated WebRTC negotiation</title><body><script src="configuration.js"></script><script src="adapter.js"></script><script src="negotiation-probe.js"></script></body>
'@)
@{adapterBuild=$contract.build;adapterSha256=(Get-FileHash -LiteralPath $adapter -Algorithm SHA256).Hash;fixtureSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'negotiation-probe.js') -Algorithm SHA256).Hash;generatedMediaOnly=$true;externalIceServers=$false}|ConvertTo-Json|Set-Content (Join-Path $output 'provenance.json')
$exe=Join-Path $output 'WebViewNegotiationProbe.exe'
& $compiler /nologo /target:winexe /platform:x64 "/out:$exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "/r:$($files[0])" "/r:$($files[1])" (Join-Path $PSScriptRoot 'WebViewNegotiationProbe.cs') *> (Join-Path $output 'compile.log')
if($LASTEXITCODE){throw "Negotiation fixture compilation failed; see $output/compile.log"}
$process=Start-Process -FilePath $exe -ArgumentList ('"'+$output+'"') -WindowStyle Hidden -PassThru
if(!$process.WaitForExit(60000)){
    $process.Kill($true)
    [void]$process.WaitForExit(5000)
    throw 'The generated negotiation fixture timed out; its own process tree was stopped.'
}
$resultPath=Join-Path $output 'result.json'
if(!(Test-Path -LiteralPath $resultPath)){throw 'The negotiation fixture did not produce evidence.'}
$result=Get-Content -LiteralPath $resultPath -Raw|ConvertFrom-Json
$result|ConvertTo-Json -Depth 10
if(!$result.passed -or !$result.browserExitObserved){throw 'The real-WebView2 negotiation check did not fully pass; inspect its bounded result.'}
