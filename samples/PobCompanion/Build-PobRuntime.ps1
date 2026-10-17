[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'Choose a fresh runtime directory.'}
if($out -match '[%!"\r\n]'){throw 'Runtime build path contains unsupported command-file characters.'}
New-Item -ItemType Directory -Path $out | Out-Null
$locks=Get-Content (Join-Path $PSScriptRoot engine-lock.json) -Raw|ConvertFrom-Json
$sources=@{luaJit='https://github.com/LuaJIT/LuaJIT.git';luaUtf8='https://github.com/starwing/luautf8.git'}
foreach($name in @('luaJit','luaUtf8')){
    $lock=$locks.$name
    if($lock.repository -ne $sources[$name] -or $lock.commit -notmatch '^[a-f0-9]{40}$'){throw 'Invalid runtime source lock.'}
    $target=Join-Path $out $name;New-Item -ItemType Directory -Path $target|Out-Null
    & git -C $target init --quiet
    if($LASTEXITCODE -ne 0){throw 'Git initialization failed.'}
    & git -C $target fetch --depth=1 $lock.repository $lock.commit 2>&1|Tee-Object (Join-Path $out "$name-fetch.log")
    if($LASTEXITCODE -ne 0){throw 'Runtime source fetch failed.'}
    & git -C $target checkout --detach FETCH_HEAD
    if($LASTEXITCODE -ne 0 -or (& git -C $target rev-parse HEAD).Trim() -ne $lock.commit){throw 'Runtime source revision does not match its lock.'}
}
$locator=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs=& $locator -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(-not $vs){throw 'Visual Studio x64 C++ build tools are required.'}
$vcvars=Join-Path $vs 'VC/Auxiliary/Build/vcvars64.bat'
$jit=Join-Path $out 'luaJit/src';$utf8=Join-Path $out luaUtf8
$commands=@"
@echo off
call "$vcvars"
if errorlevel 1 exit /b 1
cd /d "$jit"
call msvcbuild.bat
if errorlevel 1 exit /b 1
cd /d "$utf8"
cl /nologo /LD /O2 /MD /DLUA_BUILD_AS_DLL /I "$jit" lutf8lib.c "$jit/lua51.lib" /link /OUT:lua-utf8.dll
exit /b %errorlevel%
"@
$commandPath=Join-Path $out build-runtime.cmd
[IO.File]::WriteAllText($commandPath,$commands,[Text.Encoding]::ASCII)
& $env:ComSpec /d /c $commandPath 2>&1|Tee-Object (Join-Path $out build-runtime.log)
if($LASTEXITCODE -ne 0){throw 'Pinned Lua runtime compilation failed.'}
$runtime=Join-Path $out runtime;New-Item -ItemType Directory -Path $runtime|Out-Null
Copy-Item -LiteralPath (Join-Path $jit luajit.exe),(Join-Path $jit lua51.dll),(Join-Path $utf8 lua-utf8.dll) -Destination $runtime
Copy-Item -LiteralPath (Join-Path $out luaJit/COPYRIGHT) -Destination (Join-Path $runtime LuaJIT-COPYRIGHT)
Copy-Item -LiteralPath (Join-Path $utf8 LICENSE) -Destination (Join-Path $runtime LuaUTF8-LICENSE)
@{luaJit=$locks.luaJit.commit;luaUtf8=$locks.luaUtf8.commit;files=@(Get-ChildItem $runtime -File|ForEach-Object{@{name=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})}|
    ConvertTo-Json -Depth 5|Set-Content (Join-Path $runtime provenance.json) -Encoding utf8
Write-Output "Built pinned runtime at $runtime"
