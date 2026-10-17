[CmdletBinding()]
param([Parameter(Mandatory)][int]$AppPid, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$checks = [Collections.Generic.List[string]]::new()
function Ui([string[]]$Arguments) {
    $result = & winapp ui @Arguments -a $AppPid --json
    if ($LASTEXITCODE -ne 0) { throw "winapp ui $Arguments failed: $result" }
    return $result
}
function Verify([string]$Expected) {
    $null = Ui @('wait-for', 'DialogInput.Status', '--value', $Expected, '-t', '3000')
    $checks.Add($Expected)
}
try {
    $null = Ui @('wait-for', 'DialogInput.Open.choices', '-t', '10000')
    foreach ($kind in @('choices', 'grouped', 'library')) {
        foreach ($index in @(1, 0)) {
            $null = Ui @('click', "DialogInput.Open.$kind")
            $target = if ($kind -eq 'library') { "Browser.Library.Item.$index" } else { "Host.Choice.Option.$index" }
            $null = Ui @('wait-for', $target, '-t', '3000')
            $null = Ui @('click', $target)
            $value = @('one', 'two')[$index]
            if ($kind -eq 'library') { $value = "https://$value.invalid" }
            Verify "${kind}: $value"
        }
        foreach ($key in @('F6', 'Enter', 'Space')) {
            $null = Ui @('click', "DialogInput.Open.$kind")
            $target = if ($kind -eq 'library') { 'Browser.Library.Item.1' } else { 'Host.Choice.Option.1' }
            $null = Ui @('wait-for', $target, '-t', '3000')
            $null = Ui @('focus', $target)
            $null = Ui @('send-keys', $key, '--via', 'send-input')
            Verify "${kind}: $(if($kind -eq 'library'){'https://two.invalid'}else{'two'})"
            $checks.Add("$kind accepts $key (F6 routes controller A)")
        }
        $null = Ui @('click', "DialogInput.Open.$kind")
        $target = if ($kind -eq 'library') { 'Browser.Library.Item.0' } else { 'Host.Choice.Option.0' }
        $null = Ui @('wait-for', $target, '-t', '3000')
        $null = Ui @('click', $target, '--right')
        $null = Ui @('wait-for', $target, '-t', '1000')
        $checks.Add("$kind right-click does not activate")
        $close = if ($kind -eq 'library') { 'CloseButton' } else { 'Host.Choice.Close' }
        $null = Ui @('click', $close)
    }
    foreach ($target in @('Host.Choice.Secondary', 'Host.Choice.Primary')) {
        $null = Ui @('click', 'DialogInput.Open.decision')
        $null = Ui @('wait-for', $target, '-t', '3000')
        $null = Ui @('click', $target)
        Verify "decision: $(if($target.EndsWith('Primary')){'one'}else{'two'})"
    }
    $null = Ui @('click', 'DialogInput.Open.grouped')
    $null = Ui @('wait-for', 'Host.Choice.Option.1', '-t', '3000')
    Start-Sleep -Milliseconds 400
    $null = Ui @('screenshot', 'Host.Choice.Dialog', '-o', (Join-Path $output 'grouped.png'), '--capture-screen')
    $null = Ui @('click', 'Host.Choice.Close')
    @{ passed=$true; checks=$checks } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'result.json')
    "Passed: $($checks.Count), Failed: 0"
}
catch {
    @{ passed=$false; checks=$checks; error="$($_.Exception.Message)" } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'result.json')
    throw
}
