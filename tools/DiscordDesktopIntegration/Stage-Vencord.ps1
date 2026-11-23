[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [string]$PluginDirectory,
    [switch]$PrepareOnly,
    [switch]$PlanOnly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$artifactRoot = Join-Path $repo 'artifacts/discord-desktop'
if (!$OutputDirectory) { $OutputDirectory = Join-Path $artifactRoot ('vencord-build-' + [guid]::NewGuid().ToString('N')) }
if (!$PluginDirectory) { $PluginDirectory = Join-Path $repo 'samples/DiscordDesktopAdapter/plugin' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$plugin = [IO.Path]::GetFullPath($PluginDirectory)
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'vencord-source.json') -Raw | ConvertFrom-Json

function Assert-NoReparseAncestors([string]$Path) {
    $current = $Path
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and
            ((Get-Item -LiteralPath $current).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Reparse points are not allowed in staged build paths: $current"
        }
        $current = Split-Path -Parent $current
    }
}
if (!$output.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Build output must be a fresh child of this checkout artifacts/discord-desktop directory.'
}
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh build output directory; staging never replaces existing files.' }
Assert-NoReparseAncestors $output
Assert-NoReparseAncestors $plugin
if (!(Test-Path -LiteralPath (Join-Path $plugin 'index.ts') -PathType Leaf) -or
    !(Test-Path -LiteralPath (Join-Path $plugin 'native.ts') -PathType Leaf)) {
    throw 'The reviewed plugin directory must contain index.ts and native.ts.'
}
foreach ($directory in @(Get-ChildItem -LiteralPath $plugin -Recurse -Directory)) { Assert-NoReparseAncestors $directory.FullName }
$pluginFiles = @(Get-ChildItem -LiteralPath $plugin -Recurse -File | Where-Object {
    $_.Extension -in @('.ts', '.tsx', '.mjs', '.js', '.json', '.css') -and $_.Name -notmatch '\.test\.'
})
if ($pluginFiles.Count -gt 64) { throw 'Plugin exceeds the staged source-file limit.' }
foreach ($file in $pluginFiles) { Assert-NoReparseAncestors $file.FullName }
if ($pin.schemaVersion -ne 1 -or $pin.commit -notmatch '^[a-f0-9]{40}$' -or
    $pin.repository -ne 'https://github.com/Vendicated/Vencord.git' -or $pin.lockfileSha256 -notmatch '^[A-F0-9]{64}$') {
    throw 'Invalid pinned upstream build contract.'
}
$inventory = @($pluginFiles | Sort-Object FullName | ForEach-Object {
    [pscustomobject]@{ path=[IO.Path]::GetRelativePath($plugin,$_.FullName).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$plan = [ordered]@{
    schemaVersion=1; source=$pin; outputDirectory=$output; pluginDirectory=$plugin; pluginFiles=$inventory
    installPerformed=$false; launchPerformed=$false; productionSafeToInstall=$false
    scope='Isolated source build only. No inject/uninject/installer command, Discord launch, user profile access or installed-client changes.'
}
if ($PlanOnly) { $plan | ConvertTo-Json -Depth 7; return }

$nodeVersion = (& node --version).Trim()
if ($LASTEXITCODE -or $nodeVersion -notmatch '^v(\d+)\.' -or [int]$Matches[1] -lt $pin.minimumNodeMajor) { throw "Node >=$($pin.minimumNodeMajor) is required." }
if (!(Get-Command corepack -ErrorAction SilentlyContinue)) { throw 'Corepack is required to select the pinned pnpm; do not substitute the global pnpm version.' }
New-Item -ItemType Directory -Path $output | Out-Null
$source = Join-Path $output 'source'
$logs = Join-Path $output 'logs'
New-Item -ItemType Directory -Path $logs | Out-Null
function Invoke-BuildCommand([string]$Name,[string]$Executable,[string[]]$Arguments) {
    & $Executable @Arguments 2>&1 | Tee-Object -FilePath (Join-Path $logs ($Name+'.log')) | Write-Host
    if ($LASTEXITCODE) { throw "$Name failed with exit code $LASTEXITCODE; see the isolated build logs." }
}
function Replace-Exact([string]$Path,[string]$Before,[string]$After) {
    $text = [IO.File]::ReadAllText($Path).Replace("`r`n","`n")
    $Before=$Before.Replace("`r`n","`n");$After=$After.Replace("`r`n","`n")
    if ([regex]::Matches($text,[regex]::Escape($Before)).Count -ne 1) { throw "Pinned source hardening anchor changed: $Path" }
    [IO.File]::WriteAllText($Path,$text.Replace($Before,$After),[Text.UTF8Encoding]::new($false))
}
Invoke-BuildCommand 'clone' 'git' @('clone','--no-checkout','--filter=blob:none',$pin.repository,$source)
Invoke-BuildCommand 'checkout' 'git' @('-C',$source,'checkout','--detach',$pin.commit)
$actualCommit = (& git -C $source rev-parse HEAD).Trim()
if ($LASTEXITCODE -or $actualCommit -ne $pin.commit) { throw 'Upstream commit verification failed.' }
if ((Get-FileHash -LiteralPath (Join-Path $source 'pnpm-lock.yaml') -Algorithm SHA256).Hash -ne $pin.lockfileSha256) { throw 'Pinned dependency lockfile hash changed.' }
$package = Get-Content -LiteralPath (Join-Path $source 'package.json') -Raw | ConvertFrom-Json
if ($package.packageManager -ne $pin.packageManager -or $package.version -ne $pin.version) { throw 'Pinned package metadata changed.' }

# Explicit isolated-build deviations. Do not allow this probe to install another
# Discord version's bootstrap, update itself, or activate unrelated core/plugins.
Replace-Exact (Join-Path $source 'src/main/patcher.ts') 'require("./persistAfterDiscordUpdates");' '// WidgetRail isolated build: automatic Discord bootstrap repatching is disabled.'
Replace-Exact (Join-Path $source 'src/main/patcher.ts') 'import { onceDefined } from "@shared/onceDefined";' '// WidgetRail isolated build does not force-enable Discord developer tools.'
Replace-Exact (Join-Path $source 'src/main/patcher.ts') 'import { IS_VANILLA } from "./utils/constants";' 'import { DATA_DIR, IS_VANILLA } from "./utils/constants";'
Replace-Exact (Join-Path $source 'src/main/patcher.ts') 'process.env.DATA_DIR = join(app.getPath("userData"), "..", "Vencord");' 'process.env.DATA_DIR = DATA_DIR; // Keep the explicitly selected isolated Vencord data directory.'
$devtoolsBlock = @'
    // Patch appSettings to force enable devtools
    onceDefined(global, "appSettings", s => {
        s.set("DANGEROUS_ENABLE_DEVTOOLS_ONLY_ENABLE_IF_YOU_KNOW_WHAT_YOURE_DOING", true);
    });
'@
Replace-Exact (Join-Path $source 'src/main/patcher.ts') $devtoolsBlock '    // WidgetRail isolated build leaves the user''s Discord developer-tools setting unchanged.'
Replace-Exact (Join-Path $source 'scripts/build/common.mjs') 'const pluginDirs = ["plugins/_api", "plugins/_core", "plugins", "userplugins"];' 'const pluginDirs = ["userplugins"]; // WidgetRail isolated probe: no bundled core or optional plugins.'
Replace-Exact (Join-Path $source 'scripts/build/build.mjs') 'const pluginDirs = ["plugins", "userplugins"];' 'const pluginDirs = ["userplugins"]; // WidgetRail isolated probe native helpers only.'
$destination = Join-Path $source 'src/userplugins/widgetRailDesktop'
foreach ($file in $inventory) {
    $target = Join-Path $destination $file.path
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $plugin $file.path) -Destination $target
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $file.sha256) { throw 'Plugin source changed during staging; use a fresh build after editing stops.' }
}
& git -C $source diff -- src/main/patcher.ts scripts/build/common.mjs scripts/build/build.mjs | Set-Content -LiteralPath (Join-Path $output 'upstream-hardening.patch')
if ($LASTEXITCODE) { throw 'Could not record isolated upstream deviations.' }
$plan.nodeVersion = $nodeVersion
$plan.hardeningPatchSha256 = (Get-FileHash -LiteralPath (Join-Path $output 'upstream-hardening.patch') -Algorithm SHA256).Hash
$plan.hardening = @('disable-updater-build-flag','no-automatic-host-repatch','only-widgetrail-plugin-and-native-helpers','no-forced-discord-devtools','isolated-vencord-data-directory')
$isolatedData = Join-Path $output 'vencord-data'
$bootstrapConfig = [ordered]@{ patcher=Join-Path $source 'dist/patcher.js';dataDirectory=$isolatedData;
    runtimeFiles=@('patcher.js','preload.js','renderer.js','renderer.css') | ForEach-Object {Join-Path $source ('dist/'+$_)} }
$bootstrapTemplate = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Bootstrap.cjs'))
if ([regex]::Matches($bootstrapTemplate,'__WR_CONFIG__').Count -ne 1) { throw 'Bootstrap configuration marker changed.' }
$bootstrap = $bootstrapTemplate.Replace('__WR_CONFIG__',($bootstrapConfig | ConvertTo-Json -Depth 3 -Compress))
[IO.File]::WriteAllText((Join-Path $output 'bootstrap-index.js'),$bootstrap,[Text.UTF8Encoding]::new($false))
$plan.bootstrapProposal = [ordered]@{path='bootstrap-index.js';sha256=(Get-FileHash -LiteralPath (Join-Path $output 'bootstrap-index.js') -Algorithm SHA256).Hash;dataDirectory=$isolatedData;installed=$false;
    templateSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'Bootstrap.cjs')).Hash;missingRuntimeFallback='original-sibling-asar-before-hooks';partialInitializationFallback=$false}
