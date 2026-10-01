[CmdletBinding()]
param([Parameter(Mandatory)][string]$CompilerPath, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh syntax evidence directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('wrail-setup-syntax-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
foreach ($name in @('WidgetRail.iss', 'UninstallData.iss')) {
    Copy-Item -LiteralPath (Join-Path $repository ('eng/installer/' + $name)) -Destination $fixture
}
Copy-Item -LiteralPath (Join-Path $repository 'assets/branding/widgetrail.ico') -Destination $fixture
foreach ($name in @('GameInputRedist.msi', 'MicrosoftEdgeWebview2Setup.exe', 'release.json')) {
    [IO.File]::WriteAllText((Join-Path $fixture $name), 'SYNTHETIC SYNTAX FIXTURE - NEVER EXECUTE THIS INSTALLER')
}
$include = @"
#define DisplayName "WidgetRail syntax fixture"
#define AppVersion "1.0.0"
#define OutputDirectory "$output"
#define InstallerName "DO-NOT-RUN-syntax-fixture"
#define PayloadId "syntax-fixture"
#define Edition "production"
#define GameInputVersionMS 196611
#define GameInputVersionLS 14483456
[Files]
Source: "GameInputRedist.msi"; DestDir: "{app}\versions\syntax-fixture\prerequisites"; Flags: ignoreversion
Source: "MicrosoftEdgeWebview2Setup.exe"; DestDir: "{app}\versions\syntax-fixture\prerequisites"; Flags: ignoreversion
Source: "release.json"; DestDir: "{app}\versions\syntax-fixture"; Flags: ignoreversion
"@
[IO.File]::WriteAllText((Join-Path $fixture 'Payload.iss'), $include)
& $CompilerPath /Qp (Join-Path $fixture 'WidgetRail.iss') *> (Join-Path $output 'compiler.txt')
if ($LASTEXITCODE -ne 0) { throw "Installer syntax compilation failed; see $output/compiler.txt" }
# Never launch the fixture: source and output are retained solely as compile evidence.
@{ compiled=$true; executed=$false; syntheticPayload=$true; fixture=$fixture } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'result.json')
'PASS production installer Pascal syntax with synthetic payload; installer was not executed.'
