# Run a validation frontend against a packaged workspace containing Now Playing and Discord.
# Direct digital input frames exercise the same shell paths as a physical controller.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Frontend,
    [Parameter(Mandatory)][string]$Config,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$IncludeYouTubeMusic,
    [switch]$CaptureActionsOnly,
    [switch]$ExpectEmptyCatalog,
    [switch]$DesktopOnly,
    [switch]$UnavailableReasonsOnly,
    [switch]$CaptureVisible,
    [string]$ReplayFixtureTarget,
    [string]$CaptureProvider = "capture-library"
)
$ErrorActionPreference='Stop'
$Frontend=(Resolve-Path -LiteralPath $Frontend).Path
if(Test-Path -LiteralPath $OutputDirectory){ throw 'Use a fresh directory for the isolated profile and evidence.' }
$out=(New-Item -ItemType Directory $OutputDirectory).FullName
$cfg=Get-Content $Config -Raw|ConvertFrom-Json -AsHashtable
$cfg.SettingsRoot=(New-Item -Force -ItemType Directory "$out/profile").FullName
$cfg.InitialWidgetId=if ($CaptureVisible) { $CaptureProvider } elseif ($CaptureActionsOnly -or $ExpectEmptyCatalog -or $UnavailableReasonsOnly) { 'settings' } else { 'media-sessions' }
$cfg.LayoutDiagnosticsPath="$out/layout.json"
if ($ExpectEmptyCatalog) { $cfg.InstalledCatalogRoot = (New-Item -ItemType Directory "$out/empty-catalog").FullName }
$cfg|ConvertTo-Json|Set-Content "$out/config.json"
@{Version=1;Step='complete';Paused=$false}|ConvertTo-Json|Set-Content "$out/profile/introduction-progress.json"
$slots = @{}
if (!$CaptureActionsOnly) {
    $slots.Up = @{Provider=$CaptureProvider; Action='save-replay'; Fallback='start-replay'; Publisher='widgetrail.firstparty'; Package='widgetrail.firstparty.capture-library'; Label='Save replay'}
}
if ($UnavailableReasonsOnly) {
    $slots.Left = @{Provider=$CaptureProvider; Action='stop-replay'; Publisher='widgetrail.firstparty'; Package='widgetrail.firstparty.capture-library'; Label='Stop replay'}
    $slots.Right = @{Provider='removed-widget'; Action='toggle-mute'; Publisher='example'; Package='example.removed'; Label='Toggle mute'}
    $slots.Down = @{Provider='removed-widget'; Action='deafen'; Publisher='example'; Package='example.removed'}
}
if ($ReplayFixtureTarget) {
    if (!$DesktopOnly -or $CaptureVisible) { throw 'The live fallback test requires a background provider and desktop retries.' }
    $slots.Left = @{Provider=$CaptureProvider; Action='stop-replay'; Publisher='widgetrail.firstparty'; Package='widgetrail.firstparty.capture-library'; Label='Stop replay'}
}
if (!$ExpectEmptyCatalog) {
    @{Order=@();LastWidget=$null;ReopenWidget=$false;TrayActions=$slots}|ConvertTo-Json -Depth 5|Set-Content "$out/profile/winui-shell-state.json"
}
$launchArgs = @(('--shell-config="'+$out+'/config.json"'),'--validation-platform-activation','--shell-no-controller','--replay-shell-input', '--tray-test-directions')
if ($ReplayFixtureTarget) { $launchArgs += '--replay-fixture-target="'+(Resolve-Path $ReplayFixtureTarget).Path+'"' }
$p=Start-Process $Frontend -WorkingDirectory (Split-Path $Frontend) -ArgumentList $launchArgs -PassThru
function Ui([string[]]$argsList){
    $reply=& winapp ui @argsList -a $p.Id --json
    if($LASTEXITCODE){throw "Native tray $($argsList -join ' ') failed: $reply"}
}
function ChooseRow([int]$index, [string]$name) {
    $row = "Host.Choice.Option.$index"
    Ui @('wait-for',$row,'-p','Name','--contains','--value',$name,'-t','5000')
    Ui @('focus',$row)
    Ui @('wait-for',$row,'-p','HasKeyboardFocus','--value','True','-t','4000')
    Ui @('send-keys','F9','--via','send-input')
}
function WheelHubBounds {
    $reply = & winapp ui get-property Overlay.Radial.Paging -p BoundingRectangle -a $p.Id --json
    if ($LASTEXITCODE) { throw "Could not measure the wheel hub: $reply" }
    $bounds = ($reply | ConvertFrom-Json).properties.BoundingRectangle
    if (!$bounds) { throw "Missing wheel hub bounds: $reply" }
    $bounds | ConvertTo-Json -Compress
}
function CheckReorderLayout([string]$hint, [string]$label) {
    $before = WheelHubBounds
    Ui @('send-keys','F10','--via','send-input') # Short Y press
    Ui @('wait-for',$hint,'--gone','-t','4000')
    $during = WheelHubBounds
    Ui @('screenshot','Overlay.Radial','-o',"$out/reorder-$label.png")
    Ui @('send-keys','F9','--via','send-input') # A finishes reordering
    Ui @('wait-for',$hint,'-t','4000')
    $after = WheelHubBounds
    if ($before -ne $during -or $before -ne $after) {
        throw "Wheel moved during reorder ($label): $before -> $during -> $after"
    }
}
try {
    Ui @('wait-for',("Overlay.Widget."+$cfg.InitialWidgetId),'-t','18000')
    Start-Sleep -Milliseconds 1200
    Ui @('send-keys','F4','--via','send-input')
    Start-Sleep -Milliseconds 350
    Ui @('screenshot','Overlay.TrayHelp','-o',"$out/rail-guide.png")
    if ($ExpectEmptyCatalog) {
        Ui @('send-keys','F6','--via','send-input')
        Ui @('wait-for','Overlay.Radial','-t','4000')
        foreach ($slot in 'Up','Right','Down','Left') { Ui @('wait-for',("Overlay.Radial.Favorite."+$slot),'--gone','-t','2000') }
        Ui @('send-keys','1','--via','send-input')
        Ui @('wait-for','Host.Choice.Dialog','-t','5000')
        Ui @('send-keys','F6','--via','send-input')
        $stateFile = "$out/profile/winui-shell-state.json"
        if (Test-Path -LiteralPath $stateFile) {
            $prefs = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
            if (@($prefs.TrayActions.psobject.Properties | Where-Object Value).Count) { throw 'Fresh Bare profile has a direction assigned' }
        }
        @{passed=$true; checked=@('Four empty directions','Chooser with zero providers')} | ConvertTo-Json | Set-Content "$out/result.json"
        return
    }
    Ui @('send-keys','3','--via','send-input')
    Ui @('wait-for','Host.Choice.Dialog','--gone','-t','2000')
    Ui @('focus','Overlay.QuickActions')
    Ui @('send-keys','ENTER','--via','send-input')
    Ui @('wait-for','Favorite.Menu.Edit','-t','4000')
    if ($UnavailableReasonsOnly) {
        Ui @('wait-for','Favorite.Menu.Right','-p','Name','--contains','--value','Toggle mute, Right','-t','4000')
        Ui @('wait-for','Favorite.Menu.Down','-p','Name','--contains','--value','deafen, Down','-t','4000')
        Ui @('wait-for','Favorite.Menu.Right','-p','IsEnabled','--value','False','-t','2000')
        Ui @('wait-for','Favorite.Menu.Left','-p','Name','--contains','--value','Replay recording is off.','-t','4000')
        Ui @('wait-for','Favorite.Menu.Left','-p','IsEnabled','--value','False','-t','2000')
        Ui @('wait-for','Favorite.Menu.Up','-p','Name','--contains','--value','Start replay','-t','4000')
    }
    Ui @('screenshot','-o',"$out/desktop-favorites.png")
    Ui @('send-keys','ESC','--via','send-input')
    Ui @('wait-for','Favorite.Menu.Edit','--gone','-t','4000')
    Ui @('wait-for','Overlay.QuickActions','-p','HasKeyboardFocus','--value','True','-t','4000')
    if ($UnavailableReasonsOnly) {
        Ui @('send-keys','F6','--via','send-input')
        Ui @('wait-for','Overlay.Radial.Favorite.Left','-t','4000')
        Ui @('send-keys','4','--via','send-input')
        $end = [datetime]::UtcNow.AddSeconds(3)
        do {
            $windows = & winapp ui list-windows -a $p.Id --json | ConvertFrom-Json
            $banner = $windows | Where-Object title -eq 'WidgetRail notification' | Select-Object -First 1
        } while (!$banner -and [datetime]::UtcNow -lt $end)
        if (!$banner) { throw 'Unavailable press did not display the banner.' }
        & winapp ui wait-for 'Replay recording is off.' -w $banner.hwnd -t 1500 --json
        if ($LASTEXITCODE) { throw 'Banner did not contain the reason shown on the desktop row.' }
        if (Select-String -LiteralPath "$out/profile/overlay.log" -Pattern 'widget=capture-library .*event=worker-started' -Quiet) {
            throw 'Reading recorder state or pressing a known unavailable action started Capture Library.'
        }
        @{passed=$true; reason='Replay recording is off.'; captureWorkerStarts=0} | ConvertTo-Json | Set-Content "$out/result.json"
        return
    }
    if ($DesktopOnly) {
        Ui @('click','Overlay.QuickActions')
        Ui @('wait-for','Favorite.Menu.Up','-p','Name','--value','Start replay, Up, Capture Library','-t','4000')
        Ui @('click','Favorite.Menu.Up')
        Ui @('wait-for','Host.Choice.Close','-t','12000')
        Ui @('send-keys','F6','--via','send-input')
        Ui @('wait-for','Host.Choice.Dialog','--gone','-t','4000')
        Ui @('send-keys','F6','--via','send-input')
        Ui @('wait-for','Overlay.Radial.Favorite.Up','-t','4000')
        foreach ($retry in 1..2) {
            Ui @('wait-for','Overlay.Radial.Favorite.Up','-p','Name','--value','D-pad Up, Start replay','-t','4000')
            Ui @('send-keys','1','--via','send-input')
            try { Ui @('wait-for','Host.Choice.Close','-t','5000') }
            catch {
                Ui @('screenshot','-o',"$out/retry-$retry.png")
                throw
            }
            Ui @('send-keys','F6','--via','send-input')
            Ui @('wait-for','Host.Choice.Dialog','--gone','-t','4000')
            if ($retry -eq 1 -and !$CaptureVisible) {
                $deadline = [datetime]::UtcNow.AddSeconds(12)
                do {
                    $unloaded = Select-String -Path "$out/profile/overlay.log" -Pattern 'widget=capture-library .*event=cooperative-unload-completed'
                    if (!$unloaded) { Start-Sleep -Milliseconds 150 }
                } while (!$unloaded -and [datetime]::UtcNow -lt $deadline)
                if (!$unloaded) { throw 'The second retry requires a retired capture worker' }
            }
        }
        if ($ReplayFixtureTarget) {
            Ui @('send-keys','1','--via','send-input')
            Ui @('wait-for','Host.Choice.Close','-t','5000')
            Ui @('focus','WidgetRail synthetic capture target')
            Ui @('send-keys','F9','--via','send-input')
            Ui @('wait-for','Host.Choice.Primary','-p','HasKeyboardFocus','--value','True','-t','4000')
            Ui @('send-keys','F9','--via','send-input')
            $deadline = [datetime]::UtcNow.AddSeconds(20)
            do {
                $starts = @(Select-String -Path "$out/profile/overlay.log" -Pattern 'widget=capture-library .*event=worker-started').Count
                $stops = @(Select-String -Path "$out/profile/overlay.log" -Pattern 'widget=capture-library .*event=cooperative-unload-completed').Count
                if ($stops -lt $starts) { Start-Sleep -Milliseconds 150 }
            } while ($stops -lt $starts -and [datetime]::UtcNow -lt $deadline)
            if ($starts -eq 0 -or $stops -ne $starts) { throw 'Capture Library remained resident during the live hint check' }
            Ui @('wait-for','Overlay.Radial.Favorite.Up','-p','Name','--value','D-pad Up, Save replay','-t','16000')
            Ui @('wait-for','Overlay.Radial.Favorite.Up','-p','IsEnabled','--value','True','-t','16000')
            Ui @('screenshot','Overlay.Radial.Favorite.Up','-o',"$out/save-label.png")
            Ui @('send-keys','4','--via','send-input')
            Ui @('wait-for','Overlay.Radial.Favorite.Up','-p','Name','--value','D-pad Up, Start replay','-t','8000')
            Ui @('screenshot','Overlay.Radial.Favorite.Up','-o',"$out/start-label.png")
        }
        @{passed=$true; liveRecordingTransition=[bool]$ReplayFixtureTarget; captureVisible=[bool]$CaptureVisible; checked=@('D-pad Down bypasses the desktop menu','Keyboard opens flyout and Escape restores launcher focus','Mouse invokes a favorite from the flyout','Replay picker reopens on both radial retries after cancellation'); waitedForRetirement=(!$CaptureVisible)} |
            ConvertTo-Json | Set-Content "$out/result.json"
        return
    }
    Ui @('send-keys','F6','--via','send-input')
    Ui @('wait-for','Overlay.Radial','-t','4000')
    Ui @('screenshot','Overlay.Radial','-o',"$out/wheel.png")
    if ($CaptureActionsOnly) {
        $captures = @(
            @{ Key='2'; Slot='Right'; Action='screenshot'; Name='Take screenshot'; Media='screenshots' },
            @{ Key='3'; Slot='Down'; Action='record-clip'; Name='Record clip'; Media='recordings' }
        )
        foreach ($capture in $captures) {
            Ui @('send-keys',$capture.Key,'--via','send-input')
            $group = 'Host.Choice.Section.' + $CaptureProvider
            Ui @('wait-for',$group,'-t','5000')
            Ui @('focus',$group)
            Ui @('send-keys','F9','--via','send-input')
            Ui @('wait-for',$capture.Name,'-t','4000')
            Ui @('focus',$capture.Name)
            if ($capture.Action -eq 'screenshot') {
                Start-Sleep -Milliseconds 300
                Ui @('screenshot','Host.Choice.Dialog','-o',"$out/capture-actions.png")
            }
            Ui @('send-keys','F9','--via','send-input')
            Ui @('wait-for',("Overlay.Radial.Favorite."+$capture.Slot),'-t','4000')
            $slots=(Get-Content "$out/profile/winui-shell-state.json" -Raw|ConvertFrom-Json).TrayActions
            if ($slots.($capture.Slot).Action -ne $capture.Action -or $slots.($capture.Slot).Provider -ne $CaptureProvider) { throw "Assignment failed for $($capture.Name)" }
        }
        if (Select-String -Path "$out/profile/overlay.log" -Pattern 'widget=capture-library .*event=worker-started' -Quiet) {
            throw 'Assigning a capture shortcut started its worker.'
        }
        foreach ($capture in $captures) {
            Ui @('send-keys',$capture.Key,'--via','send-input')
            if ($capture.Action -eq 'screenshot' -and $CaptureProvider -eq 'widgetrail.firstparty.capture-library') {
                Ui @('wait-for','Open Capture Library','-t','12000')
                Ui @('focus','Open Capture Library')
                Ui @('send-keys','F9','--via','send-input')
                Ui @('wait-for','capture-library.photo','-t','12000')
                Ui @('invoke','capture-library.photo')
                Ui @('wait-for','Allow','-t','8000')
                Ui @('focus','Allow')
                Ui @('send-keys','F9','--via','send-input')
                Ui @('wait-for','Host.Choice.Remember','-t','15000')
                Ui @('send-keys','F6','--via','send-input')
                Ui @('wait-for','Host.Choice.Dialog','--gone','-t','5000')
                Ui @('send-keys','F4','--via','send-input')
                Start-Sleep -Milliseconds 350
                Ui @('send-keys','F6','--via','send-input')
                Ui @('wait-for','Overlay.Radial.Favorite.Right','-t','5000')
                Ui @('send-keys',$capture.Key,'--via','send-input')
            }
            Ui @('wait-for','Host.Choice.Remember','-p','Name','--contains','--value',$capture.Media,'-t','20000')
            Ui @('send-keys','F6','--via','send-input')
            Ui @('wait-for','Host.Choice.Dialog','--gone','-t','4000')
            Ui @('wait-for','Overlay.Radial.Favorite.Down','-t','4000')
        }
        @{passed=$true; checked=@('Cold assignment','Screenshot confirmation','Record clip confirmation','Controller cancellation')} |
            ConvertTo-Json | Set-Content "$out/result.json"
        return
    }
    CheckReorderLayout 'Overlay.Radial.Favorite.Up' 'up'
    Ui @('send-keys','1','--via','send-input')
    Ui @('wait-for','Host.Choice.Close','-t','12000')
    Ui @('send-keys','F6','--via','send-input')
    Ui @('wait-for','Host.Choice.Dialog','--gone','-t','4000')
    $captureStartsAfterReplay = @(Select-String -Path "$out/profile/overlay.log" -Pattern 'widget=capture-library .*event=worker-started').Count
    Start-Sleep -Milliseconds 700
    Ui @('send-keys','2','--via','send-input')
    $discord='Host.Choice.Section.widgetrail.samples.discord-controls'
    Ui @('wait-for',$discord,'-t','4000')
    Ui @('screenshot','Host.Choice.Dialog','-o',"$out/chooser.png")
    function ToggleSection([string]$row) {
        Ui @('focus',$row)
        Ui @('send-keys','F9','--via','send-input')
    }
    function MuteName {
        $assignments = (Get-Content "$out/profile/winui-shell-state.json" -Raw | ConvertFrom-Json).TrayActions
        $slots = @('Up','Right','Down','Left' | Where-Object { $assignments.$_.Action -eq 'mute' })
        if ($slots.Count) { return 'Mute or unmute, ' + ($slots -join ', ') }
        return 'Mute or unmute'
    }
    function SelectMute {
        $name = MuteName
        Ui @('wait-for',$name,'-t','5000')
        Ui @('focus',$name)
        Ui @('send-keys','F9','--via','send-input')
    }
    ToggleSection $discord
    SelectMute
    Ui @('wait-for','Overlay.Radial.Favorite.Right','-t','4000')
    Ui @('screenshot','Overlay.Radial','-o',"$out/wheel-side-hint.png")
    $pref=Get-Content "$out/profile/winui-shell-state.json" -Raw|ConvertFrom-Json
    if($pref.TrayActions.Right.Action -ne 'mute'){throw 'Wrong provider action persisted'}
    foreach ($test in @('cancel','copy','move')) {
        $key = if ($test -eq 'move') { '3' } else { '4' }
        Ui @('send-keys',$key,'--via','send-input')
        Ui @('wait-for',$discord,'-t','5000')
        ToggleSection $discord
        Ui @('wait-for',(MuteName),'-p','Name','--contains','--value','Right','-t','5000')
        SelectMute
        Ui @('wait-for','Host.Choice.Option.0','-p','Name','--contains','--value','Move from Right','-t','5000')
        if ($test -eq 'cancel') { Ui @('screenshot','Host.Choice.Option.0','-o',"$out/move-row.png") }
        if ($test -eq 'cancel') {
            Ui @('send-keys','F6','--via','send-input')
            Ui @('wait-for',(MuteName),'-p','Name','--contains','--value','Mute or unmute','-t','5000')
            Ui @('send-keys','F6','--via','send-input')
        } elseif ($test -eq 'copy') {
            ChooseRow 1 'Also assign to Left'
        } else {
            ChooseRow 0 'Move from Right'
        }
        Ui @('wait-for','Host.Choice.Dialog','--gone','-t','4000')
        $pref=Get-Content "$out/profile/winui-shell-state.json" -Raw|ConvertFrom-Json
        if ($test -eq 'cancel' -and ($pref.TrayActions.Left -or $pref.TrayActions.Right.Action -ne 'mute')) { throw 'Cancelled transfer changed assignments' }
        if ($test -eq 'copy' -and ($pref.TrayActions.Left.Action -ne 'mute' -or $pref.TrayActions.Right.Action -ne 'mute')) { throw 'Copy failed' }
        if ($test -eq 'move' -and ($pref.TrayActions.Right -or $pref.TrayActions.Down.Action -ne 'mute' -or $pref.TrayActions.Left.Action -ne 'mute')) { throw 'Move changed the wrong assignments' }
    }
    CheckReorderLayout 'Overlay.Radial.Favorite.Down' 'up-down-side'
    if ($IncludeYouTubeMusic) {
        Ui @('send-keys','2','--via','send-input')
        $music = 'Host.Choice.Section.widgetrail.samples.ytmusic'
        Ui @('wait-for',$music,'-t','5000')
        ToggleSection $discord
        Ui @('wait-for',(MuteName),'-t','4000')
        ToggleSection $music
        Ui @('wait-for',(MuteName),'--gone','-t','4000')
        Ui @('wait-for','Next song','-t','4000')
        Ui @('screenshot','Host.Choice.Dialog','-o',"$out/youtube-music.png")
        Ui @('focus','Next song')
        Ui @('send-keys','F9','--via','send-input')
        Ui @('wait-for','Overlay.Radial.Favorite.Right','-t','4000')
        $pref=Get-Content "$out/profile/winui-shell-state.json" -Raw|ConvertFrom-Json
        if($pref.TrayActions.Right.Action -ne 'next' -or $pref.TrayActions.Right.Provider -ne 'widgetrail.samples.ytmusic'){throw 'Incorrect YouTube Music favorite'}
    }
    $workers=Get-Content "$out/profile/overlay.log"|Where-Object {$_ -match 'event=worker-started'}
    if($workers -match 'spotify|youtube|ytmusic|discord-controls' -or @($workers -match 'widget=capture-library ').Count -ne $captureStartsAfterReplay){throw 'Chooser woke a widget'}
    @{passed=$true;bound=$pref.TrayActions.Down.Action;checked=@('Keyboard opens the desktop flyout and Escape restores launcher focus','Radial Up starts source choice','B cancels before recording','Empty Right opens grouped catalog','Unavailable Discord binding saved without starting Discord','Assigned directions shown','Back cancels move','Copy keeps both assignments','Move clears only the chosen source','Y reorder keeps the hub fixed with Up alone and with vertical and side favorites');workers=$workers.Count}|ConvertTo-Json|Set-Content "$out/result.json"
} finally {
    if(!$p.HasExited){ & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $p.Id; if(!$p.WaitForExit(10000)){throw 'Tray test window still active'} }
}
