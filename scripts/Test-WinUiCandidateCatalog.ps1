[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InstallationRoot,
    [string]$CatalogPath
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$installation=[IO.Path]::GetFullPath($InstallationRoot)
if(!$CatalogPath){$CatalogPath=Join-Path $installation 'widget-catalog.json'}
$catalog=Get-Content -LiteralPath $CatalogPath -Raw|ConvertFrom-Json
function InInstallation([string]$Path) {
    $resolved=[IO.Path]::GetFullPath($Path)
    if(!$resolved.StartsWith($installation.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {
        throw "Candidate package path escapes its installation: $resolved"
    }
    return $resolved
}
$genericWorker = InInstallation (Join-Path $installation $catalog.genericWorkerExecutable)
$bridgeDirectory = InInstallation (Join-Path $installation 'runtime/Bridge')
foreach ($assembly in 'WidgetProtocol.dll', 'WidgetSdk.dll', 'WidgetRuntime.dll') {
    $bridgeFile = Join-Path $bridgeDirectory $assembly
    $workerFile = Join-Path (Split-Path $genericWorker) $assembly
    if (!(Test-Path -LiteralPath $bridgeFile -PathType Leaf) -or
        !(Test-Path -LiteralPath $workerFile -PathType Leaf) -or
        (Get-FileHash -LiteralPath $bridgeFile).Hash -cne (Get-FileHash -LiteralPath $workerFile).Hash) {
        throw "Candidate Bridge and generic worker host disagree on $assembly. Publish them from the same build before launching."
    }
}
$settings=@($catalog.widgets|Where-Object {$_.id -ceq 'settings'})
if($settings.Count -ne 1){throw 'Candidate requires exactly one configured Settings companion.'}
$arguments=@($settings[0].workerArguments)
$index=[Array]::IndexOf($arguments,'--bundled-widget-root')
if($index -lt 0 -or $index+1 -ge $arguments.Count){throw 'Settings bundled widget root is missing.'}
$worker=InInstallation (Join-Path $installation $settings[0].workerExecutable)
$discovery=InInstallation (Join-Path (Split-Path $worker) $arguments[$index+1])
$declared=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($entry in @($catalog.widgets)+@($catalog.bundledWidgets)) {
    $relative=if($entry.PSObject.Properties.Name -contains 'packageRoot'){$entry.packageRoot}
        elseif($entry.PSObject.Properties.Name -contains 'iconPackageRoot'){$entry.iconPackageRoot}else{$null}
    if(!$relative){continue}
    $path=InInstallation (Join-Path (Join-Path $installation $relative) 'manifest.json')
    $manifest=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
    if($manifest.id -cne $entry.packageId){throw "Catalog/package identity mismatch for $($entry.id)."}
    $null=$declared.Add($path)
}
$count=0
foreach($directory in Get-ChildItem -LiteralPath $discovery -Directory) {
    $path=Join-Path $directory.FullName 'manifest.json'
    if(!(Test-Path -LiteralPath $path)){continue}
    ++$count
    if(!$declared.Contains([IO.Path]::GetFullPath($path))) {
        throw "Settings discovers $($directory.Name) as built-in, but the host catalog does not register its package root. Do not deploy this candidate."
    }
}
"Verified Settings discovery and host catalog agree on $count packaged widgets."
