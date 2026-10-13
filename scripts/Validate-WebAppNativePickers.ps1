param(
    [Parameter(Mandatory = $true)][int]$AppPid,
    [Parameter(Mandatory = $true)][string]$ResultPath
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts/webapps')) + [IO.Path]::DirectorySeparatorChar
$resultFile = [IO.Path]::GetFullPath($ResultPath)
if (!$resultFile.StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Use a fixture result under artifacts/webapps.' }
$fixtureProcess = Get-CimInstance Win32_Process -Filter "ProcessId=$AppPid"
if ($fixtureProcess.CommandLine -notlike '*--validate-webapp-files=*' -or $fixtureProcess.CommandLine -notlike '*--webapp-native-pickers*') { throw 'The target is not the native file-picker fixture.' }
$stateFile = $resultFile + '.picker'
$actionsFile = $resultFile + '.actions.log'
$completed = @{}
function Get-PickerElements($Items) {
    foreach ($item in $Items) { $item; if ($item.children) { Get-PickerElements $item.children } }
}
$deadline = (Get-Date).AddSeconds(60)
while (!(Test-Path -LiteralPath $resultFile) -and (Get-Date) -lt $deadline) {
    if (!(Test-Path -LiteralPath $stateFile)) { Start-Sleep -Milliseconds 200; continue }
    try { $state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json }
    catch { Start-Sleep -Milliseconds 100; continue }
    if ($completed.ContainsKey($state.id)) { Start-Sleep -Milliseconds 200; continue }
    $ui = winapp ui inspect -a $AppPid --depth 12 --interactive --json 2>$null | ConvertFrom-Json
    $dialog = $ui.windows | Where-Object { $_.className -eq '#32770' -and $_.title -match 'Open|Save' } | Select-Object -First 1
    if ($null -eq $dialog) { Start-Sleep -Milliseconds 200; continue }
    $elements = @(Get-PickerElements $dialog.elements)
    if ($state.mode -eq 'cancel') {
        $button = $elements | Where-Object { $_.type -eq 'Button' -and $_.name -eq 'Cancel' } | Select-Object -First 1
        if ($null -eq $button) { throw 'Picker Cancel is unavailable.' }
    } else {
        $field = $elements | Where-Object { $_.type -eq 'Edit' -and $_.name -eq 'File name:' } | Select-Object -First 1
        $button = $elements | Where-Object { $_.name -in @('Select for upload','Save download') } | Select-Object -First 1
        if ($null -eq $field -or $null -eq $button) { throw 'Picker file controls are unavailable.' }
        $chosenPath = [IO.Path]::GetFullPath($state.path)
        if (!$chosenPath.StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'The fixture attempted to select a non-fixture path.' }
        winapp ui set-value $field.selector $chosenPath -w $dialog.hwnd --json >> $actionsFile
        if ($LASTEXITCODE -ne 0) { throw 'Could not set the fixture filename.' }
        winapp ui wait-for $field.selector -w $dialog.hwnd --value $chosenPath -t 2000 --json >> $actionsFile
        if ($LASTEXITCODE -ne 0) { throw 'The fixture filename value did not match.' }
    }
    winapp ui invoke $button.selector -w $dialog.hwnd --json >> $actionsFile
    if ($LASTEXITCODE -ne 0) { throw 'Could not submit the native picker.' }
    $completed[$state.id] = $true
    Write-Output "Completed picker $($state.id): $($state.mode)"
    Start-Sleep -Milliseconds 200
}
if (!(Test-Path -LiteralPath $resultFile)) { throw 'Fixture result is not available; inspect its live process and progress before retrying.' }
$result = Get-Content -LiteralPath $resultFile -Raw | ConvertFrom-Json
$result | ConvertTo-Json -Depth 8
if (!$result.passed) { throw 'Native file-picker fixture failed.' }
