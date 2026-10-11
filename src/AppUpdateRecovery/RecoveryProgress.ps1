# UI adapter for the standalone engine. No WidgetRail process/runtime dependency.
function New-RecoveryProgress([bool]$Visible = $true) {
    $script:RecoveryCancelled = $false
    $script:RecoveryCanCancel = $true
    $script:RecoveryProgressForm = New-Object Windows.Forms.Form
    $script:RecoveryProgressForm.Text = 'WidgetRail update and recovery'
    $script:RecoveryProgressForm.Width = 500
    $script:RecoveryProgressForm.Height = 190
    $script:RecoveryProgressForm.StartPosition = 'CenterScreen'
    $script:RecoveryProgressForm.FormBorderStyle = 'FixedDialog'
    $script:RecoveryProgressForm.MaximizeBox = $false
    $script:RecoveryProgressForm.MinimizeBox = $false
    $script:RecoveryProgressLabel = New-Object Windows.Forms.Label
    $script:RecoveryProgressLabel.SetBounds(20, 16, 445, 42)
    $script:RecoveryProgressLabel.Text = 'Preparing recovery. Keep WidgetRail closed.'
    $progress = New-Object Windows.Forms.ProgressBar
    $progress.SetBounds(20, 64, 445, 18)
    $progress.Style = 'Marquee'
    $script:RecoveryCancelButton = New-Object Windows.Forms.Button
    $script:RecoveryCancelButton.SetBounds(340, 100, 125, 30)
    $script:RecoveryCancelButton.Text = 'Cancel preparation'
    $script:RecoveryCancelButton.Add_Click({ $script:RecoveryCancelled = $true })
    $script:RecoveryProgressForm.Add_FormClosing({ param($sender, $eventArgs)
        if ($script:RecoveryProgressActive) { $eventArgs.Cancel = $true; if ($script:RecoveryCanCancel) { $script:RecoveryCancelled = $true } }
    })
    $script:RecoveryProgressForm.Controls.AddRange(@($script:RecoveryProgressLabel, $progress, $script:RecoveryCancelButton))
    $script:RecoveryProgressActive = $true
    if ($Visible) { $script:RecoveryProgressForm.Show() }
    $script:RecoveryProgress = {
        param([string]$phase)
        if ($phase -in @('installing','restoring-installer','restoring-profile','profile-preserved','installed','restored')) { $script:RecoveryCanCancel = $false }
        $script:RecoveryCancelButton.Enabled = $script:RecoveryCanCancel
        $script:RecoveryProgressLabel.Text = switch ($phase) {
            'backing-up' { 'Backing up your profile. Keep WidgetRail closed.' }
            'copy' { 'Copying and verifying your profile. Keep WidgetRail closed.' }
            'verify' { 'Verifying recovery files. Keep WidgetRail closed.' }
            'installing' { 'Opening Windows Setup. Continue in the installer.' }
            'restoring-installer' { 'Opening the previous installer. Continue in Windows Setup.' }
            'restoring-profile' { 'Restoring the previous profile. Keep WidgetRail closed.' }
            default { 'Preparing recovery. Keep WidgetRail closed.' }
        }
        [Windows.Forms.Application]::DoEvents()
        if ($script:RecoveryCancelled -and $script:RecoveryCanCancel) { throw [OperationCanceledException]::new('Preparation cancelled. Existing profile and recovery files were preserved; no installer was started.') }
    }
}
function Close-RecoveryProgress {
    $script:RecoveryProgress = $null
    $script:RecoveryProgressActive = $false
    if ($null -ne $script:RecoveryProgressForm) { $script:RecoveryProgressForm.Dispose() }
}
