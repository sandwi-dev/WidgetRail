[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AssetsPath,
    [Parameter(Mandatory)][string]$FrontendManifest,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
# Offline x64-Windows runtime payload. Does not register/provision packages,
# install licenses, alter trust stores, or invoke a runtime installer.
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Choose a fresh prerequisite directory'}
$assets=Get-Content -LiteralPath $AssetsPath -Raw|ConvertFrom-Json -AsHashtable
$references=@($assets.libraries.Keys|Where-Object {$_ -like 'Microsoft.WindowsAppSDK.Runtime/*'})
if($references.Count -ne 1){throw 'Expected one resolved Microsoft.WindowsAppSDK.Runtime package'}
$reference=$references[0];$library=$assets.libraries[$reference]
$packageVersion=$reference.Split('/')[1]
if($library.path -cne ('microsoft.windowsappsdk.runtime/'+$packageVersion)){throw 'Unexpected runtime package path'}
$archives=@(foreach($folder in $assets.packageFolders.Keys){
    $candidate=Join-Path (Join-Path $folder $library.path) ('microsoft.windowsappsdk.runtime.'+$packageVersion+'.nupkg')
    if(Test-Path -LiteralPath $candidate){[IO.Path]::GetFullPath($candidate)}
})
if($archives.Count -ne 1){throw 'Expected exactly one restored runtime archive'}
$archive=$archives[0]
$metadata=Get-Content -LiteralPath (Join-Path (Split-Path $archive) '.nupkg.metadata') -Raw|ConvertFrom-Json
if($metadata.contentHash -cne $library.sha512){throw 'Restored runtime content hash differs from the build graph'}
$cachedHash=(Get-Content -LiteralPath ($archive+'.sha512') -Raw).Trim()
$actualHash=[Convert]::ToBase64String([Convert]::FromHexString((Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash))
if($cachedHash -cne $actualHash){throw 'Runtime archive differs from its restored archive hash'}
# NuGet's content hash excludes signing additions and is different from raw SHA512.
# Compute it from the archive itself, not from the independently editable cache metadata.
$sdkVersion=(& dotnet --version).Trim()
if($LASTEXITCODE -ne 0){throw 'Cannot resolve the .NET SDK for NuGet content verification'}
$sdkLines=@(& dotnet --list-sdks)
if($LASTEXITCODE -ne 0){throw 'Cannot locate the .NET SDK for NuGet content verification'}
$sdkMatches=@($sdkLines|Where-Object {$_ -match ('^'+[regex]::Escape($sdkVersion)+' \[(.+)\]$')})
if($sdkMatches.Count -ne 1){throw 'Expected one selected .NET SDK installation'}
$sdkDirectory=Join-Path ([regex]::Match($sdkMatches[0],'\[(.+)\]$').Groups[1].Value) $sdkVersion
foreach($assembly in @('NuGet.Common','NuGet.Versioning','NuGet.Frameworks','NuGet.Packaging')){
    [void][Reflection.Assembly]::LoadFrom((Join-Path $sdkDirectory ($assembly+'.dll')))
}
$packageStream=[IO.File]::OpenRead($archive)
try {
    $packageReader=[NuGet.Packaging.PackageArchiveReader]::new($packageStream)
    try {
        $computedContentHash=$packageReader.GetContentHash([Threading.CancellationToken]::None,$null)
        $packageIdentity=$packageReader.GetIdentity()
        if($packageIdentity.Id -cne 'Microsoft.WindowsAppSDK.Runtime' -or
            $packageIdentity.Version.ToNormalizedString() -cne $packageVersion){throw 'Runtime archive identity differs from the build graph'}
        if($computedContentHash -cne $library.sha512){throw 'Computed runtime archive content hash differs from the build graph'}
    } finally {$packageReader.Dispose()}
} finally {$packageStream.Dispose()}

function ReadXml([IO.Stream]$Stream) {
    $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit
    $reader=[Xml.XmlReader]::Create($Stream,$settings)
    try{$document=[Xml.XmlDocument]::new();$document.XmlResolver=$null;$document.Load($reader);return ,$document}
    finally{$reader.Dispose()}
}
function EntryText($Zip,[string]$Name) {
    $entry=$Zip.GetEntry($Name);if(-not $entry){throw "Missing runtime archive entry: $Name"}
    $reader=[IO.StreamReader]::new($entry.Open());try{return $reader.ReadToEnd()}finally{$reader.Dispose()}
}
function SaveEntry($Entry,[string]$Path) {
    $entryStream=$Entry.Open();try{$file=[IO.File]::Create($Path);try{$entryStream.CopyTo($file)}finally{$file.Dispose()}}finally{$entryStream.Dispose()}
}
$frontendStream=[IO.File]::OpenRead([IO.Path]::GetFullPath($FrontendManifest))
try{$frontend=ReadXml $frontendStream}finally{$frontendStream.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try {
    $versionText=EntryText $zip 'WindowsAppSDK-VersionInfo.json'
    $version=$versionText|ConvertFrom-Json
    $publisher=$version.Runtime.Identity.Publisher
    if($publisher -cne 'CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US'){throw 'Unexpected Windows App Runtime publisher'}
    $publisherId=$version.Runtime.Identity.PublisherId
    $frameworkName=$version.Runtime.Packages.Framework.PackageFamilyName -replace ('_'+[regex]::Escape($publisherId)+'$'),''
    $required=@($frontend.GetElementsByTagName('PackageDependency')|Where-Object {$_.GetAttribute('Name') -ceq $frameworkName})
    if($required.Count -ne 1 -or $required[0].GetAttribute('Publisher') -cne $publisher -or
       [version]$version.Runtime.Version.String -lt [version]$required[0].GetAttribute('MinVersion')){throw 'Runtime package does not satisfy the frontend manifest dependency'}
    New-Item -ItemType Directory -Path $output | Out-Null
    dotnet nuget verify --all $archive *> (Join-Path $output 'nuget-signatures.log')
    if($LASTEXITCODE -ne 0){throw 'NuGet runtime signature verification failed'}
    $records=[Collections.Generic.List[object]]::new()
    foreach($entry in $zip.Entries|Where-Object {$_.FullName -match '^tools/MSIX/win10-(x64|x86)/[^/\\:]+\.msix$'}) {
        $architecture=($entry.FullName -split '/')[2].Substring(6)
        $memory=[IO.MemoryStream]::new()
        $source=$entry.Open();try{$source.CopyTo($memory)}finally{$source.Dispose()}
        $memory.Position=0;$msix=[IO.Compression.ZipArchive]::new($memory,[IO.Compression.ZipArchiveMode]::Read,$false)
        try {
            $manifestStream=$msix.GetEntry('AppxManifest.xml').Open()
            try{$manifest=ReadXml $manifestStream}finally{$manifestStream.Dispose()}
            $identity=$manifest.GetElementsByTagName('Identity')[0]
            $name=$identity.GetAttribute('Name');$packageFamily=$name+'_'+$publisherId
            $role=$null
            foreach($candidate in @('Framework','Main','Singleton','DDLM')){
                $family=if($candidate -eq 'DDLM'){$version.Runtime.Packages.DDLM.$architecture.PackageFamilyName}else{$version.Runtime.Packages.$candidate.PackageFamilyName}
                if($packageFamily -ceq $family){$role=$candidate;break}
            }
            if(-not $role){throw 'Runtime MSIX identity is not in its version metadata'}
            if($architecture -eq 'x86' -and $role -notin @('Framework','DDLM')){continue}
            $expectedVersion=if($role -eq 'Singleton'){$version.Runtime.VersionSingleton.String}else{$version.Runtime.Version.String}
            if($identity.GetAttribute('Publisher') -cne $publisher -or $identity.GetAttribute('ProcessorArchitecture') -cne $architecture -or
                $identity.GetAttribute('Version') -cne $expectedVersion){throw 'Runtime MSIX identity/version/architecture mismatch'}
            $frameworkProperties=@($manifest.GetElementsByTagName('Framework'))
            $isFramework=$frameworkProperties.Count -eq 1 -and $frameworkProperties[0].InnerText -ceq 'true'
            if($isFramework -ne ($role -eq 'Framework')){throw 'Runtime package type differs from its declared role'}
            $dependencies=@($manifest.GetElementsByTagName('PackageDependency')|ForEach-Object {
                if($_.GetAttribute('Name') -cne $frameworkName -or $_.GetAttribute('Publisher') -cne $publisher -or
                    [version]$_.GetAttribute('MinVersion') -gt [version]$version.Runtime.Version.String){throw 'Unexpected runtime dependency graph'}
                @{name=$_.GetAttribute('Name');minimumVersion=$_.GetAttribute('MinVersion');architecture=$architecture;publisher=$publisher}
            })
            $expectedDependencyCount=if($role -eq 'Framework'){0}else{1}
            if($dependencies.Count -ne $expectedDependencyCount){throw 'Incomplete runtime dependency graph'}
            $licenses=@($msix.Entries|Where-Object {$_.FullName -match '^MSIX/(main|singleton)_license\.xml$'}|ForEach-Object {
                $stream=$_.Open();try{$hash=[Security.Cryptography.SHA256]::HashData($stream)}finally{$stream.Dispose()}
                @{path=$_.FullName;bytes=$_.Length;sha256=[Convert]::ToHexString($hash)}
            })
            $expectedLicenses=if($role -eq 'Framework'){'MSIX/main_license.xml,MSIX/singleton_license.xml'}else{''}
            if((@($licenses|ForEach-Object {$_.path}|Sort-Object) -join ',') -cne $expectedLicenses){throw 'Framework licensing payloads are incomplete'}
            $relative=$architecture+'/'+$entry.Name
            $directory=Join-Path $output $architecture;New-Item -ItemType Directory -Path $directory -Force|Out-Null
            $target=Join-Path $output $relative;SaveEntry $entry $target
            $signature=Get-AuthenticodeSignature -LiteralPath $target
            if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -cne $publisher){throw 'Microsoft runtime MSIX signature is not valid'}
            $records.Add(@{role=$role;architecture=$architecture;name=$name;family=$packageFamily;version=$expectedVersion;
                isFramework=$isFramework;
                publisher=$publisher;path=$relative;sha256=(Get-FileHash -LiteralPath $target).Hash;signature='Valid';
                signerThumbprint=$signature.SignerCertificate.Thumbprint;dependencies=$dependencies;embeddedLicenses=$licenses})
        } finally {$msix.Dispose();$memory.Dispose()}
    }
    if($records.Count -ne 6 -or @($records|ForEach-Object {$_.role+'/'+$_.architecture}|Select-Object -Unique).Count -ne 6){throw 'Incomplete x64-Windows runtime package set'}
    [IO.File]::WriteAllText((Join-Path $output 'WindowsAppSDK-VersionInfo.json'),$versionText)
    [IO.File]::WriteAllText((Join-Path $output 'LICENSE.txt'),(EntryText $zip 'license.txt'))
    $ordered=@($records|Sort-Object @{Expression={if($_.role -eq 'Framework'){0}else{1}}},role,architecture)
    @{schemaVersion=1;nativeOperatingSystemArchitecture='x64';nugetPackage=$reference;nugetContentHash=$library.sha512;
      archiveSha512=$actualHash;computedNugetContentHash=$computedContentHash;contentHashAlgorithm='NuGet.PackageArchiveReader.GetContentHash';
      frontendManifestSha256=(Get-FileHash -LiteralPath $FrontendManifest).Hash;
      packages=$ordered;runtimeInstalled=$false;licensesInstalled=$false;cleanMachineQualified=$false}|
        ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $output 'runtime-prerequisites.json')
    'Staged six verified Microsoft runtime packages. No runtime or license was installed.'
} finally {$zip.Dispose()}
