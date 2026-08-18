[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputRoot = 'artifacts/evidence/auth-free',
    [string]$BuildId = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ'),
    [string]$VerifyManifest,
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
$env:MSBUILDDISABLENODEREUSE = '1'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$script:stageReports = [System.Collections.Generic.List[object]]::new()

function ConvertTo-EvidenceRelativePath {
    param([string]$Root, [string]$Path)
    [System.IO.Path]::GetRelativePath($Root, $Path).Replace('\', '/')
}

function Get-EvidenceFileInventory {
    param([string]$Root)
    @(Get-ChildItem -LiteralPath $Root -Recurse -File |
        Where-Object Name -ne 'manifest.json' |
        ForEach-Object {
            [ordered]@{
                path = ConvertTo-EvidenceRelativePath $Root $_.FullName
                length = $_.Length
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        } | Sort-Object path)
}

function Invoke-BoundedProcess {
    param(
        [Parameter(Mandatory)][string]$Stage,
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [string]$WorkingDirectory = $repoRoot,
        [ValidateRange(1, 1800)][int]$TimeoutSeconds = 180,
        [hashtable]$EnvironmentVariables = @{},
        [int[]]$AllowedExitCodes = @(0),
        [switch]$Quiet
    )

    Write-Host "[$Stage] starting (timeout ${TimeoutSeconds}s)"
    $started = [DateTimeOffset]::UtcNow
    $process = $null
    try {
        $start = [System.Diagnostics.ProcessStartInfo]::new()
        $start.FileName = $FilePath
        $start.WorkingDirectory = $WorkingDirectory
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        foreach ($argument in $ArgumentList) { $start.ArgumentList.Add($argument) }
        foreach ($entry in $EnvironmentVariables.GetEnumerator()) {
            $start.Environment[[string]$entry.Key] = [string]$entry.Value
        }

        $process = [System.Diagnostics.Process]::new()
        $process.StartInfo = $start
        if (-not $process.Start()) { throw "Process did not start." }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            try { $process.Kill($true) } catch { }
            try { $null = $process.WaitForExit(5000) } catch { }
            throw "Stage exceeded ${TimeoutSeconds}s; its process tree was terminated."
        }
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if (-not $Quiet) {
            if (-not [string]::IsNullOrWhiteSpace($stdout)) { Write-Host $stdout.TrimEnd() }
            if (-not [string]::IsNullOrWhiteSpace($stderr)) { Write-Warning $stderr.TrimEnd() }
        }
        if ($process.ExitCode -notin $AllowedExitCodes) {
            $diagnostic = ($stdout + [Environment]::NewLine + $stderr).Trim()
            if ($diagnostic.Length -gt 3000) { $diagnostic = $diagnostic.Substring(0, 3000) }
            throw "Stage exited with code $($process.ExitCode): $diagnostic"
        }
        $duration = [Math]::Round(([DateTimeOffset]::UtcNow - $started).TotalMilliseconds, 1)
        $script:stageReports.Add([ordered]@{
            name = $Stage; status = 'passed'; durationMs = $duration; exitCode = $process.ExitCode
        })
        Write-Host "[$Stage] passed in ${duration}ms"
        [pscustomobject]@{ StdOut = $stdout; StdErr = $stderr; ExitCode = $process.ExitCode }
    }
    catch {
        $duration = [Math]::Round(([DateTimeOffset]::UtcNow - $started).TotalMilliseconds, 1)
        $script:stageReports.Add([ordered]@{
            name = $Stage; status = 'failed'; durationMs = $duration
        })
        throw "[$Stage] $($_.Exception.Message)"
    }
    finally {
        if ($null -ne $process) { $process.Dispose() }
    }
}

function Assert-SemanticArtifactsSafe {
    param([string]$SemanticRoot)
    $forbidden = @(
        # JSON escapes each backslash. Require a lexical boundary before a drive
        # letter so the "p:/" suffix in "http://" cannot look like a drive, and
        # require four encoded backslashes for UNC so ".\\tools" stays valid.
        [ordered]@{ name = 'absolute Windows or UNC path'; pattern = '(?i)(?:(?<![a-z0-9+.-])[a-z]:(?:\\\\|/)|\\\\\\\\)[^\s\"'']+' },
        [ordered]@{ name = 'absolute user-home path'; pattern = '(?i)/(?:home|users)/[^/\s\"'']+' },
        [ordered]@{ name = 'OAuth callback secret'; pattern = '(?i)[?&](?:code|state|access_token|refresh_token|client_secret)=[^&\s\"'']+' },
        [ordered]@{ name = 'bearer credential'; pattern = '(?i)bearer\s+[a-z0-9._~+\-/]+=*' }
    )
    foreach ($file in Get-ChildItem -LiteralPath $SemanticRoot -Recurse -File -Filter '*.json') {
        $content = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($rule in $forbidden) {
            if ($content -match $rule.pattern) {
                $relative = ConvertTo-EvidenceRelativePath $SemanticRoot $file.FullName
                throw "Semantic artifact '$relative' contains a forbidden $($rule.name)."
            }
        }
    }
}

function Test-EvidenceBundle {
    param([Parameter(Mandatory)][string]$ManifestPath)
    $resolvedManifest = [System.IO.Path]::GetFullPath($ManifestPath)
    if (-not (Test-Path -LiteralPath $resolvedManifest -PathType Leaf)) {
        throw "Evidence manifest does not exist: $resolvedManifest"
    }
    $bundleRoot = Split-Path -Parent $resolvedManifest
    $manifest = Get-Content -LiteralPath $resolvedManifest -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 2) { throw "Unsupported evidence schema '$($manifest.schemaVersion)'." }
    if ($manifest.harness.kind -ne 'standalone-widget-body') {
        throw "Manifest does not declare the standalone widget-body harness boundary."
    }

    $expected = @($manifest.retainedFiles | ForEach-Object path | Sort-Object)
    if ($expected.Count -ne (@($expected | Select-Object -Unique)).Count) {
        throw 'Manifest retained-file paths are not unique.'
    }
    foreach ($path in $expected) {
        if ([string]::IsNullOrWhiteSpace($path) -or
            [System.IO.Path]::IsPathFullyQualified($path) -or
            $path.Contains('..') -or $path.Contains('\')) {
            throw "Manifest retained-file path is unsafe: '$path'."
        }
    }
    $actual = @(Get-ChildItem -LiteralPath $bundleRoot -Recurse -File |
        Where-Object FullName -ne $resolvedManifest |
        ForEach-Object { ConvertTo-EvidenceRelativePath $bundleRoot $_.FullName } |
        Sort-Object)
    $missing = @($expected | Where-Object { $_ -notin $actual })
    $extra = @($actual | Where-Object { $_ -notin $expected })
    if ($missing.Count -or $extra.Count) {
        throw "Retained-file set mismatch. Missing: [$($missing -join ', ')]. Extra: [$($extra -join ', ')]."
    }
    foreach ($entry in $manifest.retainedFiles) {
        $path = Join-Path $bundleRoot ([string]$entry.path).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        $item = Get-Item -LiteralPath $path
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($item.Length -ne [long]$entry.length -or $hash -ne [string]$entry.sha256) {
            throw "Retained artifact '$($entry.path)' does not match its recorded length/hash."
        }
    }
    foreach ($package in $manifest.packages) {
        $entry = $manifest.retainedFiles | Where-Object path -eq $package.archive | Select-Object -First 1
        if (-not $entry -or $entry.sha256 -ne $package.archiveSha256) {
            throw "Package archive '$($package.archive)' is absent or does not match its package digest."
        }
    }
    foreach ($capture in $manifest.captures) {
        $snapshot = $manifest.retainedFiles | Where-Object path -eq $capture.semanticSnapshot | Select-Object -First 1
        $png = $manifest.retainedFiles | Where-Object path -eq $capture.png | Select-Object -First 1
        if (-not $snapshot -or $snapshot.sha256 -ne $capture.semanticSnapshotSha256 -or
            -not $png -or $png.sha256 -ne $capture.pngSha256) {
            throw "Capture '$($capture.id)' does not match retained snapshot/PNG digests."
        }
        if ([int]$capture.rendererDiagnosticCount -ne 0) {
            throw "Capture '$($capture.id)' retained an unexpected renderer diagnostic."
        }
    }
    Assert-SemanticArtifactsSafe (Join-Path $bundleRoot 'semantic')
    Write-Host "Evidence bundle verified: $resolvedManifest ($($actual.Count) retained files)."
}

function Invoke-EvidenceVerifierSelfTest {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) "wrail-evidence-verifier-$([Guid]::NewGuid().ToString('N'))"
    try {
        $semantic = Join-Path $root 'semantic'
        New-Item -ItemType Directory -Path $semantic | Out-Null
        $artifact = Join-Path $semantic 'sample.json'
        Set-Content -LiteralPath $artifact -Value '{"safe":true}' -Encoding utf8NoBOM
        $manifest = [ordered]@{
            schemaVersion = 2
            harness = [ordered]@{ kind = 'standalone-widget-body' }
            retainedFiles = Get-EvidenceFileInventory $root
            packages = @()
            captures = @()
        }
        $manifestPath = Join-Path $root 'manifest.json'
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
        Test-EvidenceBundle $manifestPath

        Set-Content -LiteralPath (Join-Path $root 'extra.txt') -Value 'extra' -Encoding utf8NoBOM
        $extraRejected = $false
        try { Test-EvidenceBundle $manifestPath } catch { $extraRejected = $true }
        Remove-Item -LiteralPath (Join-Path $root 'extra.txt') -Force

        Set-Content -LiteralPath $artifact -Value '{"safe":false}' -Encoding utf8NoBOM
        $mismatchRejected = $false
        try { Test-EvidenceBundle $manifestPath } catch { $mismatchRejected = $true }
        Remove-Item -LiteralPath $artifact -Force
        $missingRejected = $false
        try { Test-EvidenceBundle $manifestPath } catch { $missingRejected = $true }
        if (-not ($extraRejected -and $mismatchRejected -and $missingRejected)) {
            throw 'Evidence verifier did not reject extra, mismatched, and missing artifacts.'
        }
        'pass/missing/extra/mismatch'
    }
    finally {
        if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
    }
}

$profiles = @(
    [ordered]@{ id = 'compact-default'; logicalWidth = 560; logicalHeight = 420; dpi = 96; textScale = 1.0; reducedTransparency = $false; highContrast = $false },
    [ordered]@{ id = 'standard-default'; logicalWidth = 880; logicalHeight = 520; dpi = 120; textScale = 1.0; reducedTransparency = $false; highContrast = $false },
    [ordered]@{ id = 'compact-accessible'; logicalWidth = 560; logicalHeight = 420; dpi = 144; textScale = 1.5; reducedTransparency = $true; highContrast = $false },
    [ordered]@{ id = 'wide-high-contrast'; logicalWidth = 1120; logicalHeight = 620; dpi = 144; textScale = 1.5; reducedTransparency = $false; highContrast = $true }
)
$cases = @(
    [ordered]@{ id = 'games-auto-library-compact'; packageId = 'widgetrail.firstparty.games-apps'; state = 'initial'; profile = 'compact-default' },
    [ordered]@{ id = 'games-auto-library-accessible'; packageId = 'widgetrail.firstparty.games-apps'; state = 'initial'; profile = 'compact-accessible' },
    [ordered]@{ id = 'games-catalog-standard'; packageId = 'widgetrail.firstparty.games-apps'; state = 'catalog'; profile = 'standard-default' },
    [ordered]@{ id = 'games-catalog-wide'; packageId = 'widgetrail.firstparty.games-apps'; state = 'catalog'; profile = 'wide-high-contrast' },
    [ordered]@{ id = 'games-populated-standard'; packageId = 'widgetrail.firstparty.games-apps'; state = 'populated'; profile = 'standard-default' },
    [ordered]@{ id = 'games-populated-wide'; packageId = 'widgetrail.firstparty.games-apps'; state = 'populated'; profile = 'wide-high-contrast' },
    [ordered]@{ id = 'games-removing-standard'; packageId = 'widgetrail.firstparty.games-apps'; state = 'removing'; profile = 'standard-default' },
    [ordered]@{ id = 'games-removed-compact'; packageId = 'widgetrail.firstparty.games-apps'; state = 'removed'; profile = 'compact-default' },
    [ordered]@{ id = 'games-removed-accessible'; packageId = 'widgetrail.firstparty.games-apps'; state = 'removed'; profile = 'compact-accessible' },
    [ordered]@{ id = 'games-removed-wide'; packageId = 'widgetrail.firstparty.games-apps'; state = 'removed'; profile = 'wide-high-contrast' },
    [ordered]@{ id = 'spotify-client-id-compact'; packageId = 'widgetrail.samples.spotify'; state = 'initial'; profile = 'compact-default' },
    [ordered]@{ id = 'spotify-client-id-accessible'; packageId = 'widgetrail.samples.spotify'; state = 'initial'; profile = 'compact-accessible' },
    [ordered]@{ id = 'spotify-setup-compact'; packageId = 'widgetrail.samples.spotify'; state = 'setup'; profile = 'compact-default' },
    [ordered]@{ id = 'spotify-setup-standard'; packageId = 'widgetrail.samples.spotify'; state = 'setup'; profile = 'standard-default' },
    [ordered]@{ id = 'spotify-setup-accessible'; packageId = 'widgetrail.samples.spotify'; state = 'setup'; profile = 'compact-accessible' },
    [ordered]@{ id = 'spotify-setup-wide'; packageId = 'widgetrail.samples.spotify'; state = 'setup'; profile = 'wide-high-contrast' }
)

if ($VerifyManifest) {
    Test-EvidenceBundle $VerifyManifest
    return
}
if (($profiles.id | Select-Object -Unique).Count -ne $profiles.Count) { throw 'Evidence profile IDs are not unique.' }
if (($cases.id | Select-Object -Unique).Count -ne $cases.Count) { throw 'Evidence case IDs are not unique.' }
foreach ($case in $cases) {
    if ($case.profile -notin $profiles.id) { throw "Evidence case '$($case.id)' names an unknown profile." }
}
if ($SelfTest) {
    $verifierResult = Invoke-EvidenceVerifierSelfTest
    [ordered]@{
        schemaVersion = 2
        profileCount = $profiles.Count
        captureCaseCount = $cases.Count
        harness = 'standalone-widget-body'
        processTimeouts = 'bounded with full process-tree termination'
        artifactVerification = 'exact retained file set, length, SHA-256, semantic safety scan'
        verifierSelfTest = $verifierResult
        rendererDiagnostics = 'fail closed; no allowlist is configured'
        passCriteria = 'schema, semantic invariants, renderer success, and artifact integrity; never a golden full-image hash'
    } | ConvertTo-Json -Depth 5
    return
}
if ($BuildId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$') {
    throw 'BuildId must be 1-64 letters, digits, dots, underscores, or hyphens.'
}

$resolvedOutputRoot = if ([System.IO.Path]::IsPathFullyQualified($OutputRoot)) {
    [System.IO.Path]::GetFullPath($OutputRoot)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
}
$buildDirectory = Join-Path $resolvedOutputRoot $BuildId
if (Test-Path -LiteralPath $buildDirectory) { throw "Evidence build directory already exists: $buildDirectory" }

$scratchRoot = Join-Path ([System.IO.Path]::GetTempPath()) "wrail-overlay-evidence-$([Guid]::NewGuid().ToString('N'))"
$stagedBundle = Join-Path $scratchRoot 'bundle'
$semanticDirectory = Join-Path $stagedBundle 'semantic'
$pngDirectory = Join-Path $stagedBundle 'png'
$toolDirectory = Join-Path $stagedBundle 'tools'
$nativeBuildDirectory = Join-Path $scratchRoot 'native-build'
$objectDirectory = Join-Path $nativeBuildDirectory 'obj'
New-Item -ItemType Directory -Path $semanticDirectory, $pngDirectory, $toolDirectory, $objectDirectory | Out-Null

try {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $testProject = Join-Path $repoRoot 'tests/FirstPartyWidgetConformance.Tests/FirstPartyWidgetConformance.Tests.csproj'
    $commonEnvironment = @{ MSBUILDDISABLENODEREUSE = '1' }
    $null = Invoke-BoundedProcess -Stage 'managed-exporter-build' -FilePath $dotnet -TimeoutSeconds 300 `
        -EnvironmentVariables $commonEnvironment -ArgumentList @(
            'build', $testProject, '--configuration', $Configuration, '--nologo',
            '--property:UseSharedCompilation=false', '--property:BuildInParallel=false')
    $null = Invoke-BoundedProcess -Stage 'semantic-export' -FilePath $dotnet -TimeoutSeconds 180 `
        -EnvironmentVariables $commonEnvironment -ArgumentList @(
            'run', '--no-build', '--configuration', $Configuration, '--project', $testProject,
            '--property:UseSharedCompilation=false', '--property:BuildInParallel=false',
            '--', '--evidence-output', $semanticDirectory)

    $vsWhere = Join-Path ${env:ProgramFiles} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (-not (Test-Path -LiteralPath $vsWhere)) { $vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe' }
    if (-not (Test-Path -LiteralPath $vsWhere)) { throw 'Visual Studio vswhere.exe was not found.' }
    $vsResult = Invoke-BoundedProcess -Stage 'toolchain-discovery' -FilePath $vsWhere -TimeoutSeconds 20 -Quiet `
        -ArgumentList @('-latest', '-products', '*', '-requires', 'Microsoft.VisualStudio.Component.VC.Tools.x86.x64', '-property', 'installationPath')
    $vsRoot = $vsResult.StdOut.Trim()
    if (-not $vsRoot) { throw 'Visual Studio C++ build tools were not found.' }
    $vcTools = Get-ChildItem -LiteralPath (Join-Path $vsRoot 'VC/Tools/MSVC') -Directory |
        Sort-Object Name -Descending | Select-Object -First 1
    if (-not $vcTools) { throw 'The MSVC toolset was not found.' }
    $cl = Join-Path $vcTools.FullName 'bin/Hostx64/x64/cl.exe'

    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10'
    $sdk = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Include') -Directory |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'um/Windows.h') } |
        Sort-Object Name -Descending | Select-Object -First 1
    if (-not $sdk) { throw 'A Windows 10 SDK was not found.' }
    $includeArguments = @(
        "/I$($vcTools.FullName)/include", "/I$sdkRoot/Include/$($sdk.Name)/ucrt",
        "/I$sdkRoot/Include/$($sdk.Name)/shared", "/I$sdkRoot/Include/$($sdk.Name)/um",
        "/I$sdkRoot/Include/$($sdk.Name)/winrt", "/I$sdkRoot/Include/$($sdk.Name)/cppwinrt")
    $libraryArguments = @(
        "/LIBPATH:$($vcTools.FullName)/lib/x64", "/LIBPATH:$sdkRoot/Lib/$($sdk.Name)/ucrt/x64",
        "/LIBPATH:$sdkRoot/Lib/$($sdk.Name)/um/x64")
    $overlaySource = Join-Path $repoRoot 'src/OverlayHost'
    $cargo = (Get-Command cargo -ErrorAction SilentlyContinue).Source
    if (-not $cargo) {
        $cargoCandidate = Join-Path $env:USERPROFILE '.cargo/bin/cargo.exe'
        if (Test-Path -LiteralPath $cargoCandidate) { $cargo = $cargoCandidate }
    }
    if (-not $cargo) { throw 'Rust Cargo was not found for the pinned Taffy evidence build.' }
    $taffyManifest = Join-Path $overlaySource 'taffy_bridge/Cargo.toml'
    $taffyTarget = Join-Path $nativeBuildDirectory 'cargo'
    $taffyArguments = @('build', '--locked', '--manifest-path', $taffyManifest,
        '--target', 'x86_64-pc-windows-msvc')
    if ($Configuration -eq 'Release') { $taffyArguments += '--release' }
    $taffyEnvironment = @{ CARGO_TARGET_DIR = $taffyTarget }
    $null = Invoke-BoundedProcess -Stage 'taffy-static-library-build' -FilePath $cargo -TimeoutSeconds 300 -EnvironmentVariables $taffyEnvironment -ArgumentList $taffyArguments
    $taffyProfile = if ($Configuration -eq 'Release') { 'release' } else { 'debug' }
    $taffyLibrary = Join-Path $taffyTarget "x86_64-pc-windows-msvc/$taffyProfile/wrail_taffy_layout.lib"
    if (-not (Test-Path -LiteralPath $taffyLibrary)) {
        throw "Pinned Taffy evidence library was not produced at '$taffyLibrary'."
    }
    $captureExecutable = Join-Path $nativeBuildDirectory 'OverlayEvidenceCapture.exe'
    $retainedRenderer = Join-Path $toolDirectory 'OverlayEvidenceCapture.exe'
    $optimization = if ($Configuration -eq 'Release') { @('/O2', '/DNDEBUG') } else { @('/Od', '/Zi') }
    $rendererSourceNames = @(
        'OverlayEvidenceCapture.cpp', 'WidgetBridgeClient.cpp', 'DeclarativeRenderer.cpp',
        'DeclarativeLayout.cpp', 'NativeStyle.cpp', 'NativeTextLayout.cpp', 'DeclarativeMotion.cpp',
        'NativeIcons.cpp', 'RemoteImageCache.cpp')
    $compilerArguments = @(
        '/nologo', '/std:c++20', '/utf-8', '/EHsc', '/W4', '/permissive-',
        '/DUNICODE', '/D_UNICODE', '/DWIN32_LEAN_AND_MEAN', '/DNOMINMAX',
        '/DWRAIL_WIDGET_BRIDGE_CLIENT_TESTING') + $optimization + $includeArguments +
        @($rendererSourceNames | ForEach-Object { Join-Path $overlaySource $_ }) + @(
            "/Fo:$objectDirectory/", "/Fe:$captureExecutable", '/link', '/SUBSYSTEM:CONSOLE') +
        $libraryArguments + @($taffyLibrary, 'ntdll.lib', 'userenv.lib', 'ws2_32.lib', 'd2d1.lib', 'dwrite.lib', 'windowsapp.lib', 'winhttp.lib', 'windowscodecs.lib', 'ole32.lib')
    $null = Invoke-BoundedProcess -Stage 'native-renderer-build' -FilePath $cl -TimeoutSeconds 300 `
        -ArgumentList $compilerArguments
    Copy-Item -LiteralPath $captureExecutable -Destination $retainedRenderer

    $indexPath = Join-Path $semanticDirectory 'evidence-index.json'
    $index = Get-Content -LiteralPath $indexPath -Raw | ConvertFrom-Json
    $captures = [System.Collections.Generic.List[object]]::new()
    foreach ($case in $cases) {
        $profile = $profiles | Where-Object id -eq $case.profile | Select-Object -First 1
        $snapshot = $index.snapshots | Where-Object { $_.packageId -eq $case.packageId -and $_.state -eq $case.state } | Select-Object -First 1
        if (-not $snapshot) { throw "Authoritative snapshot missing for capture '$($case.id)'." }
        $snapshotPath = Join-Path $semanticDirectory $snapshot.snapshotPath
        $pngPath = Join-Path $pngDirectory "$($case.id).png"
        $pixelWidth = [int][Math]::Round($profile.logicalWidth * $profile.dpi / 96.0)
        $pixelHeight = [int][Math]::Round($profile.logicalHeight * $profile.dpi / 96.0)
        $arguments = @('--input', $snapshotPath, '--output', $pngPath, '--width', $pixelWidth,
            '--height', $pixelHeight, '--dpi', $profile.dpi, '--text-scale', $profile.textScale)
        if ($profile.reducedTransparency) { $arguments += '--reduced-transparency' }
        if ($profile.highContrast) { $arguments += '--high-contrast' }
        $null = Invoke-BoundedProcess -Stage "render-$($case.id)" -FilePath $captureExecutable `
            -TimeoutSeconds 45 -ArgumentList $arguments
        if (-not (Test-Path -LiteralPath $pngPath)) { throw "Native evidence capture '$($case.id)' produced no PNG." }
        $captures.Add([ordered]@{
            id = $case.id; packageId = $case.packageId; state = $case.state; profile = $case.profile
            logicalWidth = $profile.logicalWidth; logicalHeight = $profile.logicalHeight
            pixelWidth = $pixelWidth; pixelHeight = $pixelHeight
            rendererDiagnosticCount = 0
            semanticSnapshot = ConvertTo-EvidenceRelativePath $stagedBundle $snapshotPath
            semanticSnapshotSha256 = (Get-FileHash -LiteralPath $snapshotPath -Algorithm SHA256).Hash.ToLowerInvariant()
            png = ConvertTo-EvidenceRelativePath $stagedBundle $pngPath
            pngSha256 = (Get-FileHash -LiteralPath $pngPath -Algorithm SHA256).Hash.ToLowerInvariant()
        })
    }

    Assert-SemanticArtifactsSafe $semanticDirectory
    $gitRevision = (Invoke-BoundedProcess -Stage 'git-revision' -FilePath (Get-Command git).Source `
        -TimeoutSeconds 20 -Quiet -ArgumentList @('rev-parse', 'HEAD')).StdOut.Trim()
    $gitStatus = (Invoke-BoundedProcess -Stage 'git-dirty-state' -FilePath (Get-Command git).Source `
        -TimeoutSeconds 20 -Quiet -ArgumentList @('status', '--porcelain=v1', '--untracked-files=all')).StdOut
    $dotnetVersion = (Invoke-BoundedProcess -Stage 'dotnet-version' -FilePath $dotnet `
        -TimeoutSeconds 20 -Quiet -ArgumentList @('--version')).StdOut.Trim()
    $cargoVersion = (Invoke-BoundedProcess -Stage 'cargo-version' -FilePath $cargo `
        -TimeoutSeconds 20 -Quiet -ArgumentList @('--version')).StdOut.Trim()
    $rustc = Join-Path (Split-Path -Parent $cargo) 'rustc.exe'
    if (-not (Test-Path -LiteralPath $rustc)) { $rustc = (Get-Command rustc -ErrorAction Stop).Source }
    $rustcVersion = (Invoke-BoundedProcess -Stage 'rustc-version' -FilePath $rustc `
        -TimeoutSeconds 20 -Quiet -ArgumentList @('--version')).StdOut.Trim()
    $sourceFiles = @($rendererSourceNames | ForEach-Object {
        $path = Join-Path $overlaySource $_
        [ordered]@{ path = "src/OverlayHost/$_"; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
    }) + @(@(
        'TaffyLayoutBridge.h',
        'taffy_bridge/Cargo.toml',
        'taffy_bridge/Cargo.lock',
        'taffy_bridge/rust-toolchain.toml',
        'taffy_bridge/src/lib.rs'
    ) | ForEach-Object {
        $path = Join-Path $overlaySource $_
        [ordered]@{ path = "src/OverlayHost/$_"; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
    }) + @(
        [ordered]@{ path = 'scripts/Capture-OverlayEvidence.ps1'; sha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant() },
        [ordered]@{ path = 'tests/FirstPartyWidgetConformance.Tests/Program.cs'; sha256 = (Get-FileHash -LiteralPath (Join-Path $repoRoot 'tests/FirstPartyWidgetConformance.Tests/Program.cs') -Algorithm SHA256).Hash.ToLowerInvariant() })
    $manifestPackages = @($index.packages | ForEach-Object {
        [ordered]@{
            id = $_.id; version = $_.version; publisher = $_.publisher
            archive = "semantic/$($_.archive)"; archiveSha256 = $_.archiveSha256
        }
    })
    $manifestTraces = @($index.traces | ForEach-Object {
        [ordered]@{ name = $_.name; tracePath = "semantic/$($_.tracePath)" }
    })
    $manifestInvariants = @($index.snapshots | ForEach-Object {
        [ordered]@{
            packageId = $_.packageId; state = $_.state
            snapshotPath = "semantic/$($_.snapshotPath)"; sequence = $_.sequence
            nodeCount = $_.nodeCount; nodeIdsUnique = $_.nodeIdsUnique
            everyNodeHasComputedStyles = $_.everyNodeHasComputedStyles
            initialFocusId = $_.initialFocusId
        }
    })

    $manifest = [ordered]@{
        schemaVersion = 2
        buildId = $BuildId
        generatedUtc = (Get-Date).ToUniversalTime().ToString('o')
        authority = $index.evidenceAuthority
        harness = [ordered]@{
            kind = 'standalone-widget-body'
            includes = @('retained installed package archive', 'generic AppContainer widget worker', 'simulated broker companion', 'production bridge style resolver', 'offscreen WIC/Direct2D renderer built from recorded production sources')
            excludes = @('OverlayHost shell/window', 'backdrop and z-order', 'tray/footer composition', 'focus and input ownership', 'shell/widget transitions', 'onscreen compositor and physical-display fidelity')
        }
        provenance = [ordered]@{
            source = [ordered]@{
                gitRevision = $gitRevision
                dirty = -not [string]::IsNullOrWhiteSpace($gitStatus)
                dirtyEntryCount = @($gitStatus -split "`r?`n" | Where-Object { $_ }).Count
                dirtyStatusSha256 = if ([string]::IsNullOrEmpty($gitStatus)) { $null } else { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($gitStatus))).ToLowerInvariant() }
                files = $sourceFiles
            }
            toolchain = [ordered]@{
                configuration = $Configuration
                dotnetSdk = $dotnetVersion
                cargo = $cargoVersion
                rustc = $rustcVersion
                taffy = '0.12.2'
                msvcToolset = $vcTools.Name
                msvcCompilerFileVersion = (Get-Item -LiteralPath $cl).VersionInfo.FileVersion
                windowsSdk = $sdk.Name
                powershell = $PSVersionTable.PSVersion.ToString()
                os = [Environment]::OSVersion.VersionString
                processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
            }
            stages = $script:stageReports
        }
        passCriteria = @(
            'retained real package archive and generic worker/broker route produced a valid semantic snapshot',
            'every exported node has a production computed-style entry',
            'standalone production-source renderer completed with zero diagnostics',
            'the exact retained file set, lengths, and SHA-256 digests pass the independent verifier')
        excludedPassCriteria = @(
            'golden full-image hashes', 'pixel-perfect equality across Windows font/rendering revisions',
            'shell/window/backdrop/tray/footer/transition/input fidelity')
        packages = $manifestPackages
        profiles = $profiles
        captures = $captures
        traces = $manifestTraces
        semanticInvariants = $manifestInvariants
        gaps = $index.gaps
        limitations = @($index.limitations) + @(
            'The retained capture matrix covers automatic, catalog, mixed, removing, and removed Games & Apps surfaces plus Spotify setup surfaces; managed suites retain the full loading, empty, failure, mutation, and bounded-page semantic matrix.',
            'Settings permission-detail capture remains an explicit gap.',
            'Hardware/window evidence for controller suppression and overlay z-order is outside this standalone auth-free artifact set.')
        retainedFiles = $null
    }
    $manifest.retainedFiles = Get-EvidenceFileInventory $stagedBundle
    $manifestPath = Join-Path $stagedBundle 'manifest.json'
    $manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
    Test-EvidenceBundle $manifestPath

    New-Item -ItemType Directory -Path $resolvedOutputRoot -Force | Out-Null
    Move-Item -LiteralPath $stagedBundle -Destination $buildDirectory
    $publishedManifest = Join-Path $buildDirectory 'manifest.json'
    Test-EvidenceBundle $publishedManifest
    Write-Host "Evidence manifest: $publishedManifest"
    Write-Host "Captured $($captures.Count) standalone widget-body PNGs from $($index.snapshots.Count) authoritative semantic snapshots."
}
finally {
    if (Test-Path -LiteralPath $scratchRoot) {
        Remove-Item -LiteralPath $scratchRoot -Recurse -Force
    }
}