$plan.preparedUtc = [DateTimeOffset]::UtcNow.ToString('O')
$plan.buildCompleted = $false
if (!$PrepareOnly) {
    $previousEpoch = [Environment]::GetEnvironmentVariable('SOURCE_DATE_EPOCH','Process')
    $previousHash = [Environment]::GetEnvironmentVariable('VENCORD_HASH','Process')
    try {
        # Upstream consumes this value as milliseconds (its default is Date.now).
        $env:SOURCE_DATE_EPOCH = ([long]$pin.sourceDateEpoch * 1000).ToString([Globalization.CultureInfo]::InvariantCulture)
        $env:VENCORD_HASH = $pin.commit.Substring(0,8)
        Push-Location $source
        try {
            $pnpmVersion = (& corepack pnpm --version).Trim()
            if ($LASTEXITCODE -or ('pnpm@'+$pnpmVersion) -ne $pin.packageManager) { throw 'Corepack did not select the pinned pnpm.' }
            $plan.pnpmVersion = $pnpmVersion
            Invoke-BuildCommand 'restore' 'corepack' @('pnpm','install','--frozen-lockfile','--ignore-scripts','--store-dir',(Join-Path $output 'pnpm-store'))
            Invoke-BuildCommand 'build' 'corepack' @('pnpm','run','build','--disable-updater')
            Invoke-BuildCommand 'typecheck' 'corepack' @('pnpm','exec','tsc','--noEmit','--pretty','false')
        } finally { Pop-Location }
    } finally {
        [Environment]::SetEnvironmentVariable('SOURCE_DATE_EPOCH',$previousEpoch,'Process')
        [Environment]::SetEnvironmentVariable('VENCORD_HASH',$previousHash,'Process')
    }
    $buildTools = Join-Path $output 'build-tools'
    Invoke-BuildCommand 'bootstrap-tests' 'node' @('--test',(Join-Path $PSScriptRoot 'bootstrap.test.mjs'))
    $plan.bootstrapProposal.fallbackTestsPassed = $true
    New-Item -ItemType Directory -Path $buildTools | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'package.json'),(Join-Path $PSScriptRoot 'package-lock.json') -Destination $buildTools
    Invoke-BuildCommand 'bootstrap-tool-restore' 'npm' @('ci','--prefix',$buildTools,'--ignore-scripts','--no-audit','--no-fund')
    Invoke-BuildCommand 'bootstrap-package' 'node' @((Join-Path $PSScriptRoot 'Pack-Bootstrap.mjs'),$output)
    $archive = Get-Content -LiteralPath (Join-Path $output 'bootstrap-archive.json') -Raw | ConvertFrom-Json
    $plan.bootstrapProposal.archivePath = $archive.archivePath
    $plan.bootstrapProposal.archiveSha256 = $archive.archiveSha256
    $plan.bootstrapProposal.roundTripVerified = $archive.roundTripVerified
    $plan.bootstrapProposal.packagingLibrary = $archive.library
    $plan.bootstrapProposal.packagingLockSha256 = (Get-FileHash -LiteralPath (Join-Path $buildTools 'package-lock.json')).Hash
    $plan.bootstrapProposal.packagingToolSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'Pack-Bootstrap.mjs')).Hash
    foreach ($requiredArtifact in @('patcher.js','preload.js','renderer.js','renderer.css')) {
        if (!(Test-Path -LiteralPath (Join-Path $source ('dist/'+$requiredArtifact)))) { throw "Expected desktop build artifact missing: $requiredArtifact" }
    }
    $plan.artifacts = @(Get-ChildItem -LiteralPath (Join-Path $source 'dist') -File | Where-Object { $_.Name -match '^(patcher|preload|renderer)\.' } | Sort-Object Name | ForEach-Object {
        [pscustomobject]@{ path='source/dist/'+$_.Name; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash; bytes=$_.Length }
    })
    $rendererMap = Get-Content -LiteralPath (Join-Path $source 'dist/renderer.js.map') -Raw | ConvertFrom-Json
    $patcherMap = Get-Content -LiteralPath (Join-Path $source 'dist/patcher.js.map') -Raw | ConvertFrom-Json
    $allSources = @($rendererMap.sources) + @($patcherMap.sources)
    # Vencord's exported settings/API modules statically import a few plugin
    # definitions. Their presence in a sourcemap is not plugin activation.
    # Check the generated registration modules, whose format is pinned above.
    $registryIndex = [Array]::IndexOf($rendererMap.sources,'import-plugins:~plugins')
    $nativeIndex = [Array]::IndexOf($patcherMap.sources,'import-natives:~pluginNatives')
    if ($registryIndex -lt 0 -or $nativeIndex -lt 0) { throw 'Expected generated plugin registration modules are missing.' }
    $registry = $rendererMap.sourcesContent[$registryIndex]
    $nativeRegistry = $patcherMap.sourcesContent[$nativeIndex]
    $registrations = @([regex]::Matches($registry,'(?m)^import p\d+ from "([^"]+)";') | ForEach-Object { $_.Groups[1].Value })
    $nativeRegistrations = @([regex]::Matches($nativeRegistry,'(?m)^import \* as p\d+ from "([^"]+)";') | ForEach-Object { $_.Groups[1].Value })
    if ($registrations.Count -ne 1 -or $registrations[0] -ne './userplugins/widgetRailDesktop' -or
        $nativeRegistrations.Count -ne 1 -or $nativeRegistrations[0] -ne './userplugins/widgetRailDesktop/native') {
        throw 'An unexpected plugin/native helper was registered in the isolated probe build.'
    }
    if (@($allSources | Where-Object { $_ -match 'persistAfterDiscordUpdates' }).Count) { throw 'Automatic host repatching leaked into the isolated probe build.' }
    if ([IO.File]::ReadAllText((Join-Path $source 'dist/patcher.js')).Contains('DANGEROUS_ENABLE_DEVTOOLS_ONLY_ENABLE_IF_YOU_KNOW_WHAT_YOURE_DOING')) { throw 'Forced Discord developer-tools setting leaked into the probe.' }
    if (!@($rendererMap.sources | Where-Object { $_ -match 'src/userplugins/widgetRailDesktop/index\.ts$' }).Count -or
        !@($patcherMap.sources | Where-Object { $_ -match 'src/userplugins/widgetRailDesktop/native\.ts$' }).Count) {
        throw 'The expected WidgetRail renderer/native plugin entrypoints were not bundled.'
    }
    $plan.bundleScopeVerified = $true
    $plan.typecheckPassed = $true
    $plan.registeredPlugins = $registrations
    $plan.registeredNativeHelpers = $nativeRegistrations
    $plan.staticallyImportedSupportSources = @($allSources | Where-Object { $_ -match '(?:^|/)src/plugins/' } | Sort-Object -Unique)
    $plan.buildCompleted = $true
}
$plan | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'provenance.json')
$plan | ConvertTo-Json -Depth 8
