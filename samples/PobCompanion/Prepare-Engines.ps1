[CmdletBinding()]
param([string]$CacheDirectory, [string]$VisualStudioTools)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $CacheDirectory) { $CacheDirectory = Join-Path $repository 'artifacts/pob/prepared' }
$CacheDirectory = [IO.Path]::GetFullPath($CacheDirectory)
$locks = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'engine-lock.json') -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Force -Path $CacheDirectory | Out-Null
foreach ($name in @('poe1','poe2','luaJit','luaUtf8')) {
    $dependency = $locks.$name
    $target = Join-Path $CacheDirectory ($name + '-' + $dependency.commit)
    if (-not (Test-Path -LiteralPath (Join-Path $target '.git'))) {
        & git init $target
        & git -C $target remote add origin $dependency.repository
        & git -C $target fetch --depth 1 origin $dependency.commit
        if ($LASTEXITCODE -ne 0) { throw "Could not fetch $name." }
        if ($name.StartsWith('poe')) {
            & git -C $target sparse-checkout set --no-cone '*.lua' '*.bin' '*.jsonc' '/src/Data/TimelessJewelData/' '/runtime/zlib1.dll' '/LICENSE.md' '/manifest.xml' '/spec/TestBuilds/' '!/src/Export/'
            if ($LASTEXITCODE -ne 0) { throw 'Could not configure engine checkout.' }
        }
        & git -C $target checkout --detach FETCH_HEAD
        if ($LASTEXITCODE -ne 0) { throw "Could not prepare $name." }
    }
    if ($name.StartsWith('poe') -and (Test-Path -LiteralPath (Join-Path $target '.git/info/sparse-checkout'))) {
        & git -C $target sparse-checkout add '/src/Data/TimelessJewelData/' '/runtime/zlib1.dll'
        if ($LASTEXITCODE -ne 0) { throw 'Could not include upstream jewel data.' }
    }
    if ((& git -C $target rev-parse HEAD).Trim() -ne $dependency.commit) { throw "Unexpected commit in $target." }
    & git -C $target diff --quiet HEAD
    if ($LASTEXITCODE -ne 0) { throw "Tracked changes in $target; restore the pinned sources before building." }
}
if (-not $VisualStudioTools) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $installation) { throw 'Visual Studio C++ x64 build tools are required to prepare the Lua runtime.' }
    $VisualStudioTools = Join-Path $installation 'VC/Auxiliary/Build/vcvars64.bat'
}
$jit = Join-Path $CacheDirectory ('luaJit-' + $locks.luaJit.commit)
$utf8 = Join-Path $CacheDirectory ('luaUtf8-' + $locks.luaUtf8.commit)
$runtime = Join-Path $CacheDirectory 'runtime'
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
foreach ($path in @($VisualStudioTools,$jit,$utf8)) {
    if ($path.Contains('"') -or $path.Contains("`n") -or $path.Contains('%')) { throw 'Build paths contain unsupported command characters.' }
}
$build = Join-Path $CacheDirectory 'build-runtime.cmd'
@"
@echo off
call "$VisualStudioTools"
if errorlevel 1 exit /b 1
cd /d "$jit\src"
call msvcbuild.bat
if errorlevel 1 exit /b 1
cd /d "$utf8"
cl /nologo /LD /O2 /MD /DLUA_BUILD_AS_DLL /I "$jit\src" lutf8lib.c "$jit\src\lua51.lib" /link /OUT:lua-utf8.dll
exit /b %errorlevel%
"@ | Set-Content -LiteralPath $build -Encoding ascii
& $build
if ($LASTEXITCODE -ne 0) { throw 'Lua runtime build failed.' }
Copy-Item (Join-Path $jit 'src/luajit.exe'),(Join-Path $jit 'src/lua51.dll'),(Join-Path $utf8 'lua-utf8.dll') $runtime
Copy-Item (Join-Path $jit 'COPYRIGHT') (Join-Path $runtime 'LuaJIT-COPYRIGHT')
Copy-Item (Join-Path $utf8 'LICENSE') (Join-Path $runtime 'LuaUTF8-LICENSE')
[ordered]@{ RuntimeDirectory=$runtime; Poe1=Join-Path $CacheDirectory ('poe1-'+$locks.poe1.commit); Poe2=Join-Path $CacheDirectory ('poe2-'+$locks.poe2.commit) } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $CacheDirectory 'paths.json') -Encoding utf8
Get-Content -LiteralPath (Join-Path $CacheDirectory 'paths.json')
