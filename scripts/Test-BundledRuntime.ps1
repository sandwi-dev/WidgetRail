[CmdletBinding()]
param([Parameter(Mandatory)][string]$RuntimeRoot)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('wrail-runtime-' + [guid]::NewGuid().ToString('N'))
$worker = Join-Path $fixture 'runtime/Tests'
New-Item -ItemType Directory -Path $worker -Force | Out-Null
Copy-Item -LiteralPath $RuntimeRoot -Destination (Join-Path $fixture 'dotnet') -Recurse
$output = Join-Path $repository 'tests/WidgetRuntime.Tests/bin/Release/net8.0'
Get-ChildItem -LiteralPath $output | Copy-Item -Destination $worker -Recurse
foreach ($prefix in @('Bundled runtime', 'Community workers have package-specific AppContainer authority')) {
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $worker 'WidgetRuntime.Tests.exe'))
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.ArgumentList.Add('--test-prefix')
    $start.ArgumentList.Add($prefix)
    $start.Environment['DOTNET_ROOT'] = Join-Path $fixture 'dotnet'
    $start.Environment['DOTNET_ROOT_X64'] = Join-Path $fixture 'dotnet'
    $start.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    $start.Environment['WRAIL_EXPECT_PRIVATE_RUNTIME'] = Join-Path $fixture 'dotnet'
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(120000)) { throw "Private runtime fixture timed out. PID $($process.Id)" }
    Write-Output $stdout.GetAwaiter().GetResult()
    Write-Output $stderr.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) { throw "Private runtime test failed: $prefix" }
    $process.Dispose()
}
Write-Output "PASS private-runtime host and isolated worker startup. Fixture retained: $fixture"
