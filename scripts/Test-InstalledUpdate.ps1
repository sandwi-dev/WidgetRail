# Opt-in native acceptance. Uses real release payloads and Inno setup code with
# isolated registration/data/shortcut names; never registers a production install.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PreparedInstallerRoot,
    [Parameter(Mandatory)][string]$CompilerPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('Prepare','Run')][string]$Phase = 'Prepare'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
. (Join-Path $repo 'src/AppUpdateInstaller/UpdateInstaller.ps1')
Import-Module (Join-Path $PSScriptRoot 'ReleasePackaging.psm1') -Force -DisableNameChecking
function Require($condition, [string]$message) { if (!$condition) { throw $message } }
function RunSetup([string]$file, [string[]]$arguments) {
    $process = Start-Process -FilePath $file -ArgumentList $arguments -WindowStyle Hidden -PassThru
    try {
        if (!$process.WaitForExit(180000)) { throw "Setup exceeded acceptance deadline: $($process.Id)" }
        return $process.ExitCode
    } finally { $process.Dispose() }
}
$configPath = Join-Path $output 'acceptance.json'
if ($Phase -eq 'Prepare') {
    Require (!(Test-Path -LiteralPath $output)) 'Use a fresh acceptance directory.'
    New-Item -ItemType Directory -Path $output | Out-Null
    $name = 'WidgetRailInstallerAcceptance-' + [guid]::NewGuid().ToString('N')
    $appId = [guid]::NewGuid().ToString().ToUpperInvariant()
    $config = [ordered]@{ name=$name; appId=$appId; registry="Software\$name\Installation";
        installRoot=(Join-Path ([IO.Path]::GetTempPath()) ('wri-' + [guid]::NewGuid().ToString('N').Substring(0,8))); dataRoot=(Join-Path $env:LOCALAPPDATA $name);
        sourceCommit=(& git -C $repo rev-parse HEAD).Trim(); editions=@() }
    foreach ($edition in @('bare','production')) {
        $source = Join-Path ([IO.Path]::GetFullPath($PreparedInstallerRoot)) $edition
        $payload = Join-Path $source 'payload'
        Test-ReleaseInventory $payload
        $manifest = Get-Content (Join-Path $payload 'release.json') -Raw | ConvertFrom-Json
        $compile = Join-Path $output $edition
        New-Item -ItemType Directory -Path $compile | Out-Null
        $iss = Get-Content (Join-Path $repo 'eng/installer/WidgetRail.iss') -Raw
        $iss = $iss.Replace('B72CEDF4-8B83-4B69-9E63-F90C6F54AA31', $appId).
            Replace('Software\WidgetRail', "Software\$name").
            Replace("'WidgetRail'", "'$name'").
            Replace('{userprograms}\WidgetRail', "{userprograms}\$name").
            Replace('{localappdata}\WidgetRail', "{localappdata}\$name").
            Replace('{localappdata}\Programs\WidgetRail', $config.installRoot)
        Require (!$iss.Contains('Software\WidgetRail\') -and !$iss.Contains("'WidgetRail'") -and
            !$iss.Contains('{userprograms}\WidgetRail"') -and !$iss.Contains('{localappdata}\WidgetRail\')) 'Installer isolation substitution is incomplete.'
        # Keep prerequisite checks, real host mutex/window checks and all installer
        # lifecycle logic unchanged. Only persistent identity/path names differ.
        [IO.File]::WriteAllText((Join-Path $compile 'WidgetRail.iss'), $iss)
        Copy-Item (Join-Path $repo 'eng/installer/UninstallData.iss') $compile
        Copy-Item (Join-Path $repo 'assets/branding/widgetrail.ico') $compile
        $index = Get-Content (Join-Path $source 'Payload.iss') -Raw
        $index = [regex]::Replace($index, '(?m)^#define OutputDirectory .*$', '#define OutputDirectory "' + $compile + '"')
        $index = [regex]::Replace($index, '(?m)^#define InstallerName .*$', '#define InstallerName "update"')
        [IO.File]::WriteAllText((Join-Path $compile 'Payload.iss'), $index)
        & $CompilerPath /Qp (Join-Path $compile 'WidgetRail.iss')
        Require ($LASTEXITCODE -eq 0) "Isolated $edition installer compilation failed."
        $config.editions += @{ edition=$edition; payload=$payload; version=$manifest.version;
            installer=(Join-Path $compile 'update.exe'); sha256=(Get-FileHash (Join-Path $compile 'update.exe')).Hash }
    }
    $config | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $configPath
    Write-Output "Prepared isolated real installers: $configPath"
    return
}
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
Require ($config.name -match '^WidgetRailInstallerAcceptance-[a-f0-9]{32}$') 'Invalid isolated namespace.'
Require ($config.registry -ceq "Software\$($config.name)\Installation") 'Invalid isolated registry key.'
Require ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($config.installRoot)) -ceq [IO.Path]::GetTempPath().TrimEnd('\')) 'Installation escapes temporary directory.'
Require ([IO.Path]::GetFileName($config.installRoot) -match '^wri-[a-f0-9]{8}$') 'Invalid isolated installation directory.'
Require (!(Get-Process OverlayFrontend.WinUI -ErrorAction SilentlyContinue)) 'Close WidgetRail before native installer acceptance.'
$normalKey = 'HKCU:\Software\WidgetRail\Installation'
$normalBefore = Get-ItemProperty -LiteralPath $normalKey -ErrorAction SilentlyContinue | Select-Object ApplicationRoot,Edition | ConvertTo-Json -Compress
$normalShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'WidgetRail.lnk'
$shortcutHash = if (Test-Path $normalShortcut) { (Get-FileHash $normalShortcut).Hash } else { '' }
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$registry = 'HKCU:\' + $config.registry
$results = [Collections.Generic.List[object]]::new()
$attempt = Join-Path $output ('run-' + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $attempt | Out-Null
$script:app = $null
try {
    New-Item -ItemType Directory -Path $config.dataRoot -Force | Out-Null
    foreach ($entry in $config.editions) {
        $edition = $entry.edition
        $baseline = Join-Path $config.installRoot "versions\baseline-$edition"
        if (!(Test-Path -LiteralPath $baseline)) { Copy-ReleaseTree $entry.payload $baseline $config.installRoot }
        $release = Get-Content (Join-Path $baseline 'release.json') -Raw | ConvertFrom-Json
        $release.version = '0.1.0-acceptance.1'
        $release | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $baseline 'release.json')
        foreach ($choice in @('enabled','disabled','foreign')) {
            $case = Join-Path $attempt "$edition-$choice"
            New-Item -ItemType Directory -Path $case | Out-Null
            Copy-Item -LiteralPath $entry.installer -Destination (Join-Path $case 'update.exe')
            New-Item -Path $registry -Force | Out-Null
            Set-ItemProperty -LiteralPath $registry -Name ApplicationRoot -Value $baseline
            Set-ItemProperty -LiteralPath $registry -Name Edition -Value $edition
            $oldCommand = '"' + $baseline + '\OverlayFrontend.WinUI.exe" --hidden'
            $foreignCommand = '"C:\Unrelated Fixture\example.exe" --hidden'
            if ($choice -eq 'disabled') { Remove-ItemProperty $runKey -Name $config.name -ErrorAction SilentlyContinue }
            else { Set-ItemProperty $runKey -Name $config.name -Value $(if ($choice -eq 'foreign') { $foreignCommand } else { $oldCommand }) }
            $preference = Join-Path $config.dataRoot 'automatic-app-updates.txt'
            $preferenceValue = if ($choice -eq 'enabled') { 'enabled' } else { 'disabled' }
            [IO.File]::WriteAllText($preference, $preferenceValue)
            $plan = [pscustomobject]@{ schema=1; edition=$edition; previousVersion=$release.version;
                updateVersion=$entry.version; previousRoot=$baseline; updateSha256=$entry.sha256 }
            $script:events = [Collections.Generic.List[string]]::new()
            $script:outcome = ''
            $script:newRoot = ''
            $read = {
                $installed = Get-ItemProperty -LiteralPath $registry
                $metadata = Get-Content (Join-Path $installed.ApplicationRoot 'release.json') -Raw | ConvertFrom-Json
                [pscustomobject]@{ ApplicationRoot=$installed.ApplicationRoot; Edition=$installed.Edition; version=$metadata.version }
            }
            Invoke-UpdateHandoff $case $plan $read { $script:events.Add('ready') } {
                Wait-UpdateHostClosed
                $script:events.Add('closed')
            } {
                param($installer)
                $script:events.Add('install')
                RunSetup $installer (Get-UpdateSetupArguments $case $false $baseline)
            } {
                param($newRoot)
                $script:newRoot = $newRoot
                $script:events.Add('relaunch')
                $script:app = Start-Process (Join-Path $newRoot 'OverlayFrontend.WinUI.exe') -ArgumentList @('--hidden', ('--settings-root="' + (Join-Path $case 'profile') + '"')) -WindowStyle Hidden -PassThru
            } -Preflight {
                $status = Join-Path $case 'preflight.txt'
                $null = RunSetup (Join-Path $case 'update.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART',('/WRAILPREFLIGHT="' + $status + '"'))
                Require ((& $read).ApplicationRoot -ceq $baseline) 'Preflight changed registration.'
                Require ((Get-Content $preference -Raw) -ceq $preferenceValue) 'Preflight changed preference.'
                $script:events.Add('preflight')
                Get-Content -LiteralPath $status -Raw
            } -RecordOutcome { param($value) $script:outcome=$value }
            # A hidden frontend needs time to create its owned window even when
            # testing failure recovery. Cleanup must not race its initialization.
            $readyDeadline = [DateTime]::UtcNow.AddSeconds(30)
            do {
                $script:app.Refresh()
                if ($script:app.HasExited -or $script:app.MainWindowHandle -ne 0) { break }
                Start-Sleep -Milliseconds 200
            } while ([DateTime]::UtcNow -lt $readyDeadline)
            Require ($script:outcome -eq 'success') "Upgrade failed: $edition/$choice ($script:outcome)"
            Require (($script:events -join ',') -ceq 'preflight,ready,closed,install,relaunch') 'Unexpected handoff order.'
            Require ($script:newRoot -ne $baseline) 'Payload directory was not replaced.'
            Require ((& $read).Edition -ceq $edition) 'Edition changed during update.'
            Require ((Get-Content $preference -Raw) -ceq $preferenceValue) 'Silent update changed update-check preference.'
            $runValue = [Microsoft.Win32.Registry]::GetValue('HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run', $config.name, $null)
            $expected = if ($choice -eq 'enabled') { '"' + $script:newRoot + '\OverlayFrontend.WinUI.exe" --hidden' } elseif ($choice -eq 'foreign') { $foreignCommand } else { $null }
            Require ($runValue -ceq $expected) "Startup choice not preserved: $choice"
            Start-Sleep -Seconds 4
            Require (!$script:app.HasExited) 'Verified application failed to remain running after relaunch.'
            & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $script:app.Id
            Require ($script:app.WaitForExit(10000)) 'Acceptance application did not close.'
            $script:app.Dispose(); $script:app=$null
            $results.Add(@{ edition=$edition; startup=$choice; events=@($script:events); outcome=$script:outcome; payloadVerified=$true; hiddenRelaunch=$true })
            $results | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'results.json')
            Write-Output "PASS actual Inno upgrade, inventory, hidden relaunch, edition and $choice startup preservation ($edition)"
        }
    }
} finally {
    try {
        if ($script:app -and !$script:app.HasExited) { & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $script:app.Id; $null=$script:app.WaitForExit(10000) }
    } finally { Remove-ItemProperty $runKey -Name $config.name -ErrorAction SilentlyContinue }
    $normalAfter = Get-ItemProperty -LiteralPath $normalKey -ErrorAction SilentlyContinue | Select-Object ApplicationRoot,Edition | ConvertTo-Json -Compress
    Require ($normalBefore -ceq $normalAfter) 'Normal installation registration changed.'
    if ($shortcutHash) { Require ((Get-FileHash $normalShortcut).Hash -ceq $shortcutHash) 'Normal shortcut changed.' }
}
Write-Output 'PASS normal installation registration and shortcut preserved. Isolated install retained for inspection.'
