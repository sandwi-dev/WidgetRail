param([Parameter(Mandatory)][string]$ReadyPath, [Parameter(Mandatory)][string]$StopPath, [string]$PeerName = 'peer', [switch]$StartMinimized, [switch]$OwnedPopup)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class ForegroundTestPeerIdentity {
 [DllImport("user32.dll", CharSet=CharSet.Unicode)]
 [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
 public static extern int GetClassNameW(IntPtr window, StringBuilder name, int capacity);
 [DllImport("user32.dll")]
 [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
 public static extern int ShowWindow(IntPtr window, int command);
 [DllImport("user32.dll")]
 [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
 public static extern int IsWindowVisible(IntPtr window);
}
'@
$peerForm = [System.Windows.Forms.Form]::new()
$peerForm.Text = "WidgetRail foreground test $PeerName"
$peerForm.ClientSize = [System.Drawing.Size]::new(320, 100)
$peerForm.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
$peerWorkArea = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$peerLeft = $peerWorkArea.Left + $(if ($PeerName.StartsWith('A')) { 40 } else { 420 })
$peerForm.Location = [System.Drawing.Point]::new($peerLeft, $peerWorkArea.Top + 40)
$peerLabel = [System.Windows.Forms.Label]::new()
$peerLabel.Text = 'Foreground validation only. No actions or settings.'
$peerLabel.Dock = [System.Windows.Forms.DockStyle]::Fill
$peerLabel.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
$peerForm.Controls.Add($peerLabel)
$script:peerPublished = $false
$script:popupArmed = $false
$script:ownedPeerPopup = $null
$peerForm.Add_Activated({
    if ($script:popupArmed -and $null -eq $script:ownedPeerPopup) {
        $script:ownedPeerPopup = [System.Windows.Forms.Form]::new()
        $script:ownedPeerPopup.Text = 'Owned fixture dialog'
        $script:ownedPeerPopup.ClientSize = [System.Drawing.Size]::new(240, 80)
        $script:ownedPeerPopup.Show($peerForm)
    }
})
function Publish-VisiblePeer {
    # Hide the PowerShell console, not the test HWND. Startup SW_HIDE can override
    # WinForms' first Show, so make this explicit after the message loop starts.
    [void][ForegroundTestPeerIdentity]::ShowWindow($peerForm.Handle, 8)
    if ([ForegroundTestPeerIdentity]::IsWindowVisible($peerForm.Handle) -eq 0) { throw 'Test peer is not visible' }
    $peerClass = [System.Text.StringBuilder]::new(256)
    [void][ForegroundTestPeerIdentity]::GetClassNameW($peerForm.Handle, $peerClass, 256)
    $peerIdentity = @{ Handle = [uint64]$peerForm.Handle.ToInt64(); ProcessId = [uint32]$PID;
        ProcessCreated = [uint64][System.Diagnostics.Process]::GetCurrentProcess().StartTime.ToUniversalTime().ToFileTimeUtc(); ClassName = $peerClass.ToString() }
    if ($StartMinimized) { [void][ForegroundTestPeerIdentity]::ShowWindow($peerForm.Handle, 6) }
    @{ processId = $PID; window = $peerForm.Handle.ToInt64(); target = $peerIdentity } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ReadyPath
    $script:peerPublished = $true
    $script:popupArmed = [bool]$OwnedPopup
}
$peerTimer = [System.Windows.Forms.Timer]::new()
$peerTimer.Interval = 100
$peerTimer.Add_Tick({
    if (Test-Path -LiteralPath $StopPath) { $peerForm.Close(); return }
    if (!$script:peerPublished) { Publish-VisiblePeer }
})
$peerTimer.Start()
try { [System.Windows.Forms.Application]::Run($peerForm) }
finally { $peerTimer.Dispose(); if ($script:ownedPeerPopup) { $script:ownedPeerPopup.Dispose() }; $peerForm.Dispose() }
