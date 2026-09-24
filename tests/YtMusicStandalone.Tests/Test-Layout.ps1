param([Parameter(Mandatory)][string]$TaffyLibrary)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$native = Join-Path $repo 'src/OverlayHost'
$out = Join-Path $repo 'artifacts/ytmusic/layout-probe'
New-Item -ItemType Directory -Path $out -Force | Out-Null
foreach ($project in @('YtMusicStandalone.Tests', 'WidgetBridge.Tests')) {
    & dotnet build "$repo/tests/$project/$project.csproj" -c Release --nologo "-bl:$out/$project-{}.binlog"
    if ($LASTEXITCODE -ne 0) { throw "$project build failed." }
}
& dotnet run --project "$repo/tests/YtMusicStandalone.Tests/YtMusicStandalone.Tests.csproj" -c Release --no-build -- --export-layout $out
if ($LASTEXITCODE -ne 0) { throw 'Widget fixture export failed.' }
foreach ($state in @('empty', 'playing', 'status-short', 'status-long', 'library', 'home', 'library-return', 'library-refresh', 'search')) {
    & dotnet run --project "$repo/tests/WidgetBridge.Tests/WidgetBridge.Tests.csproj" -c Release --no-build -- --export-styled-fixture "$out/$state.snapshot.json" "$repo/samples/YtMusicWidget/styles/default.wrss" "$out/$state.json"
    if ($LASTEXITCODE -ne 0) { throw 'Production style projection failed.' }
}
$vs = & "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$vc = Get-ChildItem "$vs/VC/Tools/MSVC" -Directory | Sort-Object Name -Descending | Select-Object -First 1
$sdkRoot = "${env:ProgramFiles(x86)}/Windows Kits/10"
$sdk = Get-ChildItem "$sdkRoot/Include" -Directory | Sort-Object Name -Descending | Select-Object -First 1
$env:PATH = "$sdkRoot/bin/$($sdk.Name)/x64;$($vc.FullName)/bin/Hostx64/x64;$env:PATH"
$arguments = @('/nologo', '/std:c++20', '/utf-8', '/EHsc', '/W4', '/MP2', '/DUNICODE', '/D_UNICODE', '/DNOMINMAX', '/DWIN32_LEAN_AND_MEAN', '/DWRAIL_DECLARATIVE_RENDERER_TESTING', '/DWRAIL_WIDGET_BRIDGE_CLIENT_TESTING', "/I$native", "/I$($vc.FullName)/include")
foreach ($part in @('ucrt', 'shared', 'um', 'winrt', 'cppwinrt')) { $arguments += "/I$sdkRoot/Include/$($sdk.Name)/$part" }
$arguments += Join-Path $PSScriptRoot 'LayoutRendererProbe.cpp'
foreach ($source in @('WidgetBridgeClient', 'PublicSuffixDomainAuthority', 'DeclarativeRenderer', 'DeclarativeLayout', 'NativeStyle', 'NativeTextLayout', 'DeclarativeMotion', 'NativeIcons', 'RemoteImageCache', 'ArtworkDecoderProcessOwner', 'WidgetSurfaceFocus', 'FocusNavigation')) { $arguments += Join-Path $native "$source.cpp" }
$arguments += @("/Fo$out\", "/Fe$out/LayoutRendererProbe.exe", '/link', '/SUBSYSTEM:CONSOLE', "/LIBPATH:$($vc.FullName)/lib/x64", "/LIBPATH:$sdkRoot/Lib/$($sdk.Name)/ucrt/x64", "/LIBPATH:$sdkRoot/Lib/$($sdk.Name)/um/x64", $TaffyLibrary, 'd2d1.lib', 'dwrite.lib', 'winhttp.lib', 'windowscodecs.lib', 'ole32.lib', 'windowsapp.lib', 'user32.lib', 'bcrypt.lib', 'normaliz.lib', 'ntdll.lib', 'userenv.lib', 'ws2_32.lib')
& "$($vc.FullName)/bin/Hostx64/x64/cl.exe" $arguments
if ($LASTEXITCODE -ne 0) { throw 'Native layout probe build failed.' }
& "$out/LayoutRendererProbe.exe" "$out/empty.json" "$out/playing.json" "$out/status-short.json" "$out/status-long.json" "$out/library.json" "$out/home.json" "$out/library-return.json" "$out/library-refresh.json" "$out/search.json"
if ($LASTEXITCODE -ne 0) { throw 'Native layout geometry checks failed.' }
