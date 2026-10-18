# Opt-in native acceptance. Uses real release payloads and Inno setup code with
# isolated registration/data/shortcut names; never registers a production install.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PreparedInstallerRoot,
    [Parameter(Mandatory)][string]$CompilerPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('Prepare','Run')][string]$Phase = 'Prepare',
    # Run one additional Bare case only; never repeats the six baseline cases.
    [string]$BareOptionalPackage,
    # Resume only the native tail of an already successful isolated Bare upgrade.
    [string]$ResumeOptionalNativeCase
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
function InvokeBounded([string]$File,[string[]]$Arguments,[string]$Log) {
    $info=[Diagnostics.ProcessStartInfo]::new($File)
    $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    foreach($argument in $Arguments){$info.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::Start($info)
    try {
        $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        Require ($process.WaitForExit(60000)) 'Acceptance command exceeded its 60-second deadline; inspect the process before retrying.'
        $text=$stdout.GetAwaiter().GetResult();$errors=$stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText($Log,$text+[Environment]::NewLine+$errors)
        Require ($process.ExitCode -eq 0) "Acceptance command failed; see $Log"
        return $text
    } finally {$process.Dispose()}
}
function OptionalState([string]$CatalogRoot) {
    $catalog=Get-Content -LiteralPath (Join-Path $CatalogRoot 'catalog-state.json') -Raw|ConvertFrom-Json
    return @($catalog.widgets|Where-Object id -eq 'widgetrail.firstparty.performance-monitor')
}
function OptionalCli([string]$Payload,[string[]]$Arguments,[string]$Log) {
    InvokeBounded (Join-Path $Payload 'dotnet/dotnet.exe') (@((Join-Path $Payload 'tools/wrail/wrail.dll'))+$Arguments) $Log|Out-Null
}
function OptionalInventory([string]$Payload,[string]$CatalogRoot,[string]$Log) {
    $json=InvokeBounded (Join-Path $Payload 'dotnet/dotnet.exe') @((Join-Path $Payload 'tools/wrail/wrail.dll'),'list','--catalog',$CatalogRoot,'--json') $Log|ConvertFrom-Json
    return @($json.widgets|Where-Object id -eq 'widgetrail.firstparty.performance-monitor')
}
function CloseAcceptanceApp {
    & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $script:app.Id
    Require ($script:app.WaitForExit(10000)) 'Acceptance application did not close.'
    $script:app.Dispose();$script:app=$null
}
function OptionalNative([string]$Payload,[string]$Case,[string]$Initial,[bool]$ExpectWidget) {
    $name=if($ExpectWidget){'optional-loaded'}else{'removed-restarted'}
    $shell=Join-Path $Case ($name+'-shell.json')
    @{InstallationRoot=$Payload;SettingsRoot=$optionalProfile;InstalledCatalogRoot=(Join-Path $optionalProfile 'widgets');InitialWidgetId=$Initial}|
        ConvertTo-Json|Set-Content -LiteralPath $shell
    $winapp=(Get-Command winapp -ErrorAction Stop).Source
    # Project-mode launch supplies the WinUI runtime environment while selecting
    # exactly the installed Release payload. Both shell flags are ordinary Release
    # launch options; no debug-only validation entrypoint is used here.
    $launch=InvokeBounded $winapp @('run',(Join-Path $repo 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj'),
        '--no-build','--detach','--json','--arch','x64','-c','Release','-p:Platform=x64',
        ('-p:OutDir='+[IO.Path]::GetFullPath($Payload).TrimEnd('\','/')+'/'),'--args',
        ('--shell-no-controller --shell-config="'+$shell+'"')) (Join-Path $Case ($name+'-launch.json'))|ConvertFrom-Json
    Require ($launch.ProcessId -gt 0) 'Project-mode launch did not return the installed frontend PID.'
    $script:app=Get-Process -Id $launch.ProcessId -ErrorAction Stop
    Require ([string]::Equals([IO.Path]::GetFullPath($script:app.Path),(Join-Path ([IO.Path]::GetFullPath($Payload)) 'OverlayFrontend.WinUI.exe'),[StringComparison]::OrdinalIgnoreCase)) 'Project-mode launch selected another frontend payload.'
    $target=if($ExpectWidget){'Widget.performance.heading'}else{'Overlay.Widget.settings'}
    try {
        $ready=InvokeBounded $winapp @('ui','wait-for',$target,'-a',"$($script:app.Id)",'-t','25000','--json') (Join-Path $Case ($name+'-ready.json'))|ConvertFrom-Json
    } catch {
        $null=InvokeBounded $winapp @('ui','inspect','--interactive','-a',"$($script:app.Id)",'--json') (Join-Path $Case ($name+'-failure-tree.json'))
        throw
    } finally {
        # A timed-out wait-for exits nonzero. Preserve child-root evidence on
        # that path too, before the outer cleanup closes the isolated app.
        Get-CimInstance Win32_Process -Filter "ParentProcessId=$($script:app.Id)"|
            Select-Object ProcessId,ParentProcessId,Name,ExecutablePath,CommandLine|ConvertTo-Json -Depth 4|
            Set-Content (Join-Path $Case ($name+'-children.json'))
    }
    Require ($ready.found -and !$ready.element.isOffscreen) "Installed native surface did not load: $target"
    if(!$ExpectWidget){
        $tree=InvokeBounded $winapp @('ui','inspect','--interactive','-a',"$($script:app.Id)",'--json') (Join-Path $Case ($name+'-tree.json'))|ConvertFrom-Json
        Require (!($tree|ConvertTo-Json -Depth 50).Contains('Overlay.Widget.widgetrail.firstparty.performance-monitor')) 'Removed widget still appears in the restarted rail.'
    }
    CloseAcceptanceApp
}
$configPath = Join-Path $output 'acceptance.json'
if ($Phase -eq 'Prepare') {
    Require (!$BareOptionalPackage -and !$ResumeOptionalNativeCase) 'Optional continuity is a Run-only scenario using an existing isolated installer.'
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
Require ([IO.Path]::GetFullPath($config.dataRoot) -ceq (Join-Path $env:LOCALAPPDATA $config.name)) 'Invalid isolated data directory.'
$optionalMode=!([string]::IsNullOrWhiteSpace($BareOptionalPackage))
# Keep CLI and native launch on the same filesystem view: a packaged command
# runner can redirect AppData even when the unpackaged native app does not.
$optionalProfile=Join-Path $output 'optional-profile'
Require (!$ResumeOptionalNativeCase -or $optionalMode) 'Resume requires the same -BareOptionalPackage archive.'
if($optionalMode){
    $BareOptionalPackage=[IO.Path]::GetFullPath($BareOptionalPackage)
    Require (Test-Path -LiteralPath $BareOptionalPackage -PathType Leaf) 'Optional acceptance package is missing.'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive=[IO.Compression.ZipFile]::OpenRead($BareOptionalPackage)
    try {
        $reader=[IO.StreamReader]::new($archive.GetEntry('manifest.json').Open())
        try {$optionalManifest=$reader.ReadToEnd()|ConvertFrom-Json}finally{$reader.Dispose()}
        Require ($optionalManifest.id -ceq 'widgetrail.firstparty.performance-monitor' -and $optionalManifest.publisher -ceq 'widgetrail.firstparty') 'Use the ordinary Performance Monitor package for this bounded case.'
    }finally{$archive.Dispose()}
    Require (@($config.editions|Where-Object edition -eq 'bare').Count -eq 1) 'Prepared installer does not identify exactly one Bare edition.'
    $null=Get-Command winapp -ErrorAction Stop
    if(!$ResumeOptionalNativeCase){Require (!(Test-Path -LiteralPath (Join-Path $optionalProfile 'widgets/packages/widgetrail.firstparty.performance-monitor'))) 'Optional continuity case requires an isolated profile without Performance Monitor.'}
}
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
    if($ResumeOptionalNativeCase){
        $prior=[IO.Path]::GetFullPath($ResumeOptionalNativeCase)
        Require ($prior.StartsWith($output+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and
            [IO.Path]::GetFileName($prior) -ceq 'bare-disabled') 'Resume case must be an existing Bare case under this acceptance output.'
        $previousShell=Get-Content -LiteralPath (Join-Path $prior 'optional-loaded-shell.json') -Raw|ConvertFrom-Json
        $before=Get-Content -LiteralPath (Join-Path $prior 'optional-before.json') -Raw|ConvertFrom-Json
        $after=Get-Content -LiteralPath (Join-Path $prior 'optional-after-upgrade.json') -Raw|ConvertFrom-Json
        $installed=Get-ItemProperty -LiteralPath $registry
        Require ($installed.Edition -ceq 'bare' -and $installed.ApplicationRoot -ceq $previousShell.InstallationRoot -and
            [IO.Path]::GetFullPath($installed.ApplicationRoot).StartsWith([IO.Path]::GetFullPath($config.installRoot)+'\versions\',[StringComparison]::OrdinalIgnoreCase)) 'Resume installation does not match the successful case.'
        Require ($before.enabled -and $after.enabled -and $before.id -ceq $optionalManifest.id -and $after.id -ceq $optionalManifest.id) 'Earlier case did not preserve the enabled optional package.'
        $script:newRoot=$installed.ApplicationRoot;Test-ReleaseInventory $script:newRoot
        $catalogRoot=Join-Path $optionalProfile 'widgets';$case=Join-Path $attempt 'bare-native-resume';New-Item -ItemType Directory -Path $case|Out-Null
        $inventory=@(OptionalInventory $script:newRoot $catalogRoot (Join-Path $case 'optional-inventory.json'))
        Require ($inventory.Count -eq 1 -and $inventory[0].enabled -and $inventory[0].selectedVersion -ceq $optionalManifest.version) 'Resume optional version or enabled state changed.'
        $packageRoot=Join-Path $catalogRoot ('packages/'+$optionalManifest.id+'/'+$inventory[0].selectedVersion)
        # Validate unchanged payload against the exact archive used by the earlier
        # CLI installation, without reinstalling or selecting a version manually.
        $archive=[IO.Compression.ZipFile]::OpenRead($BareOptionalPackage);$checked=0
        try{foreach($entry in $archive.Entries){
            if($entry.FullName.EndsWith('/')){continue}
            $target=[IO.Path]::GetFullPath((Join-Path $packageRoot $entry.FullName))
            Require ($target.StartsWith([IO.Path]::GetFullPath($packageRoot)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'Archive member escapes installed package.'
            $stream=$entry.Open();$hasher=[Security.Cryptography.SHA256]::Create()
            try{$expected=[BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose();$hasher.Dispose()}
            Require ((Get-FileHash -LiteralPath $target).Hash -ceq $expected) 'Resume package differs from its original archive.';$checked++
        }}finally{$archive.Dispose()}
        OptionalNative $script:newRoot $case $optionalManifest.id $true
        OptionalCli $script:newRoot @('disable',$optionalManifest.id,'--catalog',$catalogRoot) (Join-Path $case 'optional-disable.log')
        OptionalCli $script:newRoot @('uninstall',$optionalManifest.id,'--catalog',$catalogRoot) (Join-Path $case 'optional-uninstall.log')
        Require (@(OptionalState $catalogRoot).Count -eq 0) 'Uninstall retained the optional widget catalogue entry.'
        OptionalNative $script:newRoot $case 'settings' $false
        Require (@(OptionalState $catalogRoot).Count -eq 0) 'Restart restored the removed optional widget.'
        @{passed=$true;resumedFrom=$prior;installerRepeated=$false;selectedVersion=$inventory[0].selectedVersion;archiveFilesVerified=$checked;
            nativeWidgetLoaded=$true;removedAndRestarted=$true}|ConvertTo-Json|Set-Content (Join-Path $case 'optional-continuity.json')
        return
    }
    foreach ($entry in @($config.editions|Where-Object { !$optionalMode -or $_.edition -eq 'bare' })) {
        $edition = $entry.edition
        $baseline = Join-Path $config.installRoot "versions\baseline-$edition"
        if (!(Test-Path -LiteralPath $baseline)) { Copy-ReleaseTree $entry.payload $baseline $config.installRoot }
        $release = Get-Content (Join-Path $baseline 'release.json') -Raw | ConvertFrom-Json
        $release.version = '0.1.0-acceptance.1'
        $release | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $baseline 'release.json')
        $choices=if($optionalMode){@('disabled')}else{@('enabled','disabled','foreign')}
        foreach ($choice in $choices) {
            $case = Join-Path $attempt "$edition-$choice"
            New-Item -ItemType Directory -Path $case | Out-Null
            Copy-Item -LiteralPath $entry.installer -Destination (Join-Path $case 'update.exe')
            if($optionalMode){
                $catalogRoot=Join-Path $optionalProfile 'widgets'
                OptionalCli $baseline @('install',$BareOptionalPackage,'--catalog',$catalogRoot) (Join-Path $case 'optional-install.log')
                OptionalCli $baseline @('enable','widgetrail.firstparty.performance-monitor','--catalog',$catalogRoot) (Join-Path $case 'optional-enable.log')
                $optionalBefore=@(OptionalState $catalogRoot)
                Require ($optionalBefore.Count -eq 1 -and $optionalBefore[0].enabled) 'CLI did not enable the isolated optional widget.'
                $inventory=@(OptionalInventory $baseline $catalogRoot (Join-Path $case 'optional-inventory-before.json'))
                Require ($inventory.Count -eq 1 -and $inventory[0].enabled -and $inventory[0].selectedVersion -ceq $optionalManifest.version) 'CLI inventory did not select the installed package version.'
                $optionalVersion=$inventory[0].selectedVersion
                $packageRoot=Join-Path $catalogRoot ('packages/widgetrail.firstparty.performance-monitor/'+$optionalVersion)
                $packageHashes=@(Get-ChildItem -LiteralPath $packageRoot -File -Recurse|ForEach-Object { [pscustomobject]@{path=$_.FullName;hash=(Get-FileHash -LiteralPath $_.FullName).Hash} })
                $optionalBefore|ConvertTo-Json -Depth 10|Set-Content (Join-Path $case 'optional-before.json')
            }
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
                $relaunchProfile=if($optionalMode){$optionalProfile}else{Join-Path $case 'profile'}
                $script:app = Start-Process (Join-Path $newRoot 'OverlayFrontend.WinUI.exe') -ArgumentList @('--hidden', ('--settings-root="' + $relaunchProfile + '"')) -WindowStyle Hidden -PassThru
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
            if($optionalMode){
                $after=@(OptionalState $catalogRoot)
                $inventory=@(OptionalInventory $script:newRoot $catalogRoot (Join-Path $case 'optional-inventory-after.json'))
                Require ($after.Count -eq 1 -and $after[0].enabled -and $inventory.Count -eq 1 -and $inventory[0].selectedVersion -ceq $optionalVersion) 'Bare upgrade changed optional widget version or enabled state.'
                Require (@(Get-ChildItem -LiteralPath $packageRoot -File -Recurse).Count -eq $packageHashes.Count) 'Bare upgrade changed optional package inventory.'
                foreach($file in $packageHashes){Require ((Get-FileHash -LiteralPath $file.path).Hash -ceq $file.hash) 'Bare upgrade changed an optional package file.'}
                $after|ConvertTo-Json -Depth 10|Set-Content (Join-Path $case 'optional-after-upgrade.json')
                OptionalNative $script:newRoot $case 'widgetrail.firstparty.performance-monitor' $true
                OptionalCli $script:newRoot @('disable','widgetrail.firstparty.performance-monitor','--catalog',$catalogRoot) (Join-Path $case 'optional-disable.log')
                OptionalCli $script:newRoot @('uninstall','widgetrail.firstparty.performance-monitor','--catalog',$catalogRoot) (Join-Path $case 'optional-uninstall.log')
                Require (@(OptionalState $catalogRoot).Count -eq 0) 'Uninstall retained the optional widget catalogue entry.'
                OptionalNative $script:newRoot $case 'settings' $false
                Require (@(OptionalState $catalogRoot).Count -eq 0) 'Restart restored the removed optional widget.'
                @{passed=$true;edition='bare';package=$optionalManifest.id;version=$optionalVersion;packageFiles=$packageHashes.Count;
                    installedAndEnabledByCli=$true;realUpgradePreservedPackage=$true;nativeWidgetLoaded=$true;removedAndRestarted=$true;
                    scope='One additional isolated Bare case; no production registration, provider consent, or physical controller assertion.'}|
                    ConvertTo-Json|Set-Content (Join-Path $case 'optional-continuity.json')
            }
            $results.Add(@{ edition=$edition; startup=$choice; events=@($script:events); outcome=$script:outcome; payloadVerified=$true; hiddenRelaunch=$true })
            $resultName=if($optionalMode){'optional-results.json'}else{'results.json'}
            $results | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output $resultName)
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
