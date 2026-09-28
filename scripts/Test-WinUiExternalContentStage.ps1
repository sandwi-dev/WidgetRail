[CmdletBinding()]
param([Parameter(Mandatory)][string]$Receipt)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$receiptPath=[IO.Path]::GetFullPath($Receipt)
$root=Split-Path $receiptPath
$stage=Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
$checks=[Collections.Generic.List[string]]::new()
function Check([bool]$Condition,[string]$Description) { if(-not $Condition){throw $Description};$checks.Add($Description) }
function ReadXml([string]$Path) {
    $options=[Xml.XmlReaderSettings]::new();$options.DtdProcessing=[Xml.DtdProcessing]::Prohibit
    $reader=[Xml.XmlReader]::Create($Path,$options)
    try {$xml=[Xml.XmlDocument]::new();$xml.XmlResolver=$null;$xml.Load($reader);return ,$xml}finally{$reader.Dispose()}
}
$manifest=ReadXml $stage.manifest
$ns=[Xml.XmlNamespaceManager]::new($manifest.NameTable)
$ns.AddNamespace('p','http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$ns.AddNamespace('u','http://schemas.microsoft.com/appx/manifest/uap/windows10/10')
$ns.AddNamespace('visual','http://schemas.microsoft.com/appx/manifest/uap/windows10')
$identity=$manifest.SelectSingleNode('/p:Package/p:Identity',$ns)
$app=$manifest.SelectSingleNode('/p:Package/p:Applications/p:Application',$ns)
Check ($manifest.SelectSingleNode('/p:Package/p:Properties/u:AllowExternalContent',$ns).InnerText -ceq 'true') 'Manifest explicitly uses external content'
Check ($app.Executable -ceq 'OverlayFrontend.WinUI.exe' -and $app.GetAttribute('RuntimeBehavior',$ns.LookupNamespace('u')) -ceq 'win32App' -and $app.GetAttribute('TrustLevel',$ns.LookupNamespace('u')) -ceq 'mediumIL') 'Win32 external executable and medium-integrity identity agree'
$binding=ReadXml (Join-Path $root 'embedded-app.manifest')
$msix=@($binding.GetElementsByTagName('msix','urn:schemas-microsoft-com:msix.v1'))
Check ($msix.Count -eq 1 -and $msix[0].packageName -ceq $identity.Name -and $msix[0].publisher -ceq $identity.Publisher -and $msix[0].applicationId -ceq $app.Id) 'Extracted executable identity matches package identity'
$zip=[IO.Compression.ZipFile]::OpenRead($stage.unsignedPackage)
try {
    $names=@($zip.Entries.FullName)
    Check ($names.Count -eq 3 -and $names -contains 'AppxManifest.xml' -and $names -contains 'AppxBlockMap.xml' -and $names -contains '[Content_Types].xml') 'MSIX contains identity metadata only'
    Check (-not ($names -contains 'AppxSignature.p7x')) 'Probe remains unsigned; no signing or trust prerequisite was silently applied'
} finally {$zip.Dispose()}
Check ((Get-FileHash -LiteralPath (Join-Path $stage.externalLocation 'OverlayFrontend.WinUI.dll')).Hash -ceq $stage.assemblySha256 -and (Get-FileHash -LiteralPath (Join-Path $stage.sourceFrontend 'OverlayFrontend.WinUI.dll')).Hash -ceq $stage.assemblySha256) 'Frontend assembly bytes match the supplied published output'
Check ((Get-FileHash -LiteralPath (Join-Path $stage.sourceFrontend 'OverlayFrontend.WinUI.exe')).Hash -ceq $stage.originalExeSha256 -and (Get-FileHash -LiteralPath (Join-Path $stage.externalLocation 'OverlayFrontend.WinUI.exe')).Hash -ceq $stage.embeddedExeSha256) 'Only the copied executable receives identity embedding'
$runtime=@($manifest.SelectNodes('/p:Package/p:Dependencies/p:PackageDependency',$ns) | Where-Object {$_.Name -like 'Microsoft.WindowsAppRuntime.*'})
Check ($runtime.Count -gt 0 -and $runtime[0].MinVersion -ceq $stage.packageDependencies[0].minimumVersion) 'Windows App Runtime dependency is retained'
Check (@($manifest.SelectNodes('/p:Package/p:Capabilities/*',$ns) | Where-Object {$_.Name -eq 'allowElevation'}).Count -eq 0) 'Sparse template does not add elevation capability'
$visual=$app.SelectSingleNode('visual:VisualElements',$ns)
foreach($asset in @($manifest.SelectSingleNode('/p:Package/p:Properties/p:Logo',$ns).InnerText,$visual.Square150x150Logo,$visual.Square44x44Logo)) {
    Check (Test-Path -LiteralPath (Join-Path $stage.externalLocation $asset) -PathType Leaf) "External identity asset exists: $asset"
}
Check (Test-Path -LiteralPath (Join-Path $stage.externalLocation 'runtime/Bridge/WidgetBridge.exe')) 'Bridge stays in the owned external payload'
Check ($stage.launchArguments.Count -eq 2 -and $stage.launchArguments[0] -like '--settings-root=*' -and $stage.launchArguments[1] -like '--installed-catalog-root=*') 'Future probe launch uses two separate isolated-profile arguments'
Check (@(Get-AppxPackage -Name $stage.packageName).Count -eq 0) 'Offline probe has no package registration'
$arguments=@{FrontendDirectory=$stage.sourceFrontend;InstallationDirectory=$stage.sourceInstallation;GeneratedManifest=$stage.sourceManifest;PackageName=$stage.packageName;Version=$stage.version}
foreach($invalidOutput in @($root,(Join-Path $stage.sourceFrontend 'forbidden-nested-stage'))) {
    $rejected=$false
    try { & (Join-Path $PSScriptRoot 'New-WinUiExternalContentStage.ps1') @arguments -OutputDirectory $invalidOutput | Out-Null }
    catch { $rejected=$true }
    Check $rejected 'Existing or overlapping output is rejected before staging'
}
@{passed=$true;checks=$checks;receipt=$receiptPath} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $root 'staging-checks.json')
Write-Output "Passed $($checks.Count) external-content staging checks. No package was registered or launched."
