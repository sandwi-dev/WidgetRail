# Playnite scrolling and memory fixture

Use this fixture to reproduce library scrolling with artwork without touching a
real library or making service requests. Compare exact source commits and matched
build settings. Historical measurements and rejected experiments are available
in Git history; they are not performance guarantees for today's build.

## Repeatable fixture and reproduction

`scripts/Test-PlaynitePerformance.ps1` uses an isolated catalogue/profile and the
production Playnite widget/application, bridge, indexed leases, native GridView
and artwork decoder. Only the upstream provider is fake. The normal three-game,
no-artwork process-test fixture remains the default.

Performance mode uses 2,000 games, distinct resource handles, eight 1000×1500
cover JPEGs and eight 1920×1080 background JPEGs (about 5.4 MB on disk), plus
15 ms provider delay. It traverses/reverses the library in three 20-second
passes, settles between passes, then tests lifecycle transitions. There are no
network requests, credentials, game launches or user-library edits. Generated
high-entropy artwork and this scroll rate deliberately stress the pipeline.

First build a normal candidate with `scripts/Build-WinUiReleasePayload.ps1`.
For measurements, build the validation frontend with the same native inputs:

```powershell
$candidate = (Resolve-Path 'artifacts/performance/playnite-candidate').Path
dotnet build src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj `
  -c Release -p:EnableWidgetValidation=true -p:PublishTrimmed=false `
  -p:Platform=x64 `
  "-p:OverlayPlatformInteropPath=$candidate/native/platform/OverlayPlatformInterop.dll" `
  "-p:WindowPreviewNativePath=$candidate/native/preview/WinUiWindowPreviewNative.dll" `
  '-bl:artifacts/performance/validation-{}.binlog'

& scripts/Test-PlaynitePerformance.ps1 -Prepare -NavigationChecks `
  -CandidateRoot $candidate `
  -FrontendExecutable src/OverlayFrontend.WinUI/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64/OverlayFrontend.WinUI.exe `
  -Workspace artifacts/performance/reproduction -RunName artwork-01

& scripts/Summarize-PlaynitePerformance.ps1 `
  -RunDirectory artifacts/performance/reproduction/artwork-01
```

Use a fresh workspace with `-Prepare`; omit it for further runs with fresh run
names. Use `-WithoutArtwork` to isolate presentation cost, and `-HeapSnapshot`
only with the portable dump tools available at the paths expected by the script.
Keep builds, other tests and profiler collection out of timed runs. Do not
compare a validation build with a trimmed production frontend: the former has
additional test metadata. Preserve executable hashes and identical build flags
when comparing revisions.

The output includes per-process private working set/private bytes/CPU/handles,
managed allocation/GC statistics, native tile/slot/image counts, sampled visible
artwork coverage, composition callback gaps, dispatcher delays, and test
results. These are validation-only diagnostics, not permanent production logs.

## Scroll and focus regression checks

Use `-ScrollDirectionChecks` for held-stick direction checks, or
`-WheelComparison stick` / `-WheelComparison wheel-input` for matched native
scroll captures. `-CaptureComparison` records the visual comparison. See the
[runner](../../scripts/Test-PlaynitePerformance.ps1) for supported arguments.
Do not tune realization cache sizes based only on a static memory number: include
artwork coverage, focus return, scroll direction, latency and retained memory.

Controller scrolling uses native animated viewport changes with a retained target;
it does not synthesize mouse-wheel input in production. Passive focus settlement
must not scroll backwards against an active gesture. Explicit navigation and
reveal still need to bring their target into view. Keep these cases separate in
regression coverage.

Measure frontend, bridge, worker and browser processes separately. Task Manager
working set is not total committed memory, GPU memory or allocation rate. Compare
matched cold and warm runs, with no simultaneous builds/profilers unless that is
the explicit measurement. A synthetic fixture does not certify all libraries,
DPI settings, storage devices or controllers.

For a real-library check, `scripts/New-WinUiPlayniteWorkspace.ps1` prepares an
isolated catalogue with a separately reviewed package. It needs the user's local
Bridge connection; do not copy tokens into reports or replace the normal profile.
See the [application fixture](../../tests/PlayniteLibraryApplicationFixture/README.md)
and [performance guide](performance.md).
