[CmdletBinding()]
param([Parameter(Mandatory)][string]$Frontend, [Parameter(Mandatory)][string]$Config,
    [Parameter(Mandatory)][string]$OutputDirectory, [string]$AuthorizedFixtureProfile)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $Frontend).Path
$out = (New-Item -ItemType Directory $OutputDirectory).FullName
$fixture = Start-Job -ArgumentList $out -ScriptBlock {
    param($directory)
    Add-Type -AssemblyName System.Windows.Forms,System.Drawing
    $form = New-Object Windows.Forms.Form
    $form.Text = 'WidgetRail generated capture fixture'
    $form.ClientSize = New-Object Drawing.Size(640,360)
    $form.BackColor = [Drawing.Color]::DarkBlue
    $panel = New-Object Windows.Forms.Panel
    $panel.BackColor = [Drawing.Color]::Gold
    $panel.Bounds = New-Object Drawing.Rectangle(24,48,240,180)
    $form.Controls.Add($panel)
    $timer = New-Object Windows.Forms.Timer
    $timer.Interval = 100
    $timer.add_Tick({
        if (Test-Path -LiteralPath "$directory/close-fixture") { $timer.Stop(); $form.Close(); return }
        $panel.Left = 24 + (($panel.Left + 13) % 330)
    })
    $form.add_Shown({
        @{window=$form.Handle.ToInt64(); pid=$PID} | ConvertTo-Json |
            Set-Content -LiteralPath "$directory/fixture.json"
        $timer.Start()
    })
    [Windows.Forms.Application]::Run($form)
}
try {
    $end = [datetime]::UtcNow.AddSeconds(15)
    while (!(Test-Path "$out/fixture.json")) {
        if ($fixture.State -ne 'Running' -or [datetime]::UtcNow -gt $end) { throw 'Generated window did not start' }
        Start-Sleep -Milliseconds 100
    }
    $target = Get-Content "$out/fixture.json" -Raw | ConvertFrom-Json
    foreach ($scenario in 'screenshot-immediate','screenshot','record-clip','screenshot-source-closed') {
        $immediate = $scenario -in 'screenshot-immediate','screenshot-source-closed'
        $action = if ($immediate) { 'screenshot' } else { $scenario }
        $case = (New-Item -ItemType Directory "$out/$scenario").FullName
        $cfg = Get-Content $Config -Raw | ConvertFrom-Json -AsHashtable
        $cfg.InitialWidgetId = 'settings'
        $cfg.SettingsRoot = (New-Item -ItemType Directory "$case/profile").FullName
        $cfg.LayoutDiagnosticsPath = "$case/layout.json"
        $cfg | ConvertTo-Json | Set-Content "$case/config.json"
        $motion = if ($action -eq 'record-clip') { 'reduced' } else { 'full' }
        @{schemaVersion=3; appearance=@{themeId='builtin.default'; themeVersion='1.0.0'; interfaceScale=1;
            textScale=1; backdropOpacity=0.4; motion=$motion}} | ConvertTo-Json -Depth 4 |
            Set-Content "$case/profile/platform-settings.json"
        @{Version=1; Step='complete'; Paused=$false} | ConvertTo-Json | Set-Content "$case/profile/introduction-progress.json"
        $binding = @{Provider='capture-library'; Package='widgetrail.firstparty.capture-library'; Publisher='widgetrail.firstparty'; Action=$action}
        if ($AuthorizedFixtureProfile) {
            $slots = (Get-Content -LiteralPath "$AuthorizedFixtureProfile/winui-shell-state.json" -Raw | ConvertFrom-Json).TrayActions
            $slot = if ($action -eq 'screenshot') { $slots.Right } else { $slots.Down }
            if ($slot.Action -ne $action -or $slot.Package -ne 'widgetrail.firstparty.capture-library') {
                throw 'Authorized fixture has an unexpected assignment'
            }
            $binding = $slot
            Copy-Item -LiteralPath "$AuthorizedFixtureProfile/consent" -Destination "$case/profile/consent" -Recurse
        }
        if ($scenario -eq 'screenshot') {
            $store = New-Item -ItemType Directory "$case/profile/widget-state"
            $authority = [Text.Encoding]::UTF8.GetBytes($binding.Publisher + [char]0 + $binding.Package)
            $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($authority)).ToLowerInvariant()
            $prefs = [Text.Encoding]::UTF8.GetBytes('{"Preparation":{"DelaySeconds":5}}')
            $document = [ordered]@{schemaVersion=1; revision=1; exists=$true; canonicalJsonBase64=[Convert]::ToBase64String($prefs);
                rateUnits=8000; rateTimestampUnixMilliseconds=0} | ConvertTo-Json -Compress
            [IO.File]::WriteAllText("$store/$hash.json", $document)
        }
        @{Order=@(); ReopenWidget=$false; TrayActions=@{Right=$binding}} |
            ConvertTo-Json -Depth 5 | Set-Content "$case/profile/winui-shell-state.json"
        # Establish an actual foreground source before launching the overlay.
        # A background job showing a window does not guarantee activation.
        if (-not ('TrayCaptureWindowState' -as [type])) {
            Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class TrayCaptureWindowState {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
}
"@
        }
        $activated = (New-Object -ComObject WScript.Shell).AppActivate('WidgetRail generated capture fixture')
        Start-Sleep -Milliseconds 150
        if (!$activated -or [TrayCaptureWindowState]::GetForegroundWindow().ToInt64() -ne $target.window) {
            throw 'Generated capture window did not acquire foreground.'
        }
        $app = Start-Process $exe -WorkingDirectory (Split-Path $exe) -PassThru -ArgumentList @(
            ('--shell-config="'+$case+'/config.json"'),'--validation-platform-activation','--shell-no-controller',
            '--replay-shell-input','--tray-test-directions',("--library-fixture-window="+$target.window),("--library-fixture-pid="+$target.pid))
        function Ui([string[]]$words) {
            $reply = & winapp ui @words -a $app.Id --json
            if ($LASTEXITCODE) { throw "Native fixture failed at $($words -join ' '): $reply" }
        }
        try {
            Ui @('wait-for','Overlay.Widget.settings','-t','18000')
            Start-Sleep -Milliseconds 1100
            Ui @('send-keys','F4','--via','send-input')
            Start-Sleep -Milliseconds 350
            Ui @('send-keys','F6','--via','send-input')
            Ui @('wait-for','Overlay.Radial.Favorite.Right','-t','4000')
            Ui @('send-keys','2','--via','send-input')
            Ui @('wait-for','Host.Choice.Remember','-t','20000')
            Ui @('wait-for','Overlay.Radial.Favorite.Right','-p','Name','--contains','--value','In progress','-t','5000')
            Ui @('screenshot','Overlay.Radial','-o',"$case/progress.png")
            Ui @('screenshot','Overlay.Radial.Favorite.Right','-o',"$case/hint.png")
            Start-Sleep -Milliseconds 350
            Ui @('screenshot','Overlay.Radial.Favorite.Right','-o',"$case/hint-next.png")
            Ui @('screenshot','Overlay.Radial','-o',"$case/progress-next.png")
            if ($scenario -eq 'screenshot-source-closed') {
                New-Item -ItemType File "$out/close-fixture" | Out-Null
                if (!(Wait-Job $fixture -Timeout 5)) { throw 'Generated source remained open.' }
            }
            Ui @('focus','Proceed')
            Ui @('send-keys','F9','--via','send-input')
            if ($scenario -eq 'screenshot-source-closed') {
                $log = "$env:LOCALAPPDATA/WidgetRail/WinUI/diagnostics/frontend-errors.log"
                $end = [datetime]::UtcNow.AddSeconds(8)
                while (!(Select-String -LiteralPath $log -SimpleMatch "pid=$($app.Id) source=capture-run hresult=0x80070005" -Quiet)) {
                    if ([datetime]::UtcNow -gt $end) { throw 'Missing rejection for closed screenshot source.' }
                    Start-Sleep -Milliseconds 100
                }
                Ui @('wait-for','Overlay.Radial.Favorite.Right','-t','3000')
                if (Get-ChildItem "$case/profile/capture-library-v2" -Filter '*.json' -ErrorAction SilentlyContinue |
                    Where-Object BaseName -Match '^[a-f0-9]{32}$') { throw 'Closed target produced a saved capture.' }
                @{passed=$true; sourceClosed=$true} | ConvertTo-Json | Set-Content "$case/result.json"
                continue
            }
            $end = [datetime]::UtcNow.AddSeconds(70)
            do {
                Start-Sleep -Milliseconds 200
                $records = @(Get-ChildItem "$case/profile/capture-library-v2" -Filter '*.json' |
                    Where-Object BaseName -Match '^[a-f0-9]{32}$')
            } while (!$records -and !$app.HasExited -and [datetime]::UtcNow -lt $end)
            if ($records.Count -ne 1) { throw "Expected one saved $action, found $($records.Count)" }
            $record = Get-Content -LiteralPath $records[0].FullName -Raw | ConvertFrom-Json
            $kind = if ($action -eq 'screenshot') { 'Screenshot' } else { 'Clip' }
            $sourceApp = (Get-Process -Id $target.pid).ProcessName
            $notice = "$kind saved from $sourceApp."
            $until = [datetime]::UtcNow.AddSeconds(5)
            do {
                $windows = & winapp ui list-windows -a $app.Id --json | ConvertFrom-Json
                $banner = $windows | Where-Object title -eq 'WidgetRail notification' | Select-Object -First 1
                if ($banner) { break }
                Start-Sleep -Milliseconds 60
            } while ([datetime]::UtcNow -lt $until)
            if (!$banner) { throw 'Save notification window did not appear' }
            & winapp ui wait-for $notice -w $banner.hwnd -t 3000 --json | Out-Null
            if ($LASTEXITCODE) { throw "Expected save banner body: $notice" }
            $name = $record.FileName
            if ($name -ne [IO.Path]::GetFileName($name) -or !$name.StartsWith('WidgetRail-')) { throw 'Unexpected capture filename' }
            $captured = Join-Path ([Environment]::GetFolderPath('MyPictures')) "WidgetRail/Captures/$name"
            if ((Get-FileHash -LiteralPath $captured).Hash -ne $record.Sha256) { throw 'Saved bytes do not match the fixture record' }
            Move-Item -LiteralPath $captured -Destination "$case/$name"
            if ($action -eq 'screenshot') {
                Add-Type -AssemblyName System.Drawing
                $image = [Drawing.Bitmap]::new("$case/$name")
                try {
                    $blue = $gold = 0
                    for ($y = 0; $y -lt $image.Height; $y += 8) {
                        for ($x = 0; $x -lt $image.Width; $x += 8) {
                            $pixel = $image.GetPixel($x, $y)
                            if ($pixel.B -gt 100 -and $pixel.R -lt 16 -and $pixel.G -lt 16) { $blue++ }
                            if ($pixel.R -gt 225 -and $pixel.G -gt 175 -and $pixel.B -lt 24) { $gold++ }
                        }
                    }
                    if ($blue -lt 500 -or $gold -lt 100) { throw 'Screenshot did not contain the generated fixture.' }
                } finally { $image.Dispose() }
            }
            if ($scenario -eq 'screenshot-immediate') {
                [uint32]$foregroundPid = 0
                [void][TrayCaptureWindowState]::GetWindowThreadProcessId([TrayCaptureWindowState]::GetForegroundWindow(), [ref]$foregroundPid)
                if ($foregroundPid -ne $app.Id) { throw 'Overlay lost foreground after immediate capture.' }
                Ui @('wait-for','Overlay.Radial.Favorite.Right','-t','3000')
            }
            $events = Get-Content "$case/profile/overlay.log"
            $provider = [regex]::Escape($binding.Provider)
            if ($events -match "widget=$provider .*lifecycle=interactive") { throw 'Capture took foreground widget ownership' }
            if ($action -eq 'record-clip' -and !($events -match "widget=$provider .*event=cooperative-unload-completed")) {
                throw 'Worker did not unload before the clip was saved'
            }
            @{passed=$true; motion=$motion; file=$name; notification=$notice; width=$record.Item.Media.Width; height=$record.Item.Media.Height;
                duration=$record.Item.Media.DurationSeconds} | ConvertTo-Json | Set-Content "$case/result.json"
        } finally {
            if (!$app.HasExited) { & "$PSScriptRoot/Close-WinUiTestShell.ps1" -AppPid $app.Id
                if (!$app.WaitForExit(12000)) { throw 'Isolated capture fixture remained active' } }
        }
    }
} finally {
    New-Item -ItemType File -Path "$out/close-fixture" -Force | Out-Null
    $null = Wait-Job $fixture -Timeout 5
    Remove-Job $fixture -Force
}
