[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DependencyDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$artifactRoot=Join-Path $repo 'artifacts/discord-desktop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
$dependencies=[IO.Path]::GetFullPath($DependencyDirectory)
if(!$output.StartsWith($artifactRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or
    (Test-Path -LiteralPath $output)){throw 'Choose a fresh artifact child for the generated native geometry build.'}
foreach($path in @($output,$dependencies)) {
    $ancestor=$path
    while($ancestor) {
        if((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Geometry build paths must not use reparse points.'
        }
        $ancestor=Split-Path -Parent $ancestor
    }
}
# Pinned Node-API headers/import library only. No Node/Electron/client install.
# Official https://nodejs.org/dist/v22.22.3/SHASUMS256.txt entries.
$pins=@{
    'headers.tar.gz'='723B896D68A288E9877EA929538494AFE8B5808F6FEDC3358D2AD77307D1B393'
    'node.lib'='0D8D8BCC11DAEA60F5DD4DA414E72CCB785718345EC8FBEC52CFC7D1A2326293'
}
foreach($file in $pins.Keys) {
    if((Get-FileHash -LiteralPath (Join-Path $dependencies $file) -Algorithm SHA256).Hash -cne $pins[$file]) {
        throw "Pinned Node build dependency mismatch: $file"
    }
}
$sources=@('geometry_lease.cpp','win_delay_load_hook.cpp') | ForEach-Object { Join-Path $PSScriptRoot "native/$_" }
foreach($source in $sources){if(!(Test-Path -LiteralPath $source -PathType Leaf)){throw "Missing source: $source"}}
New-Item -ItemType Directory -Path $output | Out-Null
$headers=Join-Path $output 'headers'
$objects=Join-Path $output 'obj'
New-Item -ItemType Directory -Path $headers,$objects | Out-Null
# Extract verified bytes afresh; never trust a mutable prior header expansion.
& tar -xzf (Join-Path $dependencies 'headers.tar.gz') -C $headers
if($LASTEXITCODE){throw 'Pinned Node header extraction failed.'}
$nodeInclude=Join-Path $headers 'node-v22.22.3/include/node'
$vsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vsRoot=& $vsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vsRoot){throw 'Existing Visual Studio x64 C++ tools are required.'}
$vc=Get-ChildItem -LiteralPath (Join-Path $vsRoot 'VC/Tools/MSVC') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$sdkRoot=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10'
$sdk=Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Include') -Directory | Where-Object {Test-Path (Join-Path $_.FullName 'um/Windows.h')} | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$compilerDirectory=Join-Path $vc.FullName 'bin/Hostx64/x64'
$compiler=Join-Path $compilerDirectory 'cl.exe'
$includes=@("/I$($vc.FullName)/include","/I$nodeInclude")+@('ucrt','shared','um' | ForEach-Object {"/I$sdkRoot/Include/$($sdk.Name)/$_"})
$libraries=@("/LIBPATH:$($vc.FullName)/lib/x64","/LIBPATH:$sdkRoot/Lib/$($sdk.Name)/ucrt/x64","/LIBPATH:$sdkRoot/Lib/$($sdk.Name)/um/x64")
$binary=Join-Path $output 'geometry_lease.node'
$arguments=@('/nologo','/MT','/std:c++20','/utf-8','/EHsc','/W4','/WX','/O2','/permissive-',
    '/DUNICODE','/D_UNICODE','/DWIN32_LEAN_AND_MEAN','/DNOMINMAX','/DBUILDING_NODE_EXTENSION','/DNAPI_VERSION=8','/LD')+
    $includes+$sources+@("/Fo:$objects\","/Fe:$binary",'/link',"/IMPLIB:$output/geometry_lease.lib",
    '/DELAYLOAD:node.exe','delayimp.lib','user32.lib',(Join-Path $dependencies 'node.lib'))+$libraries
$oldPath=$env:PATH
try {
    $env:PATH="$compilerDirectory;$oldPath"
    & $compiler @arguments *> (Join-Path $output 'build.log')
    if($LASTEXITCODE){throw "Generated geometry build failed; inspect $output/build.log"}
} finally {$env:PATH=$oldPath}
[ordered]@{
    schemaVersion=1;nodeHeaders='22.22.3';nodeApiVersion=8;dependencyHashes=$pins;
    compiler=$compiler;compilerSha256=(Get-FileHash $compiler).Hash;sdk=$sdk.Name;
    sources=@($sources | ForEach-Object {@{path=$_;sha256=(Get-FileHash $_).Hash}});
    binary=$binary;binarySha256=(Get-FileHash $binary).Hash;installed=$false;launched=$false;
    scope='Generated own-process geometry qualification only; not a production addon or Discord modification.'
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'provenance.json')
Write-Output $binary
