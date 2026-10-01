[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string]$BuildDirectory,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$build = (Resolve-Path -LiteralPath $BuildDirectory).Path
$process = Get-Process -Id $AppPid
if ($process.ProcessName -ne 'OverlayFrontend.WinUI') { throw 'The target is not the WinUI frontend.' }
$created = $process.StartTime.ToUniversalTime()
$deployed = Split-Path $process.Path
$files = @(Get-ChildItem -LiteralPath $build -File -Filter '*.dll')
if ($files.Name -notcontains 'OverlayFrontend.WinUI.dll' -or $files.Name -notcontains 'WidgetProtocol.dll') {
    throw 'The expected build must contain the frontend and its shared protocol.'
}
# WinApp can assemble AppX from both intermediate and output files. A live
# process/startup log alone does not prove that the resulting payload is coherent.
# Read-only: compare every top-level DLL with the explicitly qualified build.
$evidence = foreach ($file in $files) {
    $target = Join-Path $deployed $file.Name
    $expected = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    $actual = if (Test-Path -LiteralPath $target -PathType Leaf) { (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash } else { $null }
    [pscustomobject]@{ file=$file.Name; expected=$expected; actual=$actual; matches=($actual -ceq $expected) }
}
if ((Get-Process -Id $AppPid).StartTime.ToUniversalTime() -ne $created) { throw 'The candidate process identity changed.' }
$result = [pscustomobject]@{ processId=$AppPid; started=$created; build=$build; deployed=$deployed; files=$evidence }
$output = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh payload evidence path.' }
$null = New-Item -ItemType Directory -Force -Path (Split-Path $output)
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $output
$mismatches = @($evidence | Where-Object { -not $_.matches })
if ($mismatches.Count) { throw "Candidate payload differs from its build: $($mismatches.file -join ', '). See $output" }
"Verified $($files.Count) deployed DLLs against the qualified build."
