[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ShellConfig,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][int[]]$TargetProcessIds
)
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh evidence directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class LiveTaskTargetIdentity {
    [DllImport("user32.dll", CharSet=CharSet.Unicode, ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int GetClassNameW(IntPtr window, [Out] char[] name, int capacity);
}
'@
$checks = @()
for ($index = 0; $index -lt $TargetProcessIds.Count; $index++) {
    $target = Get-Process -Id $TargetProcessIds[$index]
    $window = $target.MainWindowHandle
    if ($window -eq [IntPtr]::Zero) { throw 'The selected process has no main window.' }
    $name = [char[]]::new(256)
    $length = [LiveTaskTargetIdentity]::GetClassNameW($window, $name, $name.Length)
    if ($length -le 0) { throw 'Target identity could not be read.' }
    $identity = Join-Path $output "$index-target.json"
    @{ target=@{Handle=[uint64]$window.ToInt64(); ProcessId=[uint32]$target.Id;
        ProcessCreated=[uint64]$target.StartTime.ToUniversalTime().ToFileTimeUtc(); ClassName=[string]::new($name,0,$length)} } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $identity
    $result = Join-Path $output "$index-result.json"
    # Do not send a keyboard/mouse eligibility pulse: the last user input may
    # belong to another app, as it does after opening via a controller Guide.
    $arguments = "--shell-config=`"$([IO.Path]::GetFullPath($ShellConfig))`" --shell-no-controller --validation-platform-activation --validate-task-activation=`"$result`" --activation-task-peer=`"$identity`""
    $launch = winapp run (Join-Path (Split-Path $PSScriptRoot) 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') -c Release --arch x64 -p Platform=x64 --no-build --detach --json --args $arguments | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $launch.ProcessId -le 0) { throw 'Live task-switching fixture did not launch.' }
    $fixture = Get-Process -Id $launch.ProcessId
    try {
        $until = [DateTime]::UtcNow.AddSeconds(40)
        while (!(Test-Path -LiteralPath $result)) {
            if ($fixture.HasExited -or [DateTime]::UtcNow -ge $until) { throw 'Live switching did not produce evidence.' }
            Start-Sleep -Milliseconds 100
        }
        $evidence = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
        if (!$evidence.passed) { throw $evidence.error }
        $checks += @{targetProcess=$target.Id;checks=$evidence.checks.Count;passed=$true}
    } finally {
        # The user's target windows are never closed or otherwise modified.
        if (!$fixture.HasExited) {
            & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $fixture.Id
            if (!$fixture.WaitForExit(15000)) { throw 'Owned fixture did not close.' }
        }
    }
}
$checks | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'summary.json')
"PASS $($checks.Count) live target switches and overlay reentries without keyboard input priming."
