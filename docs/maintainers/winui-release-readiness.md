# WinUI Release readiness

The first analyzer-enabled Release publish of the integrated frontend failed
with IL2026. Release enables trimming; reflection-based JSON calls in production
configuration, media transport, diagnostics and validation pages are not safe in
that configuration. This is an unresolved shipping gate, not permission to suppress
trim warnings or describe a Debug candidate as release-qualified.

The initial correction gives shell options/preferences a generated JSON context
and constructs media command/DevTools envelopes from explicit native JSON values.
Options retain their case-insensitive reading; persisted preferences retain their
existing strict names, bounds, validation and atomic replacement. Media messages
retain authority numbers, command sequencing and optional-field omission.

All 67 managed shell checks pass both normally and with
`JsonSerializerIsReflectionEnabledByDefault=false`; the emitted runtimeconfig was
checked for that false feature switch. The native frontend analyzer build passes
without warnings. Evidence: `artifacts/winui-release-probe/` and unique binlogs.
This does not yet prove a successful trimmed publish or native browser playback.

The frontend now compiles its validation entrypoints/pages only when
`EnableWidgetValidation=true` (the Debug default). Release defaults to false and
also excludes the gallery and embedded validation adapter resource. A metadata
inspection of the Release assembly confirms the absence of validation/gallery
types. The production window uses the same activation/lifecycle setup; optional
partial hooks retain controller replay and fixture activation in validation builds.
Launch arguments are parsed once and passed to the selected route.

Shell and explicit layout diagnostics now emit native JSON values rather than
reflecting anonymous objects, preserving existing field names and nonfinite layout
sentinels. The next Release publish passed frontend compilation and reached the
linker. It then exposed reflection serialization in the shared settings store,
Bridge framing, presentation-session admission and snapshot codec, plus Windows
SDK/WinRT assembly warnings. Those dependency findings remain unresolved; trimming
was not disabled and warnings were not suppressed. Debug still builds analyzer-clean.

Evidence: `shipping-types.json`, `split-debug.log`, `explicit-debug.log` and
`explicit-diagnostics.log` under the same probe directory. Native routing and
diagnostic-shape checks after this entrypoint split are still pending the shared
deployment slot.

Still required:

- Complete explicit serialization through the shared dependency graph and inspect
  the full Windows SDK/WinRT linker warnings before choosing a correction.
- Keep new switch diagnostics reflection-free, and keep expensive tree/file capture
  opt-in and outside ordinary frame work.
- Rerun the actual trimmed publish and resolve subsequent dependency findings.
- Verify a Release production shell against its matching Bridge/workers, including
  settings round trips, media commands, artwork, menus and lifecycle cleanup.
- Complete installer bootstrap, runtime provisioning and rollback qualification.
  No package registration, certificate change, installation or distribution was
  performed by the publish probe.

Release and Debug comparisons must use the same actual widget workload and normal
motion/depth settings. The resource observer alone cannot establish smooth frames
or complete first-frame presentation; use switch milestones plus native pixel/frame
evidence and repeated runs.

## First successful trimmed publish, 2026-09-28

The shared metadata correction removed WidgetRail's trim diagnostics. The remaining
35 IL2081 diagnostics originated in the older Windows SDK projection's generic ABI
fallbacks. The frontend now targets Windows SDK .NET projection `10.0.26100.87`,
the stable .NET 9/10 projection documented by the
[C#/WinRT 2.3.1 release](https://github.com/microsoft/CsWinRT/releases/tag/2.3.1.260716.1).
The target OS version and the Windows App SDK package remain unchanged.

Its collection-expression analyzer also requires a concrete representation where
WinRT marshalling could observe a non-mutable interface. Production call sites and
validation fixtures now explicitly create arrays rather than relying on compiler
generated read-only collection types. These changes do not change collection
membership or the public widget declarations.

Analyzer-enabled Release publish with trimming and detailed linker diagnostics
now succeeds without suppressions or blanket assembly roots. Evidence:
`artifacts/winui-release-probe/concrete-publish.log` and its unique binlog. Debug
also builds with zero warnings/errors, and all 73 integrated managed shell tests
pass. Native regression under this projection, Release launch, installer bootstrap,
provisioning and rollback remain required. This result is not shipping acceptance.

Subsequent native qualification passes 200 style checks, 21 switching checks,
four corrected radial scale/text cases, 56 context-menu checks, and all five real
Playnite eviction/modal checks. Embedded-media behavior passes 101 checks; its
screen-pixel gate remains open because popup-phase capture could not establish
foreground and direct window capture was blank.

Both the ordinary Release build output and the actual trimmed/ReadyToRun publish
pass the five real Playnite checks. The latter was launched through project-mode
`winapp run --no-build` with `OutDir` set to the publish directory. Publish did not
include AppxManifest.xml, so the matching generated Release manifest was copied
into the test layout before development registration. The running AppX assembly's
SHA-256 matched the published assembly exactly. Evidence:
`artifacts/winui-shell/{release-runtime-01,trimmed-runtime-01}/`, including
`trimmed-runtime-01/identity.json`. This is a tested development layout, not a
completed installer or a distributable release. Installer bootstrap and runtime
provisioning still need a deliberate production path.

The stable media pixel gate now passes using `Capture-WinUiTestWindow.ps1`, which
reads screen pixels without asking UIA to focus the main HWND. It verifies the
owned window/process before and after capture, preserves physical DPI coordinates,
and checks the sealed media fixture's characteristic button color. Both native
fullscreen and popup captures were inspected: media is visible and the dropdown
draws above it. All 101 behavior checks also pass. Evidence:
`artifacts/winui-shell/media-readonly-capture-01/`. This resolves this capture gate;
it does not replace motion, device-loss or wider media qualification.

## Continued implementation build gate, 2026-09-29

The current migration tree again publishes trimmed/ReadyToRun Release with the
bundled WinUI analyzers and detailed linker warnings enabled, without suppressions
or warnings/errors. Debug validation code also compiles analyzer-clean in an
isolated output directory while the prior physical candidate remains running.
Evidence: artifacts/winui-shell/native-continuation-20260929/{release01,debug-isolated01}.binlog.
This is build qualification only: no package registration or installer change was
performed, and the new native surface/collection regressions await the exclusive
frontend slot.
