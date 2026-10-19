[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

# Pin the data independently of library releases; no SymSpell code is bundled.
$revision = 'c239062ae02961df18ab7da1671d01b4388204e0'
$source = "https://raw.githubusercontent.com/wolfgarbe/SymSpell/$revision/SymSpell/frequency_dictionary_en_82_765.txt"
$response = Invoke-WebRequest -Uri $source
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$words = foreach ($line in ([string]$response.Content -split '\r?\n')) {
    # Preserve source frequency order and the runtime's supported word syntax.
    if ($line -cmatch '^([a-z]{2,24})\s+\d+$' -and $seen.Add($Matches[1])) {
        $Matches[1]
    }
}
if ($words.Count -lt 80000) { throw 'Unexpected vocabulary size; review upstream data before replacing the resource.' }
$destination = Join-Path $PSScriptRoot '../src/OverlayFrontend.WinUI/Assets/Words/english.txt'
[IO.File]::WriteAllText($destination, ($words -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Output "Wrote $($words.Count) frequency-ordered words; $((Get-Item $destination).Length) bytes."
