[CmdletBinding()]
param([Parameter(Mandatory)][string]$CompilerPath)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('wrail-uninstall-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
foreach ($directory in @('data/nested','outside','blocked','kept')) {
    New-Item -ItemType Directory -Path (Join-Path $fixture $directory) -Force | Out-Null
}
foreach ($file in @('data/nested/settings.json','outside/preserve.txt','blocked/locked.txt','kept/settings.json')) {
    Set-Content -LiteralPath (Join-Path $fixture $file) -Value 'fixture'
}
New-Item -ItemType Junction -Path (Join-Path $fixture 'data/link') -Target (Join-Path $fixture 'outside') | Out-Null
New-Item -ItemType Junction -Path (Join-Path $fixture 'root-link') -Target (Join-Path $fixture 'outside') | Out-Null
(Get-Item -LiteralPath (Join-Path $fixture 'blocked/locked.txt')).IsReadOnly = $true
Copy-Item -LiteralPath (Join-Path $repository 'eng/installer/UninstallData.iss') -Destination $fixture
$escapedFixture = $fixture.Replace("'", "''")
$test = @'
[Setup]
AppName=WidgetRail cleanup fixture
AppVersion=1
DefaultDirName={tmp}\unused-widgetrail-fixture
PrivilegesRequired=lowest
Uninstallable=no
CreateAppDir=no
OutputDir=.
OutputBaseFilename=cleanup-tests
[Code]
#include "UninstallData.iss"
function InitializeSetup: Boolean;
var
  Root: String;
begin
  Result := False; { Exit before any installation or wizard UI. }
  Root := 'FIXTURE_ROOT';
  try
    if not IsWidgetRailProfile('widgetrail.widget.0123456789abcdef0123456789abcdef') then RaiseException('Valid profile rejected');
    if not IsWidgetRailProfile('WidgetRail.Widget.0123456789ABCDEF0123456789ABCDEF') then RaiseException('Uppercase profile rejected');
    if IsWidgetRailProfile('widgetrail.widget.0123456789abcdef0123456789abcdeg') then RaiseException('Non-hex profile accepted');
    if IsWidgetRailProfile('unrelated.widget.0123456789abcdef0123456789abcdef') then RaiseException('Foreign profile accepted');
    if not RemoveDataTree(Root + '\missing') then RaiseException('Missing directory failed');
    if not RemoveDataTree(Root + '\data') then RaiseException('Nested cleanup failed');
    if DirExists(Root + '\data') then RaiseException('Data directory remains');
    if not FileExists(Root + '\outside\preserve.txt') then RaiseException('Junction target was changed');
    if not RemoveDataTree(Root + '\root-link') then RaiseException('Root junction cleanup failed');
    if not FileExists(Root + '\outside\preserve.txt') then RaiseException('Root junction target was changed');
    if RemoveDataTree(Root + '\blocked') then RaiseException('Read-only failure was hidden');
    if not FileExists(Root + '\kept\settings.json') then RaiseException('Unselected data was changed');
    SaveStringToFile(Root + '\result.txt', 'PASS', False);
  except
    SaveStringToFile(Root + '\result.txt', 'FAIL: ' + GetExceptionMessage, False);
  end;
end;
'@
$test.Replace('FIXTURE_ROOT', $escapedFixture) | Set-Content -LiteralPath (Join-Path $fixture 'tests.iss')
& $CompilerPath /Qp (Join-Path $fixture 'tests.iss')
if ($LASTEXITCODE -ne 0) { throw "Cleanup fixture compilation failed: $fixture" }
# This executable only runs InitializeSetup on synthetic paths and then exits.
# It never calls RemoveAllWidgetRailData or enumerates/deletes real profiles.
$testProcess = Start-Process -FilePath (Join-Path $fixture 'cleanup-tests.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/SP-' -WindowStyle Hidden -PassThru
if (!$testProcess.WaitForExit(30000)) { throw "Cleanup fixture did not exit: $fixture" }
$result = Get-Content -LiteralPath (Join-Path $fixture 'result.txt') -Raw
if ($result -cne 'PASS') { throw "Cleanup fixture failed: $result ($fixture)" }
Write-Output "PASS nested cleanup, missing paths, root/nested junctions, failure reporting, preserved data and profile ownership names. Evidence: $fixture"
