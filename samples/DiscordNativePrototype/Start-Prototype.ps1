[CmdletBinding()]
param([Parameter(Mandatory)][string]$VesktopExe, [int]$Port = 19227)
$ErrorActionPreference = 'Stop'
$exe = [IO.Path]::GetFullPath($VesktopExe)
if (!(Test-Path -LiteralPath $exe) -or [IO.Path]::GetFileName($exe) -ine 'vesktop.exe') { throw 'Supply the portable Vesktop executable.' }
if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Invalid port.' }
$root = Join-Path $env:LOCALAPPDATA 'WidgetRail/DiscordPrototype'
New-Item -ItemType Directory -Force $root | Out-Null
if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) { throw 'Prototype port is already in use. Keep the existing session or quit it first.' }
$previous = $env:VENCORD_USER_DATA_DIR
try {
    $env:VENCORD_USER_DATA_DIR = Join-Path $root 'Vesktop'
    # Interactive window is intentional for login and explicit capture selection.
    $process = Start-Process -FilePath $exe -ArgumentList @('--remote-debugging-address=127.0.0.1', "--remote-debugging-port=$Port", ('--user-data-dir="' + $root + '/Chromium"')) -PassThru
    @{ Port = $Port; Pid = $process.Id; Executable = $exe } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'runtime.json')
} finally { $env:VENCORD_USER_DATA_DIR = $previous }
Write-Output 'Sign in through Vesktop. Keep it running for voice and streams. Quit Vesktop to close the development debugging endpoint.'
