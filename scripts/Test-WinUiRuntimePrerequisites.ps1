[CmdletBinding()]
param([Parameter(Mandatory)][string]$Receipt,[string]$AssetsPath,[string]$FrontendManifest)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$receiptPath=[IO.Path]::GetFullPath($Receipt);$root=Split-Path $receiptPath
function AssertNoReparsePoint([string]$Path){
 $current=$Path
 while($current){
  if(Test-Path -LiteralPath $current){
   if((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Reparse point is not allowed in runtime payload: $current"}
  }
  $parent=Split-Path $current -Parent
  if($parent -eq $current){break};$current=$parent
 }
}
function PayloadPath([string]$RelativePath){
 if([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath) -or $RelativePath.Contains(':') -or
    @($RelativePath -split '[/\\]'|Where-Object {$_ -in @('','.', '..')}).Count){throw 'Runtime payload path must be a contained relative path'}
 $full=[IO.Path]::GetFullPath((Join-Path $root $RelativePath))
 if(-not $full.StartsWith($root.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Runtime payload path escapes its receipt directory'}
 AssertNoReparsePoint $full
 return $full
}
AssertNoReparsePoint $receiptPath
$record=Get-Content -LiteralPath $receiptPath -Raw|ConvertFrom-Json
$metadata=Get-Content -LiteralPath (PayloadPath 'WindowsAppSDK-VersionInfo.json') -Raw|ConvertFrom-Json
$checks=[Collections.Generic.List[string]]::new()
function Check([bool]$Condition,[string]$Name){if(-not $Condition){throw $Name};$checks.Add($Name)}
$publisher='CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US'
$publisherId='8wekyb3d8bbwe'
Check ($record.schemaVersion -eq 1) 'Supported runtime receipt schema'
Check ($metadata.Runtime.Identity.Publisher -ceq $publisher -and $metadata.Runtime.Identity.PublisherId -ceq $publisherId) 'Runtime metadata names the Microsoft publisher'
Check ($metadata.Release.Channel -ceq 'stable') 'Runtime verifier supports the stable deployment family'
$runtimeVersion=[version]$metadata.Runtime.Version.String
$major=$runtimeVersion.Major
$frameworkName="Microsoft.WindowsAppRuntime.$major"
$expectedFamilies=@{Framework=$frameworkName+'_'+$publisherId;Main="MicrosoftCorporationII.WinAppRuntime.Main.${major}_$publisherId";Singleton="MicrosoftCorporationII.WinAppRuntime.Singleton_$publisherId"}
foreach($role in @('Framework','Main','Singleton')){
 Check ($metadata.Runtime.Packages.$role.PackageFamilyName -ceq $expectedFamilies[$role]) ('Metadata family matches the documented role: '+$role)
}
foreach($arch in @('x64','x86')){
 $suffix=if($arch -eq 'x64'){'x6'}else{'x8'}
 $expectedFamilies['DDLM/'+$arch]="Microsoft.WinAppRuntime.DDLM.$runtimeVersion-${suffix}_$publisherId"
 Check ($metadata.Runtime.Packages.DDLM.$arch.PackageFamilyName -ceq $expectedFamilies['DDLM/'+$arch]) ('Metadata family matches the versioned DDLM role: '+$arch)
}
Check ($record.nativeOperatingSystemArchitecture -ceq 'x64' -and $record.packages.Count -eq 6) 'Complete x64-Windows package matrix contains six packages'
$matrix=@($record.packages|ForEach-Object {$_.role+'/'+$_.architecture}|Sort-Object)
Check (($matrix -join ',') -ceq 'DDLM/x64,DDLM/x86,Framework/x64,Framework/x86,Main/x64,Singleton/x64') 'Architecture roles match the Microsoft deployment matrix'
Check (-not $record.runtimeInstalled -and -not $record.licensesInstalled -and -not $record.cleanMachineQualified) 'Receipt does not claim provisioning or clean-machine qualification'
Check ($record.packages[0].role -eq 'Framework' -and $record.packages[1].role -eq 'Framework') 'Framework prerequisites precede dependent packages'
$seenPaths=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$seenIdentities=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($item in $record.packages){
 $path=PayloadPath $item.path
 Check ($seenPaths.Add($path)) ('Distinct runtime package path: '+$item.path)
 Check ((Get-FileHash -LiteralPath $path).Hash -ceq $item.sha256) ('Staged bytes retain integrity: '+$item.path)
 $signature=Get-AuthenticodeSignature -LiteralPath $path
 Check ($signature.Status -eq 'Valid' -and $signature.SignerCertificate.Subject -ceq $publisher -and $signature.SignerCertificate.Thumbprint -ceq $item.signerThumbprint) ('Microsoft package signature verifies: '+$item.path)
 $zip=[IO.Compression.ZipFile]::OpenRead($path)
 try{
  $entry=$zip.GetEntry('AppxManifest.xml');$stream=$entry.Open()
  $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit
  $reader=[Xml.XmlReader]::Create($stream,$settings)
  try{$manifest=[Xml.XmlDocument]::new();$manifest.XmlResolver=$null;$manifest.Load($reader)}finally{$reader.Dispose();$stream.Dispose()}
  $ns=[Xml.XmlNamespaceManager]::new($manifest.NameTable);$ns.AddNamespace('p','http://schemas.microsoft.com/appx/manifest/foundation/windows10')
  $identities=@($manifest.SelectNodes('/p:Package/p:Identity',$ns))
  Check ($identities.Count -eq 1) ('Exactly one manifest identity: '+$item.path)
  $identity=$identities[0]
  $actualFamily=$identity.GetAttribute('Name')+'_'+$publisherId
  $familyKey=if($item.role -eq 'DDLM'){'DDLM/'+$item.architecture}else{$item.role}
  $expectedVersion=if($item.role -eq 'Singleton'){$metadata.Runtime.VersionSingleton.String}else{$metadata.Runtime.Version.String}
  Check ($actualFamily -ceq $expectedFamilies[$familyKey] -and $item.family -ceq $actualFamily -and $item.version -ceq $expectedVersion) ('Actual package belongs to the expected runtime role: '+$item.path)
  Check ($identity.GetAttribute('Publisher') -ceq $publisher -and $item.publisher -ceq $publisher) ('Actual manifest publisher is Microsoft: '+$item.path)
  Check ($seenIdentities.Add($actualFamily+'/'+$identity.GetAttribute('ProcessorArchitecture')+'/'+$identity.GetAttribute('Version'))) ('Distinct manifest identity: '+$item.path)
  Check ($identity.GetAttribute('Name') -ceq $item.name -and $identity.GetAttribute('Version') -ceq $item.version -and $identity.GetAttribute('ProcessorArchitecture') -ceq $item.architecture) ('Actual MSIX identity agrees with receipt: '+$item.path)
  $frameworkProperties=@($manifest.SelectNodes('/p:Package/p:Properties/p:Framework',$ns))
  $actualFramework=$frameworkProperties.Count -eq 1 -and $frameworkProperties[0].InnerText -ceq 'true'
  Check ($frameworkProperties.Count -le 1 -and $actualFramework -eq ($item.role -eq 'Framework') -and $item.isFramework -eq $actualFramework) ('Actual package type matches its role: '+$item.path)
  $actualDependencies=@($manifest.SelectNodes('/p:Package/p:Dependencies/p:PackageDependency',$ns))
  $expectedCount=if($item.role -eq 'Framework'){0}else{1}
  Check ($actualDependencies.Count -eq $expectedCount -and @($item.dependencies).Count -eq $expectedCount) ('Complete dependency set agrees with manifest: '+$item.path)
  for($i=0;$i -lt $actualDependencies.Count;$i++){
   $actual=$actualDependencies[$i];$declared=$item.dependencies[$i]
   Check ($actual.GetAttribute('Name') -ceq $frameworkName -and $actual.GetAttribute('Publisher') -ceq $publisher -and
       [version]$actual.GetAttribute('MinVersion') -le $runtimeVersion -and
       $declared.name -ceq $actual.GetAttribute('Name') -and $declared.publisher -ceq $publisher -and
       $declared.minimumVersion -ceq $actual.GetAttribute('MinVersion') -and $declared.architecture -ceq $item.architecture) ('Dependency edge agrees with signed manifest: '+$item.path)
  }
  $actualLicensePaths=@($zip.Entries|Where-Object {$_.FullName -match '^MSIX/(main|singleton)_license\.xml$'}|ForEach-Object {$_.FullName}|Sort-Object)
  $declaredLicensePaths=@($item.embeddedLicenses|ForEach-Object {$_.path}|Sort-Object)
  $expectedLicensePaths=if($item.role -eq 'Framework'){'MSIX/main_license.xml,MSIX/singleton_license.xml'}else{''}
  Check (($actualLicensePaths -join ',') -ceq $expectedLicensePaths -and ($declaredLicensePaths -join ',') -ceq $expectedLicensePaths) ('Complete license set agrees with signed package: '+$item.path)
  foreach($license in $item.embeddedLicenses){
   $licenseEntry=$zip.GetEntry($license.path)
   $stream=$licenseEntry.Open();try{$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))}finally{$stream.Dispose()}
   Check ($hash -ceq $license.sha256 -and $licenseEntry.Length -eq $license.bytes) ('Embedded license preserved: '+$item.architecture+'/'+$license.path)
  }
 }finally{$zip.Dispose()}
}
$singleton=$record.packages|Where-Object role -eq 'Singleton'
Check ($singleton.version -ceq $metadata.Runtime.VersionSingleton.String) 'Singleton uses its own authoritative version rather than inventory text'
foreach($item in $record.packages|Where-Object role -eq 'DDLM'){
 Check ($item.family -ceq $metadata.Runtime.Packages.DDLM.($item.architecture).PackageFamilyName) ('Versioned DDLM family uses actual metadata: '+$item.architecture)
}
Check (Test-Path -LiteralPath (PayloadPath 'LICENSE.txt')) 'Redistribution license accompanies the unchanged packages'
if($AssetsPath -or $FrontendManifest){
 if(-not $AssetsPath -or -not $FrontendManifest){throw 'Supply both source inputs for rejection checks'}
 $stage=Join-Path $PSScriptRoot 'Stage-WinUiRuntimePrerequisites.ps1'
 function Rejected([hashtable]$Arguments,[string]$Message,[string]$Name){
  $rejected=$false
  try{& $stage @Arguments|Out-Null}catch{$rejected=$_.Exception.Message -like $Message}
  Check $rejected $Name
 }
 Rejected @{AssetsPath=$AssetsPath;FrontendManifest=$FrontendManifest;OutputDirectory=$root} '*fresh prerequisite*' 'Existing runtime payload cannot be overwritten'
 $assets=Get-Content -LiteralPath $AssetsPath -Raw|ConvertFrom-Json -AsHashtable
 $key=@($assets.libraries.Keys|Where-Object {$_ -like 'Microsoft.WindowsAppSDK.Runtime/*'})[0]
  $badAssets=PayloadPath 'rejected-assets.json'
 @{libraries=@{$key=@{path=$assets.libraries[$key].path;sha512='wrong-content-hash'}};packageFolders=$assets.packageFolders}|ConvertTo-Json -Depth 8|Set-Content $badAssets
 $badOutput=Join-Path $root 'rejected-hash-output'
 Rejected @{AssetsPath=$badAssets;FrontendManifest=$FrontendManifest;OutputDirectory=$badOutput} '*content hash differs*' 'Restore graph content mismatch is rejected'
 Check (-not (Test-Path $badOutput)) 'Hash rejection creates no staging directory'
 $badManifest=PayloadPath 'rejected-manifest.xml'
 $xml=[xml](Get-Content -LiteralPath $FrontendManifest -Raw)
 $dependency=@($xml.GetElementsByTagName('PackageDependency')|Where-Object {$_.GetAttribute('Name') -like 'Microsoft.WindowsAppRuntime.*'})[0]
 $dependency.SetAttribute('MinVersion','99.0.0.0');$xml.Save($badManifest)
 $badOutput=Join-Path $root 'rejected-version-output'
 Rejected @{AssetsPath=$AssetsPath;FrontendManifest=$badManifest;OutputDirectory=$badOutput} '*does not satisfy*' 'Unsatisfied frontend runtime dependency is rejected'
 Check (-not (Test-Path $badOutput)) 'Dependency rejection creates no staging directory'
}
@{passed=$true;checks=$checks;receipt=$receiptPath}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (PayloadPath 'validation.json')
"Passed $($checks.Count) offline runtime-package checks."
