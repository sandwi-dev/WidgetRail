# WinUI migration implementation status

Updated 2026-10-01. Accepted migration checkpoint merged into main. Remaining qualification and deferred improvements below remain tracked.
The user reversed the full-MSIX direction on 2026-10-01. Distribution is now an
unpackaged WinUI application with Inno; neither full MSIX nor a signed identity
package may be required. Earlier MSIX entries below are historical evidence only.

## Current summary

### Native toggles and widget-list rows — 2026-10-01, validated; packaging pending

User authorized the remaining suggested improvements. Continue on the existing
codex/settings-usability branch (preview.17 is delivered but not accepted/merged).
UI.Switch keeps its SDK API and action authority, with a native ToggleSwitch
renderer. Snapshot publication must never emit actions; busy/disabled/inactive,
controller repeats, focus retention and live theme changes require native checks.
Settings widget-list entries use existing ActionSurface composition for separate
name/version/status, one stable focus target and unchanged package review gates.
User additionally requested Exclusive control be a separate page reached from
Controllers, merging its toggle/status/recovery with driver information and help.
Do not move that toggle back into the primary Controllers page. Installed app and
machine settings remain untouched; physical acceptance is user-owned. Preserve
normal Inno/manual-install delivery. No push or merge requested.

Implementation complete. UI.Switch keeps its SDK signature and existing semantic
marker/action contract. The host composes a stock sealed WinUI ToggleSwitch with a
small publication/event adapter: applying a snapshot never dispatches an action;
busy/disabled/passive or retired controls cannot dispatch. The control survives
state and busy updates, while changes between switch and ordinary command retire
the old native element. Retirement detaches its event handler. Screen-reader names
no longer repeat On/Off because native TogglePattern supplies the state. Ordinary
selected command buttons retain their Invoke semantics. Native stock brushes,
including Color-valued Off animation resources, follow theme/high-contrast colors.
Missing/transparent On palettes retain the native paired palette.

The Settings list now composes one existing ActionSurface per widget with distinct
name, status and version fields. No new public widget-list component. IDs/actions,
review gates and return focus remain stable. Controllers now contains a navigation
option for Exclusive control; its dedicated page merges toggle, live status,
recovery/check-again, drivers and help. Polling includes this page. Old inline
exclusive actions are rejected; valid dedicated-page actions keep existing driver
and recovery safeguards.

Passed: Settings81; SDK148; compatibility14; native toggles50; production Settings
layout253 at 880/520 DIP and ordinary/150% text; separate trimmed Release runtime
smoke includes ToggleSwitch template, native accessibility, user activation and
publication suppression plus the existing slider/artwork checks. Native toggle
checks cover all five bundled themes, high contrast, hover/pressed resource types,
controller repeat/release, busy and delayed publications, passive input denial,
retired callbacks and semantic control replacement. Rendered widget-list and
exclusive-page screenshots inspected. No system-setting/driver mutation performed.
Native fixtures use simulated actions and isolated profiles; installed app untouched.

Evidence: artifacts/settings-completion/. Initial build attempted derivation, but
WinUI ToggleSwitch is sealed: corrected to composition around the stock control.
A trim-fixture collection expression required explicit string[] for CsWinRT AOT;
fixed in the fixture. New catalog fixtures initially used invalid publisher IDs;
corrected them to reverse DNS. Existing SDK name expectations were updated for the
native Toggle provider, retaining assertions for serialized state and action IDs.
Code review also corrected Color-versus-Brush resources for stock visual states,
and verified disposal and controller-page admission. The pre-existing unrelated
documentation publication assertion recorded in preview.17 remains unchanged.
Normal preview.18 / FileVersion0.1.0.17 and Settings0.1.6 are ready for packaging.



### Settings organization and consistent rows — 2026-10-01, first-pass installer delivered

User accepted preview.16; main fast-forwarded to bf15d9d0. Unrelated instruction
edits preserved. Prior evidence copied to
C:/Users/dwive/Projects/WidgetRail-worktree-archive-20261001/winui-usability/.
Codex refused archive because a pinned task/workspace protects that checkout.
No bypass or pin changes. New branch: codex/settings-usability from bf15d9d0.

Scope: category ownership, consistent label/control rows, explicit preference
choices, stable Back/page title, concise help and management-first widget details.
Preserve persisted preferences, focus and permission/recovery gates. Synthetic
checks only; installed app update and physical acceptance remain user-owned.
Normal Inno distribution, no MSIX.

Implemented: General owns position/navigation/startup; Appearance owns theme,
sizing/backdrop and grouped animations; Accessibility has explicit three-state
motion/contrast choices in one page. Controllers uses an explicit shortcut picker,
with driver help separated while recovery stays actionable. About/troubleshooting
owns diagnostics/reset and shows the build version. Every subpage has stable Back
and title; Quit/Restart remain secondary home actions. Widget technical information
moves behind Technical details; trust/enable/permission confirmations remain intact.

Cause of scanning problems: settings were split by implementation history, and
combined-label buttons, toggles, steppers and selections used unrelated positions.
The new shared UI.SettingsField composition provides native responsive label/control
columns without duplicating control identity during reflow. Select.ShowLabel hides
only repeated visual text and preserves accessible name/value. Explicit preference
set actions are idempotent. Vertical focus links follow rows and preserve stepper
columns. Cards/headings wrap at larger text sizes. Existing settings remain valid.

Passed: Settings80 (including new category/focus/authority/choice tests), SDK148,
SDK compatibility14, PlatformSettings27, native layout213 (six actual pages,
880/520 DIP widths, ordinary/150% text, same-snapshot focus retention). Native
fixture reused unchanged accepted native DLLs with recorded hashes; the release
pipeline rebuilds them. Initial native fixture reused snapshot sequence1 across
pages; its stale-frame guard correctly retained the first page. Corrected the
fixture sequence, not production admission. Final layout screenshots inspected.

Documentation mapping for the new SDK file passes. The documentation suite retains
one unrelated baseline failure: its publication assertion expects inline code in
Build-WinUiReleasePayload.ps1, now delegated to a module. Both script and assertion
are unchanged from bf15d9d0. Not waived or reported green. No OS/provider mutations
were exercised. The installed preview.16 is running and remains untouched. Full
native live-service action qualification is user-owned; synthetic layout and managed
state/action guards passed. Evidence: artifacts/settings-usability/.

Code review checked stable identities, correct Back hierarchy, theme tokens,
accessible select values, recovery action availability on both controller pages,
exact preference setters and preserved permission/confirmation gates. Prepared
normal preview.17 / FileVersion0.1.0.16, Settings widget0.1.5.

Delivery complete: source599b162d built cleanly through the normal release and Inno
pipelines. Both 8/12-widget catalogs and release inventories passed. Production
107,295,411 bytes; Developer115,214,238 bytes. Installers, SHA256SUMS.txt and build
metadata copied and hash-verified in
C:/Users/dwive/Downloads/WidgetRail-Inno-0.1.0-preview.17/.
Published Settings retains the About version metadata after trimming. No installer
was executed; installed preview.16 remains running. Final regenerated layouts pass
213 native checks and their Appearance screenshot was inspected. Settings80,
SDK148, compatibility14 and PlatformSettings27 are green; the separate documentation
baseline assertion remains recorded above. New work stays on codex/settings-usability;
no push or integration of this unaccepted UI iteration.

Remaining design polish from the review: converting the existing SDK switch-button
contract to a native toggle renderer, and giving the installed-widget list separate
name/version/status fields. This first pass preserves those interaction contracts;
its delivered changes address category ownership, reading alignment, explicit choices,
page navigation and secondary detail hierarchy.


### Post-migration usability batch — 2026-10-01, installer delivered

FINAL: Normal Production and Developer preview.16 Inno installers are delivered in
C:/Users/dwive/Downloads/WidgetRail-Inno-0.1.0-preview.16/ with installer metadata and
SHA256SUMS.txt; copied hashes verified. Release source is d00323d1, FileVersion
0.1.0.15. Normal release pipeline rebuilt native libraries, trimmed frontend and
private services, and validated the 8/12-widget catalogs and both release inventories.
Actual pinned Inno 6.7.3 compiler built both editions. No installer was executed.

Installed CLI updated/enabled Music 0.3.35 and Video 0.3.37 in the normal user catalog;
list verifies both, with old versions retained. Playnite and Spotify unchanged.
Settings, credentials and widget data were preserved. Package copies are in the
Downloads delivery's Widgets folder. Initial install correctly refused an enabled
widget; disable/install/select/enable completed through normal CLI gates afterward.
No playback or system-changing widget commands ran. The app remains closed for the
user's manual installer update. Managed checks: 388; native checks: 534. Physical
appearance/media acceptance remains user-owned. No merge to main or push in this batch.
User approved parallel implementation on `codex/winui-usability`, based on main
16388cf4. Pinned Move/Resize and Opacity must retain the radial and return its
selection, matching Interact. Other entry contexts must restore remembered focus.
Additional scope: left-aligned Network rows; persistent Music and optional Video
volume; compact Music library filters; themed cold-start feedback that transitions
to the ready shell; Settle as the default focus motion; default declared permissions
for verified bundled widgets while preserving explicit revocations.

Ownership: pinned_modes handles pin adjustment/input; music_preferences handles
both media packages/preferences/layout; revert_msix_runtime handles bundled consent;
root handles startup, network style, default motion, integration and delivery.
Managed builds are serialized to avoid shared output races. Installed app/data are
unchanged until needed for validation; user authorized closing the overlay. Delivery
remains a normal Inno update for manual installation. No MSIX or test-profile changes.
Evidence: `artifacts/usability-20261001/` in the implementation checkout.
Implementation complete. Pinned adjustments retain their logical radial/widget
context, revoke input during adjustment, and restore exact native focus on Save/
Cancel. Hide/deactivation revokes deferred return. Network profile controls use
explicit start alignment. Focus defaults to Settle while explicit choices remain.
Startup reuses the themed widget loading indicator with the app mark, masks only
incomplete content, and crossfades once the first presentation is ready. Readiness
is separate from Bridge connection. Retry reuses an already connected Bridge;
input releases and hide/reopen cancellation remain owned by the host.

Verified bundled first-party packages seed only missing persisted permission grants
under the consent lock; explicit denials, downloaded packages and normal action/
lifecycle/gesture checks retain their existing behavior. Music and Video persist
intentional volume separately from provider observations using bounded atomic
coalesced writes. Music filters use compact widths/padding with 44px targets.
Music 0.3.35 / Video 0.3.37 packages are in artifacts/usability-music; both validated.

Managed checks passed: PlatformBroker64, bundled provenance2, window previews2,
Music31, YouTube55 (including JS adapter), PlatformSettings27, Network31,
WinUiMotion15, WinUiShell161. Native startup9 and pinned placement65 passed.
All 460 native style checks and normal preview.16 publication passed. Initial motion-test
invocation incorrectly passed MSBuild-only switches to the MTP runner; canonical
repository invocation discovered all tests. Updated the explicit Fade test to
select Fade now that the default is Settle. Native compile corrections were a
captured nullable Bridge owner and validation imports. A native retry regression
caught the previous Ready badge surviving into a new startup; clearing the old
badge on Begin fixed it and the replacement gate passed. First native harness
invocation used space-separated options instead of the required equals syntax;
that infrastructure timeout did not run the requested scenario.

The installed preview.15 was closed with its normal shutdown path. User explicitly
approved updating both media widgets via installed CLI after installer preparation.
No audio/network/display/power actions were sent; native checks used isolated fixture
profiles. Unchanged native DLLs for Debug validation came from accepted preview.15,
with hashes recorded; normal release pipeline rebuilds them from source.

### Accepted migration checkpoint and worktree retirement — 2026-10-01

The user confirmed the YouTube fix works and authorized merging the current
migration into main and deleting the old worktrees. The complete migration source
matches the accepted preview.15 release snapshot e8103e837965c0ee089757a856ddf9e93cbee8dc;
only this progress record differs. Existing release/native/test evidence remains
applicable; no further implementation changes are included in this integration.

Old worktree heads and any unfinished local changes are preserved separately from
main. Ignored diagnostic artifacts are retained outside the retired checkouts at
C:/Users/dwive/Projects/WidgetRail-worktree-archive-20261001/ with inventory and
recovery references. The unrelated local implementation-agent-goal.md edit in the
primary checkout is preserved. Installed app, data and downloaded installers are
untouched. No remote push is included.

Merge commit: 1818268f; migration completion commit: aa606458. User chose to
retain six Codex-protected worktrees: compose-host-assessment, cursor-diagnostics,
inno-distribution, playnite-details-poster-fill, spotify-profile-cleanup and
ytmusic-standalone. Their original ignored evidence remains in place. The other
87 worktrees are retired with recovery refs and evidence in the archive above.

### YouTube player API blocked after unpackaged rollback — 2026-10-01, installer delivered

Confirmed cause: removing MSIX identity left EmbeddedMediaSurface passing a null
application identity, so its request policy rejected every remote resource. The
local adapter became ready but YouTube's API could not load. The original native
host had an unpackaged executable-manifest fallback that the rollback missed.

The host now derives a stable identifier from its own managed assembly name
(Referer https://overlayfrontend.winui/), independent of package identity. Existing
HTTPS/origin/domain-family restrictions remain unchanged. Resource denials log one
bounded reason per surface; widgets cannot choose the host identifier.

Validation complete: 305/305 PresentationSession tests and 51 native checks pass,
including loading the real public YouTube iframe API and dependent script through
the production WebView2 request policy. No video/player was instantiated and no
playback, account or system-setting action was taken. The full published Debug
fixture supplied SDK theme resources missing from direct build output.
Evidence: artifacts/youtube-unpackaged-20261001/, including native-result.json.

Normal clean-source release and Inno builds passed for both editions at detached
release snapshot e8103e837965c0ee089757a856ddf9e93cbee8dc. Production preview.15
(FileVersion 0.1.0.14), Developer installer, SHA256SUMS.txt and installer-build.json
were copied to C:/Users/dwive/Downloads/WidgetRail-Inno-0.1.0-preview.15/; all four
copied SHA256 hashes match their originals. Production installer is
WidgetRail-0.1.0-preview.15-win-x64-setup.exe (107,282,007 bytes), SHA256
D949376769613C735B10C2F11C0FABB0C6E6EAE78B62CF1AB84F82425F598946.

User installs manually. Installed app/data left untouched; no automatic restart,
profile reset or installed-file patch. Existing settings/widgets are preserved by
the normal update; the YouTube widget package does not require reinstalling.

### Playnite, YouTube Video and Spotify installed through Inno CLI — 2026-10-01

At user request, installed and enabled the current migration packages using the
installed normal wrail.cmd and its default catalog: Playnite Library0.2.114,
YouTube Video0.3.36, Spotify0.3.83. Inspections match source manifest versions;
existing full-trust widget model approved through CLI. All three plus previously
installed YouTube Music0.3.34 are listed enabled. No playback, game launch, account
or system-setting actions performed. Evidence: `artifacts/inno-media-install-20261001/`.

### YouTube Music installed through the Inno CLI — 2026-10-01

User installed normal Production Inno0.1.0-preview.14; HKCU ApplicationRoot is
`%LOCALAPPDATA%/Programs/WidgetRail/versions/0.1.0-preview.14-production-25dc6780a1312103`.
At user request, its actual wrail.cmd/private-dotnet CLI inspected, installed and
enabled YouTube Music0.3.34 into the default `%LOCALAPPDATA%/WidgetRail/widgets`
catalog (initially empty). Reused reviewed full-trust application approval; no
profile override, copied MSIX state or playback action. CLI list verifies enabled.
Evidence: `artifacts/inno-ytmusic-install-20261001/`. Archive SHA256 remains
424899b8f5a036e03991e45d0e52af11207c9d8acc64d07a41e13691f9879eaf.

### MSIX rollback and unpackaged Inno restoration — 2026-10-01, installer delivered

FINAL: Normal Production and Developer Inno installers are in
`C:/Users/dwive/Downloads/WidgetRail-Inno-0.1.0-preview.14/`, with SHA256SUMS.txt and
installer-build.json. Primary installer is WidgetRail-0.1.0-preview.14-win-x64-setup.exe
(107,296,727 bytes); Developer is115,208,726 bytes. No installer was executed and no
application auto-launched; user explicitly chose manual installer testing.

Built through unchanged clean-source gates in normal Build-Release/Build-Installer,
using clean detached release snapshot39f5153ed074e56e1fa0d240cd6af7c54ac60156 at
`C:/Users/dwive/.codex/worktrees/inno-distribution/WidgetRail`. Snapshot captured
the migration work without changing the migration branch HEAD/index. Actual Inno6.7.3
compiler validated both editions. Both catalogs/inventories passed. Final Production
Audio Mixer/native UI loaded with8 bundled widgets, frontend and Bridge explicitly
verified without package identity; WinUI loaded app-local, Bridge loaded directly
from runtime/Bridge with private dotnet, no runtime-cache created, normal shutdown
drained workers. No system-setting actions sent. Evidence is final-runtime-local/
under artifacts/msix-revert-20261001; clean-worktree logs are artifacts/distribution/.

No profile override or test mode is embedded in the installer. Default paths and
shortcuts are normal distribution behavior. The unused MSIX-profile copy remains
only evidence. User data was neither reset nor moved; existing Inno uninstall
retain/delete/cancel choices remain. Old installed MSIX test package/certificate
were not uninstalled. Current SVG/open-close physical behavior should be retested
on this normal installation; no unrelated speculative fix was introduced.

User abandoned full MSIX and requested reverting its application changes. Clarified
that the former Inno identity package also requires a trusted signature; user
directed removing that identity dependency too. Preserve independent migration
fixes (including generated slider/native interop, monotonic Bridge invalidations,
focus/input handoff, controller recovery, capture and authoring improvements).

Saved the complete tracked diff and untracked-path inventory under
`artifacts/msix-revert-20261001/`; removed runtime sources have verified backups.
Runtime rollback restores direct Bridge startup, ordinary worker .NET environment,
and original sandbox content grants. Removed all protected-package/runtime-cache
machinery, including the unfinished sharing/cleanup work. Catalog37/37,
Runtime113/113 and PresentationSession304/304 pass. Named development-job ownership
and bounded Bridge startup diagnostics are retained.

Frontend is changing to WindowsPackageType=None with app-local WindowsAppSDK;
direct args/restart and external settings/browser paths replace package activation
and package-local storage. Settings StartupTask provider removed; ordinary owned
Run-entry support is restored. Inno/publication agent owns installer/packaging
rework; CLI agent owns direct EXE discovery/launch and is currently testing.
No MSIX uninstall, certificate trust change or live profile/cache deletion has been
performed. Installed0.1.0.4 may still be running; close normally only when the new
candidate is ready for isolated native checks and relaunch. The reported cold-start
SVG fallback/open-close symptoms remain unconfirmed; retest on the restored path
before attributing them to Bridge identity or introducing separate behavior fixes.

Runtime gates pass37/37 Catalog,113/113 Runtime,304/304 PresentationSession; direct
CLI gate82/82; synthetic Inno contracts/release assembly and actual Pascal compile
pass. Installed0.1.0.4 PID4052 was closed normally. A hash-verified copy of its
profile exists (336 files/33,853,173 bytes, excluding runtime-cache), but the user
subsequently chose a CLEAN profile to test first-use behavior. Do not launch using
the copied profile or import its widgets/settings. Fresh unpackaged publication
is building at `artifacts/msix-revert-20261001/publication` (exec session6842).
After publication, qualify direct native startup/slider controls and relaunch with
a separate new empty profile plus the8 production bundled widgets.

Latest user instruction supersedes that launch plan: prepare the NORMAL Inno
distribution installer for manual installation, with standard paths/shortcuts and
no test profile or application changes for a clean profile. Do not reset, move or
delete live local data. Original Inno uninstall retain/delete/cancel choice is
preserved and explained to the user. The copied MSIX profile is inactive evidence.

Unpackaged self-contained frontend builds. Native launch initially failed because
disabling EnableMsixTooling also disables the SDK's compiled XAML/PRI output for
WinExe. Restored that build-tool flag while keeping WindowsPackageType=None and
GenerateAppxPackageOnBuild=false: no MSIX is produced/registered. Unpackaged
publication emits OverlayFrontend.WinUI.pri (not packaged resources.pri); release
and verification inventories now require it. Direct trimmed native controls pass
(slider callbacks, endpoints, template, value-button and artwork accessibility).
Settings77/77, PlatformSettings27/27 and shell161/161 pass after restoring ordinary
synchronous Run-entry service contracts. Build real distribution via a clean source
snapshot/worktree and normal Build-Release + Build-Installer; do not add test-only
installer flags or weaken clean-source gates. No installer has been executed.

### Shared runtime cache and upgrade cleanup — 2026-10-01, in progress

User requested removing duplicated .NET cache bytes and obsolete per-version
entries. Split immutable .NET into its own verified lease, shared by Bridge and
generic workers; their code roots remain separate. Content keys use selection,
paths/sizes/hashes, not package version; current protected inventory remains the
authority and warm acquisitions still hash/pin every file. Runtime grants admit
two disjoint exact roots and serialize each root independently, without granting
sandbox access to Bridge/Settings code.

Added background cleanup retaining every current inventory selection (including
lazy widget entries). Cache publication/cleanup share a bounded cross-process
lock. Obsolete entries must be renamed as a whole before removal; pinned roots
deny rename, preserving active old processes. Unknown directories/reparse trees
are left alone. Tests cover content reuse, code-only upgrades sharing .NET, warm
integrity, active old leases, legacy-entry cleanup, lazy content retention and
concurrent acquire/cleanup. Catalog40/40 passes. Bridge/runtime native checks and
package preparation remain pending. Evidence: `artifacts/winui-shell/runtime-cache-20261001/`.
Installed0.1.0.4 is now running (observed PID4052); leave it running during these
isolated tests. No live cache deletion or automatic MSIX update performed.

### MSIX YouTube Music test installation — 2026-10-01

At the user's request, used the freshly published wrail CLI to inspect, install
and enable the migration-tested YouTube Music0.3.34 package in the installed test
identity's LocalState/WidgetRail/widgets catalog. Used the existing full-trust
application approval; archive SHA256
`424899b8f5a036e03991e45d0e52af11207c9d8acc64d07a41e13691f9879eaf`.
CLI list confirms enabled/selected0.3.34; no account/profile data copied and no
playback actions performed. Evidence: `artifacts/winui-shell/msix-ytmusic-install-20261001/`.
OS registration still reports host0.1.0.3; signed0.1.0.4 remains ready for the user
to install. Did not launch the known slider-broken0.1.0.3 for this installation.

### Installed Audio Mixer native slider failure — 2026-10-01, signed 0.1.0.4 ready

User reported Audio Mixer still failing on installed0.1.0.3. Its worker5964 stayed
alive with successful lifecycle responses. Frontend33656 logged E_NOINTERFACE
(0x80004002) in RangeBase.Maximum/Value from WidgetViewPresenter.UpdateSlider.
This is a host projection error, not worker startup or provider failure.

Reproduced the same error with an isolated trimmed/R2R Release fixture using only
synthetic slider values. WidgetSlider was non-partial, so C#/WinRT could not emit
its native override interfaces. Making it partial generates WinRTExposedType and
the inherited callback tables. The same fixture now passes three slider lifetimes,
fractional ranges, native value events, endpoints, coercion and template application.
Preserves all slider geometry, focus, settlement and interaction behavior.
Evidence: `artifacts/winui-shell/audio-slider-20261001/` baseline/fixed result files.
`scripts/Test-WinUiTrimmedControls.ps1` repeats this small trim-safe native check;
its fixture is excluded from shipping and ordinary debug builds. Both temporary
probe registrations were removed. Installed overlay closed with user permission.
Signed0.1.0.4 is ready in `C:/Users/dwive/Downloads/WidgetRail-MSIX-Test-20261001/WidgetRail-Test-x64-0.1.0.4.msix`;
user owns package installation. Existing certificate remains valid for this update.

User asked whether other required code could be missing. Enabled
CsWinRTAotWarningLevel=1 (warnings are errors): the production audit confirmed38
additional missing-partial declarations on WinRT-derived controls/automation peers
and explicit WinRT collection adapters. Corrected those declarations and the
containing PinnedWidgetWindow; applied the same correction to20 debug fixture types
plus their containing helper. This is generated-interop coverage, not evidence of
38 observed crashes. Previously these diagnostics were informational. The source
generator's documented partial requirement applies to trimming as well as AOT.
Expanded trimmed fixture also passes value-button accessibility and artwork
layout/accessibility; final-regression/result.txt records it. Fixed a test-driver
cleanup race by waiting for the fixture's own Close before attempting WM_CLOSE.
Temporary probe registration verified removed; no real provider actions used.
Debug builds cleanly with the diagnostic gate; all67 ordinary native slider
interaction/geometry checks pass. The native driver initially timed out because
WinApp development activation redirected the result to package LocalCache. It now
checks both known locations for a fresh result from the exact launched family;
the corrected driver passes. Full production package passed779 staging checks;
the published async-cleanup check still passes. Signature verified and747 unsigned
archive entries match the signed payload. SHA256:
`B4291730CB87099E88700BB8E20A64DDE91CFF9A8979B732830F2A28648BCC89`.
The installed0.1.0.3 app remains closed; no automatic package installation or old
developer-candidate relaunch. User must validate Audio Mixer in installed0.1.0.4.

### Startup cache size audit — 2026-10-01

Read-only measurement of installed0.1.0.3 protected inventory and live runtime-cache:
Bridge+private .NET = 198,914,200 bytes (189.70 MiB); Settings = 29,538,178 bytes
(28.17 MiB); generic worker+private .NET = 170,748,794 bytes (162.84 MiB); all seven
bundled sandbox widget payloads = 618,926 bytes (0.59 MiB). Complete fresh cache is
399,820,098 bytes (381.30 MiB). Bridge is acquired at startup; Settings and generic
worker runtime are lazy, and bundled widget content is acquired on demand.
Warm cache hits do not copy file contents, but hash and pin them again. The cache
key includes package full identity, so a version update creates new entries even
for unchanged runtime bytes. The private .NET tree is duplicated between Bridge
and generic-worker closures (160.10 MiB each). Live cache currently totals
940,666,264 bytes (897.09 MiB), including older entries; no old-entry pruning is
implemented in this cache. No deletion, restart, or production changes made.
These are logical file sizes/source-path findings, not a disk-I/O trace or timing
benchmark; verification and OS page caching affect actual startup I/O.

### Installed Settings worker launch correction — 2026-10-01, signed 0.1.0.3 ready

After the capability review, user requested continuing the crash investigation;
retain strict widget capability isolation. Installed0.1.0.2 had frontend424
and neutral Bridge30572 alive, but Settings failed before process creation. Direct neutral
launch of its installed executable reproduces Win32 error5 Access denied, saved
in `artifacts/winui-shell/installed-settings-20261001/direct-launch.json`.
BridgePackagedWorkerEnvironment previously returned null for every non-sandboxed
worker and therefore missed host-trusted Settings.

Added a fixed, separately verified Settings-only cache selection. Only the exact
trusted Settings identity/executable is mapped. Its original bundled-manifest root
is made absolute so moving the executable cannot change catalog discovery. The
Settings lease stays pinned until Bridge shutdown and receives no sandbox access
grants; its private .NET runtime remains inherited from the neutral Bridge. Other
full-trust community applications retain their existing path. No capability guard
or machine permission was relaxed.

New --installed-widget-startup probe uses the actual installed0.1.0.2 code and
inventory through the corrected production Bridge server/worker launch pipeline,
with isolated profile and simulated providers. All8 initial views pass: Settings,
Now Playing, Games & Apps, Task Switcher, Audio Mixer, Network, Power and Display.
No actions sent or system settings changed. Catalog39/39 and registry18/18 pass.
Full production0.1.0.3 build completed, including 779 staging checks. Signed update:
`C:/Users/dwive/Downloads/WidgetRail-MSIX-Test-20261001/WidgetRail-Test-x64-0.1.0.3.msix`.
Signature verified and all 747 archive entries match the unsigned payload; evidence
is in `artifacts/winui-shell/installed-settings-20261001/signed-update.json`.
The failed frontend was closed normally; frontend424 and Bridge30572 have exited.
User still owns installation (previously chose Prepare it; I'll install). The update
uses the existing certificate; no certificate reinstall needed. Complete installed
0.1.0.3 host startup remains user validation; the probe used installed0.1.0.2 payloads
with the corrected current Bridge launch pipeline and simulated providers.
Full physical workflow qualification remains distinct. Startup registration is
not qualified by this test: the host currently does not advertise that feature.

### User-requested package-capability design review — 2026-10-01

User rejected continuing the startup-copy/cache approach and requested review of
allowing the automatically added MSIX capability. Pause further cache-based fixes
and package rebuilds until that design is resolved. No new implementation changes
were made during the review. Installed0.1.0.2 still fails: frontend424 and neutral
Bridge/dotnet30572 are alive, Settings fails before process creation (start0).
Its exact underlying launch exception has not yet been captured; do not label it
confirmed Settings file-access failure solely from analogy with0.1.0.1.

Read-only live ACL inspection confirms package capability
S-1-15-3-3348665716-2650962135-2922169281-183895853-4255586732-4182594799-4213010897
has FullControl grants on platform-settings.json, consent/, widgets/ and LocalCache.
This shared grant bypasses the exclusivity intended by individual AppContainer
SID grants. Effective read/write still needs a real-token canary check including
mandatory integrity and filesystem virtualization; ACL inspection alone is not
proof of every operation. Secrets use Windows Credential Manager, so do not claim
all credentials are readable merely from this filesystem capability. Embedded
browser data is under package LocalCache and needs separate consideration.

Candidate direction: permit only the exact OS-derived package capability while
retaining exact widget SID, Low integrity, job and broker checks; first isolate
host consent/settings/browser data and per-widget writable stores from shared
package access, then prove direct packaged runtime loading and cross-widget
denials. Allowing the capability with current storage layout changes the security
boundary toward package-wide trust; it is not just a harmless token-check change.
Current cache copies only on first materialization/version change, but rehashes
and pins on later startup, so it does impose repeated startup I/O.

### Installed 0.1.0.1 early Bridge load correction — 2026-10-01, signed 0.1.0.2 ready

User installed0.1.0.1; frontend27620 survives but Bridge exits before Program.Main.
Windows .NET Runtime event confirms FileLoadException Access denied for protected
WindowsApps/runtime/Bridge/WidgetBridge.dll. Prior neutral-worker probe did not
cover loading the actual protected Bridge DLL. Evidence preserved under
`artifacts/winui-shell/installed-startup-20261001/`.

Host now acquires a separate verified Bridge+private-dotnet closure and starts the
cached Bridge DLL. This closure is distinct from the sandbox worker runtime and
receives no widget access grants. ProtectedPackageSource.OpenRegistered continues
to obtain authority from Windows registration and protected package-payload.json;
if executing outside protected storage, it verifies/pins the entire actual Bridge
directory against that inventory, rejecting substitution, omission and extra code.
Source owns this code lease until Bridge shutdown; parent retains the full runtime
lease. Early runtime stdout/stderr now survives in the frontend startup exception
instead of reporting only a process exit code.

Catalog39/39 and Session304/304 pass. New --installed-bridge-files native probe
reads the actual installed0.1.0.1 inventory, copies its verified Bridge closure,
and confirms the neutral cached Bridge reaches Program.Main (stops deliberately
at missing argument validation; no providers/actions executed). This closes the
observed DLL load failure without claiming full installed startup acceptance.
Full0.1.0.2 update build completed with779 package checks; signed with existing
certificate and verified747 unchanged archive entries. Delivered
`C:/Users/dwive/Downloads/WidgetRail-MSIX-Test-20261001/WidgetRail-Test-x64-0.1.0.2.msix`,
129,712,124 bytes, SHA256
08FAE69A35CBAEBEB95264E44532B8C6E951707755414ADFDCC0BF2B4C94EBCC.
User explicitly chose "Prepare it; I'll install"; no installation or trust change
performed. Failed frontend27620 closed normally for the update. Installed package
is still0.1.0.1 until user updates; full0.1.0.2 startup/physical acceptance remains
pending. All command sessions are complete. Do not launch the old development
candidate in place of the user's installed package test.

### Installed MSIX sandbox startup correction — 2026-10-01, signed update ready

User installed signed0.1.0.0 successfully. Settings starts, but every sandboxed
built-in failed before WorkerStarted. Preserved installed logs under
`artifacts/winui-shell/installed-worker-failure-20261001/`. A native worker probe
run through Invoke-CommandInDesktopPackage with PreventBreakaway reproduced the
exact failure: Windows adds the parent MSIX shared-data capability SID to nested
AppContainers; the existing zero-capability token guard rejects them. The same
probe without package identity passes. Do not relax that guard: the implicit SID
has access to the package's shared data. Worker desktop policy alone and keeping
the Bridge packaged did not solve it; those experimental changes are not retained.

Implemented a neutral private-dotnet launcher for the existing protected Bridge
DLL. Frontend obtains the current installed package authority, materializes and
pins the verified runtime through Bridge shutdown, and launches its cached
dotnet.exe using documented DESKTOP_APP_POLICY ENABLE_PROCESS_TREE. Bridge code
and Settings stay under WindowsApps. Bridge receives a package-full-name lookup
key and revalidates OS installation identity, non-development registration and
its own executing assembly location through ProtectedPackageSource.OpenRegistered;
no writable receipt or arbitrary root becomes authority. Existing broker, worker
jobs, exact SID/Low-integrity/zero-capability checks and content ACLs remain intact.

Neutral-launch native probe passes under the real installed identity, including
two sandbox workers and cross-widget/read-only isolation checks. Session304/304
and catalog39/39 pass; registered-package authority rejection cases included.
Full production update completed:779 staging checks for744 source files, version
0.1.0.1, signed with the existing certificate. Delivered
`C:/Users/dwive/Downloads/WidgetRail-MSIX-Test-20261001/WidgetRail-Test-x64-0.1.0.1.msix`.
Signature verified;747 archive entries match the validated unsigned package.
SHA256 BEBEAD31A52F52D3B831B4204B4A840F1AA4A056D63609EFFFBAF2C455C7E156,
129,709,610 bytes. Build/evidence under installed-worker-failure-20261001;
all command sessions have completed. Installed frontend20452 closed normally with
user approval; update installation and physical acceptance remain user-owned and
pending. No installed package or certificate was changed automatically. Do not
launch the old developer candidate over the installed app. Source changes are
uncommitted migration work. Future package tests must use the mandatory identity
probe (--packaged-runtime-policy), not merely the earlier unbound runtime probe.

### Signed MSIX prepared for user installation — 2026-10-01

User resumed installed-package testing and requested the MSIX plus certificate.
Fresh full production build includes the worker-revision fix and branding;778
staging checks passed for743 source files. Native boundaries and all managed
payloads were built fresh through Build-WinUiFullRelease with analyzer/binlog
coverage. The only frontend publication warning was missing mspdbcmf.exe for
optional symbol-package generation; this does not affect the application package.

Deliverables: `C:/Users/dwive/Downloads/WidgetRail-MSIX-Test-20261001/` contains
WidgetRail-Test-x64.msix, WidgetRail-Test.cer and INSTALL.txt. Identity remains
WidgetRail.WinUI.FullProductionProbe0.1.0.0; eight production widgets, separate
package-local settings. Candidate community widgets are not part of its catalog.
MSIX is129,627,168 bytes; SHA256
F34C3B8EB0F2A85CE4EA79A9A9318B3ACDF1540DA8E061E08E1FE531A6A56CA6.
Public certificate thumbprint9AAF2E82C20505A60B1302A525C79B4231796F55, expires
2026-12-30. Signature verified cryptographically, signer matches public certificate,
and746 archive entries are byte-identical to the validated unsigned package.
Private test key and DPAPI-protected password stay in ignored artifacts under
msix-testing-20261001/private-signing02, outside the delivered testing folder.

Evidence: `artifacts/winui-shell/msix-testing-20261001/`. No certificate trust,
package installation, or system-provider actions were performed. Required x64
Windows App Runtime2.5.1 is already registered on this PC. Actual WindowsApps
installation/launch remains user-owned and pending. Candidate26040 stays running
until the user quits it for installation; no candidate update was needed here.

### Application branding — 2026-10-01

Task Manager branding still used the project template: package DisplayName and
VisualElements named OverlayFrontend.WinUI, executable title/product were implicit,
and package/ICO assets were template artwork. Set user-facing metadata to WidgetRail,
link the canonical multi-resolution ICO into the frontend executable and window,
and apply it through AppWindow.SetIcon. Extended the existing SVG asset generator
to generate all package logos/splash assets from the same project-owned vector;
removed the duplicate template ICO. Internal executable/assembly identity stays
stable, preserving CLI, startup and packaging references.

Analyzer-enabled Debug build passed with zero warnings/errors. Checked executable
FileDescription/ProductName, extracted its icon and inspected the generated logo.
Candidate deployment validation passes: all244 DLLs and the branded package assets
match; registered package and executable display metadata both read WidgetRail.
Final candidate PID26040 is running. Two initial no-build launches retained
older native artifacts through the cached package recipe; the payload gate caught
both DLL mismatches. Rebuilding the recipe with explicit qualified native paths
resolved it. Future build and run commands must supply both native DLL paths from
`artifacts/winui-shell/focused-input-20261001/installation`, avoiding older default
artifact paths. Profile, installed catalog and fixed Bridge are preserved.
Evidence: `artifacts/winui-shell/app-branding-20261001/`. The previous full
MSIX archive is still unsigned and predates both this branding and the bridge
revision fix; rebuild before preparing the requested installed-package test.

### Worker restart update starvation — 2026-10-01

User reproduced Playnite accepting local focus movement while actions/section
changes appeared frozen until radial entry or reopening. The worker had restarted
after idle unload (start ordinal2). Bridge forwarded raw worker invalidation
revisions, which restart at1, while the presentation session retained the prior
high-water mark. New updates were discarded until the counter caught up; lifecycle
establishment fetched a checkpoint directly and temporarily refreshed the display.

Bridge now stamps admitted outgoing invalidations with its own monotonic sequence,
independent of worker lifetime. Registry current-run admission, notification
coalescing, snapshot ordering, action authority and input-release guards are unchanged.
The real-worker idle-resume regression now sends three actions before unload and
requires the first post-restart action to publish without manual refresh or any
further lifecycle transition. It timed out before the fix and passes afterward.

Evidence: `artifacts/winui-shell/focused-input-20261001/`. Idle lifecycle tests2/2,
registry tests18/18, wire invalidation1/1, presentation-session tests304/304.
Release build/publish succeeded. Running candidate was closed normally with user
approval. Staged a fresh installation copy with the corrected Bridge; the only
changed executable/DLL is WidgetBridge.dll (additional dependency PDBs are symbols).
Canonical physical options point to that installation; profile and installed
widget catalog remain unchanged. Candidate relaunched through project-mode winapp
as PID9560. User physical acceptance remains pending. Full-MSIX installed-package
testing remains deferred; no system provider actions were performed.

### Earlier installed-package deferral — superseded by test-package preparation above

The user initially deferred installation into protected WindowsApps/disposable-
machine testing. Do not request a VM, enable Windows Sandbox, change certificate
trust or perform an installation for this gate until the user resumes it. Keep
full-MSIX installation/update/uninstall and clean-machine prerequisite behavior
unverified; passing archive checks and native cached-worker tests does not replace
those checks. This deferral does not block independent migration implementation,
distribution cleanup or documentation. Full MSIX remains the selected route;
pre-migration installation removal may be manual. Candidate remains available.

### Full MSIX runtime integration — 2026-10-01, checked and relaunched

Cache foundation now compiles and passes all39 catalog checks after fixing two
resume defects: fixture helpers were file-local, and cache ancestor handles used
data-read sharing that blocked atomic directory publication. Ancestors now pin
namespace identity with FILE_READ_ATTRIBUTES and permit child changes; verified
payload files/directories retain strict byte/replacement protection.

Program owns BridgePackagedWorkerEnvironment outside the Bridge server lifetime.
Only sandboxed generic workers use the verified shared runtime cache. Bundled
widget content is materialized separately and granted with the existing exact
per-widget content transaction; installed external content retains its original
admission. Runtime grants are additive per individual AppContainer, direct rather
than inherited, serialized across processes, and checked against retained object
identities. Worker DOTNET_ROOT explicitly names the cached runtime; Bridge/Settings
remain in the installed package. No sandbox relaxation or package DACL mutation.

Actual Clock and SDK Gallery workers now run concurrently from the cache in the
native test. Their loaded coreclr paths match the cached private runtime. Using
the actual worker token, own content/shared runtime reads succeed, while another
widget's content, an unlisted late runtime file and content writes are denied.
The normal launcher verifies exact AppContainer SID, Low integrity and zero
capabilities before resuming the worker. Test profiles and files are reclaimed.
Evidence: full-msix-20260930/resume01/packaged-runtime03.log and bridge04.binlog.
This fixture uses a synthetic trusted source authority and real sandbox processes;
it is not proof of an installed WindowsApps package.

Real runtime files exposed MAX_PATH failures in direct Win32 opens after the
cache's content-hash directory was published. Shared extended-path normalization
now covers both byte pinning and handle-based DACL operations. The native test
above passes with the previously failing long satellite-assembly paths.

Full release01 built both complete packages, but inspection found the publisher
reused a stale generated AppX development manifest without WidgetRail.Startup.
Release02's new assertion correctly rejected that manifest. The SDK actually
generates its current manifest outside the winapp-owned AppX directory. The
publisher now builds an unsigned frontend package and extracts that invocation's
manifest/assets, then checks the disabled startup task in the final full package.
Release03 passes: Production8 widgets/743 inventoried files/129,579,435 bytes;
Developer12/771 files/136,786,384 bytes. Both catalogs and all778/806 respective
package checks pass. Evidence: full-msix-20260930/resume01/release03/0.1.0-preview.14/
release.json with exact staging receipts. Earlier artifacts remain failure/history
evidence. All build sessions are terminal; no operation is pending.

Default settings now use the physical package LocalState path and embedded browser
data uses LocalCache, avoiding ambiguous desktop file redirection across tokens.
Explicit test/development profiles remain unchanged. Current Debug analyzer build
passes with zero warnings/errors. Catalog39/39 and Runtime113/113 pass. The final
native cached-worker check passes with production timeouts against the Developer
payload (packaged-runtime04.log, bridge05.binlog). Embedded media main/pinned root
transfers pass80 checks/3 observations with video pixels (media01/result.json).
This also exercises WebView2 initialization using the new package cache location.

Physical options were backed up in resume01/physical-options-before.json, then
InstallationRoot was updated to release03's staged Production payload. Profile and
installed catalog remain the previous physical ones. Candidate PID16256 launched
through project-mode winapp; all244 DLLs match (candidate01.json), registered
manifest includes the disabled startup task, catalogCount16 and switching=false
were observed. Left running. The candidate is a development registration, not an
installed Store/full-MSIX qualification. No real startup enablement or system
provider changes, certificate trust, Store submission, commit, merge or push.

Remaining package gate: installed protected WindowsApps execution, signed/Store
installation/update/uninstall and GameInput/WebView2 prerequisite handling on a
disposable machine. WindowsSandbox.exe is still absent and winapp reports no
sandbox running. Do not confuse the synthetic-source native cache test with this
unperformed gate. Historical Inno paths remain outside the full publisher; further
retirement and user documentation should follow this current full-MSIX direction.

### Restart checkpoint — 2026-09-30, user-requested pause

All agents stopped at saved file boundaries. No build/test or installer operation
is running. Candidate PID29432 is still the previously qualified Debug candidate;
the full-MSIX source changes have NOT been staged into it. No signing, certificate
trust, full-package installation, startup toggle or real system/provider change
was performed. No commit, merge or push. Resume in this worktree on
`codex/winui3-frontend`, not the primary checkout.

Completed and checked in this batch:

- Initial offline full package: full-msix-20260930/stage01, unsigned MSIX123,176,134
  bytes, all744 source/staged/archive files match, MakeAppx semantic validation
  and initial staging checks passed. This predates the new cache inventory and
  publication refactor, so it does not validate those later edits.
- Packaged startup: async IStartupRegistration and portable Windows-state policy,
  native StartupTask provider injected by trusted Settings.Worker. Worker now
  targets net8.0-windows10.0.19041.0. PlatformSettings28/28 and Settings77/77 pass;
  worker compiles with zero warnings/errors. Evidence in migration-audit-20260930/
  msix-startup-evidence01.json. No real StartupTask state was changed.
- Root added disabled WidgetRail.Startup declaration to frontend manifest and
  translates StartupTask activation to --hidden. Linked shell suite162/162 passes
  (full-msix-20260930/shell01.log); actual frontend build remains pending.
- CLI host discovery now reads AppxManifest.xml instead of installer identity.json;
  validates expected frontend, registered location and bounded DTD-free XML.
  Focused discovery, named-job ownership and foreign-ownership checks pass when
  running the test apphost. Initial cli01 full run was incorrectly invoked with
  `dotnet WrailCli.Tests.dll`; self-spawn fixtures then launched dotnet instead of
  themselves. That run was explicitly stopped and retained, not claimed green.
  Build cli01.binlog succeeded; source-owned harness PID57148 was stopped after
  exact command-line verification. Use WrailCli.Tests.exe for subsequent runs.

Saved, syntactically complete but NOT yet functionally validated:

- scripts/WinUiPublication.psm1 shared publisher; Build-WinUiReleasePayload sparse
  wrapper; Get-ReleaseRuntimes PrivateRuntimeOnly; ReleasePackaging shared edition
  assembly; Build-WinUiFullRelease new entrypoint; full stager/verifier cache
  inventory changes. All seven PowerShell files parse cleanly at pause. Review
  separate Production/Developer package identities before adopting that choice.
- WidgetCatalog cache foundation: ProtectedPackageSource, PackagedWorkerRuntimeCache,
  VerifiedFileTreeLease, PinnedFileSystemObject, and extraction from
  InstalledPackageLaunchLease. New PackagedRuntimeCacheTests and registrations are
  saved but uncompiled/unrun. This is NOT wired to Bridge/WidgetRuntime yet.

Resume order: review/build cache extraction and targeted tests; finish Bridge
integration with a separate runtime lease, exact per-widget content authority and
explicit cached DOTNET_ROOT (do not inherit Bridge's protected-package runtime);
review/build the full publisher and startup manifest; rebuild complete coherent
candidate/payload; run focused native startup/media/CLI checks; relaunch candidate.
Actual protected WindowsApps execution is still unqualified. Windows Sandbox.exe
is absent here; no feature was enabled. A loose development registration is not
proof of protected-storage access. Old-install migration is explicitly optional:
the user will uninstall the pre-migration version manually if needed.

### Full MSIX direction authorized — 2026-09-30, implementation underway

The user selected full MSIX as the long-term distribution route. Store signing
replaces the planned Inno plus external-content identity distribution. Migration
from the pre-WinUI installation is not required; the user may uninstall it manually.
Preserve current widget behavior, data ownership and sandbox admission rather than
carry forward Inno transactions solely for compatibility. Existing probes/artifacts
remain evidence, not the new shipping format.

First gate: construct and inspect a complete unsigned Store-shaped package from
the corrected Release payload, then prove worker/media/startup behavior under full
package identity. Protected package storage requires an explicit sandbox-runtime
access solution; a loose development registration cannot prove WindowsApps access.
Root owns lifecycle and integration. Distribution agent owns offline MSIX staging;
runtime agent audits least-privilege worker access. No production signing, Store
submission, local certificate trust, driver or system-setting changes are authorized
merely by selecting this route. Test-only registration remains isolated.

### Qualification boundary audit — 2026-09-30

The same boundary remained through three consecutive goal turns after source and
runtime qualification. Autonomous work is now blocked pending the requested
signed identity/disposable Windows environment or the user's decision to defer
that qualification. No further confirmed implementation fix is available from
the completed scoped audits. Candidate PID29432 remains running. Resume from
release02/editions02 and the pending qualification decision; do not repeat the
already passing builds or restart the candidate without a new reason.

Current plan, lineup, authoring/build audit, release scripts and actual result
receipts were rechecked after the native deployment sequence. The preceding turn
made concrete progress; this audit found no further confirmed implementation
defect to fix within the available qualification scope. No new product change
was made. Candidate PID29432 remains running; it was not restarted just for this audit.

Full completion remains unproven. The next distribution evidence requires an
approved signed identity and disposable Windows environment for actual installer
execution, startup registration, runtime provisioning and installation recovery.
The prepared release02 payload, native registration sequence and compiler checks
do not establish those outcomes. An asynchronous question is pending for the
identity/environment or an explicit decision to defer that qualification.
Physical device/provider, mixed-DPI and assistive-technology checks remain
user-owned. Scrolling/performance and memory investigations remain explicitly
deferred; the bounded capture-close non-reproduction does not prove that the
reported intermittent capture hang is fixed. Do not mark migration complete or
resume those investigations merely to fill the qualification wait.

### Native deployment sequence and final source audit — 2026-09-30

Previous goal turn fixed and qualified the linker-corrupted Release cleanup. This
continuation verifies actual Windows external-content registration using two
versions of one isolated development identity and the corrected release02 payload.
All 12 checks pass: initial registration, version/location update, exact loaded
runtime after each activation, rejection of an obsolete removal identity and
unintended downgrade, explicit rollback, repair after removal, and exact cleanup.
Three runtime launches verified AppContainer/private-runtime ownership and normal
shutdown. Evidence: migration-audit-20260930/deployment-sequence01/result.json and
its operation/runtime receipts; deployment-probe01.binlog. No production identity,
startup metadata, signing trust, controller/device settings or user profile changed.
This proves unsigned registration mechanics on the current machine, not signed
installer journaling, same-version rebinding, data-schema rollback or clean-machine
runtime provisioning.

Parallel read-only source audits found no additional confirmed broken CLI,
authoring/template, renderer-retirement or deployment-preservation path. They did
find that the uninstall dialog promised deletion of all sign-ins while cleanup
only removes owned sandbox profiles and WidgetRail local/temp directories.
The dialog and user guide now explicitly state that Windows Credential Manager
credentials and add-on data stored elsewhere are retained. Deletion behavior is
unchanged. Actual Pascal syntax compilation and installer contracts pass; the
synthetic installer was never executed (installer-syntax04/result.json).

Candidate PID20128 closed normally before the deployment sequence. It was relaunched
as PID29432 using the preserved physical profile/catalog; all 244 deployed DLLs
match the qualified Debug build (candidate-relaunch02.json). Left running.

Signed/disposable-machine deployment qualification remains open and requires a
deliberately approved identity/environment. User-owned physical workflows and
explicitly deferred performance/memory investigations remain outside this batch.
Migration is active, with no commit, merge, push or production installation.

### Confirmed trimmed-release cleanup corruption — 2026-09-30, correction validated

The previous offline qualification was progress but did not prove runtime startup.
Executing the exact trimmed frontend with the current Production installation under
an isolated development identity revealed a confirmed shipping blocker. Now Playing's
worker initialized, but the UI stopped in switching and normal WM_CLOSE could not
finish. Native/C# dumps show UI thread blocked in WidgetPresentationSession.GetState
on _gate. The first lock owner had exited; a repeat owner had returned to an idle
thread-pool stack while still owning the monitor. Source locks are balanced.

Published IL proves the cause: RefreshInvalidationsAsync.MoveNext has two Monitor.Enter
calls but only one Monitor.Exit after trimming (versus two/two and an additional
endfinally before trimming). ILLink removes the nested lock's cleanup inside the async
finally. This matches dotnet/runtime#131088; upstream fix is PR131236. Turning off
ReadyToRun alone still fails; untrimmed Release starts, verifies private .NET8/AppContainer
ownership, and closes/cleans normally. No synchronization-code rewrite is justified.

The WinUI project now sets _TrimmerIPConstProp=false with the upstream reference.
Trimming and ReadyToRun stay enabled. Corrected frontend-trimfix01 restores both
Monitor.Exit calls. Its actual runtime passes startup, exact package/executable
identity, sandboxed worker, loaded private .NET8 runtime, normal WM_CLOSE, child
exit and removal of the isolated development registration (trimfix-runtime01).
Application synchronization and input/lifecycle authority were not changed.

ReleaseVerifier --frontend reads the actual emitted assembly without executing it.
It rejects the known bad publication (Enter2/Exit1), accepts corrected and untrimmed
publications (2/2), and fails when the expected method is absent. Both publication
pipelines run this guard before success. This targets the confirmed linker defect;
balanced call counts are not a general proof of control-flow or finally correctness.
Evidence: cleanup-verifier-evidence01.json and cleanup-verifier01.binlog.

Evidence: migration-audit-20260930/trimmed-runtime01, trimmed-runtime02,
trimmed-no-r2r01, untrimmed-runtime01, linker-cleanup-comparison.json, native/C# dumps
and decompiled IL. Diagnostics tools dotnet-dump and ILSpy were installed only into
that artifact tree. CDB detached explicitly. All three hung isolated probe processes
were reclaimed after saving evidence; only their exact development registrations
were removed. The successful untrimmed probe cleaned itself. No production signing,
certificate trust, installer or real provider control was used.

Physical candidate74272 was closed normally before the experiment. The source
correction changes publication only; the established Debug candidate is unchanged.
Earlier release01/editions01 remain offline-valid but runtime-failing evidence and
must not be treated as releasable.

Fresh full release02 and editions02 pass after the correction: Production8 widgets/
978 inventoried files; Developer12/1006. Both catalogs, sealed package/icon checks,
inventories, exact native DLL hashes and async cleanup guards pass, with no retired
renderer artifacts. Session ReadyToRun header remains196 bytes; private Core/Desktop
runtimes are8.0.31. All18 external-content staging checks pass. Final assembled
Production service/runtime content plus identical published frontend DLLs pass
isolated startup and shutdown again (release02-runtime/result.json). Development
identity embedding changes only the staged executable's identity resource.
All probe processes/children exited and their registrations were removed.
Evidence: release02-qualification.json, release02/payload.json, editions02-path.txt.
No signing, production install or trust-store change occurred. Signed/disposable-
machine installation qualification and user-owned physical workflows remain open.

Regular candidate relaunched through project-mode winapp as PID20128 using the
preserved physical-options.json/profile/catalog. All244 deployed DLLs match the
qualified Debug build (candidate-relaunch01.json); Settings worker startup and
visible lifecycle completed normally. Left running for physical testing. No
commit, merge, push or production installation; migration remains active.

### Broad migration qualification — 2026-09-30

Previous goal turn added the process-level Playnite workflow and fixed activity
identity filtering. All 59 default managed verification steps now have passing
results across two bounded runner invocations. The first stopped at SDK Gallery's
stale 0.1.24 assertion against the already shipped 0.1.25 manifest; the exact version
expectation was corrected, then the remaining steps resumed. No widget implementation
or production guard changed for that failure. Both initial failure and corrected
11/11 Gallery result are preserved under migration-audit-20260930/managed and
managed-remaining. All preceding passes remain applicable because that assertion
was the sole source change between the two runs. The optional legacy YT Music
companion suite stays explicitly excluded by the existing manifest.

This covers installer/release contracts, SDK/public API/scenarios, worker/session/
capability boundaries, author CLI (82), providers, first-party/sample widgets,
catalog, docs and WinUI policy/motion/state. System-changing controls use fakes;
read-only native catalog/media/activity smoke did not invoke those controls.
Current lineup documentation now matches installed catalog versions (Playnite
0.2.114, YT Music 0.3.34, SDK Gallery 0.1.25) and current migration ownership.

Earlier unsigned complete payload and both editions passed offline from the retired-renderer
source; the runtime failure and corrected replacement are recorded above.
Evidence: migration-audit-20260930/release-qualification.json and
managed-qualification.json. Production has 8 widgets/978 inventoried files;
Developer has 12 widgets/1006 files. Catalog admission, sealed package/icon integrity,
all release inventory hashes, exact current native platform/preview hashes, matching
identity bytes, private .NET 8 Core/Desktop runtimes and self-contained .NET 10 are
verified. No OverlayHost, ArtworkDecoderHost or Taffy artifact remains. All 18
external-content staging checks pass. Build01 in this batch is release01; previous
pre-retirement build05 remains historical evidence.

Artifacts are validation-only, unsigned and built from the explicitly dirty migration
worktree. Nothing was signed, registered, launched, installed or added to certificate
trust by this release qualification. It does not substitute for signed installation
or establish execution of this exact trimmed payload. Candidate PID74272 was
running at that stage; the later runtime investigation and relaunch supersede it.
The only source correction was the stale Gallery package-contract test expectation.

Remaining proof boundaries: signed install/upgrade/repair/edition rollback/uninstall
and clean-machine runtime provisioning need an approved signed identity and a
disposable Windows environment. Native peer checks establish accessibility metadata,
not Narrator/NVDA speech. The intermittent capture-close RPC hang has a completed
bounded non-reproduction and diagnostics, not a confirmed fix. Physical workflows
are user-owned; performance and memory work is explicitly deferred. These boundaries
must not be conflated with incomplete widget implementation or silently declared
passed. The migration goal remains active; no main merge or push.

| Requirement area | Current proof | Boundary still open |
| --- | --- | --- |
| One WinUI frontend, retired renderer | Corrected trimmed/R2R startup and shutdown, binary inventory/native hashes, native platform policies | Signed deployment remains unqualified |
| Widget lineup and workflows | Current 16-widget physical catalog; 59 managed gates; targeted native suites and prior user acceptance; Playnite cross-process fake-provider workflow | Real device/account/game workflows remain user-owned |
| Authoring and themes | Public API/preflight/SDK/scenario tests, external author CLI 82, Gallery native grids, shared style checks | No new implementation gap confirmed |
| Shell input/focus/media/pinning | Existing native focused gates and user acceptance; state/motion/session tests rerun | Broad assistive-technology speech and mixed-monitor/device acceptance remain physical |
| Capture teardown | Bounded visible/hidden real-capture close passed; independent watchdog retained | Original intermittent RPC hang has no confirmed corrective fix |
| CLI and distribution integration | Native dev lifecycle/inspector, all CLI tests, unsigned two-edition package/inventory proof, deployment fake service tests | Approved signed identity and disposable-machine install/update/rollback/uninstall/runtime provisioning |
| Performance/resource budgets | Targeted virtualization/lifetime guards and user report of generally smooth UI | Playnite fast-scroll stutter and further memory/performance work explicitly deferred |


### Cross-process Playnite workflow and activity identity — 2026-09-30

Previous goal turn fixed the build guard and current-runtime conformance paths.
This turn closes the detailed Playnite process-test gap with a separate, unshipped
fixture executable. It uses the production widget, application service, state store
and full-trust application bootstrap; only the provider is fake. No production test
flags, endpoint redirection or dependency injection hooks were added. Two friend
assembly declarations grant the fixture access to the existing composition seam.

The fixture installs through ordinary catalog trust approval, starts the actual
worker protocol, opens Library, searches, reads an indexed range, rejects a prior
query's lease, opens real details, survives background/interactive return in the
same process, and sends exactly the chosen game to a fake launch journal. It stops
and uninstalls its temporary package. Evidence: renderer-retirement-20260930/
playnite-process03.log and binlog. Initial fixture expectation used a null return
for stale input; the existing guard correctly throws ArgumentException(request).
The test now verifies that exact rejection, unchanged modal state and no provider
launch. The shipped executable admission/startup/read-range test remains separate;
this does not establish real external Playnite service or native focus behavior.

Recent Activities still excluded OVERLAYHOST instead of OVERLAYFRONTEND.WINUI.
It now excludes the actual frontend process, independent of version-resource display
metadata. The old test's WidgetRail display name had masked that missing identity.
The regression starts with the WinUI process as foreground and a neutral display
name, proves the initial list stays empty, then checks ordinary app observation.
All 8 provider checks pass (current-workflow-20260930/activity02). The native platform
test README also now points to Build-OverlayPlatform -TestPolicy.

Replacement shared qualification passes through the real verification runner:
SDK 148, runtime 113, generic worker 10, styling 37, app-library provider 98.
Evidence: current-workflow-20260930/shared-qualification/20261001T001308Z-002163c1.
App-library native smoke is read-only; launch/provider writes use fakes. No real
game, system-setting, driver or recovery actions were invoked.

PID5328 closed normally. Fresh physical-runtime01 stages the updated Bridge and
provider (source/staged provider hashes match), preserving profile/catalog and
seven bundled widgets. Candidate relaunched as PID74272, all 244 frontend DLLs
match (candidate02.json), and Settings/input-ready/catalog-complete were observed.
No new production Playnite package was installed merely for friend metadata.
Signed installer and disposable-machine qualification remain unproven; physical
provider workflows are user-owned, performance/memory work remains deferred.
Migration stays active. No merge, push or production installation.

### Current-runtime workflow and clean-build qualification — 2026-09-30

Previous goal turn completed renderer retirement and its replacement gates. This
turn removes obsolete conformance adapters for the retired broker-backed Playnite
application. Current Playnite is full-trust and owns its provider; tests no longer
attempt to install it as a managed AppContainer widget or expect eager parent rows.
Removed modes have no repository callers. Unknown/retired acceptance flags now
fail explicitly with exit 2 instead of silently running the default suite. Network
text-entry and package icon checks remain. Source-reference comments point to the
current widget and application tests. Steam/GOG provider policy coverage remains
in WindowsAppLibraryProvider.Tests; no provider production code was removed.

The optional Settings exporter had also used the wrong runtime. It now copies the
actual Settings.Worker dependency closure, seals its icon package, loads it through
the trusted catalog, and supplies an isolated settings profile. The catalog already
supplies its installed-widget root, so it is not passed twice. System-changing
features are disabled and no Settings mutation or private diagnostics is invoked.
The export records trusted runtime hashes separately from installed widget archives,
keeps indexed ranges separate from parent snapshots, and returns nonzero when any
evidence gap is recorded. Earlier setup failures (missing seal, duplicate catalog
argument) remain evidence; production guards were not changed to accommodate them.

Validation under `artifacts/winui-shell/current-workflow-20260930/`: default
conformance 6/6; installed Network text-entry; retired-mode rejection; Settings/Games
export with 6 authoritative snapshots, 2 traces and zero gaps (evidence03). Playnite
widget tests 164/164 pass. Application tests 39/39 pass through the actual verification
runner including its prerequisite package build. The first direct application run
was 38/39 because its package-content prerequisite was absent; the proper runner
resolved that without weakening the assertion. These are fake-provider tests, not
physical game launches. Full-process Playnite startup/admission/read-range is covered;
a full-process search/details/launch scenario against a fake service remains weaker
than the detailed in-process indexed and application coverage.

A confirmed developer build gap is fixed: the frontend silently omitted missing
native DLLs, so a clean checkout could build an unusable application. The imported
WinUiNativeInputs target now rejects missing platform/preview DLLs during real
build and publish, including NoBuild publish. Explicit input overrides remain valid;
restore and design-time evaluation do not require compiled native inputs. Seven
executable MSBuild contract cases pass. Building documentation now prepares the
actual default DLL paths and a fresh widget workspace; SDK and YT Music notes no
longer prescribe Taffy or a deleted layout script. Release-readiness distinguishes
historical failures from current qualification. Actual Debug frontend build passes
with zero warnings/errors; all 244 running candidate DLLs still match that output
(candidate01.json). Documentation checks pass across 197 Markdown files.

Candidate PID5328 remains running with the previous qualified runtime; this turn
changes verification/build/docs only. No installer, certificate trust, driver,
controller-recovery or physical provider changes. Migration remains active; signed
installer/clean-machine acceptance is unqualified, user workflows remain user-owned,
and scrolling performance/memory investigations remain deferred.

### Renderer retirement qualification — 2026-09-30

Original renderer source and its Rust/Taffy build path are retired. Shared native
controller, Guide, placement, process ownership and generated protocol code now
live in OverlayPlatformInterop. All 30 exports are retained. The standalone
controller-recovery entrypoint now runs through the WinUI startup path without
creating XAML, a profile, input session or Bridge; only fake recovery was tested.
Ignored historical outputs and evidence remain intact. No merge or push.

Replacement CI builds the platform/preview boundaries and trimmed WinUI Release
payload; it rejects retired renderer artifacts and verifies native binary hashes.
Native policies pass 2,052 checks after final protocol-header synchronization; prior
preview policy 15/diagnostic 2,021, platform foreground/process and trimmed publication
checks pass. Native indexed surface qualification now passes 21 top-level checks,
including the unchanged late-artwork identity/pixel assertion; no renderer change
was needed for that previously failing fixture.

Bridge aggregate is 178/178 against a fresh coherent managed installation
(`renderer-retirement-20260930/bridge-verification04.log`). Earlier 177/178 used a
Settings DLL staged before the latest rebuild; payload hashing correctly rejected
it. The original 171/178 failures also included tests using stale lifecycle/scope
authority, expecting eagerly embedded indexed rows, assuming action acknowledgement
meant backend completion, and outdated semantic-role/protocol-version counts.
Tests now demand real ranges and preserve replay/stale-input rejection. Production
input guards were not loosened. First-party conformance 6/6 passes with real indexed
lease/input APIs and a simulated backend; installed Games & Apps add/launch and
fresh-worker saved-library persistence also pass (conformance-games-restart02.log).
Fresh Debug analyzer build passes without warnings/errors.

Games & Apps evidence export now preserves indexed ranges separately and passes
its auto-curation/add/remove flow (5 authoritative snapshots and 1 trace in
conformance-evidence02). It no longer expects rows embedded in the parent or the
old tile style. The optional exporter still records a Settings worker preconnect
exit as a gap; this is not declared passed. Normal Settings startup is verified in
the physical candidate and Bridge gates. Older optional Playnite eager-row diagnostic
modes remain to be retired or adopted; they are outside the default passing gate.

Candidate relaunched through project-mode winapp as PID5328. All 244 deployed DLLs
match the qualified Debug build (candidate-final01.json); Settings reached input-ready
and startup completed. It uses fresh physical-runtime02 with the same physical
profile, installed catalog and seven bundled widgets. First staging attempt collided
with a concurrent test build on a SourceLink intermediate; it was retained as
evidence and cleanly rerun after serializing builds. Broad physical provider
workflows remain user-owned, performance/memory investigations remain deferred,
and signed installer/clean-machine provisioning remain unqualified. Historical
entries below record intermediate states; this summary is current.

### Final checkpoint and relaunched candidate — 2026-09-30

Release payload build05 now includes the final development ownership/close policy,
native inspector and startup failure logging. Both editions05 catalogs/inventories
verify (8 production /12 developer widgets), and all18 external-content checks pass.
The payload remains explicitly unsigned/validation-only with dirty-source provenance.
No installed identity, startup entry, runtime installation or trust store was changed.

The final already-exited frontend path also checks for an empty job before any
termination. Four ownership/cleanup regressions were rerun against freshly built
runner06 and passed (lifecycle-final03); the broader11 passed before that narrow
adjustment. Debug analyzer build05 and the full Release publication are clean.
Native07 provides13 real workflow checks; inspector UIA12 and screenshot review pass.
Abrupt process ownership is verified by unit/native child-job checks; native07 tests
cooperative CLI cancellation/reload, not arbitrary OS termination of a packaged CLI.

Physical candidate relaunched as PID74176 through project-mode winapp with the
existing physical-options.json and Playnite0.2.114. All244 deployed DLLs match the
qualified Debug output (`development-host-20260930/candidate-final.json`). Leave it
running for user testing. No widget/system-setting actions were invoked.

Next phase: original-renderer retirement using the dependency audit below, starting
with shared native extraction and replacement CI gates. Do not remove recovery-only
behavior, controller-policy tests or the native platform ABI along with the renderer.
Signed installer execution/clean-machine provisioning remain explicitly unqualified;
no production installation is authorized by the unsigned payload build. Work remains
on codex/winui3-frontend; no commit, main merge or push was performed in this batch.


### Native CLI ownership, inspector and reload qualification — 2026-09-30

The real registered-host gate now passes13 workflow checks (native07) plus12
inspector UIA checks, with screenshot review: hidden readiness, exact package and
widget instance, Bridge/worker ownership, native inspector filtering/pause/refresh/
F12 reopen, source reload, rejected broken worker retaining last-good, repair,
controlled replacement-activation failure restoring last-good, cancellation and
reclamation of every observed frontend/Bridge/worker process and temporary profile.

Two confirmed startup/lifetime gaps were fixed. Windows did not propagate the
frontend's CLI job to Bridge. The shared session launcher now optionally creates
the Bridge suspended with JOB_LIST, limits inherited handles with HANDLE_LIST,
verifies job membership and resumes only afterward. Normal production launch
keeps Process.Start. Exact owner job comes only from development options. The
private .NET child environment and bounded stdout/stderr logging are preserved.
Eleven focused session/options/launcher tests pass, including abrupt last-owner
closure. No job handle remains in the child to defeat CLI cleanup.

Immediate/unconditional TerminateJobObject also disrupted Windows package-container
teardown: replacements remained suspended before managed Main, with repeated
AppModel container-destruction events. Source traces and non-invasive detached CDB
observations are retained in native05; no debugger remains attached. CLI stop now
requests normal close on its exact owned frontend, waits for all job processes to
exit, and forces only a still-live job after the bounded grace period. Already-empty
jobs are never terminated. Native07 passes reload/rollback with this policy; earlier
native04–06 failures remain evidence, not green runs. The exact observed suspended
test processes were reclaimed after recording their nonce/path. Temporary successful
startup tracing was removed; actual process-startup failures retain durable logging.

Final11 CLI lifecycle/identity/readiness/help checks pass (lifecycle-final02.log).
Debug analyzer build05 is clean. Inspector manual retry resumes its timer after a
capture error; hidden overlay capture retains its last frame with unavailable status.
Release payload refresh is underway as build05; artifacts from build04 predate these
changes. Candidate is still closed and must be relaunched with physical-options.json
and Playnite0.2.114 after this batch. No production installer or real setting actions.
Evidence: `artifacts/winui-shell/cli-winui-dev-20260930/` and
`artifacts/winui-shell/development-host-20260930/`.


### Isolated WinUI release payload construction — 2026-09-30

`Build-WinUiReleasePayload.ps1` now builds the native platform and preview
boundaries, trimmed/self-contained WinUI frontend, framework-dependent Bridge,
generic worker, trusted Settings, sealed bundled widgets, developer samples,
CLI and self-contained deployment helper without invoking OverlayHost's builder.
Canonical frontend versioning and explicit native input paths prevent stale DLL
staging. Final `Build-Release.ps1` still requires a clean committed checkout and
an externally signed identity matching the generated manifest. No signing,
registration, certificate trust, startup, driver or prerequisite installation
was performed by this pass.

Full `release-payload-20260930/build04/payload.json` succeeded with an explicit
unsigned synthetic identity. `editions04/0.1.0-preview.14/` contains independently
inventoried production (978 files, 8 widgets including Settings) and developer
(1006 files, 12 widgets including Settings) folders. Both real catalog/seal/icon
verifications pass; 18 external-content staging checks pass; both editions have
byte-identical identity packages. Frontend includes .NET 10.0.10; private service
runtimes report .NET/Core and Desktop 8.0.31. Signed Microsoft Windows App Runtime
x64 Framework 2.5.1.0 is bound by identity receipt/hash; its embedded runtime
packages/license XMLs are preserved. Eleven focused packaging self-tests pass.

Builder corrections established by actual attempts: removed globally propagated
trimming flags (frontend Release owns trimming), prevented duplicate Settings
asset copies, and replaced strict XML member enumeration with explicit SDK
PackageReference selection. Sample icon copying now follows manifest declarations
instead of duplicating SDK Gallery's assembly-embedded artwork. The native
platform build retains existing third-party ViGEm source-encoding warnings;
managed publication logs contain no errors/warnings.

These artifacts are explicitly `validationOnly=true`, `identitySigned=false`
and retain dirty-source provenance. They are not signed-installer or clean-machine
acceptance. The subsequent CLI named-job Bridge ownership correction was not yet
included and requires frontend/session refresh before qualifying that workflow.
Evidence: `artifacts/winui-shell/release-payload-20260930/qualification04.json`;
build logs/binlogs and earlier failed attempts remain alongside it.

### CLI and deployment integration resumed — 2026-09-30

User accepted Playnite0.2.114 and requested continued migration. Candidate67936
was closed normally; preserve the existing physical profile and relaunch after
this batch. Step4 remains active; step5 retirement audit is read-only until the
replacement launch/release path is qualified. No production installation,
certificate trust changes, runtime provisioning or real system-setting actions.

Native CLI smoke discovered a real containment gap (native03): exact registered
frontend57880 joined its named CLI job, but direct child Bridge74668 and worker74256
did not. Independent IsProcessInJob confirms both outside that job; this is not a
PID-list/name assertion error. Pipe-disconnect cleanup reclaimed them in the test,
but cannot certify abrupt outer-job ownership. Explicit owned child creation is
being added to the shared session launcher for development mode only; normal
production process policy remains unchanged. Keep the native assertion intact.
Native01 separately corrected GetPackagePathByFullName2's DLL to KernelBase.
Inspector UI qualification still awaits the ownership fix. Full release payload
builder is running independently; refresh any payload built before this fix.

CLI now activates registered AUMIDs through IApplicationActivationManager and
verifies exact returned process identity plus membership in its named kill-on-close
job. Hidden probe and interactive profiles remain separate, with last-good reload
recovery. Eleven focused checks pass, including real job/descendant reclamation.
Frontend readiness and native WinUI inspector source now compile with analyzers
(build01, zero warnings/errors); three parser/readiness tests pass. Inspector
uses native TreeView, realized-only layout map, current styles/focus/navigation,
filter/pause/refresh and F12 reopen; readiness includes inspector initialization.
Real registered-host probe/interactive/inspector/reload checks are next, not yet
claimed passing. Evidence: `artifacts/winui-shell/development-host-20260930/`
and `artifacts/winui-shell/cli-winui-dev-20260930/`.

Deployment helper final47/47 checks pass (tests08), self-contained Release publish
and installer contracts pass. Durable install/repair/rollback/recovery/uninstall,
exact external binding, inventory authority, same-version edition compensation
and AUMID shortcuts are implemented. Signed native installation/edition switching,
Windows crash recovery and clean-machine provisioning remain unqualified.
Release builder source and11 packaging scenarios pass; full isolated payload build
awaits native development-mode checks. Earlier source-draft checkpoint below is
historical and superseded by this entry.

### Shared native extraction gate — 2026-09-30

The 12 shared controller/placement/targeting/process/protocol files and
NativeDependencies.csproj now live in `src/OverlayPlatformInterop`. Canonical
platform build and runtime acquisition no longer consume original-renderer paths.
The generated native protocol destination is
`src/OverlayPlatformInterop/WidgetProtocolPresentationContract.generated.h`;
the managed parity/CI lane owns its generator pointer. Existing ABI, process mutex
names, controller policy, activation and recovery behavior are preserved.

`Build-OverlayPlatform.ps1 -TestPolicy` now builds/runs 12 synthetic suites:
input ownership, open shortcut, Guide compatibility, process ownership, DualSense
reports, isolation core/reader/routing, injected ViGEm/HidHide adapters, local owner
and imported production ABI. Pure Guide debounce replaces the platform test's
dependency on OverlayState. Guide polling/edge checks are extracted from the mixed
legacy renderer-guide fixture. Actual GameInput queries and full hardware ABI
initialization remain explicit `-TestHardwareInput`, outside the synthetic gate.

Isolated native Release build and all 2,052 checks pass. All 30 production exported
symbol names match qualified release payload build05. Existing third-party ViGEm
source-encoding warnings remain; no new renderer dependency is linked. Evidence:
`artifacts/winui-shell/renderer-retirement-20260930/native-extraction01/result.json`,
per-suite build/test logs and `native-extraction01-build.txt`. No controller hardware
initialization, driver policy mutation, settings change or candidate restart was
performed. Candidate lifecycle remains owned by the coordinating lane. Remaining
renderer deletion and replacement CI integration belong to the coordinating lanes.

Retirement audit: do not delete OverlayHost wholesale. The live platform build
still uses12 shared controller/placement/targeting/process/protocol files there,
plus NativeDependencies.csproj. Rehome these and controller policy/ABI tests;
replace the CI Rust/old-host build gates with platform, preview, WinUI and managed
contracts. Preserve native recovery export and add its renderer-independent
consumer before removing legacy main.cpp's recovery-only entrypoint. Redirect
catalog tests to eng/widget-catalog.json and Bridge integration to an explicit
staged payload. Old renderer, Taffy, native artwork helper and renderer-only gates
can then retire; preserve ViGEm, managed artwork/session authority, activation and
public-suffix data. No retirement deletion has been performed at this checkpoint.


### Playnite footer uses fixed left-aligned spacing — 2026-09-30

User accepted the follow-up focus-border fix. The subsequent footer request
supersedes the responsive equal-width hint grid: Home/Library hint regions now
use UI.Row, content-sized hints, justify:start and24DIP gaps. Removing a hint no
longer recalculates equal-width columns and redistributes all remaining hints.
No host changes. Updated the two existing layout assertions that required Grid;
all48 layout/theme cases pass. Playnite0.2.114 built, sealed and selected/enabled;
inactive0.2.106 retained intact outside the catalog. Candidate relaunched as
PID67936 with the existing profile and all244 DLLs verified against the build.
Evidence: `artifacts/winui-shell/playnite-hint-spacing-20260930/`.

### Playnite controller hints use available width — 2026-09-30

The Library footer had five hints but capped its responsive grid at three columns,
forcing a second row regardless of available width. Its maximum now follows the
actual hint count while retaining the160DIP minimum and existing native reflow.
All five fit one row from840DIP of usable width (including10DIP gaps); narrower
layouts still wrap. Widget-only change, no host or controller routing changes.
Existing48 Playnite layout/theme cases pass. Playnite0.2.113 built, sealed and
selected/enabled in the candidate. Inactive0.2.105 retained intact outside the
catalog's8-version cap. Candidate relaunched as PID50460; all244 DLLs match the
qualified build. Evidence: `artifacts/winui-shell/playnite-hints-20260930/`.

User reported the RT whole-container focus problem still occurred after the first
fix. The release-cancellation race below was only one path; physical incident
closure remains pending acceptance of this follow-up.

Follow-up logs identify the INNER GridView (`...scroll.Items`) losing its item
after a query reset, with no pending entry. New regression now reproduces failure
when a two-item GridView query is republished with the same consumed default-entry
request. The prior tests exercised exact indexed targets only. Native fixture setup
was corrected to establish grid layout separately and verify action admission/status;
red03 has the real query-republish failure, while red01/02 failed during setup.
Fix now parks focus before any owned query/view replacement, recovers to a current
row even without a new authored request, keeps logical collection wrappers out of
Tab navigation, and suppresses the inner owner focus visual. Restoring a widget
cannot reassert native focus on a bare collection owner. Explicit departures still
cancel pending handoffs; stale exact requests from outside do not steal focus.
Passed44 native entry checks (green02) plus18 indexed navigation/realization checks,
including logical-entry22 and grouped-focus7 internal assertions. Added a bounded
NavigationOnly option to the existing script so this focus qualification does not
run the unrelated deferred artwork probe. The script closes its own fixture;
outer cleanup raced that close, but both fixture processes exited and all18 results
are preserved as passed. Debug analyzer and trimmed Release publish passed, as did
the19 ordinary focus-policy checks. Candidate50460 closed normally; replacement
PID42536 launched with the existing profile/Playnite0.2.113 and all244 deployed DLLs
verified. Physical RT acceptance remains pending; do not claim that user incident
closed merely from these fixtures.
Evidence: `artifacts/winui-shell/indexed-trigger-focus-20260930/followup/`.

### Indexed collection focus stranded after RT — 2026-09-30

User saw a white outline around the full Playnite Library collection after RT
changed categories. Confirmed shared-host race with a gated real-worker native
fixture: query replacement parks focus on the collection owner while the authored
row loads; RT Released ran CancelGroupEntry and aborted that entry. Red evidence
fails exactly at the pending-entry assertion, after the existing24 replacement/
refresh/deferred-activation checks pass. Logs had no focus-specific failure (only
optional artwork request failures); the controlled reproduction establishes cause.

Release/repeat no longer cancel group entry. The parking owner has no system focus
visual and is not an actionable/navigation target while entry is pending; additional
button presses cannot strand it or activate an unseen game. Explicit departures
are observed on the collection owner as well as its row subtree, so pointer/native
focus choices outside cancel the pending handoff. No widget-specific changes.
Final fix passed41 native entry checks (green02), including real-worker delayed
query replacement, release/repeat/fresh RT, A/directional input while parked and
explicit departure before rows arrive. Existing focus-policy19 also passed;
Debug analyzer build clean. Evidence:
`artifacts/winui-shell/indexed-trigger-focus-20260930/`. Candidate31656 closed
normally after preserving logs; updated candidate relaunched as PID38956 with
the existing profile and Playnite0.2.112. All244 DLLs match the qualified build.

### Playnite details: fixed header and separate sections — 2026-09-30

User requested a stationary top area and Description/Links sections. Playnite now
uses a native Auto/Star Grid, keeping poster/source/completion/actions above one
bounded lower scroll area. Tabs and their content share that scroll owner so
D-pad/left-stick fallback can read noninteractive descriptions. Description is
initial, Overview becomes Information, followed by Achievements, Activity and
Links. Notes accompany Description; metadata and browser links are split into
their respective sections. LB/RB cycling derives its count from the actual tabs;
only Achievements/Activity start optional provider loads. Opening/scroll identity,
refresh, recovery and scoped actions remain intact.

Cause of whole-modal scrolling: WidgetView.WithModal unconditionally wrapped the
content. Added public WidgetModal.ScrollContent (default true); false preserves
the authored bounded layout and normal modal scope/dismiss/focus behavior. No new
protocol feature or WinUI host special case. Reviewed public API baseline updated.

Passed164 Playnite tests, SDK modal contract1 and API baseline1, native production
details13 (fixed header during actual directional scroll, five tabs, finite resize),
and Debug analyzer build. Test helpers were updated from the removed automatic
wrapper to the widget-owned lower scroll. Evidence:
`artifacts/winui-shell/playnite-details-sections-20260930/`. Playnite0.2.112 built,
sealed, installed, selected and re-enabled in the candidate catalog. Installer
requires disabling an enabled widget before replacing its package; that sequence
was completed while the overlay was closed. Inactive0.2.104 was moved intact to
this evidence directory's retained-inactive-packages to respect the8-version cap.
Candidate relaunched as PID31656 with its existing physical profile; all244 deployed
DLLs match the qualified build. No real Playnite launch/uninstall/link actions ran.

### Directional scrolling through noninteractive content — 2026-09-30

User approved shared left-stick/D-pad fallback scrolling. Native focus candidates
still win inside the nearest owner; with none remaining, a matching ScrollViewer
reveals leading/trailing content before external links, outer candidates or the
root/tray boundary. Fresh presses versus repeats reach both ordinary and pinned
presenters and keyboard arrows. Held input stays at the edge until a fresh press.
The gesture retains its native focus anchor separately from right-stick settling;
reversing through text scrolls back toward that anchor. WinUI ChangeView handles
animation (reduced motion uses immediate steps), layout and virtualization.
Pending offsets accumulate without replaying old movement, and native ViewChanged
completion returns ownership to the actual viewport position. Input withdrawal,
scope/owner changes, unload/disposal and right-stick scrolling invalidate the
gesture. No widget API or provider/system action changes.

At a completed scroll boundary, outer navigation uses the viewport's bounds,
not the offscreen retained control. A new regression caught how the old geometry
could otherwise rank a fixed header as being below that control. Declined exits
(e.g. pinned/modal boundaries) retain their edge guard until focus actually leaves.

Validation passed:77 native directional checks (52 new scroll checks, including
horizontal/vertical and normal/reduced motion),3 native keyboard checks,15 focused
policy/right-stick/repeat unit tests,460 existing native styles and19 native focus
policy checks. Analyzer Debug build and trimmed Release publish passed. Fixture
setup now lets native focus bring-into-view complete before setting its deliberate
test offsets; original failed runs are preserved. No real system-setting actions.
Evidence: `artifacts/winui-shell/directional-scroll-20260930/` (navigation05,
styles01,focus01,policy01,build06,release01). Physical controller feel remains for
user acceptance. Candidate PID74360 was closed normally for validation. Updated
candidate relaunched as PID68884 with the existing physical-options profile;
all244 deployed DLLs match the qualified Debug build (candidate-payload.json).

### Shared directional navigation restored — 2026-09-30

User authorized fixing the audited navigation gaps. Shared presenter now resolves
explicit edges first, then retains indexed logical realization, then searches the
nearest responsive Grid/matching-axis Scroll before broader active-scope candidates.
Geometry comes from existing native Bounds measurements; a small pure scorer retains
the original vertical beam/distance/ordinal rules and strict horizontal overlap.
External remembered groups are ranked as regions and enter their current valid child.
Native Focus/StartBringIntoView reveals the selected control without maintaining
another layout tree or animation queue. Disabled/collapsed/inactive scopes are excluded;
busy actions remain focusable. Input withdrawal/unready geometry consumes movement
instead of being misreported as a root exit. Indexed authored external edges read only
the current row lease and cancel older pending navigation before leaving.

Keyboard arrows tunnel through the same policy before native default handling, while
editors/popups retain their own key handling and gamepad-key ownership is respected.
Keyboard/controller root Down exits share one shell helper; pinned/fullscreen/modal
boundaries remain separate. No Settings-specific link or alternate widget tree.

Validation: pure scorer7/7; native navigation24 plus keyboard3 pass (offscreen Settings
case, real boundary, nested orthogonal Scroll owners,1/2/3-column responsive grids,
partial final rows, explicit links, disabled/busy controls and inactive scopes).
Existing focus-policy19 and native styles460 pass. Debug analyzer and trimmed Release
publish pass. Initial fixture compile mistakes (helper name collision, CreateFrame name,
explicit array types for WinRT) were corrected before native execution.

Real-worker indexed harness passed18 navigation/realization/refresh/grouped/scroll-settle
checks, including logical entry22 and grouped-focus7 internal checks. Its subsequent
artwork-retention probe failed `late same-page artwork replaced selected row`; remaining
input-route/modal phases were not run. Preserve that failure as unresolved broader
qualification, not a green full-suite result. Outer cleanup also raced the script's
already-closing window; frontend/fixture processes subsequently exited. The artwork
probe uses direct FocusItem/parent action calls; do not silently loosen its assertion
or assume a new navigation regression without diagnosis. Evidence:
`artifacts/winui-shell/navigation-parity-20260930/`.
Candidate74360 relaunched with canonical profile/catalog;244 DLLs verified against
the final Debug build. Physical Settings Up/reveal acceptance remains user-owned.

### Navigation parity audit requested — 2026-09-30, investigation only

User reports Up from Exclusive control skipping offscreen Controller shortcut and
jumping to fixed Quit header. Source comparison confirms missing ordinary-scroll
owner priority: original SurfaceInteractionTransactions.ResolveDirectionalFocus
honors authored edges, then FindDirectionalFocusTargetInOwningSubtrees searches
nearest responsive grid/matching-axis Scroll, including revealable offscreen nodes,
before broad scope fallback and scroll-boundary admission. Original tests explicitly
assert internal offscreen rows beat nearer outside geometry at multiple widths/scales.
WinUI WidgetViewPresenter.MoveFocus handles transient/slider/indexed routes then
calls FocusManager.TryMoveFocus against the entire active scope. Settings ComposeRoot
puts fixed header and page Scroll in that scope, and links only the first available
page control Up to the header. Exclusive control has no authored header Up link.
This matches the reported host-policy regression; no live input reproduction was
performed and no navigation production code changed during this review.

Additional parity gaps to cover before old renderer retirement: owner-first nested
scroll/responsive-grid navigation; revealable versus collapsed/disabled controls;
strict horizontal overlap and deterministic beam/distance tie rules; explicit-edge
precedence against indexed navigation; scope/lease revalidation during delayed reveal.
Existing WinUI indexed path already handles logical indices, native columns,
ScrollIntoView, pending target coalescing, loaded-boundary waiting and lease admission;
ordinary overrides, group memory, analog focus settling and root rail/radial exits
also exist. Presence is not exhaustive parity proof. Preserve accepted busy-control
focus behavior instead of blindly copying the old busy-item skip rule.

WinUI supports restoring these user-visible policies using native measured geometry,
programmatic focus/bring-into-view and indexed logical targets. Its built-in XYFocus
offers strategy selection/SearchRoot/HintRect/ExclusionRect, not a pluggable exact
scorer or geometry for unrealized containers. Do not resurrect the old renderer's
layout/pagination caches; migrate behavior and original regression scenarios.

### Active CLI/deployment source checkpoint — 2026-09-30

Candidate70292 was launched at the user's request after72148 exited;244 deployed
DLLs verified. Do not confuse current source drafts with its already-qualified
presentation/reconnect payload. User may be checking it; preserve the profile.

Root added a strict DevelopmentLaunchOptions parser (canonical `--name=value`
arguments), isolated profile/catalog/instance/nonce/job identity, atomic exact
`wrail-dev-ready-v1` publication and inspector-ready guard. Three new managed tests
pass. Program joins a named CLI job before profile election/XAML/owned services;
malformed development startup exits rather than falling into normal recovery UI.
The join closes its temporary handle immediately so the CLI remains the lasting
job owner. A hidden DevelopmentProbePage draft admits the exact catalog instance
and Visible WinUI snapshot without controller/F1 registration; cancellation owns
Bridge teardown. Frontend drafts have NOT yet been compiled or exercised natively.
Interactive development readiness, inspector and CLI activation are still missing.

Root updated trusted Settings startup command to quoted
`OverlayFrontend.WinUI.exe --hidden`; PlatformSettings27/27 passes against fake
storage. No actual startup registry modification. Existing installer lifetime
markers are ALREADY preserved through `OverlayProcessInterop.cpp` ProcessLease
-> OverlayInstallationLifetime.Begin in `OverlayProcessOwner.cpp`; do not add a
second managed mutex owner. This checks Setup and holds OverlayHost.Running.

CLI audit recommendation: IApplicationActivationManager activates registered AUMID;
CLI creates unique named kill-on-close job, frontend self-enrolls before services,
CLI verifies returned process identity and job membership. Maintain hidden probe
then last-good host handoff, separate probe profile and reusable interactive
session profile. Preserve inspector semantic tree/focus/navigation/native geometry/
computed styles and F12 reopening. Agent playnite_presentation_cleanup supplied
this audit but has not implemented CLI changes yet.

Installer agent migration_checkpoint_review owns new WidgetRail.Deployment helper,
fake/staged transaction tests, Inno/Build-Installer changes. It is taking the build
slot for helper/tests, no native installation. Source draft requires signed
identity, verifies exact external binding, journals metadata transitions and rolls
back failures. It currently rejects same-version external rebinding: edition
switching needs qualification/design before completion. Signed removal cannot
use PreserveApplicationData (development-only); keep external user data untouched.

Release agent capture_teardown_audit completed read-only design and awaits source
approval/identity contract. Proposed isolated Build-WinUiReleasePayload stages
native interop/preview + trimmed frontend + Bridge/worker/Settings/sealed bundled
widgets/CLI/private runtimes without calling old host build; Build-Release retains
clean-checkout publication guard. Installer schema proposal: deployment/identity.msix
and identity.json {schemaVersion:1,name,publisher,version,applicationId,packageSha256},
self-contained helper under deployment/helper. Same identity across editions.
Signing inputs/publisher/version decision and offline Framework provisioning still
need coordination before release implementation. Keep production GameInput/WebView2
provisioning; the user's prohibition is against executing system changes in tests,
not a request to remove installer functionality. Do not silently replace runtime
provisioning with missing-prerequisite errors as the final product design. Existing
verified Stage-WinUiRuntimePrerequisites handles official signed Framework payloads;
Main/Singleton remain owned by the SDK initializer. No cert trust/runtime installers
or production installation should be executed on the user's machine for validation.

Step4 incomplete, step5 not started. Source/managed test evidence so far under
`artifacts/winui-shell/authoring-cleanup-20260930/` (development-config01 and
startup-command01). No merge/push or new checkpoint commit.

### Presentation cleanup completed; launch/distribution integration active — 2026-09-30

Removed Playnite's unused eager/cursor UI path and dead HeroRail policy; retained
provider data and production indexed authority. Updated all presenter fixtures to
SDK-acquired rows. Removed unmapped flex declarations from shipped themes/widgets
and migrated actual small wrapping groups to ResponsiveGrid. Added CLI preflight,
shipping-style diagnostics and current indexed collection author documentation.

Passed: Playnite164; first-party widget suites281; shared themes27; Gallery11,
YT Music30, Spotify77, YouTube54; six targeted CLI checks. Analyzer Debug and
trimmed/ReadyToRun Release pass. Native shared styles460, Playnite rows13, Home16,
Browse13 and Details9 pass. Home's first new fixture incorrectly required the
Browse width150 rather than authored bounded28vh; corrected the test's expected
geometry, no production sizing workaround. Scripts parse and diff whitespace checks
pass. This is scoped cleanup validation, not deferred scroll/performance acceptance.

Candidate72148 relaunched;244 DLLs match. Playnite0.2.111, YT Music0.3.34 and
Gallery0.1.25 installed/selected/enabled in the existing isolated catalog. Oldest
inactive Playnite0.2.103/YTM0.3.26 packages preserved under the evidence directory
to respect the eight-version bound. User profile, credentials, ordering and the
exclusive-control Off preference remain. Remaining inert first-party/sample style
source cleanup flows into the new distribution payload; it changes no WinUI paint.
Evidence: `artifacts/winui-shell/authoring-cleanup-20260930/` and agent suite folders.

Step4 now active: WinUI CLI development/inspector launch, per-user external-content
identity installation, startup and transactional upgrade/rollback. Shared bundled
catalog source moved from the original renderer to `eng/widget-catalog.json` as
the first ownership cleanup. Old renderer retirement remains step5, after this
integration. No production install, certificate trust, main merge or push.

### Exclusive-control reconnect correction — 2026-09-30

User reported a physical disconnect/reconnect leaving input unavailable in every
application. Candidate64220 log showed Active at19:05:22Z, then the native
"No eligible physical controller could be selected safely" failure at19:15:26Z,
followed by RecoveryRequired and stopped WinUI navigation polling. This diagnostic
maps to ambiguous/unknown discovery, not the unavailable-device branch. It did not
distinguish which of those two statuses occurred. User selected Restore controller
access and confirmed the controller works again; their resulting Off preference
is retained.

Confirmed code gap: unavailable discovery retried but incomplete/ambiguous hotplug
identity latched Fault permanently. The shared native owner now waits/retries all
non-ready discovery through its existing250ms cadence only after prior exact-owned
policy restoration succeeds. No unidentified device is selected or hidden. True
journal/restore failures still fault. Added transition-only discovery status logs.
Native owner94, routing186 and DualSense decoding172 checks pass, including both
transient identity statuses, unhidden waiting, neutral retained output, resumed
overlay containment and existing cleanup-failure protection. Native DLL builds;
existing third-party ViGEm header codepage warning remains.

Candidate11700 relaunched with only this native fix staged, same profile/catalog
and prior managed payload;244 DLLs verified. Physical reconnect acceptance remains
for the user. Evidence: `artifacts/winui-shell/exclusive-reconnect-20260930/`.
Parallel step-3 source cleanup is not yet staged; its combined native validation
remains pending. Distribution and renderer retirement still follow step3.

Continuation handoff: candidate11700 is live for the user's reconnect check.
Playnite managed final164/164 passes; its agent finished source/export/runner work
and has not run the native fixture. `Test-WinUiPlaynitePresentation.ps1` consumes
the new indexed exports plus existing production Details geometry. The sample lane
is running Gallery/YTM/Spotify/YouTube managed suites. First-party suites passed
281 checks. Root repaired two verified stale theme expectations and expanded the
shipping-style diagnostic gate to all copied package fixtures; PlatformSettings
final27/27 passes (`authoring-cleanup-20260930/themes-final01.log`). The new
collections reference now describes indexed/discovered WinUI authoring rather
than presenting rejected cursor metadata as current usage. No further native
testing or package staging has happened since the reconnect-only relaunch.

### Active presentation and authoring cleanup — 2026-09-30

The bounded capture pass is complete (results below); step 3 is active. Playnite
is removing its unused eager/cursor presentation branches after moving fixtures
to the production indexed declarations and explicit SDK row leases. Shared and
first-party styles are dropping inert flex shrink/basis declarations while
preserving native track weights and bounds. Sample wrapping groups are audited
individually; layout changes require native geometry checks. Provider cursor/data
logic, action authority and remembered focus remain in place.

The CLI now surfaces the existing WinUI stylesheet guidance during `wrail validate`,
including imported and state rules. `wrail render` applies the shared native
presentation admission contract before writing output, so unsupported old
collection declarations name their field and replacement instead of appearing
valid until opened in the frontend. Two targeted tests and four existing CLI
validation/render regressions pass. Initial new fixtures used an unsupported
`:focus` spelling and incomplete cursor metadata; corrected to `:focused` and
an actual SDK legacy declaration before claiming the passing results.

Distribution integration and renderer retirement have not started yet. Real
device/provider acceptance remains user-owned; scrolling and memory work remain
deferred. Evidence: `artifacts/winui-shell/authoring-cleanup-20260930/`.

### Focused Task Switcher capture-shutdown pass — 2026-09-30

Completed the requested bounded follow-up without reproducing the original
`GraphicsCaptureSession.Close` / capture-service RPC hang. Actual Task Switcher
loaded live previews, including an independently owned fixture source, in both
visible and hidden-overlay cases. Normal WM_CLOSE completed in 595 ms with five
active captures in the visible case, and 200 ms after hiding a seven-capture case.
Both frontend and owned Bridge exited normally. This is targeted evidence, not a
claim that the intermittent Windows capture hang is fixed or cannot recur.

Kept actionable bounded diagnostics: native close phase/source HWND/PID/slot/frame
count/start time and HRESULT use an independent shared health reader. Its read
never queues behind the capture worker, spins, or accesses a destroyed engine.
A one-second managed watchdog reports a phase only after two seconds and then
reports its eventual completion once. Capture ownership, shutdown waiting, and
session/pool teardown order are unchanged; no timeout-abandonment workaround.

Validation: native Release builds with 15 policy checks and 2021 diagnostic checks
(including concurrent publication and reader lifetime across engine destruction);
three managed watchdog/ABI tests pass; analyzer Debug builds with zero warnings
or errors. Both native shutdown runs verified 244 deployed DLLs. First fixture
attempt lacked isolated read/preview consent and never started capture; corrected
the fixture profile to grant only those read/preview capabilities, then both
cases passed. No switch/close widget action or system-setting mutation occurred.
Evidence: `artifacts/winui-shell/capture-close-diagnostics-20260930/`:
`native02-build.txt`, `managed01/`, `frontend03.binlog`, `shutdown02/results.json`.
Native diagnostics and managed reader are now staged together. Canonical
candidate 64220 relaunched with the existing physical profile/catalog; all 244
deployed DLLs match the qualified Debug build (`candidate-payload.json`). Memory testing
remains deferred. Continue the authorized legacy-cleanup/distribution/retirement
sequence rather than expanding this capture investigation.

### Next migration sequence authorized — 2026-09-30

User requested one focused/time-bounded pass on capture shutdown with Task Switcher
active, covering visible and hidden overlay states. Do not expand this into memory
testing or broad activation work; if it does not reproduce, retain actionable
diagnostics and continue rather than spending disproportionate time.
Then execute in order: (3) remaining legacy presentation/authoring cleanup,
(4) CLI/installer/runtime/startup/upgrade and rollback integration,
(5) retirement of the old renderer and unused UI dependencies. Preserve useful
native platform/provider/sandbox functionality. User owns real workflow physical
qualification and explicitly says it is not an implementation blocker for these
steps. It remains separate acceptance evidence, not an automatic success claim.
Stay on the migration branch; no main merge or push was authorized.

### Migration checkpoint cleanup — 2026-09-30

User physically accepted the release-aware Task Switcher fix ("Works perfectly
now") and requested cleanup and a local commit on codex/winui3-frontend. This is
a checkpoint of completed work, not migration completion, a merge to main, or
release approval. User explicitly intends to continue migration on this branch.

Removed temporary global keyboard/mouse/foreground hooks, key-code capture, trace
launch flag, detailed grant-observation option and their diagnostic-only tests.
Retained normal bounded activation/failure diagnostics, QPC correlation, regression
fixtures and the accepted release/motion/permission handoff. Removed the abandoned
activation-order matrix and pointer probe after preserving their source locally;
the focused production-path handoff regressions supersede them. Accepted incident
logs and observer source are preserved locally under
`task-activation-terminal-20260930/accepted-cleanup/`.

Unfinished capture-close diagnostics are parked, not silently discarded: complete
native source/tests/build script and tracked patch are saved under
`capture-close-diagnostics-20260930/source-checkpoint/`; its managed integration
draft remains in `managed-pending/`. Native production source/build restored to
the qualified version; no diagnostics DLL staged. The capture-service Close/RPC
hang remains unresolved and is a next-work item.

Cleanup validation: analyzer Debug passes;23 handoff/input ownership tests,
301 presentation-session tests,148 SDK/protocol tests and151 shell tests pass.
Independent scoped reviews found no checkpoint blocker in handoff lifetime,
widget/sample changes, shared dependencies, or file inventory. All27 changed/new
PowerShell scripts parse. Trimmed/ReadyToRun Release cleanup publish and final
analyzer Debug cleanup build pass. Canonical candidate54304 relaunched without
temporary tracing;244 deployed DLLs match the final Debug build. Checkpoint files
are being committed locally on the migration branch; migration remains active.

Remaining work is still tracked below and in the migration plan: capture teardown,
remaining native provider/workflow acceptance, Playnite eager fallback/test-consumer
cleanup, and later CLI/installer/deployment cutover. Playnite fast-scroll tuning
and further memory qualification remain deferred at the user's request. Task
Switcher acceptance is not a blanket claim that every Windows foreground denial
or every provider/monitor/controller combination has been qualified.

### Release-aware task handoff — 2026-09-30, validated and relaunched

User authorized proper leakage correction while retaining prior edge-case fixes.
Confirmed VK_GAMEPAD_A can arrive after immediate hide and before broker activation.
MainWindow.TaskHandoff now owns the asynchronous close preparation: logically retire
widget actions with retained pixels, retain real foreground/controller polling and
XAML gamepad-key consumption, wait for a fresh drained raw frame with buttons and
triggers released, run the existing normal shell/backdrop close animation, then
require a fresh neutral frame again before commit (new presses during animation
also remain owned). Stick drift does not block. No sleeping/retrying/global key
suppression. Reduced-motion uses the same release gate with zero motion duration.
ControllerHandoffRelease observes before View/Menu delivery masking (review fix).

Shared TaskWindowActivation revalidates foreground/worker/target/freshness after
preparation and retains the original post-hide identity/expiry guards. Refreshes
only the owned broker's permission immediately before hide; broker still executes
the final native activation using its existing single-use authority ticket. No
activate-before-hide retry, previous-window restoration, broad permission grant or
timeout extension. Reopen/external dismissal/close cancels preparation; expiry or
disconnect restores only a still-visible/current/foreground-owned presentation,
never activates/reopens a hidden one. One pending handoff per host. Cancellation
before commit cannot submit to broker; already committed native activation is not
claimed retractable.

23 managed release/activation/input-ownership checks and2 broker ticket/worker
retirement scenarios pass. Native owned-peer checks: full motion10 (including held
A and second press during animation), reduced9, reopen-cancel8, expiry-cancel8 pass.
First reduced run reached all release assertions but broker grant failed error5;
preserved reduced01 result/trace. Controlled retry used a guarded neutral Shift tap
only on owned fixture to establish last-input eligibility; production has no such
input injection. It passed with actual broker activation and replay rejection.
Release publish01 passes trimmed/ReadyToRun; no installed provider/system setting
actions were performed.

Ordinary motion01 then failed deferred-close assertion; motion02 exposed a pending
opening flag not consumed by direct appearance-driven entrance. Shared animation
entry now consumes the current opening intent, rather than relying solely on a
queued Loaded callback that can be superseded. Second correction/rerun passes all16
ordinary motion checks (motion03); the fixture explicitly establishes its intended
in-memory full-motion policy after session reopen. Independent review confirms
stale versions cannot consume a newer opening intent. Frontend05 and current
trimmed/ReadyToRun Release publish02 pass. All evidence under
`artifacts/winui-shell/task-activation-terminal-20260930/`.
Physical candidate61820 was closed with approval. All owned fixtures and peer20108
closed normally. Canonical candidate72272 relaunched with original installation,
catalog/profile/shortcut and the temporary trace flag;244 deployed frontend DLLs
verified. No capture-close DLL staging. Physical Terminal/Settings input-leak and
GoW acceptance remain pending for this change. The separate Windows permission
denial seen in reduced01 is preserved; this does not claim every denial is fixed.

### Key identity confirmed — 2026-09-30, no behavior changes

User reproduced with key-trace candidate61820. Preserved physical-key-reproduction.log
and physical-key-controller.log under task-activation-terminal-20260930. Event is
vk=0xC3 (VK_GAMEPAD_A, verified in installed Windows SDK WinUser.h), scan0,
injected=True. Cycle33: grant succeeds on UI thread45212; native hide starts at
QPC4872382787285; Terminal foreground observed4872382832530; gamepad A down at
4872382856301; broker SetForegroundWindow returns false at4872382977836. A-up follows
roughly59ms after A-down. Cycle35 succeeds with no A event in the handoff interval.
This identifies a translated gamepad-A event, not Enter/Space. Injection flag does
not identify the originating process. Existing GamepadKeyBoundary consumes only
owned, active XAML-root events; it provides no destination-window containment.
Settings double activation is consistent with this carried selecting gesture,
but this captured attempt targets ChatGPT/Terminal, not a Settings action trace.
No production fix, input suppression or restart performed for this read-only check.

### Handoff input captured; key identity requested — 2026-09-30

User reproduced with58672 and reports selecting Windows Settings also activates
its focused option. Saved physical-input-reproduction.log and
physical-detailed-controller.log under task-activation-terminal-20260930.
Cycle21: permission granted on UI thread49436 at QPC4867536923983; Terminal becomes
foreground at4867537068681; injected key-down at4867537110975; broker activation
denied at4867537127393 (about1.6ms after hook observation). Injected key-up follows.
Cycle22 succeeds without a key event in the hide-to-activation interval. No drops
reported. This confirms injected keyboard input during the failing handoff;
sender/key identity and exact relation to physical A remain unproven. Existing
WinUI gamepad-key boundary only handles input inside owned XAML roots, not the
destination after hide. No leak fix or activation-order change yet.

User explicitly requested key identity in the trace. Added numeric virtual-key,
scan-code and flags to keyboard records only within existing bounded capture
windows; no character translation. Analyzer key-trace-frontend01 builds cleanly.
User approved restart;58672 closed normally and61820 relaunched with trace flag,
244 deployed DLLs verified and observer-ready recorded. Defaults still install no
hooks and all observed events are forwarded without suppression. User reproduction
with key identity remains pending.

### Temporary handoff input trace — 2026-09-30, diagnostic candidate running

User requested targeted diagnostics using their Terminal reproduction. Added opt-in
`--trace-task-handoff`: dedicated Win32 observer thread installs keyboard/mouse
low-level hooks and foreground WinEvent hook. Callbacks always forward input, never
activate windows, and read only hook flags/time (not virtual keys, scan codes,
coordinates, wheel values, button identity, titles or typed content). Capture arms
for15s on opening,2s on grant, and750ms after hiding; cap1024 events per window,
drop count reported. Existing bounded async logger rotates512KiB segments in
`%LOCALAPPDATA%/WidgetRail/WinUI/diagnostics/activation-input.log`. Default launches
install no hooks. Diagnostic input samples report observed foreground, not proven
input recipient; low-level keyboard/mouse coverage does not establish all GameInput
or inaccessible-desktop input delivery.

Opt-in grant observations include native thread, QPC, foreground identity and
GetLastInputInfo before/after AllowSetForegroundWindow. Shared native activation
observation adds QPC for broker-side correlation. No activation-order, grant policy,
target permission or lifecycle change. Independent interop review checked SDK
offsets/signatures/delegate rooting; found and fixed a recycled-thread-ID Stop race
using idempotent shutdown and synchronized publication/clear/post. Observer stops
before asynchronous shell cleanup and never waits on the capture engine.

17 focused tests pass (4 diagnostic bounds/actual hook lifecycle +13 unchanged
handoff checks), analyzer frontend02 clean. No synthetic input injected. Physical
input delivery and failed/successful handoff timeline await the user's reproduction.
User approved;62896 closed normally. Candidate58672 launched with the opt-in flag;
244 frontend DLLs verified. Broker's WindowsWindowActivation.dll updated for QPC
observation only, previous DLL preserved in before-diagnostic-bridge. Installation,
profile/catalog/shortcut preserved. Capture-close native work remains isolated and
unpublished, with managed draft parked in artifacts.

### Task Switcher post-rollback physical evidence — 2026-09-30, read-only

User reproduced again in candidate62896. Saved
`task-activation-terminal-20260930/physical-after-rollback-controller.log`.
Chrome/ChatGPT switching succeeds repeatedly, then switching into Terminal at
17:41:22.946 is accepted. The next12 activations from Terminal are denied
(Chrome once, ChatGPT11 times); ChatGPT finally becomes foreground at17:41:46.886.
Foreground delegation succeeds throughout this run: no error5 grant failures.
Targets remain valid/visible/nonminimized; no expired effect, stale worker or widget
lifecycle timeout is reported for these attempts. The broker calls SetForegroundWindow
after Terminal regains foreground and Windows returns false. Failed attempts often
have LastInputTick after the grant (or in the same coarse tick); final success has
last input16ms before grant. This reinforces an input/foreground-history correlation
but does not identify the input source or prove permission revocation. Keep distinct
from61532's failed-grant path. No new production changes, test actions or restart.

### Task Switcher handoff rollback — 2026-09-30, relaunched

User reports activate-before-hide candidate61532 regressed Task Switcher. Preserved
`task-activation-terminal-20260930/physical-regression-controller.log`. Its first
Chrome activation17:33:31 granted permission and succeeded; subsequent reopenings
needed AttachThreadInput to acquire foreground, then BOTH pre-action and
pre-completion AllowSetForegroundWindow calls failed with error5. Broker activation
was denied. Thus visible foreground ownership is not proof of transferable
foreground permission. Earlier inference that hide-gap input alone explained this
workflow was too strong; the owned-window native test did not cover repeated
physical Guide reacquisition. No claim of a complete race fix survives that result.

Restored the pre-change grant-before-input, hide/revalidate, broker-activate path,
its original 13 passing tests and its native fixture expectations. Removed the
extra completion grant and activate-first completion guards; retained the accepted
Task Switcher0.2.1 startup change. Revert frontend01 builds cleanly. User approved
restore/relaunch;61532 closed normally. Canonical candidate62896 relaunched with
existing profile/installation/catalog/shortcut;244 frontend DLLs verified.

Capture close diagnostics paused for this regression. Agent completed independent
native reader with shared ownership; isolated Release native01 passes15 policy and
2021 diagnostics checks, including reader survival through actual engine destruction.
No native DLL staging. Managed integration draft preserved under
`capture-close-diagnostics-20260930/managed-pending/` and removed from active build
to keep rollback free of that unfinished change. Draft formatter needs fixing:
FormattableString.Invariant cannot take concatenated interpolated strings.

### Task Switcher Terminal handoff — 2026-09-30, focused checks passed

Superseded by physical regression/rollback above; this is historical evidence.

User physically accepted Task Switcher 0.2.1 cold opening, then reproduced repeated
Terminal -> ChatGPT activation failures before eventual success. Preserved current
controller and worker logs under `artifacts/winui-shell/task-activation-terminal-20260930/`.
PID68340 delegated foreground successfully to broker56852. Failed requests then hid
the overlay, exposed Terminal5896, and reached SetForegroundWindow(ChatGPT28700)
after LastInputTick advanced beyond the grant; Windows returned false. The final
successful attempt's last input preceded its grant. Target identity remained valid
and visible throughout; this was native activation denial, not widget startup or
stale snapshot rejection. The initial zero-byte directory listing/search was
misleading for the open log: Get-Content recovered the actual current trace, so no
logging fix was made.

Changed the shared activation handoff to request activation before physical hide.
The trusted broker still executes activation with its bounded, single-use ticket,
exact worker/window identity and expiry checks. CompleteTaskActivation now refreshes
foreground permission after transport capacity/write admission immediately before
IPC. Async completion retains the UI synchronization context, serializes pending
handoffs, and hides only the original opening/selection; a late reply cannot hide a
new opening. No retries, explicit previous-window restoration, toast policy change,
or elevated-game automation. Paused order-comparison fixture keeps its explicitly
named historical hide-first variants independently of production order.

Checks: 13 activation tests and 1 real transport queue/delegation test pass; analyzer
Debug frontend02 builds with zero warnings/errors. Native01 passes8 checks using
real Task Switcher/broker and one owned disposable window, including frontend
foreground at completion dispatch AND broker activation, exact destination after
200ms, and replay rejection. Both fixture68768 and peer57424 closed normally.
User approved this focused check/relaunch. Terminal -> ChatGPT physical acceptance
of the new handoff remains pending; prior GoW acceptance predates this change.
Canonical candidate61532 relaunched with the existing physical profile, catalog,
installation and shortcut;244 frontend DLLs verified against frontend02.

Separate capture-shutdown diagnostics remain source-only work in progress:
WindowPreviewDiagnostics.h, WrailPreviewHealth/ReadHealth and native close-phase
observations have not been built or staged. Managed reader/integration and tests
remain undone. They do not change the candidate's native DLL or fix StopCapture.

### Task Switcher cold activation — 2026-09-30, tested and relaunched

User reported first opening Task Switcher produced "Widget could not be displayed".
Live PID68812 frontend log17:08:31 and physical overlay.log agree: worker63764 started
17:08:29.536, failed its Visible lifecycle with request-timeout after~2.1s and exited.
This is separate from the native capture StopCapture shutdown hang below.
TaskSwitcherWidget.OnActivatedAsync awaited GetWindows before returning; a cold
provider read consumed the lifecycle acknowledgement's2s budget.

Changed activation to start the existing owned refresh loop and return immediately.
The loop yields before its first enumeration, then publishes current windows or
the existing friendly error state through Invalidate. Active-lifetime cancellation,
generation guards, serialized refresh and deactivation join remain. No larger
timeout, detached task, fabricated rows or automatic window action was added.
Delayed-provider regression baseline01 reproduces blocked activation. Corrected
console suite passes8/8, including loading declaration validity, hide/cancel during
initial read, fresh reopen, stable ordering, Switch/Close routing and failures.
Task Switcher version0.2.1 built and sealed in package01. Evidence:
`artifacts/winui-shell/task-switcher-cold-20260930/`.

User approved update/relaunch. Candidate68812 closed normally. Preserved the prior
canonical runtime/TaskSwitcher in previous-package, copied only sealed0.2.1 package
contents, and verified all six files against package01 hashes (no extra files).
Installation/catalog/Bridge and physical settings remain unchanged. Relaunched
canonical candidate68340;244 frontend DLLs verified. Physical cold-opening acceptance
was subsequently accepted by the user. Capture shutdown remains unresolved; the read-only audit found
no proven borrowed-frame leak or local lock cycle. Microsoft capture sample uses
the same session-before-pool close order, and its issue65 reports similar RPC hangs;
that report is context, not proof of a service crash in this incident. Further
diagnostic work should retain exact native ownership and avoid unsafe callback timeouts.

### Capture shutdown hang — 2026-09-30, confirmed and unresolved

User authorized closing candidate70432 for modal checks. WM_CLOSE hid the window
but the same process remained alive for many minutes, blocking the exclusive
fixture. User confirmed Task Switcher was open and target windows retained their
yellow capture borders. Managed thread stacks show multiple WindowPreviewSurface
pumps waiting in WrailPreviewInspect. A diagnostic mini-dump and native symbols
identify engine thread0x10ce8 in GraphicsCaptureSession.Close -> ServerCaptureSessionCore.StopCapture
-> synchronous COM/RPC receive. The single native engine queue cannot process
other Inspect/Remove requests while that call is blocked. This is stronger than
generic exit delay; no native fix has been made yet. Original native Capture.Pause
also calls session.Close before pool.Close, so that order alone is not a proven
migration regression. Do not introduce unsafe future timeouts around callbacks
that capture caller-owned output pointers, or free managed authority GCHandles
while native capture may still call them.

Preserved `artifacts/winui-shell/shutdown-wait-20260930/`: stacks.txt,
shutdown-mini.dmp, native-stacks02.txt and capture-worker-stack.txt. This is hang
diagnosis, not resumed memory qualification. Local .NET diagnostic tools and a
small dump-only DbgEng stack utility live under artifacts/tools/shutdown-diagnostics;
the utility uses official Microsoft DbgEng/SymSrv packages. No debugger is attached
to a live process. After capture, force-stopped exactly PID70432 with matching
image/start-time checks to release captures; its Bridge/WebView children also
exited. Do not report this forced recovery as a shutdown fix. Prior modal baseline
launch was correctly refused while the resident remained; no overlapping frontend.

Next priority: controlled preview close/hide regression with owned windows, native
capture lifecycle diagnostics and a correctness review of StopCapture/queue
retirement. Avoid arbitrary real-window actions, system settings and additional
memory tests. Preserve the mini-dump for more focused analysis if necessary.

### Modal automation correction — 2026-09-30, native checks passed

Baseline01 proves inactive game.12 and active Play both appear in the external
native UIA tree. Added a single transparent automation peer on WidgetMotionStage
that returns existing native peers from Modal when present, otherwise Current.
It excludes inactive/outgoing branches without disabling, reparenting or rebuilding
their visual controls. Stage replacement invalidates the native peer cache.
Native01 external tree excludes game.12 correctly, but the in-process test initially
assumed the presentation-only ContentControl had a peer. Corrected that fixture
to find native peer roots below peerless wrappers, then traverse native peers.
Native02 passes38 checks, including modal/background exposure, closing exclusion,
retained parent scroll/control identity and focus restoration; screenshot reviewed.
Analyzer Debug frontend03 and trimmed ReadyToRun Release01 pass. No action or focus
policy changed. Evidence: `artifacts/winui-shell/modal-accessibility-20260930/`.
Canonical candidate relaunched as PID68812;244 deployed DLLs verified. User profile,
catalog and shortcut preserved. Capture shutdown defect remains open above.

### Playnite production-path fixture migration — 2026-09-30

Parallel test-only work adds IndexedPresentationFixture with captured production
Home/Browse queries and SDK row leases, leaving parent cursor rows empty. Migrated
missing-artwork labels and pending-launch summary cases; added busy/retained and
saved-only warm Home coverage. Replaced a stale selected-focus fill assertion with
the earlier accepted selected-fill/visible-outline behavior. Managed tests05 passes
162/162; no skips. Evidence: artifacts/winui-shell/playnite-presentation-20260930-tests05.binlog.
No widget production/package/provider change. Remaining direct presenter tests
(Layout28, Details5, Theme5, Collection4) include legitimate supporting routes and
obsolete eager trees. Convert those and renderer-export consumers before removing
the unused eager/warm fallback branches, BrowseGrid/HomeRail helpers, cursor
anchor/pagination wiring and CreateBrowseItemCache. Keep actual provider cursor
business logic. This is independent of deferred Playnite scrolling optimization.

Historical modal preparation:

User physically accepted YouTube passive pin correction, then explicitly permitted
closing. The shutdown hang and completed modal qualification are recorded above.

Existing modal tests proved scoped focus/action rejection and retained visuals,
but never traversed the native automation peers. Added two native assertions to
ModalValidationPage: dialog controls present and background game.12 absent while
modal is open; game.12 restored and retiring Play absent during close. This uses
actual native peer children/control membership, not declaration-tree inference.
At this preparation checkpoint no modal production code changed. Analyzer Debug frontend01 passes cleanly;
new Test-WinUiModals.ps1 parses and requires the two assertions, records native
results plus external UIA tree, and closes only its owned fixture. Evidence root:
`artifacts/winui-shell/modal-accessibility-20260930/`.

The original planned next step was: verify current candidate PID and close normally; run the
new fixture for baseline evidence. If background controls are exposed, prefer a
single native automation boundary on WidgetMotionStage (Modal versus Current),
retaining native descendant peers, rather than rebuilding a separate accessibility
tree or disabling the painted parent. Modal parent is a sibling in that stage,
not a child of WidgetModalLayer; this matters for correct exclusion. Any fix needs
native peer/external-tree verification and retained-control/focus regressions.
Always relaunch canonical candidate afterward. Memory profiling/Playnite stutter
remain deferred. Migration goal stays active/incomplete.

### Passive video attachment correction — 2026-09-30, validated and relaunched

User confirmed View makes the invisible pin appear and B leaves it visible.
Before/after native window opacity, geometry, visible and cloak flags were unchanged.
Reproduced missing WebView pixels in the sealed native cross-root fixture while
audio/document animation frames progressed. Added a magenta native XAML border:
native03 showed that border but the desktop through the browser area, distinguishing
WebView presentation from an invisible island. Native Loaded/root/hostVisible
checks alone were all true. A second nonactivating AppWindow.Show did not help;
that diagnostic experiment was removed.

Cause established in the local reproduction: transfer published WebView visibility
before WinUI's destination Loaded callback rebound HWND/XamlRoot/RootVisualTarget.
Transfer now remains hidden through source Unloaded and destination Loaded, then
publishes the latest visibility/input state. Completion waits for that native event,
not a guessed render delay. Superseding destinations wake the wait and are handled
by the same transaction; hidden parking and shutdown remain supported. Bounded
attachment diagnostics include load/root/host visibility and dimensions, never URL
or media title. No window activation workaround, controller recreation or reload.

Native05 passes79 checks and4310 sealed media pixel samples without interaction.
Native07 passes80 checks after adding a during-Loaded supersession case. Native06's
new test used an unhosted Grid; corrected it to the real supported pattern of a
collapsed parking Grid in the main XamlRoot. The capture helper now explicitly
admits owned passive topmost/noactivate peers without requiring foreground; it
never activates the window. Pixel failures remain failures but no longer abort
behavioral/teardown observations early. One prior run was blocked by unrelated
foreground loss, preserved as infrastructure evidence.

Actual sealed sample passes33 production workflow checks, including passive pin,
View/B return, fullscreen, transport, controls, placement/opacity and unpin. Passive
and passive-return video pixels were captured and inspected. Analyzer Debug
frontend07 and trimmed/ReadyToRun Release01 pass. Production shell113 additionally
qualifies the recovery announcement changes below. Relaunched canonical candidate
PID70432;244 deployed DLLs match. User subsequently reported "Youtube check passed",
physically accepting the passive-pin correction in YouTube. This is acceptance of
the reported path, not a claim of universal WebView parity.
Evidence: `artifacts/winui-shell/passive-pin-incident-20260930/`.

The following preserves the original live incident observation:

User reproduced an entirely invisible compact video pin while audio keeps playing;
reported that View interaction makes it visible. Preserve live PID68192, peer
HWND292229430; do not restart/unpin/activate programmatically. Read-only evidence
under `artifacts/winui-shell/passive-pin-incident-20260930/` includes UIA tree,
native window state and copies of frontend-errors/switches logs. UIA shows peer,
island, compact host and WebView visible/on-screen at1200x675 physical pixels,
x3130/y20. Chrome's own UIA bounds show0/1, which may be a composition-coordinate
artifact and is not yet proof of a placement error. Native peer is visible,
topmost, layered/passive; alpha140 (saved55%), flagsLWA_ALPHA, DWM cloaked0.
Only explorer/TabTip windows precede it in z-order; no conclusion about pixel
occlusion from that alone. Audio/browser activity confirms document survival.
No media fault is logged at the incident; an independent Task Switcher worker
timeout occurred16:18:51 and recovered on the next selection.

User authorized closing after the before/after capture. Memory testing remains
stopped; these are bounded rendering/transfer regressions, not resource profiling.

Independent authoring work: new WinUiStyleDiagnostics.Analyze reports
source-located warnings for flex-shrink/basis/wrap without changing compilation;
WidgetStyling console harness passes37/37 with two new cases. Shared styling README
and WinUI author guide describe its source-level scope and native alternatives.
This does not implement ignored properties or change existing widget layouts.
Evidence: `artifacts/winui-shell/style-authoring-20260930/styling01.binlog`.

### Recovery accessibility parity — 2026-09-30, native checks passed

Production shell native01 passes113 checks, including five added announcement
checks. Reviewed shell capture. Included in candidate70432 above; actual Narrator
speech remains distinct from peer metadata/native event-request validation.

Read-only accessibility audit found that the original HostAccessibility status
node is a polite live region, while the WinUI recovery TextBlock only changed
text/visibility. Added native AutomationProperties.LiveSetting=Polite and a
dispatcher-queued LiveRegionChanged notification for changed visible recovery.
Repeated identical messages do not reannounce; newer messages and successful
recovery invalidate queued announcements. Hidden/background/retired shells do
not announce. Existing rail/radial focus and interactive Retry policy are retained.

Extended production layout fixture with native peer metadata and announcement
request checks, duplicate/coalescing/recovery-cancellation cases and retained
focus. The validation-only partial hook observes the native RaiseAutomationEvent
call; it does not prove actual Narrator speech or out-of-process UIA delivery.
Analyzer Debug frontend01/frontend02 pass with zero warnings/errors. Trimmed
ReadyToRun Release02 publish passes. Release01 was an invocation error: passing
PublishTrimmed globally analyzed unrelated dependency methods; Release02 uses
the frontend's existing Release trimming/ReadyToRun properties without overrides
or warning suppressions. Evidence: `artifacts/winui-shell/recovery-accessibility-20260930/`.

The prior candidate PID21472 had exited before the attempted normal close. We
observed replacements60596/68192 with physical profile activity and kept them open
until the user approved closing after passive-pin evidence capture. That approval
and subsequent validation/relaunch are recorded above. No testing-status approval
is pending from this checkpoint. Memory work remains deferred.

Separate audit lead: inactive modal parents retain enabled native controls while
TabStop/HitTest are disabled. Original host excluded inactive scopes from UIA.
Existing native modal tests verify focus/action rejection, not Control/Content
automation-tree exclusion. Verify the actual native UIA tree before making a
production change; GetIsDialog alone is insufficient evidence of parity or failure.

### Reference workflow checkpoint — 2026-09-30, two complete native passes

Added validation-only timeout diagnostics for shell visibility/interactivity and
indexed target retention, lease readiness, native realization and viewport geometry.
Analyzer frontend03 passes with zero warnings/errors. Native02 and native03 each
pass all13 checks with unchanged production code: Clock refresh, rows0/75/9999
Details/Back, current lease replacement without native list replacement, three-peer
cache eviction/deep reentry, and hide/reopen through lifecycle refresh. Read-only
audit confirms the sample preserves the keyed return target and navigation revision.
The earlier native01 timeout remains unexplained; passing reruns do not establish
its cause or prove it fixed. No speculative production correction was made. Keep
the richer timeout diagnostics for recurrence; no additional reruns are planned.
Canonical candidate relaunched as PID21472; all244 deployed DLLs match frontend03.
Existing physical profile, catalog and opening shortcut are preserved.

Analyzer Debug frontend02 builds with zero warnings/errors after adding the missing
XAML Input namespace to the validation fixture. Native01 passes eight assertions:
Clock display and X refresh, bounded realization of10,000 reference records, Details
for rows0/75/9999, and Back restoration for rows0/75. Back from row9999 times out
with empty focus; subsequent refresh/eviction/hide checks were not reached. The
available switches log does not establish external foreground loss or a production
cause. No production fix was made on this evidence. Preserve this failed gate for
bounded follow-up; do not claim full reference workflow acceptance.

Further memory testing/investigation remains deferred at the user's request; no
leak was established by the completed soak. Relaunched canonical physical candidate
PID67680 using the existing profile/catalog after the fixture exited. Evidence:
`artifacts/winui-shell/reference-workflows-20260930/`.

### Extended resource lifetime qualification — 2026-09-30, testing stopped by user

Previous goal turn resolved native WebView GPU recovery and relaunched62160.
Closed that candidate normally for isolated switching qualification. Added bounded
time-based switching (up to1800 seconds) to the existing synthetic full-shell
fixture, retaining its four-widget/three-native-surface eviction workload and one
hide/reopen every four switches. No production behavior changed; Release remains
the previous qualified production build. A companion read-only process-tree sampler
records frontend/broker/worker CPU, private commit, working set and handle counts.
No forced GC, system settings, real provider action or physical controller input.
Timed runs also hash-check the deployed244-DLL payload against the build.

Analyzer frontend01 passes. Pilot01 passes45 checks with462 switches over60.08s;
resource observer records14 samples over70.19s, correctly follows OverlayFrontend,
WidgetBridge, WidgetSwitchFixture and conhost, and exits when the owned root closes.
Whole-tree sampled median341MiB, peak358.82MiB; these are preliminary observations,
not frozen budgets or a plateau claim. No leftover frontend after pilot cleanup.

The30-minute run completed normally:45 native checks,13,029 switches and3,257
hide/reopen cycles over1800.04s. Maximum retained/native surfaces stayed3. Resource
observer recorded361 samples and exited with the root; none of the final sampled
frontend/fixture/bridge child PIDs remained when checked after completion. The
244-DLL payload was verified before measurement. Evidence:
`artifacts/winui-shell/resource-lifetime-20260930/soak30m01/`.

User asked to stop memory testing unless a concrete defect was found. The timed run had already finished; no leak or cause has been established. Further memory tests/investigation are deferred. Keep the evidence below without restarting profiling automatically. Widget workflow qualification may continue.

Resource qualification is NOT closed. Whole-tree private median rose from539.08MiB
(minutes5–10) to583.93MiB (final five minutes), peak600.15MiB. Seven fixture workers
ended at287.79MiB combined, Bridge75.46MiB, frontend226.16MiB. Later five-minute total
medians576.81/578.93/583.59MiB show slower growth, not a flat plateau. Frontend native
samples: managed heap minima6.07/11.47/17.17/24.11/25.58/28.88MiB across the six full
windows, maximum63.50MiB, final43.90MiB. Final handles1470. Bounded control counts do
not prove retirement of every managed/native reference; investigate allocation/
retention before declaring leak-free. No forced GC was used.

The analysis helper preserves raw evidence and verifies matching process/duration;
its pilot replay passes. This workload covers synthetic switching/native eviction
and worker lifetime, not artwork-heavy grids, WebView/GPU memory, physical frame
pacing or deferred Playnite stutter. Subsequent reference C# build is now allowed.
Canonical candidate must be relaunched after those checks.

### Reference inventory audit — 2026-09-30, source preparation during soak

Revalidated live soak process25612/session62826; no restart or build during the run.
Read-only parallel inventory audit confirmed16 enabled canonical widgets and corrected
stale Clock/Full Application workflow descriptions. Clock has manual Refresh/X and
no settings/timer; Full Application is the sandboxed10,000-record indexed reference,
not a full-trust declaration or a dialog/settings sample. Updated current package
versions and acceptance boundaries in winui-widget-lineup-progress.md.

Prepared production-shell reference validation for Clock X refresh, actual leased
rows0/75/9999 opening their own Details, Back restoring visible keys, content-refresh
lease replacement, native-cache eviction/reentry and hide/reopen. No system/provider
operation or credential use. Added isolated runner; PowerShell syntax parses cleanly.
C# source is intentionally unbuilt while the long soak owns the frozen payload. Added persisted-evidence analysis in Summarize-WinUiResourceSoak.ps1; validated against a copied pilot report, including short-run empty warm windows, per-process aggregation and native managed-heap/cache counters.
After soak completion, analyze process-tree evidence, build/run reference checks,
then relaunch the canonical candidate. Do not treat this source-only gate as passing.

### WebView process recovery — 2026-09-30, validated

Previous goal turn completed independent pin appearance and concrete native gates.
Closed candidate51800 normally. Current source mapped every WebView2 ProcessFailed
event to controller retirement. The shipped Microsoft.Web.WebView2 1.0.4078.44 API
XML states GPU/utility/sandbox-helper/PPAPI subprocess exits recover automatically
or are nonfatal. The original native renderer also faults all kinds; this is an
improvement using WebView2's supported lifetime behavior, not copied renderer logic.

New opt-in isolated native probe terminates only the GPU subprocess identified by
its own CoreWebView2Environment process inventory. Baseline-native01 proves a genuine
GpuProcessExited notification incorrectly destroys the admitted surface. Handler now
retains only the documented recoverable kinds and records kind/reason/exit code;
main/frame renderer, browser, unresponsive and unknown failures still fault. Existing
transport timeout and action authority remain unchanged. No process descriptions,
frame URLs or media titles are logged.

Added actual browser-process termination while fullscreen and explicit document
replacement recovery to the same opt-in gate. Standard media tests do not inject
process failures unless ProcessFailures is requested. Tests are local sealed assets;
no user browser, Windows GPU driver, real provider or machine setting is touched.
Evidence: `artifacts/winui-shell/media-process-20260930/`. Baseline analyzer passes;
corrected native01/native02 each pass139 checks (nine new), including actual GPU
and browser-process exits, fullscreen recovery ordering, one fresh controller on
explicit replacement, resident cap and early-retirement drains. Captured recovered
GPU pixels inspected; normal popup/fullscreen pixel captures remain passing. The
production handler emits process-kind/reason/exit-code diagnostics; the original
broad generic error remains for genuinely fatal cases. A separate runner/frontend
exclusivity guard prevents process-failure injection alongside another candidate.
Final analyzer frontend02 and trimmed/ReadyToRun Release01 pass without warnings
or errors. All native fixtures closed normally. Candidate62160 relaunched using the canonical physical profile/catalog/installation; all244 deployed DLLs match frontend02. Left running for physical testing; migration remains active and incomplete.
This does not claim hardware device-removal or real-provider failure acceptance.


### Pinned display appearance — 2026-09-30, validated

Previous batch made verified typography/keyboard progress; candidate67436 closed
normally for this follow-on. Confirmed main-window display override leaked into
pin appearance. Existing WindowDisplayContext/DisplayScaleIdentity read-only helpers
can resolve a peer monitor without changing broker Settings display context, so no
new protocol or broker path was needed. Hidden peer placement resolves sizing before
show; current-owner and request-revision checks reject stale asynchronous refresh.

Further confirmed source defect: computed TextScale was process-global. Applying a
pin's independent appearance would overwrite main-widget/native chrome typography.
Native text scale now inherits a weak presenter-scoped policy; chrome adapters own
their local scale. Nested/indexed text inherits through the native tree; recycling
and disposal release subscriptions. Accessibility/motion preferences remain shared
because those settings are global. Ordinary/compact pins own separate popup themes.

Analyzer frontend01 passes. Added native scope/isolation checks and production pin
checks for independent synthetic monitor IDs, defaults, late responses, unchanged
Settings context/passive ownership and popup theme. Evidence root:
`artifacts/winui-shell/pin-display-20260930/`. Final analyzer frontend03 and trimmed/
ReadyToRun Release01 pass. Native styles03 passes460 (seven new scope assertions),
production pin native01 passes41 (eight new), Settings appearance34 passes, and
native embedded-media130 passes with popup/fullscreen pixels inspected. The actual
sealed local sample passes33 complete workflow checks, with passive/interactive/
passive-return pin screenshots inspected. No remote playback or Windows settings,
device or game actions. Sample playback uses only its packaged synthetic videos.

Styles01/02 stopped on fixture IsLoaded waits for offscreen native elements. The
final probe places its specimens in the viewport and also proves inheritance before
layout via the logical parent chain; production scope lookup handles both logical
and visual ancestry. No assertion was waived. Independent monitor IDs are synthetic;
physical mixed-monitor/DPI acceptance remains for the user. Candidate51800 relaunched with the canonical physical profile/catalog/installation; all244 deployed DLLs verified against frontend03. Candidate left running; migration remains active and incomplete.


### Shared typography and text-entry scaling — 2026-09-30, validated

Previous batch resolved indexed query-reset ownership and relaunched candidate67104.
Closed it normally for this batch. Read-only contract audit confirmed two host gaps:
multiline labels on Button/Select/TextEntry mapped text-align only to the outer
Control, while text-entry ContentDialog lives outside OverlayScaleRoot and ignored
InterfaceScale. Widget styles and saved values were not the cause.

Shared WidgetTextStyleAdapter now owns reversible TextBlock alignment for both
ordinary text and control labels, using the existing logical RTL mapping. Parallel
agent added28 native style assertions for state changes, removal and descendants.
Text-entry popup now scales native key/editor/title typography, glyphs, bounds and
spacing once; themes and accessibility still supply fonts, weight and colors. Existing
controls, edit buffer and focus survive live changes. Uses native vertical scrolling
when enlarged text cannot fit the window; no transform or second keyboard renderer.

Evidence: `artifacts/winui-shell/popup-typography-20260930/`. Analyzer frontend03
passes. Text-entry03 passes70 native checks (15 new), with actual popup raster captures
at0.5/1/1.25 interface zoom and maximum text scale. Two initial fixture assumptions
were corrected: BoldText resolves to minimum600, and default settings enable it.
No production relaxation was made for those checks. Final analyzer frontend05 and
trimmed/ReadyToRun Release01 pass. Native styles03 passes453 (28 new) and Select01
passes36. Style01/02 rejected invalid fixture declarations (text-entry length128
instead of96; missing ActionSurfaceOrientation); corrected tests, no production
validation changes. Popup raster captures inspected. Candidate67436 relaunched
with the canonical physical profile/catalog/installation; all244 deployed DLLs
match frontend05. No system-setting or real-provider actions. Physical acceptance
remains separate; migration is active and incomplete.

Next confirmed source gap: pinned appearance currently uses the main window's
resolved display scale. A pin on another monitor should resolve its own display.
Do not reuse ResolveDisplayAsync unchanged: that request also changes the broker's
active main-display context used by Settings. Any fix needs read-only identity
resolution and stale-owner checks, plus synthetic mixed-display validation.


### Indexed entry ownership — 2026-09-30, validated

Closed candidate16584 normally for isolated synthetic-worker checks. Confirmed
host focus race: replacing ItemsSource while ListView retained native focus could
schedule first-row focus after the widget's authored deep-row entry had succeeded.
Trace proved both containers had correct native indices and current source-owned
slots; the later row0 transfer originated in native WinUI, not recycled row metadata.
A second path disabled a loading placeholder and cancelled entry during native fallback.

The logical collection now temporarily holds focus outside ListView during query
replacement when a matching authored indexed entry exists. WinUI no longer schedules
its own reset focus over that request. Coalesced navigation commits focus on the
low-priority dispatcher after realization/ScrollIntoView callbacks unwind. Pending
entry survives only same-root automatic disabled-control fallback; explicit focus
moves, navigation, query/scope changes, hiding and authority withdrawal still cancel.
No timer-based focus fight, extra provider cache or widget-specific workaround.

Evidence: `artifacts/winui-shell/indexed-entry-owner-20260930/`. The focused baseline
and exact deferred-input reproduction failed before correction. Final-entry01 passes25
across six repeated resets with content refresh and deferred A, plus explicit departure.
Final-activation01 passes31, resolving the previous red activation gate: one-shot stale
A admission, all nine cancellation cases, ListView/GridView UIA and busy/selected state.
Final-widget01 passes21 top-level checks, including22 logical-entry,7 grouped-navigation,
3 scroll-focus and25 modal lifecycle assertions. Screenshot inspected (synthetic blue
fixture, backdrop visible through shell). Outer harness cleanup raced the inner script's
normal Shell.Close; controller log confirms normal disposal, no crash event, no leftover
frontend or synthetic broker. This cleanup result does not invalidate the saved assertions.

Analyzer final01 Debug and trimmed/ReadyToRun release01 publish pass without warnings
or errors. Review kept stack diagnostics gated to the focused test command; Validation
files/diagnostics are excluded from Release. No physical input, real provider actions,
system-setting mutations or installer work. Playnite scrolling optimization stays deferred.
Canonical candidate67104 relaunched with the existing physical profile, catalog and installation. All244 deployed DLLs match final01; candidate left running. Physical acceptance remains pending; migration is active and incomplete.

### Settings appearance workflow — 2026-09-30, validated and relaunched

Previous goal turn made concrete authoring-preflight progress. Revalidated
candidate48948, then closed it normally for independent Settings workflow checks.
Added an isolated production Settings UI/broker/store test using native UIA actions.
It guards SettingsRoot beneath the result directory, never routes physical input,
and restores its own profile. Native platform activation is used only for real
foreground acquisition; no driver/exclusive/power/network/startup actions run.

Native03 passes34 checks spanning actual interface/text steppers, typography,
backdrop, switcher, native alignment for all three positions, every exposed focus/
section/dialog preset, motion toggles/speed, accessibility preferences and persistence
across hide/reopen. Controller preferences stay unchanged; startup remains omitted.
Native01/02 were fixture popup readiness/no-op-selection failures. Native03 establishes
native opener/menu focus and requires changed values plus selected-state publication.
No production setting defect was confirmed or changed. Analyzer frontend03 passes.
Evidence: `artifacts/winui-shell/settings-appearance-20260930/`; reproducible runner
`scripts/Test-WinUiAppearanceSettings.ps1`. Production Release code is unchanged
from the prior successful publish; all new C# is under excluded Validation files.
Physical visual acceptance and the earlier query-reset regression remain open.

Candidate16584 relaunched with the canonical physical profile/installation/catalog.
All244 deployed DLLs match frontend03. This batch adds verification and corrects
the stale host-controls inventory; it does not claim a new production Settings fix.

### Authoring preflight — 2026-09-30, candidate relaunched

Previous goal turn was concrete progress (artwork/state mapping and validation),
with the indexed query-reset probe explicitly unresolved. Continued independent
authoring work; candidate55252 was closed normally for native checks.

Moved the frontend's existing unsupported-declaration rules into one shared
WinUiPresentationContract. It reports the exact field/path, safe node ID and native
replacement guidance, without logging authored text/action values or artwork URLs.
Plan builds structural paths only when rejecting a node. Rejection still happens
before native controls/focus are changed; supported kinds/declarations are unchanged.
Added WidgetTestHost.ValidateWinUiPresentation for protocol + native admission on
main/pinned/focus trees, and ValidateSubtree for lazily acquired rows. Checks never
acquire provider ranges; bounded iteration caps depth, node count and diagnostics.
No version negotiation, additional rendering profile or compatibility engine added.

Evidence: `artifacts/winui-shell/author-preflight-20260930/`. SDK148 passes, including
four new preflight cases. Native425 passes, including precise error mapping, retained
controls/focus on rejection and subsequent valid recovery. Analyzer Debug frontend01
passes. Public API baseline regenerated (one additional SDK test helper; prior Grid
symbols preserved). Final focused preflight4 and actual lazy-declaration1 reruns
pass after adding virtual-window coverage and proving preflight performs no range
reads. Trimmed/ReadyToRun analyzer Release01 publish succeeds. Candidate48948 is
relaunched with the canonical profile/installation/catalog; all244 deployed DLLs
match frontend01. No provider/system-setting actions or installer work. Query-reset/
deferred-activation gate remains red from the previous batch; this work does not
claim to resolve or rerun it. Migration remains active and incomplete.

### Ordinary artwork and indexed state — 2026-09-30, candidate relaunched; query-reset gate open

Previous goal turn made verified progress: background style mapping, native dialog
keyboard/font fixes, resource observations and candidate10436 launch. Continued
against the current source. Candidate10436 was closed normally for native checks.

Ordinary images still lacked crop alignment/tint/scrim. WinUI Image itself uses an
internal centered ImageBrush (Microsoft imagebase.cpp); its public alignment only
positions the element. New WidgetArtworkView delegates intrinsic measure to native
Image off-tree and paints the same decoded source once through a native ImageBrush.
It preserves one named Image UIA peer and the existing decode/demand/fade ownership.
Tint/scrim move to one shared native overlay component used by both consumers.
No new provider cache, codec, managed frame timer or manual aspect-fit math.

Parallel accessibility audit confirmed indexed containers omitted authored
selected/busy ItemStatus and disabled busy rows, unlike ordinary controls. Shared
status formatting and busy-focus preservation are implemented; existing worker/lease
action admission remains authoritative. SelectionMode.None is intentional for
command rows; the SDK does not declare an editable native selection model.
Native styles01 passes420 (including23 new ordinary artwork checks), Select01
passes36 with7 additional native Toggle-provider checks. Frontend02/03/04 and
fixture-worker build pass; frontend01 rejected a nullable test provider, corrected.
Indexed activation01/02 fail before the new cases: repeated query reset moves focus
from the native list through an unbound disabled placeholder to Shell.Close,
cancelling authored row75 entry. Trace under indexed02 confirms it. A narrow native
GettingFocus placeholder redirection changed fallback to row0 but did not restore75
(indexed03), so that unproven correction was removed. Kept diagnostic-only tracing.
The UI-testing skill's two correction-cycle limit stops that repeated probe; full
query-reset/deferred-activation regression remains red and is not waived as passing.
Standalone accessibility checks establish native focus explicitly so busy/status/
provider behavior can be checked independently; they do not qualify query reset.
Busy-row guide readiness now agrees with actual native availability; command
admission still rejects busy actions. Final native styles02 passes420, Select01
passes36, standalone state01 passes20, pinned01 passes19, surface retention passes57.
Analyzer Debug frontend05 and trimmed/ReadyToRun Release01 succeed. No passing
claim is made for the full indexed query-reset/activation suite. Physical testing
and Playnite fast-scroll optimization remain deferred. Evidence root is
`artifacts/winui-shell/artwork-accessibility-20260930/`.

Candidate55252 relaunched through project-mode winapp using the canonical physical
profile and existing installation/catalog. All244 deployed DLLs match frontend05.
No real provider/system-setting actions, driver changes, source commit/merge/push,
or installer work. Next work must preserve the unresolved indexed query-reset
evidence, not misread standalone native-focus setup as restoration acceptance.

### Away continuation — artwork styles and native controls, 2026-09-30

User authorized continuation while away. Closed candidate8540 normally. Scope is
background artwork style parity, native popup keyboard/font behavior, and a bounded
synthetic switching/lifetime observation. No real provider commands, controller
isolation, system setting changes, CLI/installer work, or Playnite scrolling changes.

Confirmed missing host mapping: BackgroundSurface accepted object-position,
image-tint and scrim-color but only applied object-fit. Native ImageBrush alignment
now maps the nine supported WRSS positions on both crossfade layers. Separate native
paint layers apply tint and the original bottom-45-percent scrim underneath authored
content. Theme-only changes reuse decoded pixels; retained empty-source backgrounds
also receive current appearance. Ordinary Image object-position/tint remain open.

Native keyboard review found dialog-level Enter handling committed even when Cancel
was focused, and Space overrode native virtual-key activation. Enter now commits only
from the editor; focused buttons own Enter/Space. Removing a custom popup font now
clears its local value on retained menu items/dialogs. No controller routing changes.

Analyzer Debug frontend04 and trimmed/ReadyToRun Release01 pass. Frontend01 rejected missing required style maps
in new control tests; corrected. Native styles01 rejected the new background fixture's
missing foreground child before rendering; corrected. Styles02 then rejected an
ImageFit left on a source-less retention fixture; corrected. Styles03 passes397,
Select29 and TextEntry55 pass. Five additional genuine SendInput checks passed on
the isolated text-entry preview (Enter on Clear/Cancel/editor, Space on q/Space).
Evidence: `artifacts/winui-shell/away-polish-20260930/`. Optional switch soak
extends the existing isolated fixture with bounded cycles and frontend resource
samples; this is not real-provider/GPU/frame-pacing qualification. Soak01 passes45
checks, including240 switches and60 shell hide/reopen cycles, always3 native retained
surfaces. Soak02 passes45 checks with1000 switches and250 shell hide/reopen cycles
over129.25 seconds. Frontend private memory ranged143.75..157.29MiB, ended154.46MiB;
first/last sampled handles1400/1409 and managed heap9.51/11.34MiB. Counts stayed at3
retained/native surfaces with no recovery or abandoned preparation. No forced GC.
This supports bounded behavior in this synthetic workload, not real media/grid or
whole-process-tree memory qualification. Surface-retention regression passes57.

Candidate10436 relaunched with canonical physical-options.json, same installation,
catalog/profile/controller settings. All244 deployed DLLs match qualified frontend04.
Settings reached input-ready and catalog completed. Remaining physical workflows,
ordinary-image placement/overlay style mappings, selection accessibility and longer
real-provider resource qualification remain open. No merge/push/installer work.

### Exclusive control restoration — 2026-09-30, candidate relaunched

User reversed the earlier deferral: restore Exclusive control in Settings and wire
original behavior; candidate65744 may be closed. Current physical preference is
false, verified read-only. Do not enable real hiding/routing or mutate drivers during
automated checks; leave physical game/controller acceptance for the user.

Original native implementation still exists in OverlayPlatformInterop ABI6:
ControllerPrerequisites, ControllerControlState, SetExclusiveControl. It owns
HidHide/ViGEm routing, selected physical controller, neutral handoff, cleanup and
recovery. ViGEm client statically linked; exports already present in staged native
DLL. WinUI lacked bindings, bridge controller-control exchange, and feature opt-in.

Source adds those bindings and a serialized status/report/preference/apply loop,
advertised only with a connected input consumer. Read-only driver probes run off UI
thread; explicit native reconfiguration stays serialized with native input. Each
revision applies once, including explicit retries and reset-to-zero. Initial Off
still runs native owned-policy recovery, as Off does not prove no crash journal.
Expected native setup failure keeps
ordinary input alive; recovery-required keeps owner/F1/Settings recovery reachable.
PrepareVisible containment failure cancels opening rather than destroying the input
owner. Shutdown releases visible input before worker cleanup. No C++ routing rewrite.

Session exchange validates exact wire shape, uses generated JSON metadata for Release,
and keeps ordered transport admission through cancellation. Full session suite301
passes (`artifacts/winui-shell/exclusive-control-20260930/session01.binlog`). Boundary,
Settings and fake-native production integration checks subsequently passed below. Feature reads
and native prerequisites are not proof of actual game-input containment.

Final integration also loads saved controller intent on resident startup, independently
of the lazy widget service. First broker status awaits that startup preference so an
older file read cannot overwrite a newer exchange. Hidden startup honors the saved
exclusive/shortcut choices without starting widgets. Initial Off executes native
owned-policy recovery; native foreground acquisition remains available during recovery.

Validation: session301, platform boundary/preference47, Settings77, broker controller
gating1, and fake-native production25 checks pass. Platform01 build rejected one
constant-only test assertion (removed); platform02/03 pass. Frontend02 analyzer Debug
and trimmed/ReadyToRun Release01 succeed, zero reported build warnings/errors.
Native02 drives actual Settings controls + real broker + isolated settings profile;
only the controller adapter is fake. Covers prerequisite gating, enable/disable,
revision deduplication/reset/retry, Failed/RecoveryRequired, recovery polling, native
open failure handling and hidden saved-intent initialization. No real exclusive enable,
driver installation or live game/controller routing test was performed.

Candidate8540 relaunched using exclusive-control-20260930/installation and canonical
physical profile. All244 deployed frontend DLLs and48 Bridge files verified. Actual
startup off receipt accepted; read-only Controllers page shows Off, enabled toggle,
HidHide ready and ViGEmBus ready. Preserved user's false preference/revision0 and
Guide shortcut; candidate remains running for physical testing. Controls were inspected
on Controllers; physical-controllers.png subsequently captured Settings Home, so the
current page is not asserted. No merge/push/installer work. Physical containment and
gameplay return remain for user acceptance.

### Fullscreen return geometry — 2026-09-30, candidate relaunched

User reported ordinary widget flashing across ultrawide display when closing
embedded fullscreen. Authorized closure of candidate10036; closed normally.
Confirmed cause: SurfaceHints replaced the ordinary widget's authored hints with
FillAvailable during fullscreen even though WinUI already has a separate fullscreen
layer in its work-area HWND. Exit revealed ProductionLayout before queued shell
placement restored normal bounds, allowing an oversized widget frame/resize.

Removed that fullscreen override from ordinary sizing. The aspect-fit video retains
its independent fullscreen layer; entry/exit settles any ordinary resize, arranges
the destination before the owner reparents the existing WebView, and restores normal
widget geometry before revealing it. No new player/document, no extra timers or
cosmetic animation masking the layout error.

Evidence: `artifacts/winui-shell/fullscreen-return-20260930/`. Added production
sample assertions for retained dimensions during fullscreen, no oversized reveal,
and no ordinary widget-resize animation on fullscreen handoff. Baseline-native01
reproduced the dimension violation before production edits. Fixed native01 passes33
actual local-video checks, including playback, seek, fullscreen, pinning, input and
browser retention; compact captures and restored sample screenshot saved/inspected.
Analyzer-enabled frontend01 builds clean. All actions use the sealed sample and an
isolated profile; no real provider playback or system changes. Release check and
canonical candidate relaunch pending; physical smoothness still needs user review.

Final: analyzer-enabled trimmed/ReadyToRun release01 succeeds. Candidate65744
relaunched with canonical profile and controller adapter. All243 deployed DLLs
match frontend01. No package/provider changes, main integration or deployment work.

### Global F1 fallback — 2026-09-30, candidate relaunched

User reported original F1 shortcut missing; authorized closing candidate5164,
closed normally. Confirmed native main.cpp registers unmodified VK_F1 with
MOD_NOREPEAT and routes WM_HOTKEY to ToggleOverlay. WinUI had no production global
keyboard registration. New window-lifetime OverlayKeyboardShortcut uses existing
WinUIEx message monitoring plus RegisterHotKey/UnregisterHotKey. It calls the shared
Guide toggle/animation path independently of controller shortcut settings. Normal
cleanup releases registration before destroying the window; another application's
F1 ownership is logged without replacing widget UI or failing controller opening.
Keyboard-only fallback can also toggle when the controller pump is unavailable.

Evidence: `artifacts/winui-shell/f1-shortcut-20260930/`. Analyzer Debug frontend01
build clean. Native01 passes8 checks using genuine SendInput/Windows delivery of
registered F1, not only a posted WM_HOTKEY: hide, hidden reopen/foreground admission,
MOD_NOREPEAT held-key suppression, unrelated-message rejection, unregister/reclaim,
and fresh registration. Isolated synthetic widget/profile; no widget commands or
system-setting mutations. Release check/canonical candidate relaunch pending.

Final: analyzer-enabled trimmed/ReadyToRun release01 succeeds. Physical candidate
PID10036 relaunched with the canonical profile/controller settings unchanged;243
deployed DLLs match frontend01. No F1 registration error logged for the candidate.
Global F1 remains registered while the overlay is hidden and is independent of
Guide versus View+Menu selection. User physical acceptance remains separate.

### Overlay entrance/exit migration — 2026-09-30, candidate relaunched

User requested original overlay show/hide animation; authorized candidate closure.
Closed54380 normally. Original OverlayTransition/OverlayEntrance/CommitShellZoom:
140ms cubic ease-out opening,100ms cubic ease-in closing, opacity0..1 and scale
.88..1 about bottom/position anchor; independent backdrop fade and rapid reversal.
WinUI previously used immediate AppWindow.Show/Hide with no shell animation.

New source reuses WidgetCompositionMotion for shell/scrim in one compositor batch,
preserves the original recipe and honors shared animation speed/reduced/system
motion. Startup and reopen prime hidden pixels before reveal after native Loaded/
layout; rapid reversal supersedes old hide completion. Logical visibility/input
retire immediately; a bounded exit presentation retains artwork/previews/fullscreen
media until physical hide. No additional foreground restoration or keep-open latch.
Task activation/app-launch effects and external foreground dismissal hide immediately
to avoid delaying the accepted foreground handoff. Native/pure regressions being
added; candidate will be relaunched after validation.

Evidence: `artifacts/winui-shell/overlay-motion-20260930/`. Policy suite15 passes;
analyzer-enabled Debug frontend02 succeeds with zero warnings/errors (frontend01
had a fixture-only wrong namespace on EmbeddedMediaHostCommand, corrected).
Production native motion16, hidden-startup7, shared compositor23 and pinned33 pass.
Embedded media130 passes with pixel captures: fullscreen remains parented during
retained exit with transport input revoked; actual hide parks the same browser.
These tests use isolated profiles/fixtures, no real playback or system changes.
Reversal test verifies native animation ownership/endpoints, not pixel continuity;
subjective feel remains for physical acceptance. Source uses one scoped compositor
batch and no managed frame timer. Confirmed startup/readiness and repeated Show
idempotency; stale exit completions cannot hide a newer show. Release publish and
canonical physical candidate relaunch are the final pending steps.

Final: analyzer-enabled trimmed/ReadyToRun release01 succeeded. Candidate5164
relaunched with canonical physical-options.json, unchanged profile and bridge
installation. All243 deployed frontend DLLs match frontend02. Initial Settings
input-ready and catalog-complete logged; no new overlay-motion errors. Settled
candidate capture saved; physical animation feel remains for the user. No merge,
push, system-setting changes or real provider commands.

### Away continuation — 2026-09-30, validation in progress

Resumed the follow-on pass from the recorded state. Closed owned pin fixture26472
normally; no physical candidate currently running. Evidence root remains
`artifacts/winui-shell/followon-20260930/`. Frontend02 clean; full native styles361
passed; managed session287 and broker Worker run3/Embedded media3 passed before
this continuation. Fresh broker installation exists but is not yet the physical
candidate. Do not count pending tests as passed.

Pin01 passed all10 new operation-ownership assertions, then timed out in passive
focus validation. Fixture now awaits actual menu focus/Closed notifications rather
than racing popup teardown against pin activation; timeout diagnostics identify
the precise pin phase/state. No speculative weakening of production entry guards.
Surface01 stopped on its old requirement to restart pending artwork for every
snapshot. Fixture now verifies the intended compatible-snapshot retention and
retains stale-admission recovery and late-result rejection checks.

Further confirmed fixes being qualified: media dispatch holds existing registration
lifetime across the lookup/gate-wait/disposal race; ordinary input waits are being
audited for the same pattern. Authored selected buttons now expose native ItemStatus
alongside Busy, preserving Invoke rather than introducing toggle behavior. Native
UIA checks cover selection, clearing it, and retained-button status updates.
Computed image-fit mapping is in progress; arbitrary object-position/image-tint
remain explicitly open. Scrolling optimization remains deferred. No system-setting
actions, real playback commands, game launch, main integration, or installer work.

Continuation results: analyzer-enabled frontend04 builds with zero warnings/errors
(frontend03 rejected an IReadOnlyList collection-expression in the new fixture;
changed to explicit ViewNode[]). Native styles02 passes381 including flow layout,
computed image fit and selected/busy UIA status. Surface02 passes57; pin02 passes33
including all10 ownership guards and normal focus/move/resize/opacity behavior.
Pin01's failure was asynchronous popup teardown in the fixture; awaiting native
Closed/focus fixed it without changing production admission. Surface01's fixture
was updated for intentional compatible-snapshot artwork retention.

Session suite287 passes; Worker run5, Embedded media3, Dashboard2 broker scenarios
pass. Pinned broker suite initially5/6: automatic refresh superseded the fixture's
pre-action RefreshAsync authority. Test now subscribes before dispatch and awaits
the expected PresentationChanged result; rerun6/6 passes. No production guard was
weakened. Ordinary/action/controller/pinned dispatch now shares a bounded retained
registration lifetime; indexed operations already held theirs. Bridge publish02
is staged in followon installation/runtime/Bridge. Native media/switch qualification
and physical relaunch remain pending at this checkpoint.

Computed object-fit now overrides declared fit on ordinary native Image and existing
background ImageBrush crossfade. Appearance changes use the same resolver, preserve
sufficient decoded pixels, and restore declaration fallback when style is removed.
Explicit none preserves intrinsic decode dimensions within existing source bounds.
Arbitrary object-position and image tint remain open; no custom image renderer was
added. UIA tests establish readable ItemStatus and retained Invoke semantics, not
screen-reader announcements or SelectionItem pattern parity.

Batch complete: media01 passes129 with popup/fullscreen pixel captures (inspected);
switch01 passes44. Analyzer-enabled trimmed/ReadyToRun release01 publish succeeds.
All native fixtures shut down normally. Canonical physical-options.json now points
to followon-20260930/installation; profile/catalog/opening shortcut unchanged, with
the previous configuration preserved in physical-options-before.json. Candidate
PID54380 relaunched through project-mode winapp with controller adapter. All243
deployed frontend DLLs match frontend04. Real-provider/controller acceptance remains
for the user. No source commit/merge/push or installer work in this batch.

Next bounded migration work: remaining authored image position/tint and accepted
style mappings; full selected-item accessibility semantics where a real selection
container exists; isolated lifetime/memory plateau qualification. Confirm authoring
contract/documentation against these supported mappings before release. Playnite
fast-scrolling stutter stays deferred at the user's request. CLI/installer/deployment
remains later than widget parity and physical workflow acceptance.

### Follow-on correctness and layout pass — 2026-09-30, source work underway

After stale-guard candidate43960, the user reset Settings and restarted the app;
current physical process is57628 / broker22492 (read-only verified04:14 local).
User reported inability to reopen; logs showed ViewMenu configured, responsive
process and no Guide toggle. User confirmed forgotten reset of opening shortcut;
View+Menu accepted and app activation brought the existing57628 forward. No new
foreground/input fix or setting change was made. User said continue prior work.
Latest trimmed/ReadyToRun analyzer Release publish also passes (release01.binlog).

New bounded source findings assigned in parallel, not yet staged:
- media_snapshot_fix: qualify pin/tray async failure ownership; recheck pin after
  awaited lifecycle before enabling input; prevent old menu failure closing new
  menu; retain diagnostic traces for retired-owner genuine errors.
- indexed_stale_fix: media observations must not EnsureConnected/restart workers.
  Add exact nonstarting worker send, current snapshot-worker admission and managed
  WorkerRun receipt; reset command epoch on worker change only, never merely on
  changed resources with same pending command.
- artwork_snapshot_fix: ordinary Scroll ignores authored gap; accepted
  space-around falls through to start in both ordinary and focus-fragment layout.
  Use native StackPanel/Grid, preserve additive gap and shared track rules; add
  focused native layout regressions.
- root: shared owner-qualified failure logging (keep genuine retired errors in
  diagnostics), distinguish malformed ordinary action/controller payloads from
  recoverable stale semantics, deterministic tests. Source edits only so far.

Further audit found object-fit/object-position/image-tint and authored selected
button UIA state mapping gaps; not implemented in this pass yet. No confirmed new
collection/preview leak found; sustained fixture-only memory plateau remains an
unmeasured qualification item. Playnite fast-scroll investigation stays deferred.
Keep physical candidate available during source work; coordinate native validation
through root only. No live provider/system-setting mutations.

### Snapshot-race parity corrections — 2026-09-30, candidate relaunched

User authorized fixes and closing candidate63108; closed normally. Parallel lanes
implemented media origin/retirement, ordinary artwork lifetime, and typed indexed
input retirement. Shared WidgetInputFailure now classifies expected stale outcomes;
controller/pinned/action/context-menu/select/text/indexed callers use it. Recovery
requests coalesce with invalidation refresh, once per genuine displayed frame,
without replaying actions. Shell action/lifecycle/retry failures now retain their
selection/visible-session/surface owner and cannot replace a newer page with
recovery. Existing substantive runtime/protocol failures remain errors.

Ordinary artwork tracks continuously declared handles by owner and rejects removal/
reappearance, forged origins and replaced worker runs. Compatible UI snapshots no
longer restart unfinished ordinary requests. Media retains exact command origin,
uses typed expected command/document supersession, and updates host playback state
only after acceptance with monotonic observation ordering. Duplicate terminals,
future origins, wrong media keys/preferences and malformed requests remain faults.
Indexed lease expiry/changed binding has a typed wire outcome; malformed input,
foreign items/scopes, admission saturation and worker failures remain errors.

Evidence: `artifacts/winui-shell/stale-guards-20260930/`. Full managed session
suite283 passed; indexed broker19 passed; focused media broker3 passed. Frontend02
and bridge builds/publish clean. Native switch01 passed new ownership guards but
stopped at the repeated radial dashboard shortcut: old fixture emitted Pressed
without Released, leaving shell-owned release tracking set. Corrected fixture to
emit complete B/X gestures. Second run exposed an additional genuine omission:
dashboard routing required reference-equal snapshots across lifecycle admission,
and the broker required an exact snapshot despite an unchanged shortcut. New
SendDashboardInputAsync compares displayed/current action, repeat and capability
binding; broker revalidates the retained origin under its operation gate before
dispatching once. No replay, changed command, or changed capability is admitted.
Added dashboard session2 and broker1 regressions;34 displayed-input cases pass.

Final frontend04 and bridge/publish04 clean. Switch03 passes44 native checks,
including late failure ownership and rail/radial shortcuts. Select27, text-entry38,
slider67, context-menu62 pass. Embedded-media native129 and popup/fullscreen pixel
checks pass in media-native02; first run capture was blocked by external foreground
and is not counted. A reviewed attempt to reset broker command authority on changed
resources failed the existing anti-relabeling test and was removed before final
publish; all3 media broker scenarios pass with resource identity protections kept.
Candidate43960 relaunched with original profile; all243 deployed frontend DLLs
verified and staged bridge/contracts hashes match publication. Physical acceptance
remains separate. No game launching or system-setting actions.

User then expanded active goal: continue migration autonomously while away, favor
WinUI-native design, preserve all current features and authoring quality, use
subagents where useful, no live system-setting actions, physical checks deferred.
Continue independent qualification/audits while leaving this candidate available.

### Snapshot-race parity audit — 2026-09-30 (source review, no production edits)

User asked to compare original guards with WinUI and identify omissions. Candidate
63108 left running; no widget actions, restart, native tests or system changes.
The original host does distinguish expected stale results from runtime failure:
WidgetSessionCoordinator.cpp:642 drops superseded completions; main.cpp:15678
drops stale controller input without toast/fallback/replay and requests a refresh;
main.cpp:3713 uses retained command-origin snapshot for media completion.

Guards already present in managed/WinUI paths: published-frame provenance plus
origin/current semantic binding comparison (WidgetPresentationSession.OrdinaryInput),
exact worker-run admission, pinned selection/projection identity, retained indexed
leases/query ownership, slider local-intent generations/settle/echo protection,
scope/element/query-qualified focus memory, and atomic compare-and-commit for
session refresh/runtime failures (WidgetPresentationSession.cs:1016). Full snapshot
checkpoints are currently requested, so absence of the native delta-base recovery
retry is not itself a gap in the active managed refresh path.

Concrete inconsistencies needing targeted follow-up, not yet fixed by this audit:

1. Shell direct action path catches local stale codes but omits broker
   stale_controller_input_authority (OverlayShellPage.xaml.cs:608-631), although
   presenter IsRetiredInput includes it (WidgetViewPresenter.Binding.cs:27).
   A worker retiring after local validation can thus show whole-widget recovery.
   Context menus have a separate incomplete catch list (ContextMenus.cs:202).
2. Indexed broker input uses generic BridgeProtocolException for expired origin,
   changed binding and retired authority (BridgeClientRegistry.IndexedLeases.cs:
   280/291/307); CreateRequestFailure maps these to request_failed, which the native
   WinUI indexed catch does not classify as expected staleness (collection.cs:332).
   A client lease can still appear current while the broker has advanced. Need a
   typed stale outcome without suppressing malformed-input/protocol errors.
3. Main controller stale catch consumes input but schedules no explicit refresh
   (WidgetViewPresenter.Input.cs:64). Native host refreshes without replay. Managed
   invalidation may catch up, but equivalent self-recovery is missing from this path.
4. Shell-level async action/lifecycle failures call global ReportFailure without
   captured widget/selection revalidation (OverlayShellPage.xaml.cs:540/631/655).
   Lower session compare-and-commit and SelectAsync's version guard exist, but do
   not cover every direct exception callback. An obsolete operation must not put
   the newly displayed widget into recovery.
5. Ordinary unfinished artwork remains exact-snapshot-bound at admission and
   completion (session.cs:403/934); presenter restarts it on snapshot advance
   (Resources.cs:94). Native TrustedArtworkDemandAuthority uses widget/runtime/
   presentation identity, not snapshot sequence (RemoteImageCache.h:53). This is
   excess cancellation/reloading, normally not a whole-widget error because the
   resource boundary already contains expected stale/unavailable artwork.
6. Media command origins still differ: managed sends latest received frame
   (EmbeddedMedia.cs:129), while original retains command origin. Recent bridge
   fix accepts retained compatible intermediate origins, but long-lived command
   origin parity remains incomplete. Supersession racing queued media events can
   also still reach catch-all player Fault (EmbeddedMediaSurface.cs:360). Separate
   expected retired/superseded events from actual adapter/protocol failures.

These are source-confirmed differences/reachable error paths, not claims that each
has been reproduced in physical testing. Next corrections should consolidate typed
stale handling and bounded refresh, retain stable media origins, qualify shell
failure publication, then remove ordinary-artwork snapshot coupling. Deterministic
barriers should cover replacement during dispatch, supersession while queued,
selection changes before error completion, and cosmetic updates during artwork
resolution. Preserve rejection of replaced workers, scopes, actions and item keys;
never replay an old user action against the new snapshot.

### Games & Apps cold activation and media completion race — 2026-09-30

User physically accepted broker-owned Task Switcher completion, including the GoW
issue after task switching. Candidate13384 then reported Games & Apps recovery UI.
At10:26:49.965 visible lifecycle began, worker66756 started10:26:50.020, then exited1
and lifecycle failed with request-timeout at10:26:52.158. The next start64896 at
10:26:56.149 succeeded. Games & Apps OnActivated awaited private-state reading and
saved-app resolution inside the two-second lifecycle acknowledgement. Move cold
restore followed by catalog reconciliation into the existing Active RunLatest
operation. Warm in-memory reentry is still immediate. The normal delayed loading
state now covers restoration as well; cancellation is checked after each provider
await to discard late results before publishing saved rows. No timeout increase.

User then reported YouTube Video media stopped unexpectedly. Actual failure at
10:31:32 preceded authorized shutdown10:32:19: broker rejected media event authority,
which became media-observation-failed in the frontend. A deterministic regression
reproduced the identical broker rejection for command completion from an
intermediate UI snapshot. Broker now accepts retained intermediate origins only
within the uninterrupted matching command/resource authority, preserving one-shot
terminal admission and rejection of replaced commands. Three focused media bridge
scenarios pass; physical playback acceptance remains pending. No external media
playback or system-setting actions automated.

Evidence: `artifacts/winui-shell/games-startup-20260930/`. Games suite first run:
93 passed,1 stale exact-protocol-version assertion (expected60, valid current
snapshot64); updated it to require indexed minimum plus existing full snapshot
validation. Final Games suite:94 passed,0 failed; both new startup/cancellation
regressions passed. Read-only real-provider startup host9892 / worker14748 completed
lifecycle in547ms (including process startup), then published the loaded indexed
library with valid focus. No application launch or library mutation was invoked.
Native startup host closed and canonical candidate63108 relaunched with original
profile and new immutable installation. All243 deployed frontend DLLs verified;
staged GamesAppsWidget/WidgetBridge hashes match qualified publication. Fresh Games
package resealed; previous installations preserved. Physical acceptance pending.

### Broker-owned Task Switcher completion — 2026-09-30, candidate relaunched

User authorized the targeted correction, focused checks, and relaunch. Broader
activation-order experiments remain paused. Prior evidence showed successful
foreground delegation to the broker followed by denied activation in the frontend.
Final activation now executes in the delegated broker after frontend validation
and hide. Completion carries only widget/runtime/effect sequence; the broker owns
the target in a bounded, two-second, single-use ticket. It revalidates window
identity and exact worker registration/start, rejects stale/replayed tickets, and
reports broker PID/native outcome to the existing activation diagnostics.

Shared Win32 identity/activation code moved to WindowsWindowActivation (net8), used
by net10 frontend and net8 broker. Hiding intentionally changes widget lifecycle,
so ticket completion has separate exact-worker admission that permits Background;
new effect publication still requires Interactive. No previous-window restoration,
input-starved stay-open latch, toast changes, elevation, or game automation added.

Evidence: `artifacts/winui-shell/broker-task-activation-20260930/`.
Managed activation tests:13 passed,0 failed,0 skipped. Broker ticket/classification
and lifecycle/worker-retirement scenarios:2 passed. Frontend build03 and broker
tests03 clean. Native fixture uses a disposable window and the real Task Switcher
action, then verifies target foreground and completion replay rejection. First
staging launch lacked the unchanged Settings icon integrity inventory; corrected
by preserving that metadata. Native02 reached startup but its TaskSwitcher worker
exited1 before activation, so it is not handoff evidence. The same staged files with
the existing physical profile passed native03:6 checks, including a real worker
action, frontend-owned foreground before handoff, hiding, exact target foreground
after200ms, and actual broker rejection of a replayed ticket. Copied-profile
startup failure was not reproduced in the physical profile and is not attributed
to activation. Native trace confirms broker3580 performed SetForegroundWindow;
target45580 owned foreground at both the150ms and final observations.

Independent code review found the lifecycle race corrected by completion admission
and no additional confirmed blocker. Native test host61500 and disposable peer45580
closed. Canonical candidate PID13384 / owned broker61004 relaunched, using the fresh
installation and original profile. All243 deployed frontend DLLs match the build;
bridge/contracts/shared-activation staged hashes match publication. No automated
game or system-setting actions were performed. Elevated-GoW acquisition denial
remains a separate measured limitation; this focused proof is not physical GoW
acceptance. Broader activation-order experiments remain paused.

### Restore broker foreground delegation; experiments paused — 2026-09-30

Latest user feedback: FF7 task switching now appears fixed. GoW initially allows
overlay/switching, but after returning to elevated GoW through Task Switcher,
opening over GoW fails and later switches from other apps fail. Read-only review of
candidate65416 / broker67072 confirms delegation is invoked for Task Switcher.
At09:53:58 it returns False/error5 despite overlay foreground; at09:54:47 it returns
True/error0, yet the frontend's subsequent SetForegroundWindow(Terminal) returns
False after hide. Thus successful broker delegation is not proof of successful
frontend activation. WindowsTaskWindowControl.PrepareSwitch only revalidates; the
final activation is executed in the frontend, not the broker receiving permission.
Over GoW(PID55868), direct SetForegroundWindow returns0 and AttachThreadInput fails
with ERROR_ACCESS_DENIED(5). Preserve these as distinct measured failure points.
Trace saved to `artifacts/winui-shell/broker-foreground-20260930/physical-gow-return.log`.
No code changes, tests, elevation changes or candidate restart for this review.

User explicitly stopped activation-order testing and requested restoring the
original broker foreground-permission handoff first. Test host15532 and both
disposable peer windows were closed; no further tests or widget actions run.

Native WidgetBridgeClient delegates AllowSetForegroundWindow to its live trusted
bridge before controller input/actions, gated on actual foreground process ownership.
The managed OwnedBridgeProcess path omitted this. It now installs an internal
BeforeInputWrite callback on its own presentation transport. Ordinary/pinned actions,
quick actions, controller input (including pin routing), and indexed input call it
after transport capacity/write admission and serialization, immediately before IPC.
Only the owned live bridge receives permission, and only while this frontend
process owns foreground. Disposal/live-process checks are serialized; denial or
expected process retirement leaves the admitted action's normal behavior intact.
Catalog, lifecycle, artwork and other background requests do not grant permission.
No worker privilege, SDK authoring, elevation, or activation-order change.

Delegation outcome/request type/broker PID/foreground PID/Win32 denial code are
recorded in bounded owner diagnostics and the existing controller activation log.
Build01 clean (zero warnings/errors), diff whitespace check passed. Evidence:
`artifacts/winui-shell/broker-foreground-20260930/`. No tests executed after the
user's stop request; physical game-launch/Task Switcher outcome remains unverified.
Candidate PID65416 relaunched with the canonical physical profile. Read-only payload
verification confirms242 deployed DLLs match the build. Left running for the user;
no automated input or test workflow performed on this candidate.

The paused harness/instrumentation work remains under Validation and
`artifacts/winui-shell/activation-orders-20260930/`. It instruments exact native
foreground/restore/attach calls, captures thread/HWND/menu/input-timestamp state,
and samples500ms after three isolated order variants. Earlier managed activation
tests11 passed before the stop request. Matrix01 used accidentally hidden helper
windows and must not be used as application evidence. Explicit visible peers were
added; API-only matrices02/03 reproduced a direct SetForegroundWindow denial but
later source setup failed, and matrix04 had no established trials. Pointer setup
uses a guarded click on a verified disposable peer only; matrix05's first baseline
succeeded but a later covered-window guard stopped it. Peers were separated for
matrix06; user stopped testing before further analysis/acceptance. Do not claim a
complete order comparison or production activation fix from those partial runs.

### Task activation depends on foreground/input history — 2026-09-30 investigation

User reproduced with FF7 Rebirth launched from Playnite: game opened in background,
overlay remained open, manual close exposed ChatGPT, subsequent task switching
failed; clicking FF7 before reopening restored successful switching. Candidate64320
logs confirm identical Chrome HWND460592/PID48352 denied at09:20:13 UTC after opening
over ChatGPT; succeeded at09:20:40 after opening over FF7(PID49032); also succeeded
at09:24:13 after opening over ChatGPT again. Thus app identity alone is insufficient.
All three owned foreground before hide; first activation attempt sampled no
foreground window, latter two sampled FF7/ChatGPT respectively. Evidence points to
foreground/input-history/handoff timing, not a proven elevation-only explanation.
Saved `artifacts/winui-shell/window-return-20260930/physical-ff7-comparison.log`.

Playnite still requests CloseOnConfirmedSuccess. Broker intentionally does not
publish close for RequestAccepted-only launch observations; the exact observation
for this reported launch is not established by current logs. Explicit remembered
window restoration remains reverted, so ChatGPT appearing after hide is currently
Windows fallback, not that removed return path. Toast behavior unchanged. No
production edits, game launch, activation attempts or candidate restart in review.

### Revert foreground-acquisition latch and explicit return — 2026-09-30

User rejected the last change: a window that stayed visible without foreground
was unreachable because input did not route to it. Removed the per-show acquisition
latch, its diagnostics field, and the added latch tests/native fixture segment.
Restored the previous valid/current/external foreground dismissal behavior, including
dismissing failed acquisition attempts. User also explicitly requested removal of
the remembered-window restoration fix. Removed its opening-time HWND/PID snapshot,
SetForegroundWindow call on close, special backdrop hide and separate handoff-close
route, plus restoration-only native checks. Closing again just hides the overlay;
there is no explicit activation of an older window. Task Switcher activation
diagnostics remain; its actual activation behavior remains unchanged/unresolved.
Both following keep-open and window-return implementation entries are historical
and superseded by these explicit user-requested reversions.
Build02 clean;6 restored foreground-policy tests passed. Verified removed symbols
are absent. Evidence: `artifacts/winui-shell/foreground-acquisition-revert-20260930/`.
Candidate PID64320 relaunched with physical profile;242 deployed DLLs match build02.

### Keep unsuccessful foreground openings visible — 2026-09-30

User authorized fixing the confirmed self-dismissal over elevated GoW, and reports
Task Switcher still fails after closing GoW. Preserve that as a separate unresolved
activation failure; elevation alone does not explain all observed denials.

ForegroundDismissalPolicy now owns a per-show acquisition latch. PrepareShow and
hide reset it; successful native acquisition or a later observed owned foreground
arms it. Only an armed session may dismiss on current valid external foreground.
This distinguishes never acquiring foreground from the user switching away; no
new activation retry, privilege change or Task Switcher behavior change. Diagnostics
include foregroundAcquired. Current-window/process/visibility rechecks remain.

Evidence: `artifacts/winui-shell/foreground-acquisition-20260930/`. All10 focused
managed checks pass. Native01 confirms opening remains visible without acquisition
and ignores external dismissal in that state, but its immediate next external
activation did not transfer foreground. Native02 repeats that transfer timeout even
after150ms for activation callbacks; the full native gate is NOT passing. Both runs
pass the8 preceding checks, including the new non-acquired visible-open/consumer
guards and existing owned-window/external-dismissal/pin checks. No activation
workaround added. Build02 is clean. Physical logs
before the change are preserved, including later Task Switcher Denied results.
Candidate PID61896 relaunched with the physical profile;242 DLLs match build02.
Fixture/helper closed. Physical acceptance of staying open over GoW remains pending;
Task Switcher/native external activation investigation remains open.

### Normal-close foreground return and task activation diagnostics — 2026-09-30

User further correlates failures with elevated GoW.exe. Read-only live token probe
confirmed candidate52476 elevated=False/uiAccess=False and GoW62644 elevated=True.
Guide is delivered over GoW:09:01:56.407 foreground acquisition returned False with
GoW still foreground, then09:01:56.431 the new external-foreground rule hid main.
This confirms dismissal conflates failure to acquire initial foreground with a
later user switch away. It does not establish why Windows refused acquisition or
why later task activation to unelevated apps failed with ChatGPT foreground.
No elevation, game settings, input injection, or production changes in this review.

Latest physical log review (PID52476,08:59–09:01 UTC): two task activations succeeded
(Chrome, ChatGPT), followed by four Denied results (Chrome, SnippingTool, GoW,
WindowsTerminal). All four passed freshness/authority/target validation, hid main,
then returned false from RequestActivation; ChatGPT remained foreground at150ms.
No stale effect/admission rejection explains those attempts. A normal-close return
also returned false at09:00:17;09:00:26 correctly skipped a missing/stale Explorer
HWND. The activation failure is now observed, while the exact Windows refusal
mechanism is still unproven. Hide-before-activation losing foreground eligibility
is a hypothesis, not a diagnosed fact. Preserved trace:
`artifacts/winui-shell/window-return-20260930/physical-activation-0901.log`.
No production changes or candidate restart during this read-only review.

User reported wrong application after close and Task Switcher failing to activate
the requested task. Explicit scope: fix the confirmed omission only; add logging
for the unconfirmed Task Switcher failure without changing its activation behavior.

Confirmed native parity gap: WinUI HideOverlay did not restore the remembered
external window. Normal close now captures foreground ownership before hiding,
hides the main/backdrop windows, validates the opening-time HWND/process identity,
and makes one foreground return request. External foreground dismissal, task
activation and app-launch handoffs use the previous hide-only path and do not
restore the opening application. Pins remain independent. No retry/foreground loop.

Input diagnostics now include PID, foreground HWND/PID and return HWND/PID. Task
activation logs include target identity, initiation/visible-session times, host
effect rejection, the exact pre/post-hide admission rejection, API acceptance,
and immediate/delayed150ms foreground observations. Existing task freshness,
authority, identity, hide/activation ordering and single-attempt behavior remain
unchanged; Task Switcher failure is still unconfirmed/unfixed awaiting real logs.

Evidence: `artifacts/winui-shell/window-return-20260930/`. Build02 clean; all11
TaskWindowActivation tests pass, including new diagnostic reason/denial/failure
isolation tests. Native02 passes13 real HWND checks, including remembered external
foreground restoration and all earlier dismissal/pin/focus checks. Native01 caught
new snapshot capture interpreting tracker Observe's changed-result as validity;
capture now refreshes for every valid external opening target even when unchanged.
No real widget/provider/system-setting actions invoked. Physical acceptance pending.
Candidate PID52476 relaunched on the physical profile;242 deployed DLLs match
build02. Native fixture and helper closed; candidate left running with diagnostics.

### Dismiss main overlay on external foreground — 2026-09-30

User reports Alt+Tab leaves the overlay open while correctly excluded from system
switchers. Cause: WinUI deactivation only revoked input/focus; migration omitted
the native host's valid/current/external foreground dismissal policy.

PlatformInputPump now publishes an external-foreground observation from its
existing visible-window polling, independent of controller connection/readings.
MainWindow rechecks current HWND validity/process ownership and native visibility,
then calls the existing HideOverlay lifecycle. No new timer, foreground hook,
activation loop or AppWindow switcher-policy change. Same-process pinned/dialog/
backdrop transfers, invalid/null targets and obsolete observations are ignored.
Checking process ownership also covers leaving a pin after main is already inactive.
Passive pins survive main dismissal; Guide uses the existing focus-preserving show.

Evidence: `artifacts/winui-shell/foreground-dismissal-20260930/`. Build02 clean,
six focused managed policy tests pass; native02 passes11 checks against a separate
harmless WinForms peer and a real pinned-window class. Confirmed main/backdrop/input
hide, pin survival, same-process handoff, repeated external activation including
from pin, Settings noninitial focus restoration, stale observation rejection and
switcher exclusion. Native01 focus assertion used startup tray mode with manually
focused widget content; setting explicit InitialWidgetId in the isolated fixture
corrected its ownership setup without product focus changes. Tests invoke no real
widget/provider/system-setting actions. Physical Alt+Tab/Guide acceptance pending.
Candidate PID67176 relaunched on the physical profile;242 deployed DLLs match
build02. Native test peer and fixture windows closed. Left running for acceptance.

### Widget initialization feedback — 2026-09-30

Latest physical feedback:240ms completion fade still felt too fast. Increased
completion fade to500ms at normal animation speed (shared speed preference still
applies). No change to initialization threshold, Ready status, or input readiness.
Native validation now awaits fade completion with a bounded deadline instead of
assuming the previous300ms wait. Build07 is clean; native06 passes26 checks.
Candidate PID32188 relaunched with the physical profile,242 deployed DLLs verified,
and left running for physical acceptance of the slower fade.

Follow-up: user observed off-center icon and abrupt completion, specifically asking
about a250ms load. The loading host now uses the catalog icon's centered TileSize
layout. Arrival fades in over100ms after the existing200ms threshold; completion
changes the label to Ready without changing width, settles the halo from its
current compositor opacity, and fades the whole badge over240ms. The same motion
target retargets incomplete arrival directly into departure, avoiding a jump to
full opacity. Completion starts just before the atomic surface commit so layout
can freeze the badge position across widget resizing. Ready content/input never
waits for these animations. No minimum show time. Build06 is clean and native05
passes23 checks including geometry (centers within layout rounding), short-load
retargeting, accurate Ready status, fixed footprint/placement and readiness while
completion is still fading. Updated rail capture inspected. Native03/04 exposed
arrival starting before first measure, which violated the shared motion target's
positive-size contract and crashed the diagnostic app; arrival now starts only
from a loaded, positive-size layout. Completion before that layout simply cancels
the unseen badge. Evidence/debug dumps are retained in this pass's directory.
Follow-up candidate PID66432 is running with the physical profile;242 DLLs match
build06, Settings reached input-ready, and no fresh PID errors were found at launch.

User explicitly deferred Playnite scrolling stutter investigation. It remains
unresolved; do not continue those optimizations as part of this loading work.

Added a compact widget-icon / pulsing-halo / name / Opening badge to the shared
shell selection transaction. Cause of the silent wait: outgoing content remained
drawable during worker/lifecycle/layout preparation, with no pending indication.
The badge appears after 200ms, grants no input, retains the outgoing page, and
fades out over 120ms at the existing input-ready commit (not after all artwork).
No added readiness delay or widget SDK changes. Rail and radial use the same owner;
radial positions the badge above the wheel, including its bottom margin.

Theme palette/typography, interface/text scale, reduced motion and high contrast
are reused. Halo opacity runs on the compositor, not a frame timer. Supersession,
hide, unload, recovery and disposal retire pending delay/icon/motion work. A stale
selection cannot dismiss its replacement. Opening is a polite accessibility status
and never a controller/pointer target. Existing bridge-connection startup status
and widget-authored section/data loading remain separate.

Evidence: `artifacts/winui-shell/widget-opening-20260930/`. Build03 clean, zero
warnings/errors. Native02 passes16 checks, including real selection transactions
held at admission, rapid replacement, retained content/focus, rail/radial geometry,
fast completion, motion preferences, hide and recovery. Native01 caught a missing
radial bottom-margin allowance, now corrected. Rail/radial loading captures were
inspected; tests use an isolated profile and invoke no provider/system mutations.
Physical appearance acceptance remains pending on the relaunched candidate.
Candidate PID25164 launched with the canonical physical profile/controller adapter;
all242 deployed DLLs match build03. Left running for user testing.

### Settings focus lost after background refresh — 2026-09-30

User reports restored Settings focus disappearing about a second after returning.
PID54988 trace confirms category.overlay restored at snapshot34, then current
focus became null at snapshot36 after refresh. Regression from the preceding
home-loading change: AnimatePage still omitted its motion wrapper while loading,
then inserted settings.page-motion at completion, reparenting focused cards.

Settings now keeps the same page-motion container and transition key during
initialization and completion. Page navigation animation remains available; no
host focus override or forced refocus was added. A new blocked-initialization
snapshot regression fails on the old tree (settings.home.body vs settings.page-motion)
and passes with the correction; all77 Settings tests pass. Native production
return validation also reproduces the old defect, recording12 frames with no
native focus after the remembered category was restored. The native regression
checks widget return and hide/reopen lifecycle at two noninitial category cards,
through a later completed-refresh snapshot, preserving the same native control.

Frontend and Settings worker builds are clean. Evidence:
`artifacts/winui-shell/settings-refresh-focus-20260930/`. Fixed worker is staged
in a new installation directory with a fresh bundled integrity seal; the old
installation remains intact. The corrected native run passes all4 cases with no
lost-focus frames and identical native controls through refresh completion.
User physically accepted the Settings correction. Playnite fast scrolling still
stutters after the redundant-row optimization; that symptom remains open.
Canonical physical-options now selects this new installation. Candidate PID66052
relaunched with controller input, verified against242 build DLLs, and left running.
Actual Settings UI is present; no fresh candidate errors at launch. Main untouched.

### Scrolling/startup low-risk performance pass — 2026-09-30

User reports fast Playnite Library scrolling stutter with either stick, including
loaded posters, and requests official-guidance review plus usable Settings home
while data initializes. See [performance review](winui-performance-review-20260930.md)
for sources, concrete before/after evidence, validation and limits.

Removed duplicate indexed row fragment applications using exact immutable identity
stamps, preserving lifecycle/container/focus work. Matching native grid runs reduce
99 full applications to63 (36 skipped), same52 rows/images and focus/scroll result.
This is reduced UI work, not physical stutter closure.21 indexed and21 native
theme checks pass;145 shell and77 Settings checks pass.

Removed Bridge's eager app-library scan before its request loop; the existing
provider scans lazily on actual demand. Trusted initial Settings can now open
before installed catalog completion without bypassing package validation. Pin
restoration still waits and respects newer user intent. Added bounded startup
phase diagnostics. Settings home keeps category navigation available while data
loads; only an unready section shows loading/Back, and writes remain blocked.
Bridge/Settings rebuilt and staged in the isolated installation, with prior
payloads backed up. Native startup and focused Bridge contract test pass. Final
frontend build04 passes with zero warnings/errors. Candidate launched as PID63792;
it was replaced during user testing by PID54988 (00:45:08), which was verified
against all242 deployed DLLs and left running. No fresh errors for that incarnation.
Bridge and Settings staged assembly hashes match the rebuilt service outputs.

### Display-placement recovery error — 2026-09-30

User reported "Widget could not be displayed". Current PID65200 (started00:02:53)
logged repeated NullReferenceException in MainWindow.ResolveOverlayPlacement
at00:05:10–00:05:19, while display-profiles was active. The host dereferenced the
DisplayArea returned for its remembered foreground HWND without checking null.
This is shell placement failure, not evidence of a widget declaration failure.

Added shared OverlayDisplayArea resolution: preferred window, live overlay owner,
then primary display. Placement and backdrop callers preserve their last valid
geometry if Windows temporarily has no display. Display-change notifications
retain responsibility for retrying; no polling or system-setting mutation added.
Native shell-appearance fixture now tests a destroyed temporary AppWindow, missing
identity, live owner and primary fallback without changing actual display settings.

Build01 passes with zero warnings/errors. Evidence preserved under
`artifacts/winui-shell/placement-recovery-20260930/`. User authorized relaunch.
Native run reproduces the null lookup after destroying a temporary AppWindow;
all six placement/fallback assertions pass. The broader appearance fixture passes
ten further checks then fails its existing backdrop z-order assertion; that full
fixture is not qualified. No real display-setting operation was performed.

SDK Gallery0.1.24 was inspected (sandboxed, no permissions), installed, selected
and enabled using the catalog CLI;0.1.23 is retained for rollback. This supersedes
older staged-only Grid Gallery notes. The package adds explicit native Grid examples
to Controls. Candidate PID61884 launched on the canonical physical configuration;
all242 deployed DLLs match the build and real Settings UI is present. Left running
for physical testing. Initial fixture launch briefly failed with registration
access denied after shutdown; the subsequent launch succeeded without code changes.

### Controller guide crossfade — 2026-09-29

The guide previously replaced its committed labels directly, so stable changes
still appeared abruptly. Added a 140ms opacity crossfade through the existing
WidgetCompositionMotion backend and shared animation speed. Section animation
None, reduced motion, the system animation preference, high contrast, and reduced
transparency can disable it. No new setting or settling delay was added.

Two reusable native guide banks retain complete old/new lists. The outgoing bank
keeps its arranged size in a non-measuring Canvas and has no input/accessibility
authority; stale automation peers also cannot dispatch. Repeated proposals do not
restart motion, unready proposals retain the current guide, and an interrupted
fade immediately targets the latest committed guide without queueing. Completion,
hide, unload, resize, preference changes and disposal retire compositor resources.
Both banks share the existing cell factory and theme typography registration.
Controller-family changes initialize both banks; the first native run caught an
uninitialized spare-bank glyph, corrected before qualification.

Validation: build04 has zero warnings/errors; all 13 motion-policy tests and
69 native shell/guide checks pass. The production Games & Apps radial-return
regression passes all 10 handoffs with no intermediate semantic guide states.
The native final-state screenshot was inspected; the user subsequently physically
accepted the guide animation ("Looks good"). Evidence:
`artifacts/winui-shell/guide-fade-20260929/`. Existing stabilization/input routing
is unchanged. Work remains on `codex/winui3-frontend`, off main.
Physical candidate relaunched as PID59752 using the existing physical profile;
242 deployed DLLs match the build and the Settings widget is present. Left running.

### Radial center and paging focus — 2026-09-29

Removed the duplicate widget name from the wheel center; widget buttons retain
their themed tooltips and accessible names. Enlarged the controller hint from
24 to 48 DIPs. Single-page catalogs show the left-stick hint without paging arrows.
The asymmetric arrow highlight came from FocusPaging always focusing the Next
button. Paging now focuses a named neutral center control without a system focus
outline. Both arrow buttons retain explicit activation and keyboard accessibility,
but pointer interaction does not steal focus.

Build02 passes with zero warnings/errors. Native fixtures pass 109 checks in each
of four interface/text scale combinations, including actual right-stick frames
in both directions, neutral focus, selection preservation, single-page layout,
high contrast and reduced motion. Updated the layout-only fixture to model its
committed lifecycle and await guide publication; production guide code is unchanged.
Pixel qualification remains incomplete: both window and element captures were
blank, including a run without the platform activation adapter. Do not count
these captures as visual acceptance. Evidence: `artifacts/winui-shell/radial-hub-20260929/`.
Candidate PID62092 relaunched with the existing physical profile. All 242 deployed
DLLs match the build; Settings UI is present and no fresh candidate errors were
logged at launch. Left running for physical review; no merge to main.

### Whole-guide handoff correction — 2026-09-29

User rejected the previous guide behavior: B→radial still flashed through a middle
state. Added `--validate-guide-handoff` to sample actual arranged guide chips on
each XAML Rendering frame, using only local UI data. `guide-atomic-20260929/red01.json`
reproduces the first B transition: widget Remove/LB/RB/Refresh/Select → a mixture
of Remove/LB/RB plus radial controls → final radial guide. Prior tests proved
readiness/final state but explicitly allowed that mixture; they missed the visual
requirement. This was caused by the previous split-publication policy itself.

ControllerGuidePublication now stabilizes and publishes the complete hint list.
Pending presentation retains the previous complete list and cancels unpublished
candidates; only the first-ever pending guide uses safe shell hints. Ready handoff
replaces it once. Latest input eligibility still updates immediately and excludes
pending contextual actions, so retained labels cannot invoke changed commands.
The same-context120ms rule applies to complete candidates. Removed separate host
paint recomposition; explicit popup/pin control context changes remain immediate.
The new native regression requires every rendered frame to be either the original
guide or final guide, with at most one transition in each direction over five cycles.
26 managed guide tests pass under the corrected visual contract. Native
`fixed02.json` passes10 handoffs across five Games & Apps round trips; each records
exactly two arranged hint lists, old and final, with no intermediate/reversing frame.
`native02.json` passes55 shell/guide checks including stale-click suppression and
whole-guide replacement after readiness. Debug build02 clean. Candidate PID63768
relaunched and left running; all242 DLLs match tested Debug output, real Settings
nodes render in UIA, and no fresh diagnostic errors exist for this incarnation.
Physical acceptance remains outstanding. Main untouched.

### Guide publication aligned with rendered lifecycle — 2026-09-29

User found the120ms fix insufficient: context changes bypassed it using requested
widget/mode before corresponding disabled/enabled controls were applied. Replaced
the shell's sequential SetWidgetHints/SetState calls with a single Present path.
ControllerGuidePublication separates immediate host navigation from ready widget
actions. Pending same-owner actions retain only displayed information, never a
debounced candidate; pending new owners drop unrelated actions. Current host
buttons win pending conflicts. Readiness completion publishes immediately;120ms
remains only for transient changes within the same ready context. Invocation uses
Latest, which excludes all pending contextual actions.

Shared EstablishWidgetPresentationAsync now applies the declaration and records
the presented lifecycle. Selection, visible/interactive lifecycle reconciliation,
and interaction admission reuse it; background remains an ACK-only suspension.
Guide context uses the displayed presenter/active widget/scope instead of requested
identity. Root/scope and visible native collection rows must match current query
and content revision plus native enabled state. Only the clipped visible area
counts; offscreen rows and bitmap decoding cannot hold the guide pending. Existing
apply/focus/row notifications drive reevaluation, with a temporary LayoutUpdated
subscription queuing guide-only work outside the layout callback. No readiness
polling or added lifecycle delay. Earlier focus capture, coherent interactive
declaration, release-latch, and resource-boundary fixes remain necessary.
Requested interactive/preview mode is also excluded from displayed owner identity:
it gates readiness instead, preserving already displayed contextual hints until
the matching declaration and rows arrive. Shell navigation remains independent.

Evidence: `artifacts/winui-shell/guide-publication-20260929/`.26 managed guide tests
pass;54 native shell checks pass including delayed>120ms and immediate host controls.
The first production replay lost native foreground before assertions (Task Manager
held it); indexed keyboard fixture also refused input via foreground_not_target.
These are infrastructure failures, not accepted product results. After the user
finished with Task Manager, all21 indexed UI checks pass (logical-focus22 includes
the delayed>120ms readiness gate, offscreen exclusion, and visible reentry).
Evidence lives in `radial-focus-20260929/guide-publication02/`. `games-guide02.json`
passes21 real Games & Apps return checks, now requiring ready guides for Visible
and Interactive rows as well as exact focus restoration. Debug/Release builds are
clean. Native guide checks preserve empty-ready states and immediate host controls.
Final `games-guide03.json` also passes21 checks after owner-key consolidation.
Candidate PID25124 relaunched and left running; all242 deployed DLLs match Debug
build04, UIA finds real Settings nodes, and there are no fresh diagnostic errors
for this incarnation. No widget/system-setting actions invoked. Main untouched.

### Controller guide stability — 2026-09-29

User physically accepted the radial-return follow-up, then requested protection
against guide flashes during short-lived action changes. Native reference:
OverlayHost/main.cpp GuideSnapshotFor retains committed informational hints during
refresh (PinnedSurfaceHostTests covers this); no explicit timed hint stabilizer
was found in the native source/history inspected. WinUI SetWidgetHints previously
published every transient availability change immediately.

Added a shared120ms guide quiet-period policy and a one-shot DispatcherQueueTimer.
Same-context transient hints retain the displayed chips/accessibility text; if the
original hints return, pending replacement is canceled. Repeated identical
publications do not postpone a real change. Widget/scope, modal/transient-control,
slider adjustment, shell/radial/reorder and pinned-placement context changes are
immediate. Controller glyph family and theme paint update independently. Timers
stop on unload/disposal; first load paints current hints immediately. Controller
input is never delayed; clicking a retained hint cannot invoke a disappeared or
renamed replacement. Existing current-authority routing still owns invocation.

Validation:17 controller-guide managed tests;50 native shell-chrome checks including
eight new stabilization/invocation/context checks; Debug build02 clean. Evidence:
`artifacts/winui-shell/guide-stability-20260929/`. The first build flagged a fixture
collection-expression AOT incompatibility; corrected with the concrete array
pattern used elsewhere, without disabling the analyzer. No SDK/widget edits.
Candidate PID53188 relaunched and left running. All242 deployed DLLs match the
tested build, Settings nodes render in UIA, and this process incarnation has no
fresh diagnostic errors. Main untouched.

### Radial return follow-up: reproduced lifecycle/publication race — 2026-09-29

User rejected the previous radial fix after physical testing; blank-cell focus
was not the remaining report. Preserved their last logs in
`artifacts/winui-shell/radial-focus-20260929/followup/`. The old regression awaited
button actions serially. Expanded it with actual Receive(ControllerFrame) D-pad
navigation and12 B/open/B cycles, delivering release independently and varying
radial dwell. It reproduces the jump after four cycles (`real-replay01.json`,
`real-replay02.json`). Both initial nine checks still pass, demonstrating their gap.

Exact trace: index5 was remembered, but restoration saw snapshot73's current,
disabled Visible row and discarded that target. Snapshot75 then enabled the rows
after fallback had moved to0/1 and replaced memory. This is lifecycle ACK versus
declaration ordering, not a random WinUI focus choice or missing item capture.
The shell now uses the existing EstablishPresentationAsync Interactive transaction
inside admission, applies its declaration while input is revoked, then proceeds
through native readiness and focus restoration. Cancellation is checked before
applying. No delay, widget-specific focus policy, or collection equality change.

Added opt-in Debug focus/navigation traces via the existing bounded asynchronous
diagnostic writer (no artwork/text payloads). The first changed replay preserved
index5 through many cycles but ended in XAML stowed exception0x8000FFFF, with no
managed failure record; retained dump at LOCALAPPDATA/CrashDumps/
OverlayFrontend.WinUI.exe.48364.dmp. Minidump lacks original stowed stack memory.
Debugger triage subsequently located the reentrancy in the new fixture's
CompositionTarget.Rendering observer calling Lease.IsCurrent: that acquires a
session monitor, whose contended STA wait pumps XAML messages. The fixture now
samples only dispatcher-owned identities inside Rendering. No production lock
semantics were changed for this test defect.

Refined the shell correction to establish the intended Interactive declaration
once before resuming rows (Visible remains for previews). Admission reuses that
completed transaction; if foreground changes during preparation it obtains a real
Interactive declaration before entry instead of only an ACK. This avoids an
unnecessary intermediate preview render and resource churn.

The expanded replay also proved a separate lost-release bug: Receive discarded
the B release when it arrived during switching, leaving shellOwnedReleases latched
and later presses ignored. That suppression path now completes already-owned
gestures while still discarding new input. A current optional artwork request can
also race Bridge lease retirement before its next snapshot reaches the frontend;
the existing artwork-resource boundary now includes remote resource rejection,
timeout/unavailability/saturation, preserves pixels, and logs its code without
replacing the widget. A native artwork fixture checks that rejection plus recovery
and continues to require unexpected host defects to reach the normal boundary.

Final evidence: `real-fixed08.json` passes21 radial checks under the debugger,
and `real-fixed09.json` passes the same21 without it (nine original plus12 real
controller-frame cycles). No target drift or stuck B ownership. `artwork01`
passes40 native artwork/style checks, including retained pixels and resource-error
recovery. Normal Debug build08 and Release build02 are clean, zero warnings/errors.
Candidate relaunched as PID60372 with the usual physical profile/controller adapter.
All242 deployed DLLs match Debug output; Settings nodes are present in UIA and
there are no fresh diagnostic errors for this process incarnation. Candidate stays
open. Physical acceptance remains outstanding; do not conflate passing replay with
the user's acceptance. Main remains untouched.

### Games & Apps empty focus and radial return — 2026-09-29

Active correction in `artifacts/winui-shell/radial-focus-20260929/`.
The user's 34-item screenshot showed a blank focused final grid cell. Null row
payloads passed the old negative-pattern enabled test; native containers now
require a payload before accepting focus, including refresh/resume paths.

Real Games & Apps radial validation reproduced missing item memory with native
foreground confirmed (`real-red02.json`). The host captured the collection binding
on input handoff but relied on a separate routed focus event for the exact row.
`RememberFocus` now captures both synchronously through `CaptureFocusedItem`.
`real-fix03.json` passes nine actual B/A/B return checks at indices33,32,2 and
samples subsequent rendered frames for drift. It performs no game/app action.
No speculative rewrite of the shared B/A selection path was needed.

Query replacement in the indexed regression also exposed a retired artwork
owner accessing disposed cancellation sources. `WidgetIndexedRows` now declines
new artwork work for canceled owners/retired leases while retaining decoded paint.
The modal regression's old expectation of null background contradicted the current
scoped-background retention contract: it now verifies the same surface/artwork is
retained, old focus fragments clear, retired input is rejected, and modal focus
stays unchanged.

Test setup corrections: placeholder fixture uses a query without undeclared focus
surfaces; production radial fixture waits for actual foreground/input admission;
its opt-in activation adapter reuses existing setup without controller forwarding.
The inactive native-focus attempt is rejected by existing host policy, so no
same-collection focus-comparison change was warranted.

Final normal Debug build08: zero warnings/errors. `check07/results.json` passes
all21 indexed UI scenario checks, including18 logical-focus,25 modal,7 grouped,
8 surface and3 controller-route assertions. Real production radial checks pass9.
Candidate relaunched as PID57940; all242 deployed DLLs match the tested build,
UIA shows rendered Settings nodes, and no diagnostic entries exist for this
process incarnation. Earlier log records sharing this reused PID predate launch.
Candidate left running for physical acceptance. Main untouched.

### Mixed AppX deployment corrected — 2026-09-29

User reported "Widget could not be displayed" on the restored candidate. The
frontend error log identified MissingMethodException for ViewNode.get_GridLayout
in PID 22028, including YouTube Music and ordinary presenter creation. Read-only
metadata inspection confirmed its deployed WidgetProtocol.dll was v63 with no
GridLayout property, while the frontend DLL was the newer Grid implementation.
The prior restoration claim was too weak: Bridge lifecycle/startup success had
not established visible widget rendering or coherent AppX dependencies.

Cause: the prior OutDir-only builds shared intermediate output; subsequent
project-mode --no-build deployment assembled a newer frontend with older copied
protocol/SDK dependencies. Closed the broken candidate normally, rebuilt the
normal output with Rebuild, and redeployed the consistent build. Canonical options
now point to the already staged matching protocol-64 Bridge/worker installation. Existing
profile/catalog selection and widget versions were retained; Gallery 0.1.24 remains
staged rather than silently upgraded during this incident.

Added scripts/Assert-WinUiCandidatePayload.ps1: read-only comparison of every
build-output DLL against the running candidate's deployed directory, with process
creation identity and a fresh hash receipt. This must supplement UI health checks
before reporting a restored/updated candidate ready; a live process is insufficient.

Validation: native surface checks: 53; indexed scenario checks: 14 including 26
ownership/reentrancy and 16 retention assertions; full native style checks: 341,
including all 15 new Grid checks. The first style run hit a tooltip fixture race:
IsLoaded=false can precede native Closed, where its appearance lease releases.
The fixture now awaits Closed explicitly; production tooltip behavior is unchanged.
Final style run passes. Normal rebuild/build clean; no suppression or weakened
assertion added. Evidence: artifacts/winui-shell/mixed-deployment-20260929/.

Current physical candidate PID 61968. All 242 deployed DLLs match the rebuilt
output. Real UIA inspection found 44 visible Settings widget nodes, including
category buttons; shell state is active=settings, switching=false, and its error
log contains no entries for this process. Candidate left running for user review.
No real provider/system-setting action was invoked. The old native validation
blocker is cleared for this batch; broader migration/physical smoothness remains
incomplete. Main untouched.

### Native validation gate / restored candidate — 2026-09-29

The previous goal turn made concrete progress (Grid SDK/projection, transport,
Gallery package, matching runtime and clean build/test evidence). This turn
reverified PID 23844 with its original start time and confirmed the native
surface/indexed/Grid checks have not run. The unanswered close-candidate question
has persisted across three consecutive migration turns. Independent source,
managed tests and staged builds for this batch are complete; the next integration
step is native validation before further UI changes depend on this batch.

Attempted to avoid interrupting the candidate using the existing offline external
content staging script with package name WidgetRail.WinUI.GridFixtures.20260929.
Staging succeeded. However, project-mode winapp run with that explicit manifest
and copied OutDir returned the new AUMID plus Arg_COMException, while Windows
AppModel event 42 showed the original E9FE5D5F package being updated and PID 23844
exited. This combination did NOT isolate registration. No fixture launched and no
native check passed. Do not retry this project/manifest override against a live
candidate. The tool behavior interrupted the resident contrary to the intended
preservation; it was immediately disclosed in commentary.

Restored the original candidate through normal project-mode winapp using the
unchanged canonical physical-options.json and original Debug output. Current
resident is PID 22028, started 2026-09-29 15:15:09 local. Its visible window and
Bridge catalog validation/Settings visible lifecycle were verified. No new test
package is registered. Canonical options, installed catalog selection and existing
widget payloads were not changed. No system-setting/provider action was invoked.
The new source batch/Gallery package is still not the running candidate.

Next: wait for the user's answer to the existing close-candidate question; once
released, close the verified current PID normally, run the combined native checks
from explicit-grid-20260929/native-checks.ps1, correct any genuine failures, then
stage/select Gallery 0.1.24 with matching protocol-64 runtime and relaunch for physical review.
Until then the goal is blocked on this validation gate, not complete. Physical
parity/smoothness and broader migration gates remain unproven; main untouched.

### Explicit native Grid authoring — active 2026-09-29

The preceding continuation made progress: collection ownership, resize-safe
background motion, and AppWindow switcher policy were changed and compiled;
141 managed checks and a clean trimmed Release publish were recorded. The
physical candidate (PID 23844, reverified this turn) remains open while its closure
question is pending. This is not a global blocker: independent SDK/transport and
isolated-output work continues.

Current work replaces missing layout expression with WinUI's own Grid model:
- SDK/protocol v64: immutable bounded Auto/Pixel/Star track definitions, min/max,
  row/column spacing and attached cell positions/spans. Shared validation covers
  parent ownership, invalid bounds and protocol gating; no executable XAML.
- Frontend: plain Microsoft.UI.Xaml.Controls.Grid, direct native definitions and
  attached values, retained controls/focus, and native constrained scroll layout.
  No custom layout or frame scheduling algorithm is introduced.
- Transport: existing JSON/checkpoint/indexed freezing path preserved. Three new
  authenticated/generated-JSON tests passed. An indexed parent must advertise the
  current SDK protocol because its deferred row may introduce newer declarations;
  the SDK does this without eager row rendering or weakening protocol validation.
- Gallery 0.1.24: capability-free Controls example with fixed/star columns,
  Auto/fixed rows, spanning heading/action, and a local proportion-swap action
  preserving IDs. All 10 Gallery tests pass. Published/validated/packed
  widgetrail.samples.sdk-gallery-0.1.24.wrwidget in the explicit-grid artifact
  folder; deliberately not installed against the still-running old runtime.

Sources: docs/reference/winui-authoring.md and winui-explicit-grid.md.
Combined evidence: 144 SDK tests and all 272 presentation-session tests pass
(including the three Grid IPC cases); all three guide examples compile and their
snapshots validate. Analyzer-enabled Debug frontend02 and trimmed/ReadyToRun
Release publish pass without warnings/errors. Native fixture collection
expressions needed concrete arrays for the pinned C#/WinRT analyzer; corrected
without suppressions. Gallery WRSS needed the same --border fallback as its
existing controls; corrected before final 10/10 pass. Attached Grid resets now
avoid clearing/reapplying unchanged native positions on every publication.

Fifteen new native Grid geometry/focus checks are compiled, not executed. Run
explicit-grid-20260929/native-checks.ps1 after the current frontend is released;
this uses the latest isolated validation-build and includes surface, indexed and
full style checks. Do not run the older continuation script/build by mistake.
Gallery package and doc example artifacts are under explicit-grid-20260929/.
Do not deploy Grid64 Gallery against the existing staged63 Bridge/worker runtime.
A matching stage now exists at explicit-grid-20260929/installation: unchanged
bundled payloads/catalog copied into a fresh directory, Bridge and generic worker
freshly published from source (both clean). runtime-protocol-identity.json verifies
protocol 64 in the new frontend, Bridge and worker, with payload hashes. The
next-physical-options.json points at this new stage and the existing user test
profile/catalog. Canonical options and catalog selection remain unchanged.
After native checks and user release: install/select/enable Gallery 0.1.24 in the
isolated candidate catalog, then launch the validated frontend with these matching
options (or update canonical options deliberately). Current PID 23844 is still the
old physical candidate; it has not received this source batch. Pending native checks
from the previous batch must run with the new combined frontend once the user
releases the candidate. Existing native renderer support is not being expanded
just for backward compatibility. Main remains untouched.

### WinUI-native continuation — active 2026-09-29

User renewed implementation authority: use the migration plan as a reference,
preserve all current product/widget features and authoring quality, but discard
legacy mechanisms where WinUI has a better fit. Prioritize visible smoothness,
not just fixture counts. Subagents authorized. No automated real system-setting,
power, network, audio-device, application-launch or window-close actions; use
isolated declarations/fake providers. Physical workflow acceptance belongs to the
user. Main stays untouched. The existing candidate stays open pending the user's
answer about readiness for exclusive native checks.

Current parallel work:
- Collections: native ListView/GridView remain the realization owners. Correct
  reentrant range-publication retirement and test suspend/dispose/revision changes.
  See winui-collections-continuation-progress.md.
- Background motion: preserve the existing compositor fade across layout resize;
  no custom frame scheduler. See winui-surface-continuation-progress.md.
- Authoring: compare SDK/protocol declarations against actual frontend mappings;
  document genuine unsupported semantics and prioritize WinUI-aligned authoring
  additions without silently dropping existing widget functionality.
- Shell: restore production taskbar/Alt+Tab exclusion using the documented
  AppWindow.IsShownInSwitchers property, as already used by pinned surfaces.
  Audit publication/lifecycle behavior before adding any new coalescing layer.

Combined review and compilation complete. Analyzer-enabled Debug (isolated output)
and trimmed/ReadyToRun Release publish both pass without warnings/errors. Motion
policy tests: 12 passed; shell tests: 129 passed. Authoring guide C# examples compile
and emitted snapshots validate. Evidence: artifacts/winui-shell/native-continuation-20260929/
and artifacts/winui-shell/authoring-audit-20260929/. New native tests are compiled
but NOT RUN: candidate PID 23844 remains open pending the user response. No real
widget actions/system-setting operations were performed. No claim of physical
smoothness or completed migration is made from these build/managed checks.

Resume: after user authorizes closing candidate, verify its identity and close
normally; run native-continuation-20260929/native-checks.ps1 (uses the isolated
validation-build output; surfaces and indexed fixtures only). Resolve failures,
then rebuild/stage the normal Debug frontend or launch with matching OutDir and
canonical physical-options.json. Inspect final native result sparingly and leave
candidate running for physical review. Do not launch real-provider replay scripts
or invoke mutating widget commands. User answer about WinUI-style explicit Grid
authoring is pending; the implemented SDK contract is documented in
[winui-authoring.md](../reference/winui-authoring.md). Next authoring gaps: typed
Auto/star/fixed row/column definitions and spans, explicit handling of currently
ineffective layout properties, and early actionable declaration diagnostics.

Publication audit: no blanket latest-snapshot dispatcher coalescer was added.
View declarations can carry focus-entry intent; skipping them needs explicit
contract evidence before trading correctness for fewer queued callbacks.

Packaging/installer/startup registration remain later work after widget and
overlay parity. All changes stay on the migration branch; no main integration.

### Slider endpoint clipping: rendered-pixel correction — 2026-09-29

User confirmed the previous fix had no visible effect and authorized closing the
candidate. Reproduced the recorded flat-sided knob and displaced engagement ring
with the real Arcade Rush theme, 90% UI scale and 120-DPI display. The hidden
constraint was vertical: the 44-DIP control minus 16 DIP theme padding left a
28-DIP slot for the stock horizontal template, whose rounded height was 32.8 DIP.
WinUI's implicit layout clip then clipped both axes. The thumb's negative-margin
outer paint was cut at the endpoints and native focus fitting moved the ring.
The previous outer-control bounds assertions did not detect that inner clip.

WidgetSlider now reserves the native template minimum plus per-row rounding
clearance and theme padding. The shared layout projection passes the authored
minimum into that calculation rather than overwriting it on each publication.
Larger authored minima remain authoritative. Existing endpoint inset and native
full-size focus visuals remain; no thumb/ring reduction or copied XAML template.
The native control still owns input, range arithmetic and accessibility.

Evidence: artifacts/winui-shell/slider-clip-20260929/. `before/` reproduces the
visible defect; `final-pixels/` contains twelve actual screen captures at 90% and
125% UI scale, min/mid/max, engaged/disengaged. Pixel measurements show the same
knob size and ring width at all three positions for each scale, with ring/knob
center difference at most one physical pixel. `Capture-WinUiSliderPixels.ps1`
repeats these checkpoints. The first height experiment was overwritten by normal
layout publication; passing authored minima through WidgetSlider corrected that.

Final build02 clean. 67 native slider checks pass, including six new inner-slot
checks; 326 native styling checks pass. Actual YouTube renderer fixture passes
18 checks at widths 680/820/1000: both padded native templates and their parent
rows fit; the timeline/volume retain 3:1 widths without trailing empty space.
This verifies clipping/centering and existing slider behavior; it is not a new
claim about real-provider playback. Physical candidate relaunched as PID 23844; bridge package validation and Settings visible lifecycle completed successfully. Left running. Main untouched.

### Pre-play volume and dashboard Play correction — 2026-09-29

Reviewed the user's 12.2-second recording. User narrowed volume reversal to a
cued video before first playback, and prioritized dashboard Play (Pause worked;
Play waited until widget entry) for main and passive pinned video.

The real-provider probe reproduced the volume reversal with YouTube in state 5.
Provider volume changed to 95 while the widget still declared 100: setVolume
returned immediately and acknowledged an old getter value. The adapter's progress
loop only observes while playing. A later command therefore acknowledged the
previous value and pulled the thumb back. YouTube Video 0.3.36 now shares the
bounded, authority/cancellation-aware observed-value wait with mute, acknowledging
volume only once getVolume matches the requested integer percentage. Conformance
tests now model asynchronous cued volume, including 0, 100 and intermediate values;
the original adapter fails this test. No host timeout increase or value suppression
workaround was added.

Dashboard Play was blocked by the viewport input-ownership gate. The host now
separates playback activation from general browser/widget input: the active,
foreground dashboard can activate its visible current viewport or passive media
pin. Hidden/retired/other-widget documents and covered scopes remain gated. Browser
hit testing and controller focus ownership stay unchanged. Real-provider testing
already passed dashboard Play/Pause and passive-pin Play without widget/pin entry.

The previous focus-ring reduction is superseded: WidgetSlider retains WinUI's
original 14 DIP horizontal thumb-focus outset and reserves 15 DIP at each track
end, including rounding clearance. Space is reserved on SliderContainer so native
value/track arithmetic stays aligned. Native endpoint checks pass with zero and
theme padding; the larger focus indicator is preserved.

Evidence: artifacts/winui-shell/slider-video-20260929/. Recording frame sheet,
provider02.json (volume reversal; dashboard main/pin passes), adapter-red01,
tests03 (54 passing YouTube checks), slider01 (61 native checks), media01 (129
native media checks with pixel captures). Final build04 clean. 0.3.36 installed,
selected and enabled. provider-after.json passes the combined live check: 48
normalized held-left inputs keep the cued video's local and declared volume at
zero, and the actual provider reports volume 0/state 5 before playback. Dashboard
Play/Pause and passive-pin Play both work without taking widget or pin input.
Physical candidate relaunched through project-mode winapp as PID 65120 with the normal physical profile and controller adapter. Settings startup completed; left running. All work remains off main.

### Fractional slider stability and endpoint focus — 2026-09-29

User reported volume jumps, spontaneous deselection, endpoint clipping/offset,
focus-arrival knob movement even while paused, and insufficient volume width.
User authorized closing PID 63256. Reproduced two defects before correction:
busy acknowledgement reset a newer unsent value from .90 to .80; a cancelled
native focus transfer still ended adjustment because the host used LosingFocus.

Busy now defers the latest local intent instead of revoking it; an already-active
slider may continue editing while dispatch waits, and Done preserves the queued
final value. Disabled/retired controls still revoke unsent work. Adjustment ends
on confirmed LostFocus. WinUI source and the positive-range native test also
confirm that native disengagement restores its pre-engagement value; that
ValueChanged event is now excluded from user dispatch and the host restores its
current local value. Existing explicit A/B Done behavior remains.

The default theme's .99-to-1 slider focus scale caused knob movement without a
value change; slider scale is now 1 in both states. WidgetSlider retains the
stock WinUI template/behavior, centers the horizontal template vertically, and
uses a symmetric 2 DIP thumb focus margin. Any needed inset is applied to
SliderContainer, which is what native track arithmetic measures, with one DIP
of rounding clearance. Tests cover minimum/midpoint/maximum at zero and theme
padding. YouTube Video 0.3.35 uses 3:1 slider widths and an 80 DIP volume minimum.

Evidence: artifacts/winui-shell/slider-stability-20260929/. Managed checks: 129
shell and 54 YouTube. Native: 61 slider checks (including stationary fractional
values, cancelled-focus, positive rollback and endpoint geometry cases), 326 style
checks, and six actual YouTube layout checks. Clean final frontend build05.
Early native fixture corrections included clearing previous recorded actions,
using authored test sizes, and accounting for fractional DPI rounding; no claim
that these fixture issues were additional product defects.

A fresh isolated installation/runtime contains the rebuilt PlatformSettings in
runtime, Bridge and Settings; the new Settings package was sealed through the
bundled-package tool. The old runtime remains intact. The canonical physical
options now point at this new installation; existing profile/catalog are retained.
YouTube 0.3.35 installed/selected/enabled. Main remains untouched.
The first fresh-stage launch (PID 62932) failed preflight because widget-catalog.json
had not been copied alongside runtime; copied the unchanged installation catalog
and closed that failure window. Physical candidate relaunched via project-mode
winapp as PID 21424 with the normal controller adapter. Bridge catalog validation
and Settings visible lifecycle completed without integrity errors. Left running.
Physical controller acceptance remains separate from these checks.

### YouTube sliders fill remaining row width — 2026-09-29

The previous host fix correctly honored maximum widths, but the widget still
declared a 360 DIP maximum for its timeline group and a fixed 72 DIP volume
slider, leaving surplus row space at the end. User requested proportional fill.
YouTube Video 0.3.34 puts both sliders and their time labels in the same growing
row; timeline and volume receive 5:1 growth, labels retain intrinsic width, and
the obsolete maximum/fixed widths are removed. Existing minimum sizes remain.
This is a widget layout/style change, with no additional host layout behavior.

All 54 YouTube tests pass. Actual exported snapshot/WRSS native checks pass at
three requested widths (680, 820, 1000): the volume control reaches the row's
right edge and the sliders retain approximately 5:1 width after pixel rounding.
Clean frontend build; package validation passed. Evidence:
artifacts/winui-shell/youtube-slider-fill-20260929/. All work remains off main.
Physical candidate relaunched via project-mode winapp as PID 63256 with YouTube
Video 0.3.34 selected/enabled and the normal controller adapter; left running.

### Compact themed tooltips — 2026-09-29

User requested theme-driven focused-control tooltips without changing delay or
making them large, then authorized closing physical candidate PID 61648.
Cause: tooltip strings created stock WinUI popups outside the themed ancestor
tree, bypassing the existing popup theme adapter.

NativePopupTheme now owns shared ToolTip registration for the rail, radial menu,
fullscreen and compact media controls. It keeps the native template, placement,
wrapping and timing. Existing theme panel/text/border colors and hint typography
drive appearance; compact defaults are 8x4 DIP padding and a 280 DIP width limit.
Theme-derived corners remain bounded to 6 DIP. Bold Text, text/interface scaling
and system high contrast are applied once. The compact-media XAML root explicitly
attaches the shell popup theme. Open tooltips hold an appearance lease; closing,
unloading or clearing the tooltip releases it. Recycling updates the same popup.
No SDK, widget package, delay or animation policy changed.

Evidence: artifacts/winui-shell/tooltips-20260929/. Build02 clean, zero warnings
or errors. All 326 native style checks passed, including 12 tooltip checks for
compact measured dimensions, no focus theft, retheming, scaling/Bold Text, high
contrast and lease retirement. An isolated build while the candidate remained
open caught a missing validation-file namespace import, corrected before the
final build. Main remains untouched; physical visual acceptance is separate.
Physical candidate relaunched through project-mode winapp as PID 46552 using the
normal physical profile/controller adapter; left running for testing.

### Slider spacing, held triggers and YouTube controls — 2026-09-29

User finished testing and authorized closing PID 66492. YouTube Video 0.3.33
is packaged, installed, selected and enabled in the physical catalog.

The spacing defect reproduced against the real exported YouTube player snapshot
and resolved WRSS: a declared 6 DIP timeline/volume gap measured 97.96 DIP at
820 DIP width. The host capped the child but left its growing Grid track uncapped.
The shared layout adapter now applies authored main-axis maxima (plus margins) to
star tracks on both axes, resetting the cap when removed. The gap measures 6.76
DIP after display rounding at widths 680, 820 and 1000; timeline maximum remains
360. No widget sizing workaround or custom layout engine was introduced.

WinUI forwarded trigger edges but omitted the native host's held-action scheduler.
A shared scheduler now repeats LT/RT after 360 ms, then every 125 ms, gated by
authored WhileHeld policy or the host's compact/fullscreen seek ownership. It
captures semantic widget/scope/focus/action identity, retires on ownership loss,
release/disconnect/priming, waits for busy or in-flight commands, and never queues
missed ticks. Dashboard, ordinary/pinned widget and host-media routes share it.
Controller-guide and held-shortcut resolution reuse one focused declaration path.

Discover now declares R3 Search and uses a fresh entry request to a single-child
search-field group, so remembered Search-button/result focus cannot steal it.
Accent-filled YouTube primary buttons now use contrasting inset focus ink, matching
the Spotify approach and preserving theme tokens and system high-contrast handling.

Evidence: artifacts/winui-shell/held-trigger-20260929/. Managed checks: 129 shell,
54 YouTube. Native checks: 42 slider/held-authority checks, 30 actual local-media
workflow checks including LT/RT repeats in fullscreen and compact pin, and six
actual YouTube layout assertions. Final production build08 is clean. Fixture
setup errors (media snapshot declaration, busy state on a Stack, test after
presenter disposal) were corrected; these were not product defects. Broad native
style regression passed all 314 checks. The desktop screenshot clips the fixture's
lower controls and is not evidence of focus-outline pixels; spacing is verified
by actual native geometry, while physical visual acceptance remains separate.
Main remains untouched.

Physical candidate relaunched through project-mode winapp as PID 61648 with the
normal physical profile/controller adapter and YouTube Video 0.3.33. Left running.

### YouTube Settings return protocol correction — 2026-09-29

YouTube Video 0.3.32 is installed, selected and enabled in the physical candidate.
The reported worker runtime error is recorded at 19:41:33/19:42:05 UTC as
worker_protocol_validation_failed: $.initialFocusId referenced a missing
youtube.result item. Settings captured a lazy row's element ID, then returned it
as ordinary root initial focus even though that row is not in the page snapshot.

Settings now captures the indexed collection target supplied by the shortcut and
returns through the existing search collection entry path (generation/key/index
validation). Ordinary control returns retain their existing behavior. The media
session remains unchanged; no SDK contract change or new focus cache was needed.

The regression reproduced ProtocolValidationException before the fix. All 52
YouTube tests now pass, including an actual indexed-lease Y shortcut with retained
media and no new playback command. A live Settings/Back check restored focus to
Widget.youtube.search.results.Item.1 without a protocol failure. Live pinned
playback continuity was not established by that check; the media preservation
claim is covered by the managed regression. The opt-in shell input replay adds
F10 for Y; production input mapping is unchanged.

Evidence: artifacts/winui-shell/youtube-settings-return-20260929/ (red-01,
tests-01, frontend-01, publish-01, returned-controls.json). Frontend build and
0.3.32 package validation passed. Work remains on codex/winui3-frontend, off main.
Physical candidate relaunched through project-mode winapp as PID 66492 with the
normal physical profile and controller adapter; left running for user testing.

### Pinned B/View return focus correction — 2026-09-29

User clarified entry was View from inside a widget and authorized closing the
candidate. Original host retains widget/tray ownership and restores widget memory.
WinUI's existing remembered-focus path also passed ordinary same-widget and
Settings-to-pin returns; no second focus-memory system was added.

Found a handoff input-ownership defect: shell-owned buttons consumed release but
did not reject a duplicate Pressed before that release. After pin exit, another B
could enter main-widget Back navigation and move to the switcher; another View
could reenter the pin. RouteButtonAsync now rejects those duplicate presses while
the host retains that gesture. Release still clears it, allowing the next gesture.
This shared route covers compact media and ordinary pinned widgets.

Regression reproduced the tray exit with old code (`duplicate-press-baseline.json`).
The actual local sample now verifies B returns to Mute, View returns to the timeline,
and a cross-widget return restores Settings' noninitial Accessibility control.
Production-controller-enabled validation also exercises the real window-activation
path. Existing best-effort visible/focusable target restoration remains authoritative.
Evidence: `artifacts/winui-shell/pinned-focus-return-20260929/`; clean fix01 build and
`fixed-result.json` contain the final checks. This establishes the reproduced duplicate
press defect, not a captured trace of the user's physical incident. User physical
acceptance is still separate. All 26 actual-sample checks passed. Physical candidate
relaunched as PID 49220 with the normal physical-options.json and left running;
main remains untouched.

### YouTube Video migrated and enabled — 2026-09-29

YouTube Video 0.3.31 is installed, selected and enabled in the isolated physical
candidate catalog. This supersedes the earlier disabled/provider-pending notes.
It reuses the shared WinUI media owner, aspect-fit fullscreen, compact pinned
controls, slider settlement and discovered collections; no SDK contract change.

Real-provider validation reproduced an EmbeddedBrowserWebView.dll crash. Captured
dump identifies FrameCreated handler removal during finalization; the native frame
lifecycle regression additionally reproduced an assertion when removing that handler
inside Destroyed, on the UI thread. The final host avoids per-frame event wrappers.
Core frame-navigation and Document-resource events enforce the same exact frame
origins at all depths; media-resource CDN permissions cannot admit frame documents.
All event teardown is now on the stable core/controller lifetime. The intermediate
wrapper-retention approach was insufficient and is not the final implementation.

Validation: 51 YouTube managed tests, native media128 including nine nested-frame
creation/blocking/removal/forced-GC assertions, clean frontend build05 and final
live-provider playback pass. Exercised actual link cue/play/pause, volume, timeline
seek, Discover search/results and Player return with retained playback, player
settings return, fullscreen/B-equivalent return, passive compact video pixels,
interactive pinned pause, passive return/unpin, unavailable-video status and valid
link recovery. Final host was rechecked against the official IFrame API example
with volume zero. No credentials were read/exported or changed; search used the
existing configured provider. Physical controller acceptance remains user testing.

Evidence/package: `artifacts/winui-shell/youtube-video-20260929/`; final native run
`native-frames-04`, final playback `final-live.json`. Original 0.3.30 package is
retained. Changes remain off main. Physical candidate launched as PID 61392 using
the normal catalog/profile with a launch-only initial YouTube Video selection;
left running for physical testing. Startup defaults in the normal physical options
were not changed.

### Media controls, fullscreen and startup — 2026-09-29

Shared compact/fullscreen controls now overlay the aspect-fit video and auto-hide
after three idle seconds. Controller direction/A and pointer activity reveal them;
the first A only reveals, never executes an invisible remembered command. Hidden
controls relinquish native focus to a video focus target, preserving the remembered
button. Direct transport shortcuts still work. Compact commands use a short glyph
row with accessible labels/tooltips and adapt columns at narrow/text-scaled sizes.
Controls use shared shell themes. Timers stop when hidden/unloaded/passive.

Fullscreen no longer paints the opaque XAML background or shell canvas role and
does not reserve title/button rows. Original main.cpp explicitly leaves those
composition layers transparent; the existing desktop backdrop remains separate.
No browser/document is recreated for fullscreen or pinning.

Startup milestones now identify admission, environment/controller initialization,
document/adapter/media readiness and first Playing observation. Only fixed stage
names/widget identity/timing are logged, asynchronously off the UI thread. Actual
local sample: resources ~38 ms, first controller ~5.2 s, adapter ready ~5.4 s total.
This is measured initialization latency, not proven first-frame display time.
User explicitly chose **keep lazy startup to save memory** over runtime prewarming.
No cold-start speedup claimed. Added an accessible starting-player indicator that
clears on readiness, failure, pin transfer and retirement.

Evidence: `artifacts/winui-shell/media-ux-20260929/`. Native media119 and ordinary
pinned-window33 pass. Real sample21 passes (`sample-final-02.json`) including compact
auto-hide/reveal and passive/interactive/passive-return video captures. The initial
extended sample run pressed Next before the asynchronous Pause acknowledgement;
the fixture now waits for the host acknowledgement, preserving production's existing
busy-command policy. Build07 adds the final A-after-pointer/shortcut-reveal focus
guard; final native rerun lives in `native-final-02`. Main remains untouched.
Final native media119 rerun passed; build07 has no warnings/errors. Physical
candidate relaunched as PID 7716 with the existing physical-options.json and left
running for user testing. No provider package or SDK-contract changes were needed.

### White pinned-window corners — 2026-09-29

User screenshot shows white uncovered pixels outside rounded panel corners.
The explicit AppWindow/DesktopWindowXamlSource migration omitted the former
Window's TransparentTintBackdrop. Added that same existing WinUIEx backdrop to
`island.SystemBackdrop` after initialization, preserving passive media hosting.
Windows App SDK metadata confirms this property; checked WinUIEx 2.9.3 source
at its recorded commit for target connection/disconnection behavior.

Native validation caught a crash when the backdrop was assigned before island
Content. Dump stack points at DesktopWindowXamlSource.put_SystemBackdrop. Assign
Content first, then the backdrop. Real sample19 and pin33 then passed; passive,
interactive and passive-return video captures show no white corners. Final compact
auto-hide coverage is part of the media pass above. Isolated compile alone was not
evidence of native correctness.

### Investigated: automatic content sizing, report not reproduced — 2026-09-29

User reports original YouTube Video search pages resized to content with animation.
Existing WinUI content-resize motion is wired in ProductionLayout, but intrinsic
measurement parity is not yet established. Original renderer keeps the admitted
width definite and measures automatic root height; WinUI probes both dimensions
with finite constraints, with WRSS flex-grow mapped to native star tracks. This is
a hypothesis, not a confirmed cause. YouTube's empty search explicitly requests
Content height (preferred 820x640, minimum 420x420).

Prepared one shared MeasureSurfaceContent method for live/preparing presenters
(same finite-measure behavior, dimensions restored in finally) and a native
`--validate-styles --content-sizing-result=<path>` regression. Isolated baseline
build02 passes with no warnings/errors. Native baseline disproved the star-track
hypothesis: intrinsic height is 80, not 640. Added an actual YouTube empty-search
snapshot/styles export using the shared renderer-fixture exporter (focused MSTest
passes). Native measurement resolves that real declaration at 820x420 vs its
820x640 preferred size. All three native sizing assertions pass. No production
content-sizing defect or physical animation failure reproduced yet. Evidence:
`artifacts/winui-shell/content-sizing-20260929/`.

User subsequently finished testing and authorized closing PID 55856; it is closed.
Further work on the reported resize behavior needs the actual failing transition,
not an intrinsic-measurement rewrite based on the disproved hypothesis. YouTube
Video is now enabled following the live-provider checkpoint above.

### Tray/radial command ordering — 2026-09-29

The shared menu previously appended dashboard and package quick actions before
widget management. Pin commands, Reorder and Restart now precede those actions;
a separator appears only when quick actions exist. Visual and controller traversal
orders remain identical. Clean frontend build and all eight existing native popup
navigation checks pass in rail/radial modes with Spotify selected, without invoking
commands. Evidence: `artifacts/winui-shell/menu-order-20260929/`.

### Embedded-media completion — 2026-09-29

Implementation and automated local-media qualification completed on
`codex/winui3-frontend`; main remains untouched. Embedded Media Sample 0.2.9 is
reenabled in the isolated candidate catalog. No widget SDK migration was needed.
YouTube Video remains disabled pending its real-provider workflow qualification.

Implemented production compact-media receipts, same-browser transfer and host
controls through the shared pin coordinator: playback, optional LB/RB navigation,
LT/RT seek, B/View passive return, placement/resize/opacity and focus ownership.
Saved compact placement restores after explicit widget opening and media admission,
without replaying playback. Main view indicates pinned media; fullscreen returns
that browser from its pin. Back now follows the validated host root-exit route.

Concrete corrections: trusted activation now validates CSS coordinates against the
browser viewport rather than WinUI DIPs and handles clipped rectangles without
killing the document. Compatible publications no longer invalidate unsolicited
playback observations; retained origin/resource-epoch checks still reject retired
and foreign documents. Browser retirement unwinds callbacks and drains native
initialization/placement before close. Cross-root transfer awaits real Unloaded so
WebView2 disconnects its prior composition target, and peer teardown awaits return.

Passive media requires AppWindow + DesktopWindowXamlSource: implicit WinUI Window
visibility was not established before first activation. Ordinary XAML pins did not
exhibit that browser-specific visibility failure, but share the wrapper. Native
pin regressions now pass. AppWindow retains WS_DLGFRAME, so explicit nonclient
calculation gives the entire window bounds to XAML; tests verify actual client
bounds instead of relying on an internal style bit. The existing gamepad-key
boundary protects the pinned island against duplicate navigation.

Evidence: `artifacts/winui-shell/embedded-completion-20260929/`.
- Session269, shell127 and focused bridge2 pass (bridge race reproduced red first).
- Native media116 passes, including eight early initialization/retirement cases.
- Cross-root69 passes with visible HTML pixels, one core/document/audio instance,
  continuing playback and native peer close (`cross-root-final-03`).
- Ordinary pinned-window33 passes, including real pointer pass-through, focus
  preservation, opacity and reopen (`pinned-regression-04`).
- Actual sample19 plus passive/interactive/passive-return video pixel checks pass
  on final build22 (`production-final`): transport, slider settlement, preferences,
  both tracks, fullscreen, pin controls, move/opacity, unpin and six widget returns.

Final frontend build22 is clean; updated Bridge is published into the isolated
candidate installation. All automated windows closed normally. Source/manual review
and focused whitespace checks completed. No main merge, installer change or
real-provider acceptance is claimed. The earlier reported EmbeddedBrowserWebView
crash is not conclusively attributed from its faulting module alone. See
`winui-embedded-media.md` for architecture, SDK direction and validation limits.
Physical candidate launch follows this checkpoint; standing instruction is to
leave it running for user testing.

### Active widget-by-widget completion pass — 2026-09-29

Resume here after compaction. Worktree: `winui3-frontend`; branch:
`codex/winui3-frontend`. Keep all work off main. Complete widget functionality
before CLI/installer/deployment. Do not equate opening, fixture coverage or
automated checks with full real-provider workflow or physical acceptance.

Standing user instruction (2026-09-29): launch the updated physical candidate at
the end of each completed change. Explicit pauses/requests to keep a testing
candidate open still take precedence.

Current active pass: shared slider settlement, clearing passive pinned child focus,
and Spotify SVG raster sharpness. User finished testing and authorized closing
physical PID 4776; it is closed. Always relaunch after validated fixes.
- Slider: original native `SliderInteraction.cpp` has 150ms trailing settlement,
  forced Done/focus-exit dispatch, 2s bounded acknowledgement/echo guards. WinUI had
  none of this: each ValueChanged dispatched and every snapshot overwrote Value.
  Added pure `SliderValueState` (18 cases, managed suite passed by agent) and shared
  presenter integration with exact binding/scope/range/action/mode ownership,
  bounded pending entries, one demand-only timer, lifecycle cancellation and final
  flush. Native disengagement is suppressed/restored so Done cannot issue rollback.
  Native slider fixture passes 36 checks covering delayed/coalesced commands,
  Done flush, older/final/out-of-order echoes and scope/hide cancellation. Managed
  shell suite passes 127. Latest input-revocation cancellation/per-property no-op
  optimization needs final combined rebuild. Evidence `slider-settle-20260929`.
- Pinned: initial native-focus parking approach failed foreground-preservation
  validation (native Element.Focus reactivated pin). User clarified both View
  return and Alt+Tab; focus should only be shown while interactive and remembered.
  Replacing that approach with root-scoped focus-presentation suppression. Native
  focus remains logically remembered; no foreground transfer or disabled styling.
  Agent updating tests; do not run stale focus-parking assertions.
- SVG: removed font-sized intermediate-surface enlargement; now arrange OriginalColor
  SVG at layout size and bound its raster to physical DPI/interface/motion demand.
  New pixel-contrast test pending. Initial fixture failed by treating WinUI's
  default identity RenderTransform as non-identity; test now checks its actual matrix.
No packages or main integration changed in this active pass yet.

Previous follow-up complete: YT Music browse height 700 -> 580 DIP (compact settings
remains 440); Task Switcher close buttons removed while each tile retains X Close
window; Power Check-again button removed in favor of root Y Refresh with an explicit
guide label. Removed obsolete close-button focus references and Power's unavailable
focus target. These were widget declarations, not host rendering defects.
Tests: 8 Power, 6 Task Switcher, 29 YT Music pass. Native staged smoke verified Y
Refresh causes a new Power publication, X Close hint with no close buttons, and
actual YT Music resolved height 580. No power command or real window close executed.
Power and TaskSwitcher binaries/styles rebuilt and resealed in the isolated bundled
installation; YouTube Music 0.3.32 packaged, installed and selected normally. Previous
version remains. Evidence: `artifacts/winui-shell/widget-ux-20260929`.
Initial physical PID 50544 and smoke PID 41940 are closed. Updated physical candidate
PID **4776** is running with controller input enabled; leave it open for testing.
Main untouched. Standing launch-on-completion instruction fulfilled.

Previous resume point: pinned opacity control implemented, tested and built into
the next candidate. User finished physical testing and authorized closing PID
54156; it is closed. Isolated and default frontend builds are clean; 110 managed
shell tests and 20 production native pinned adjustment checks pass. Owned native
test PID 40884 is closed. No physical candidate is currently open. Main is untouched.

Cause: native pin windows and preferences already supported opacity, but migration
had not exposed an adjustment control. Added tray-menu "Pinned opacity: N%" using
the same host adjustment transaction, focus cue, input ownership, Save/Cancel,
transition lock and atomic preferences as move/resize. Horizontal D-pad/left stick
or keyboard arrows change whole-window alpha in 5% steps, clamped 30–100%. Guide
shows current percentage; A/Enter saves, B/Escape or hide/deactivation cancels.
Cancel/failure now restores original opacity as well as placement. Native production
fixture's six extra checks pass for preview/guide/non-resize/cancel/save/hide; it
restores the original placement and alpha in its isolated profile on cleanup.
Evidence: `artifacts/winui-shell/pinned-opacity-20260929/{shell-tests.txt,candidate.txt,native-result.json}`.

Previous resume point: Settings interface-size buttons, pinned move/resize, and Task
Switcher live previews are fixed and built into the candidate. Evidence root:
`artifacts/winui-shell/settings-pin-preview-20260929`.
- Settings native action reproduced the failure: display-qualified command names
  containing `@` were rejected by an element-ID validator. Session action admission
  now uses bounded command-name validation with exact declaration matching.
  All 266 session tests pass. Native UIA command route now changes 105% -> 110% ->
  105%; button width changes 57 -> 60 -> 57 physical pixels and per-display file
  persistence matches the host's effective scale (6 checks, `scale-native.json`).
- Pinned move/resize is an explicit Save/Cancel transaction, bounded by monitor/layout
  limits and the existing atomic store. Tray menu: "Move / resize pinned widget";
  D-pad/left stick move, right stick resizes, A saves and B cancels. Keyboard arrows,
  Shift+arrows, Enter/Escape are equivalents. Main HWND owns edit input; passive pin
  shows the themed outline. Hide/deactivation/disconnect cancel. Saves share the
  shell transition lock; exit restores main presenter input before reselection.
  Logical placement is retained through DPI/work-area changes to avoid double scaling.
- Task Switcher actual candidate reproduced blank thumbnails. Targets and renderer
  are admitted, but capture surfaces have width 0 while slots are ~264 DIP wide.
  Shared style mapping wrongly translated placeholder `text-align:center` into
  horizontal centering of the empty GPU surface. Excluded graphics preview content
  from text alignment; added centered-style native fixture and bounded preview
  state/layout diagnostics. 13 native checks cover frames, source repaint, compatible
  update, target replacement, suspend/resume and retirement. Actual Task Switcher
  displays five visible external app captures (`task-screen-after.png` and
  `task-capture-states.json`). Its own overlay target is intentionally capture-denied;
  capture policy was not weakened to render that tile.
Validation: clean combined/final frontend build; 109 managed shell tests, 266 session
tests; 14 production pinned placement checks exercise normalized controller input,
native bounds, exact cancellation, persistence, resumed rail/main admission, hide
cancellation and baseline restoration. Native pinned peer suite passes 29 checks.
Initial build caught two incorrect prompt enum names and an AOT collection expression;
fixed to existing Move prompts and explicit array. No SDK/widget package changes
were needed. Final build is the next physical candidate; media samples remain disabled.
All owned test windows are closed (including final PIDs 4540 and 29428). Native-test
slot is free. Physical controller acceptance remains pending; main remains untouched.

Previous resume point: slider/pinned-focus/lifecycle corrections are tested and staged. User authorized
closing candidate PID 7744; it is closed. No physical candidate is currently open.
Spotify pending controls incorrectly set Disabled, which forced focus departure;
they now use Busy while preserving focusability. Shared sliders now use native
focus engagement with adjustment guide/help text and retain adjustment during
pending updates. Spotify suite passes 76/76; native slider checks pass 28, including
authored WRSS outline handoff to the themed native thumb and back. That stronger
check exposed a real missing `UseSystemFocusVisuals` opt-in, now corrected.
Pinned ownership has a themed >=4 DIP overlay border, shown only while visible,
interactive and foreground. Native fixture initially assumed enabling interaction
could not activate its HWND synchronously and requested unsupported 150% scaling;
corrected to check actual foreground and the supported 125% maximum. Native pin
suite passes 27 checks; interactive screenshot shows the border overlay.
Lifecycle logs confirm both response reordering and idle worker sequence resets.
Bridge stamps worker-run identity; session coalesces older same-run replies and
retires old authority on a new run. Review caught cached snapshot stamping and
concurrent newer-response handling; both corrected with regression coverage.
Cached snapshots retain the run that produced them. Concurrent new-run replies
still use sequence ordering rather than discarding newer content. Ordinary,
controller and pinned actions echo their frame's run receipt: Bridge checks it
under its operation gate, and exact-runtime dispatch never starts/reconnects a
replacement process to deliver stale input. Legacy receipt-less clients preserve
legacy behavior. No widget SDK declaration change is required.
Validation: 262 session tests, 2 actual-process idle unload/restart tests, 2 input
run-fence tests, and 3 existing input-admission regressions pass. Separate agent
checks passed the real sandbox/full-trust session and pinned-action routes.
Full Bridge suite: 158/169 initially; three failures were a fake client still
implementing the old default-interface signature, corrected and rerun green.
Eight broader checks remain non-green: four need missing native-host build
artifacts; remaining cases concern immediate async artwork-action completion,
old Playnite Home initial-focus expectation, shell-style count 12 versus 16, and
virtual-fixture protocol version 50 versus 57. Do not report the full suite green
or imply these were baseline-verified in a clean checkout. See regression log.
Fresh Bridge/Contracts/Runtime publish staged into both candidate Bridge locations;
frontend rebuilt cleanly. Spotify 0.3.81 rebuilt from changed source, installed and
selected in isolated catalog. No sealed bundled worker payload was changed.
Staged smoke loaded all 14 enabled widgets (`staged-smoke.json`), including Audio
Mixer and the restored lineup, without executing provider commands. Smoke PID
26948 closed normally. Physical controller/provider acceptance remains pending.
Evidence: `artifacts/winui-shell/widget-lineup-20260929/slider-activation`.
Keep media samples disabled. Pin move/resize/opacity UI remains outstanding.

Previous resume point: focus-fill consistency is corrected, tested and staged after user reported that
Spotify's focused Stop action still resembled a selected device. Previous fix
missed `wrail-settings-row__action:focused` and other nonselectable action rules.
User finished testing and authorized closing PID 41552; it is now closed.
Removed remaining focus-only background overrides across shared components,
firstparty widgets, reference samples and embedded-media template. Focus retains
base/persistent-state fills; edge/depth/scale remain. Brand backgrounds stay in
their base rules. Compiled all-theme audit covers every declared focused selector
in shared styles and 15 actual widget fixtures, including selected/disabled
combinations and the exact Stop-versus-device case; 26 theme tests pass.
Native five-theme action/selected-choice checks pass; full native suite passes 305.
First new-fixture run incorrectly treated the selected depth gradient as a solid
brush; alpha checks now handle gradient stops and wait for style application.
All eight bundled styles were updated/resealed, all three PlatformSettings DLLs
refreshed. Playnite 0.2.110, YTM 0.3.31, Spotify 0.3.80 and SDK Gallery 0.1.23 were
installed/selected through normal package validation; payloads unchanged except
styles/version, old versions retained, catalog order/enabled flags preserved.
Disabled YouTube Video/Embedded Media source updated but kept disabled/unrepackaged.
Staged startup/browsing smoke loaded Settings and all four refreshed samples, with
14 active catalog entries. Physical candidate PID 7744 is now running at the user's
request with controller input enabled; leave it open for testing. Launch evidence:
`focus-fill-completion/physical-launch.json`.
Evidence: `native-styles-02/result.json` (305), `theme-tests.txt` (26),
`frontend-02.binlog` (clean), `staged-smoke-result.json`, `staging/` integrity proofs.
Evidence root: `artifacts/winui-shell/widget-lineup-20260929/focus-fill-completion`.

Previous visual fixes and scroll-restoration correction were built and staged.
Physical candidate PID 41552 launch evidence:
`crash-9740/physical-launch.json`. The staged startup smoke opened Settings
with all 14 active widgets and passed integrity validation (`crash-9740/staged-smoke-state.json`).
Physical PID 9740 crashed during rapid radial switching.
New last-chance log captured LayoutCycleException `0x802B0014`, incoming Audio
Mixer from Network Controls, selection 21, at 12:51:20 UTC. Evidence saved in
`artifacts/winui-shell/widget-lineup-20260929/crash-9740`, including incoming and
outgoing geometry. Audio ScrollViewer was not loaded although descendants had
geometry; restoring offsets inside LayoutUpdated used stale transforms. A probe
confirmed repeated offset advances with unchanged anchor coordinates before load.
Restoration now waits for the scroll owner Loaded event and coalesces work onto
the dispatcher instead of mutating scrolling reentrantly inside LayoutUpdated.
295 native style checks pass, including scroll restore before automatic focus;
eight actual-provider scrolled/evicted Audio Mixer radial returns pass, plus 22
indexed memento recreation checks (deep offset 4460 -> 4460). These fix
a demonstrated restoration defect; intermittent crash closure still needs physical
confirmation. No foreground candidate is currently open.

Parallel visual fixes: shared focus preserves authored/selected background rather
than copying selection fill; all five themes and actual widget WRSS pass 26 theme
tests. Spotify OriginalColor SVG sizing now preserves wide/tall viewBox proportions;
all 43 native package-icon checks pass, including seven new rectangular sizing/
painted-pixel checks. Candidate payload staging is complete: three PlatformSettings
DLL copies refreshed, Settings/Now Playing styles updated and resealed, Playnite
0.2.109 and YouTube Music 0.3.30 installed/selected. Old immutable versions retained;
catalog enabled flags/order unchanged, including both disabled media widgets.
Evidence: `focus-selection-refresh/candidate-payload-hashes.json` and before/after
catalog/immutable-payload proof. Package-icon first run timed out because fixture
required exact 117 DIP ActualWidth at 125% DPI; tolerance corrected. Second run
passed the new checks then hit a stale catalog-test StackPanel cast after the
tray's earlier Grid wrapper migration; fixture now locates the nested icon and
all checks pass. Indexed fixture must launch its executable, not `dotnet DLL`,
because it starts child workers through Environment.ProcessPath. First two
attempts were fixture startup failures; corrected executable run passes 22.
Evidence: `crash-9740/icons-result-03.json`, `indexed-memory-03.json`,
`styles-after-02/result.json`, `eviction-after.json`, `theme-tests.txt`.
Final frontend build: `crash-9740/frontend-icons-catalog.binlog`, zero warnings/errors.

Previous physical launch evidence: `crash-26172/physical-launch.json`. The user explicitly accepted
deferring reproduction and requested logging for the next occurrence. Do not keep
running crash reproduction or claim the underlying crash fixed. PID 26172 terminated
at 05:31:33 local while previewing Audio Mixer from Spotify through the radial.
WER dump `C:/Users/dwive/AppData/Local/CrashDumps/OverlayFrontend.WinUI.exe.26172.dmp`
contains a XAML stowed exception, HRESULT `0x88000fa8` (`AG_E_LAYOUT_CYCLE`,
4008 in Microsoft's `dxaml/xcp/inc/xcperrorresource.h`). This is distinct from the
earlier embedded browser crash. Evidence preserved in
`artifacts/winui-shell/widget-lineup-20260929/crash-26172` (switch/controller/overlay
logs, dump analysis). Initial Audio Mixer and direct/radial returns passed.
Eight real-provider radial returns passed both with and without the scroll-row
fix, so that fix is not evidence of a crash correction. Baseline comparison was
temporary; production source was restored and rebuilt with the row fix.
Last-chance XAML/CLR failure logging is implemented, without marking exceptions
handled. `%LOCALAPPDATA%/WidgetRail/WinUI/diagnostics/frontend-errors.log` records
UTC timestamp, PID, exception/HRESULT and switch context synchronously before
termination. XAML errors then capture up to 32 declaration/layout-owner entries
per incoming/active presenter, including dimensions, scroll geometry and motion
state. No user text, artwork identifiers/bytes or actions are in layout snapshots.
Capture is read-only, never forces layout, and cannot replace the original error.
Logs rotate at 512 KiB with one preceding segment; error and layout records are
bounded. Handled shell failures also persist to this log. Native fail-fast paths
that bypass managed callbacks still require Windows crash dumps.
Final evidence: `crash-26172/frontend-validated.binlog` (zero warnings/errors),
`crash-26172/shell-tests-final.txt` (103 checks, including four logger checks),
`scroll-row-fix/styles-final/result.json` (294 native checks, including bounded
layout capture). Superseded by current crash/visual pass above.

YouTube Video and Embedded
Media Sample are disabled at the user's request until embedded media is resolved;
retain their packages/source and do not reenable them yet (14 active widgets).
Windows recorded the reported crash in EmbeddedBrowserWebView.dll, exception
0x80000003, at 05:19:45 local. This locates the faulting module, not the root cause.
Evidence: `artifacts/winui-shell/widget-lineup-20260929/media-disabled-relaunch`.
Spotify header logo sizing is corrected in source; native pixel validation and
candidate build status are tracked in the current resume point above.
Physical feedback: Display Profiles cards and Playnite Categories rows shrink to
content rather than filling the row. Playnite `.playnite-library-category` already
declares `width: 100%`; Display Profiles relies on default cross-axis stretch.
Host ordinary Scroll used StackPanel children without Grid's cross-axis mapping.
Fixed shared scroll-child alignment through the same helper as Grid, including
horizontal scroll, authored full width/height, motion wrappers, fixed/max sizes,
and axis changes. No widget-style workaround or Settings redesign. Native style
suite passes 294 checks (9 new sizing checks plus one failure-snapshot check);
`scroll-row-fix/styles-final/result.json`. First new-fixture run rejected missing
ActionSurface metadata; fixture corrected before passing. No physical acceptance
of the row fix yet.
Shared Playnite modal failure is fixed and actual-package regression passes 104 checks.
Quit/Restart were verified through packaged Settings controls. Physical candidate
had been relaunched with controller input as PID 26172 after the user accidentally
quit PID 10896; PID 26172 subsequently crashed as recorded above.
Evidence: `media-disabled-relaunch/launch-02.json`.
Launch evidence: `combined/physical-launch.json` under the lineup evidence root.
Next: physical workflows that
change system/provider state, real video/fullscreen and Task Switcher previews;
then production compact-media pinning. Its cross-XamlRoot feasibility probe passed,
but it is not yet wired to production pin admission/lifetime/input ownership.
Do not mark migration complete. Do not merge main. Use normal winapp launch with
controller input for physical candidates, not a validation/no-controller shell.
YouTube Music recovery is packaged as 0.3.29. The original transient song failure
cannot currently be reproduced; its exact provider cause remains unconfirmed.

Candidate: `artifacts/winui-shell/parity-corrections-20260928/candidate/physical-options.json`.
Installed catalog: `artifacts/winui-shell/eviction-production-01/catalog`.
Controller candidates PID 26172 and PID 9740 crashed; PID 41552 was closed with user approval. Current physical candidate PID 7744 is open for testing.
Reverify processes before native checks or replacement.

| Order / widget | Status | Required workflow/backend qualification |
| --- | --- | --- |
| 1. YouTube Music | Recovery complete; 0.3.29 in candidate | 29 final managed, 35 final Python provider checks; prior 31 live-package checks and real muted WebView2 error/recovery pass. Full shell/provider acceptance remains separate. |
| 2. Now Playing | Actual WinUI Play/Pause and provider removal passed | Uses owned silent Windows session; seek/volume are not exposed by this widget. Broader physical/provider coverage remains. |
| 3. Playnite | 159 managed checks and actual WinUI modal/browsing/focus checks pass | Real launch-and-return and mutating provider actions remain physical |
| 4. Display Profiles | Restored; read/save/rename and permissions passed | 18 tests cover guarded apply/revert using fakes. Real mode-changing workflows deferred to physical testing. |
| 5. Games & Apps | Installed 0.1.1; 92 managed checks pass | Real launch-and-return remains physical |
| 6. Task Switcher | Installed 0.2.0; 6 managed checks pass | Real preview/activation lifecycle remains physical |
| 7. Audio Mixer | Installed 0.1.2; 48 managed checks pass | Real volume/device actions remain physical |
| 8. Network Controls | Installed; 31 managed checks pass | Real radio/connection actions remain physical |
| 9. Power | Installed; 8 managed checks pass | No live power commands; physical qualification only |
| 10. Spotify | Installed 0.3.79; 98 checks across four projects pass | Actual account/playback workflow remains physical |
| 11. YouTube Video | Installed 0.3.30; 50 managed checks pass | Real provider video/fullscreen remains physical |
| 12. Reference samples / SDK Gallery | Clock 0.1.1, Full Application 0.1.1, Embedded Media 0.2.9 and Gallery installed | Actual-shell declaration checks pending; Recent Apps is a staged optional legacy reference |
| Shared host | Implemented and packaged Quit/Restart verified | Restart replaced frontend/Bridge with same 16-widget profile; old processes exited. Quit cleaned replacement. Unsupported controller controls absent. 254 session, 99 shell, 77 Settings checks pass. |
| Specialized surfaces | 108 fullscreen/media checks pass; cross-window media proof passed | Same browser/document/audio transferred across XamlRoots, 69 checks. Production compact pin ownership still incomplete; real video/fullscreen remains physical. |

Exclusive controller control is deferred. Held-D-pad scrolling is not required;
right-stick scrolling already serves that workflow. Settings must not silently
offer unsupported operations. Track per-widget evidence and remaining limitations
in this section as each item advances.

User-confirmed packaging requirement: startup registration will be supported when
the WinUI application is packaged and installed. Its Settings control is hidden
temporarily in the development candidate. Restore it with the installed WinUI
startup backend and verify enable/disable plus update/uninstall cleanup during
the packaging milestone.

Updated user constraint: no live actions that change system settings; await
physical testing for those. Use computer-use sparingly for final inspection.
Agents: `host_controls_completion` (Quit/Restart, Settings capability audit),
`lineup_restore` (remaining bundled/community package staging and per-widget
backend tests), `specialized_surfaces` (video/fullscreen, preview, pinned audit).
All native frontend builds/windows must be coordinated with the root agent.
Detailed per-widget staging and tests: `winui-widget-lineup-progress.md`.
Specialized media/pin boundary: `winui-specialized-surfaces-progress.md`.
Root admitted the five bundled packages and Spotify/reference/YouTube archives
serially into the isolated candidate. New permissions remain subject to the
normal Settings consent flow. The lineup agent's staging-only wording describes
its lane, not the final candidate state. Settings/Bridge update pending host build.

Now Playing evidence: `artifacts/winui-shell/widget-lineup-20260929/now-playing`.
An owned silent MediaPlayer session appeared in the actual WinUI widget; Play,
Pause and replay commands changed the native state and UI labels. Removing the
session returned to the reload/empty state. First no-activation run correctly
refused input while backgrounded; rerun with the existing native foreground
activation path succeeded. Probe and frontend were closed. No system volume or
other user's session was changed.

Display Profiles evidence: `artifacts/winui-shell/widget-lineup-20260929/display-profiles`.
Installed sealed package into candidate, observed permission-denied UI, then
granted its two declared permissions through actual Settings and reopened.
Real current-display enumeration, save, text-entry dialog, rename and already-
active profile handling passed. Profile is stored only in candidate profile root;
test profile `WinUI renamed probe` (id `5e46bd249cea42448c4e2f4884517f05`)
remains there for inspection. No display mode was applied. The widget's 18
managed/provider tests passed, explicitly using fakes for apply/rollback.
Restart persistence, deletion and guarded real apply/revert remain physical gates.

YouTube Music evidence: `artifacts/winui-shell/widget-lineup-20260929/ytmusic`.
Recovery uses one typed player failure per load, refreshes both cached URL paths
once, then skips with a status notice. It never repeats/wraps a failed track and
stops after three consecutive failures or queue end. Manual selection and pause
supersede late recovery; permission failures do not skip. Real-provider search,
radio, muted playback, next, shuffle, queue insertion, pause/resume and package
immutability passed. A local HTTP 502 reproduced one typed WebView2 failure then
successful replacement playback. No claim that the original format caused it.
Initial package install correctly refused updating an enabled widget; disabled,
installed/selected 0.3.28 and reenabled only in the isolated catalog.
Independent recovery review reproduced two further gaps: MediaSession commands
were rejected after stopped recovery/buffered pause, and initial resolution
failures bypassed recovery. 0.3.29 separates current-player command identity from
playback-state generations and carries a closed `stream_unavailable` category
from the provider. Initial/automatic-next content failures now retry/skip; generic
helper faults retain their explicit recovery error instead of draining the queue.
Tests cover those paths, including Windows media Play after stop and Pause during
refresh. Final package is installed/selected/enabled. No raw provider error bodies
or signed URLs enter messages. Final provider tests: 35; managed widget: 29.

Combined evidence `artifacts/winui-shell/widget-lineup-20260929/combined`:
frontend builds clean. Media/native fullscreen passes 108 checks with captured
pixels. Initial harness capture failed because PowerShell splatted a scalar
string into characters; typed its argument array and reran successfully. New
Settings runtime needed resealing after payload changes; the first shell run
correctly rejected stale inventory. Resealed the candidate Settings package.
Expanded Playnite details regression is in progress (see shared-ux-02 failure and
shared-ux-03 rerun); do not report it passed before inspecting final result.
The user also observed the generic host error during this test. The diagnostic
rerun `shared-ux-04.json` captured `stale_controller_input_authority`: a release
after a modal tab published new state correctly failed worker revalidation, but
the presenter did not classify that code as retired input. It escalated normal
input retirement into the global error surface and blocked the next tab press.
Added the missing code to the shared `IsRetiredInput` path; authority checks are
unchanged. `shared-ux-05.json` passed modal workflows but its final Back-to-tray
fixture step ran before Settings finished publishing its page Back. The fixture
now observes the root before issuing the distinct root-exit Back. The final
`shared-ux-06.json` passes all 104 checks with no host error, including all three
actual Playnite details tabs and exact source-item focus on close. The fixture
records host exceptions and rejects even transient entry into host recovery.

Final combined checkpoints:
- `frontend-06.txt` / binlog: clean build, zero warnings/errors.
- `restart-result.json`: frontend 54796 → 32188, Bridge 22748 → 35928, 16-widget
  catalog retained; old frontend/Bridge gone. `quit-result.json`: replacement
  frontend/Bridge exited through actual Settings Quit. No registry/startup change.
- `controllers-tree.json`: only supported controller setting shown; no exclusive
  or held-D-pad switch. Actual UI check supplements the 77 Settings tests.
- `pinned/result.json`: 16 native peer checks pass, including border suppression,
  passive hit-through, interactive input and passive reopen; fixture closed.
- `cross-root/result.json`: 69 transfer checks, three observations; same browser
  process/Core/document/audio, one resource admission, playback and rendering
  callbacks progress through transfer and peer-close return. Pixel capture was
  not requested for this probe; this is not real-provider video acceptance.
- `catalog-probe/final.txt`: all 16 requested catalog entries, no catalog warnings.
  Embedded Media was installed but hidden by an old candidate-only disabled-ID
  preference; restored that requested sample without changing other preferences.

No frontend, MediaSessionProbe or pinned test window remains. Candidate launch is
still `winapp run src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj --no-build
--arch x64 -p Platform=x64 --detach --json --args "--shell-config=<absolute physical-options.json>
--validation-platform-activation"`. Use the path above; omit test flags.

Latest completed correction: same-widget extent changes reuse the existing
compositor resize owner, independently of the widget-switch toggle, honoring
motion/speed/accessibility policies. Evidence: `artifacts/winui-shell/content-resize-20260929`:
12 policy tests, 284 native style checks, 97 actual-package checks and 40 switch
checks passed. Real YouTube Settings/Back each ran one resize timeline. Built and
launched for physical review; no main integration.

### Radial Back reentry correction (2026-09-29)

The B shortcut used immediate chrome/focus toggles while A used the serialized
widget-entry transaction. B now awaits the same entry operation as A on the
current widget, retaining its presenter/runtime and awaiting Interactive admission.
The shell owns the closing B release so it cannot reach the newly entered widget
and cancel collection entry.

Expanded actual-package checks reproduced the reset and also identified an old
failed-slot flag canceling subsequent restoration during a resumed range read.
Retry, content-revision refresh and resume now clear that obsolete failure state
without discarding logical identity or retained pixels. Navigation still requires
current row authority. Temporary navigation tracing was removed after diagnosis.

Evidence: `artifacts/winui-shell/radial-return-20260929`. Corrected real-package
sequence passes 89 checks (B cancel, A entry, both switcher modes, hide/reopen,
exact indexed targets, and transport controls using a silent paused test session).
98 shell unit checks, 16 real-dispatcher lifetime checks, 16 retention checks and
40 native switch checks pass. B cancellation verifies the same presenter and
widget instance rather than assuming that unchanged selection intent means no
lifecycle work. Candidate rebuilt; physical review pending, main unchanged.

### Switcher-preview focus regression (2026-09-29)

Physical review still reset every widget to its first focusable control. The
earlier tests entered widgets directly; controller switching first publishes a
Visible preview, then A enters that same widget. Adding this exact sequence
reproduced Playnite selecting Home while its indexed item memory was still valid.

Selection now captures/revokes focus handling before closing switcher chrome.
Native fallback cannot enter a preview or overwrite its ordinary/indexed memory.
The radial chooser also cancels pending layout-time focus requests on close;
otherwise a later layout could focus its hidden selected button. Intentional
pointer/directional transfers remain allowed; restoration is still best effort.

`artifacts/winui-shell/preview-focus-20260929` contains the failing reproduction
and passing corrected run. The latter passes 77 actual-package checks for
Playnite Home/Library noninitial posters, YouTube Music collection items, Gallery,
Settings and Now Playing Play/Pause from a silent paused media test session.
Every widget is checked through Rail and Radial preview-then-A, direct switching,
and hide/reopen. This is automated coverage of the previously missed path, not
physical acceptance; all changes remain off main.

### Visual and focus follow-up (2026-09-29)

- Capture logical focus before revoking input and reject native fallback as new
  memory during suspension/publication. Reentry prefers a still-visible,
  focusable remembered target; disabled/removed targets use normal fallback.
  Real-package coverage now checks both hide/reopen and switching away/back,
  including noninitial Home and Library posters and exact indexed keys.
- Root Down always enters the rail. Root Back uses the configured switcher:
  radial in Radial+rail mode, rail in Rail-only mode. Both combinations are
  exercised through normalized controller input.
- Growing controls fill their native layout slots. Group headers no longer
  inherit OS separator/padding around the shared themed label. YouTube Music
  0.3.27 includes larger filter targets; Playnite 0.2.108 uses 1.10 focused poster
  scale and 82% dialog background alpha, leaving foreground content opaque.
- Uniform rounded depth edges use a continuous compositor contour instead of
  clipped rectangular strips. Dropdown font weight follows Bold Text, including
  live changes. New poster pixels fade in over 220ms before speed scaling on an
  independent compositor layer; retained pixels/size upgrades do not replay it.
  Reduced motion/transparency/high contrast show artwork immediately.
- OverlappedPresenter retained WS_DLGFRAME despite its border API. The main and
  backdrop windows explicitly clear nonclient frame bits after showing, without
  changing focus/z-order; DWM's optional outline is suppressed too. Final live
  styles are 0x14080000 for both windows, and captured white edge pixels are gone.
- Evidence: `artifacts/winui-shell/visual-focus-20260929`. Clean build, 282 native
  style, 42 popup/chrome, 40 switch, 159 Playnite and 28 YouTube standalone checks.
  Final real-package focus/retention runs pass 54 checks, including a paused silent
  Windows media session for transport-button coverage. Final frame change also
  passes the 54-check run. Visual Home/header/depth/frame captures are preserved.
  Changes remain off main; physical controller and visual acceptance are pending.

### Focus and shared-control corrections (2026-09-29)

- Retain the selected native tree while its HWND is hidden; suspend input/demand
  without collapsing it into XAML tray fallback. Widget/tray ownership is an
  explicit navigation choice. Restore retained focus presentation before async
  Interactive admission, matching the original host; commands still require
  current authority. Bounded same-owner scope memory survives loading trees.
- Focus presentation surfaces honor direction/justify/align/gap with finite
  scrolling tracks. Playnite 0.2.107 places its Home poster rail at the bottom,
  selected-game summary left-aligned immediately above, and widens Home/Library
  destinations to 128 DIPs (104 minimum).
- Dropdowns restore the original shaded panel, theme radius/border and selection
  rim/leading marker. Native MenuFlyout still owns placement and input. Uniform
  rounded button borders use an inset antialiased vector stroke while retaining
  native layout thickness. Rendered Quit/Restart and dropdown inspected at 125%.
- Evidence: `artifacts/winui-shell/focus-controls-20260929`. Clean frontend build;
  267 style, 50 surface, 40 chrome, 36 switch and 159 Playnite checks passed.
  Two 31-check actual-package runs cover Gallery, Settings, explicit tray return,
  and Now Playing both empty and with Play/Pause from a paused silent local
  Windows media session. Each reopen observes transient tray-focus events and
  verifies revoked input. Test media process and windows were closed afterward.
  Final Playnite left-aligned layout was captured separately. Physical controller
  feel and visual acceptance remain user checks; no changes merged to main.

### Physical review corrections (2026-09-28)

Follow-up physical review found blank widget content despite successful switch
commits, top-aligned tray icons/selection markers, and high private memory.
The blank-content regression reproduced in the native fixture: the new reveal
sampled zero from the presenter's composition opacity before XAML published its
new visible value. The subsequent resize correction owns only scale/center on
the presenter and shell fill; XAML retains staging opacity. Tray content stretches its internal
layout to the full tile, centering icons and placing the marker at the bottom.
Native style checks now pass 226 cases. Switch checks pass 28 and explicitly
require opaque raster pixels, rather than allowing two blank captures to match.

Memory investigation evidence is under
`artifacts/winui-shell/switch-memory-20260928`. The reported process sampled at
629 MiB frontend/555 MiB Bridge private memory. Heap collection showed much
smaller live managed graphs (about 63/66 MB; a later Bridge graph about 37 MB),
and substantial frontend LOH fragmentation. These captures force collection
and are diagnostic only; no periodic/forced GC was added to the product.
Indexed artwork replies now use the existing typed framing writer directly,
avoiding an intermediate JsonElement copy. Channels reuse one cleared read
scratch buffer capped at 1 MiB; returned envelopes retain independent ownership.
For eight 512 KiB artwork payloads, measured serialization allocations fell from
11,197,632 to 5,670,336 bytes and read allocations from 11,195,840 to 5,600,320.
Framing, cancellation, protected-secret, typed-content and 251 session tests pass.
A 24-switch real-widget run completed, ending around 449 MiB frontend/276 MiB
Bridge private memory. It is not the same workload as the user's original run
and does not establish that total memory use is solved. Native/decoded-resource
retention and a comparable long browsing run still need qualification.

The first production review exposed shared paths that the earlier isolated
fixtures did not cover. Visual/UX parity is **not accepted**. Corrections now on
this branch include:

- The Settings worker receives the same explicit settings root as the Bridge
  and frontend. Busy saves preserve native focus while blocking activation.
- The frontend consumes View+Menu through the existing native shortcut observer
  (platform ABI 6); appearance changes now reach the active profile.
- The session admits all 16 real Bridge shell theme roles. Rejecting four valid
  roles previously discarded the entire palette. Menus and the controller
  keyboard have a shared popup theme context. Scrollbars and slider state
  brushes update without replacing controls or scroll offsets.
- Private, ink-normalized controller-font subsets restore optical size using
  the original outlines. Kenney remains CC0; the renamed PromptFont-derived
  Guide subset remains SIL OFL 1.1 with its copyright/license notices.
- The radial chooser restores sectors, selection arc, hub and depth. The tray
  restores its bottom selection marker and themed controller-hint chips.
- Ready-widget reveal consumes the global switching-animation setting.
  Retired artwork sources cannot clear the retained background; artwork blends
  keep the outgoing opaque layer underneath the incoming image.
- Poster content clipping uses the element's resolved corner radius, including
  indexed focus state; focus/scale/shadow remain owned by the outer control.

Evidence under `artifacts/winui-shell/parity-corrections-20260928`: 218 native
style checks, 113 glyph checks/24 optical samples, four radial scale/text-scale
cases with composed selection-arc pixels, 35 shell/popup theme checks, 23 native
motion checks, 30 presentation-surface checks, and 27 native switch checks.
Select/text-entry/context interaction checks pass 24/38/60 cases; their
top-level window screenshots did not capture usable composition pixels and are
not visual acceptance. Managed Settings tests pass 76, presentation-session
tests 251, and the real Bridge-to-session shell-palette regression passes.
The real Settings worker saved Paging to the isolated candidate profile and the
host's effective diagnostic changed to Paging; the user's main settings file
remained unchanged by that test. Physical View+Menu and final visual acceptance
remain outstanding. Full desktop automation was interrupted by repeated hides;
the user subsequently confirmed they were using the PC during those checks.
Popup content has also been excluded from
the shell's empty-gap pointer-dismissal rule; that final production pointer
sequence remains unqualified.

The settings audit separately found missing WinUI consumers for Exclusive
Control reporting/application, held-D-pad scrolling, and application Quit/Restart
requests. Start-at-sign-in still targets the native executable and is gated in
isolated installations. Those are migration work remaining, not completed
features implied by the corrections above.

### Earlier implementation checkpoints

This branch has a functional WinUI widget frontend and isolated real-package
validation. It is not yet a complete replacement for the native overlay.

- **Host and input:** catalog discovery, worker lifecycle, displayed-frame action
  admission and the accepted shared controller/activation adapter are integrated.
  Ordinary and indexed inputs retain their captured identity across asynchronous
  activation; stale input is consumed without replay or rebasing. Guide/foreground
  behavior was physically accepted in the controller harness. The complete production
  shell still needs physical acceptance; automated runs use `--shell-no-controller`.
  Tray focus now previews widgets without transferring focus into them; explicit
  activation enters them. Saved order/reopen state uses the explicit profile.
  Native focus-policy checks pass 19 cases and real tray browsing passes four;
  native views now retain three recent widgets with explicit hidden-demand
  suspension. The five-check real deep-scroll/modal return gate passes. Radial
  presentation remains. Lightweight state restoration is integrated with bounded semantic history;
  real shell eviction validation awaits a running Playnite provider. See
  [shell navigation](winui-shell-navigation.md).
  Tray reorder/hold-restart and native command menus are integrated; five native
  command and four rapid-browse checks pass. See [tray commands](winui-tray-commands.md).
- **Native UI:** standard layouts, responsive branches, text, buttons, action/poster
  surfaces, icons, prompts, progress, text entry/controller keyboard, Select, sliders,
  context menus and widget-local modals are mapped. The same themed declaration path
  renders native indexed item fragments. OriginalColor and ThemeTint package icons
  retain exact admitted package authority. Button/panel/modal depth is integrated,
  with native button hover/pressed/disabled states preserved. Collection-container
  depth uses native outer masks and shared visual ownership while preserving stock
  ListViewItem/GridViewItem templates. Tray package icons use the same admitted
  package renderer, retaining native selection and focus semantics.
- **Collections:** the exact-count SDK/range/lease contract, native ListView/GridView
  virtualization, grouped lists, logical focus entry, background/summary retention and
  captured actions are integrated. Playnite Home/Library, YouTube Music's finite
  service snapshots, Games & Apps Library/Running and Spotify Queue use it. Games &
  Apps Catalog retains explicit paging. General opaque continuations are a separate
  integrated contract: workers retain opaque tokens and publish an append-only
  discovered prefix to native incremental loading. YouTube video Discover/Search
  now uses it, as do Spotify playlist/detail/Search. The combined
  continuation fixture passes 36 native assertions, including empty/duplicate
  pages, bounded automatic demand, Retry, held-tail/reverse input, retained
  viewport/action identities and native list/grid footer replacement. See
  [author contracts](../developers/indexed-collections.md).
- **Real widget validation:** isolated packages pass six Playnite Library/tray/details
  checks, five YouTube Music Home/Library/return checks, and six Games & Apps browse
  checks. Screenshots show actual provider artwork and resolved production styles.
  These checks do not launch apps/games or establish real-provider playback,
  sustained-scroll performance or physical controller acceptance.
- **Modal correctness:** the parent remains mounted while dialog chrome opens/closes.
  No forced scroll-offset restoration is used. Exact-binding native readiness events
  fix initial focus arriving before the target can accept it. The reviewed modal lane
  passes 36 modal checks, 25 real-worker indexed-modal checks and the full 20-check
  indexed sequence twice. Closing content loses input/automation authority immediately.
- **Motion and accessibility preferences:** sections, headers and selection surfaces
  share compositor motion; focus decorations and focused/pressed scale remain native
  presentation transforms. Background artwork crossfades retain bounded decoded
  images. Global animation settings, text scale, bold text, contrast and reduced
  transparency apply after authored styles. The reviewed depth checkpoint passes
  146 native style checks, 36 modal checks and the 20-check indexed sequence;
  background/presentation checks pass 27. Physical animation quality remains separate.
- **Sizing:** declared surface hints, per-display interface/text zoom and existing
  display identity/placement policy are integrated. Native sizing checks pass 13 cases;
  real Playnite was checked at Windows 125% DPI/interface 75%. Full mixed-monitor,
  themed/solid/transparent surface overrides and desktop backdrop opacity now use
  native windows and exact per-widget/accessibility policy. The lane passes 15
  native pixel/lifecycle checks; production appearance/Guide/mixed-monitor and
  final shell-chrome coverage remain.
- **Media:** sealed document admission, native WebView2, trusted activation, ordered
  playback observations, durable shell ownership, four-controller capacity, parking
  and persistent error/recovery are integrated. Hidden presenters detach their
  viewport without terminal retirement. Host fullscreen moves the same browser
  into an aspect-fit native view with controller guide controls, then returns or parks
  it on B/View/hide/deactivation/capability loss. The native media fixture passes 101
  checks with inspected screenshots. Real-provider fullscreen and production shell
  routing/placement still need joint verification. Pinned transfer is unfinished.
  See [fullscreen media](winui-fullscreen-media.md).
- **Window previews:** the production shell enables target/permission admission after
  its shared GPU capture owner initializes. Ordinary widget declarations now map to
  native preview slots with target replacement and deterministic retirement. The
  integrated renderer passes 15 native policy checks and 24 state/pixel stages (41
  fixture assertions), including expiry while the dispatcher is blocked and idle
  polling/binding suspension. Playnite/Music browse regressions pass six/five checks.
  Imported swapchains are opaque: safety blanking is black during a UI stall, with the
  authored fallback restored after dispatch resumes. Transparent DrawingSurface output
  was rejected because it retained expired pixels while blocked. Task-window activation
  uses the shared native identity/foreground policy; full Task Switcher acceptance
  remains. Indexed/pinned/focus-fragment capture needs separate admitted inventory.
- **Pinned authority:** genuine-frame projections, independent scopes, selected-layout
  lifetimes, controller/Select/slider input, indexed actions and continuation are
  integrated. Ordinary pinned context/text and scoped artwork now use explicit
  version-1 private bridge/worker requests. The production Pin command now owns
  genuine projections, a peer window, shared controller/lifecycle routing and passive
  restoration. Five production checks, a separate fresh-process restore check, 19
  presenter checks and 14 native peer-window checks pass. Compact media transfer,
  pin controls and display/scale reconciliation remain; see
  [production pinning](winui-production-pinning.md).
- **Managed gates:** latest relevant integrated suites pass 233 presentation-session,
  46 shell, 157 Playnite, 28 standalone music, 92 Games & Apps, 73 Spotify and 50 YouTube
  checks. The latest integrated collection gates pass 16 indexed plus one discovered
  SDK scenario and 17 indexed plus one real-worker discovered bridge scenario;
  the prior complete SDK checkpoint passed 138. Counts identify their recorded checkpoint;
  affected suites must run again after later contract changes. Analyzer builds are clean.
  The finite-range wire mismatch with the older isolated test runtime is corrected:
  default Range omits the continuation-only field. The full native Library/tray/
  details replay now passes six checks, with five Music Home/Library/return checks.
  Actual artwork is visible in the reviewed screenshots. Evidence is under
  `artifacts/winui-shell/finite-range-native-replay` and `music-integrated-replay`.
  Tray icons pass 36 native checks including 12 catalog scenarios in their lane.
  The Full Application reference sample now uses the exact-count indexed contract
  for its complete 10,000-record model; all five integrated sample tests pass.

Remaining product work includes remaining continuation-source adoption, preview
coverage beyond ordinary roots, pin display/controls/media ownership, radial tray and complete shell
appearance/backdrop behavior, remaining styling, real-provider workflows, full
controller/performance/memory/accessibility validation and packaging. Main and the
installed native candidate are untouched; fixture passes are not migration acceptance.

## Active implementation lanes

- Window previews: ordinary production wiring and controlled pixel validation complete;
  broader projection inventory and real Task Switcher workflows remain.
- Continuation collections: SDK/wire lifecycle, YouTube Discover/Search and Spotify
  library/detail/Search are integrated. Provider reachability beyond the bounded
  discovered-prefix metadata cap remains a deliberate open product requirement.
- Depth styling: button/panel/modal and native collection masks integrated.
- Pinned authority: production coordination integrated. Follow-up teardown and display/scaling commits are preserved separately and deferred by user request; they are not in this candidate.
- Root integration: inactive widget suspension/resumption and three-view retention
  pass the real Playnite/Music deep-scroll return and modal test. Appearance and
  complete shell behavior remain in progress.

After these checkpoints, remaining feature coverage includes provider reachability
beyond discovered limits, broader WindowPreview projections, pinned/fullscreen
integration, radial tray and complete shell appearance/backdrop behavior, and full
physical/performance/accessibility/package validation. The installed
native product is still the usable product; this branch is not feature-complete.

## Checkpoint history

The dated entries below preserve investigation evidence. Older failures and counts
describe their checkpoint, not current status; the summary above takes precedence.

### Initial implementation

- Microsoft WinApp CLI 0.7.0 installed with user authorization. Developer Mode
  enabled with separate explicit UAC approval. Existing .NET SDK reused.
- Official WinUI MVVM template created `src/OverlayFrontend.WinUI`; resolved stable
  dependencies pinned (Windows App SDK 2.5.1). Counter sample removed; this is a
  frontend foundation, not a widget port. Development package registration is
  separate from the existing installed product.
- XAML shell builds with Microsoft WinUI analyzer enabled: zero warnings/errors.
- OS backdrop compositor requires a Windows dispatcher queue. The initial startup
  crash is diagnosed in `artifacts/winui-shell/debug-launch.log`. Fixed using the
  framework's `DispatcherQueue.EnsureSystemDispatcherQueue`, avoiding custom P/Invoke
  or a competing message loop. Frontend then stayed live and exposed Shell.Status
  and Shell.Close through UIA; invoking Close exited normally.
- `WidgetUi.State` selects background/presentation sources from immutable logical
  membership, independent of realization. Its 15 tests pass. This policy component
  does not implement layout, spatial navigation, painting, or animation.
- Feature inventory: [contracts](winui-feature-contracts.md).
- Replaced custom backdrop code with pinned WinUIEx 2.9.3. Basic transparency
  now passes a controlled red/green desktop-background test at three margin
  coordinates. WinUIEx owns the native DWM/message integration rather than a
  copied implementation in our frontend. License retained and packaged.
- The shared script `scripts/Test-WinUiTransparency.ps1` records pixel results and
  screenshots. A real pointer click reached Close after the test. Test PID 22148
  exited; no validation frontend is intentionally left running.
- [Native binding inventory](winui-platform-binding.md) identifies existing ABI
  reuse and the separate View+Menu observer that must also be exported/preserved.

## Failed / unproven gates

The original top-level Window plus transparent SystemBackdrop brush did NOT show the
desktop through the empty margins. Both window and desktop-composited screenshots
show opaque margins. Extending content into the titlebar did not correct this.
An external diagnostic DwmExtendFrameIntoClientArea(-1) call returned success but
made the window white; it was not added to production. A subsequent external style
probe did not establish transparency either. Test window PID 11060 was closed
through Shell.Close afterward. This attempt was replaced, not hidden behind a
passing build. WinUIEx then passed the basic alpha proof described above.

The first winapp launch with --output-appx-directory failed registration (missing
appxmanifest.xml); default project-mode deployment succeeded. Revisit a supported
short staging path if package size/path limits require it. No manifest was deleted,
no unpackaged fallback was introduced to suppress the failure.

Raw debug-process sample was roughly 98 MB private / 142 MB working set; this is an
empty-shell observation, NOT a full-product memory comparison or acceptance result.

No controller routing, real widget transport, lazy collection port, native media,
pinned projection, transparent-area input policy, or frame-latency gate has passed.
Do not claim migration readiness based on shell build or source-selection tests.

## Next actions

1. Extend basic desktop-alpha proof to input regions, native media, pinning,
   monitor/DPI changes and recovery. The shell still has a visible DWM outline;
   finish intended chrome policy through supported window APIs. No undocumented
   root surgery or unstable CompositionEngine opt-in as a workaround.
2. Connect existing platform ABI input/activation rather than rewrite it. Prefer
   framework APIs whenever they remove custom code.
3. Connect immutable source selection to real page/item metadata and shared WinUI
   presentation slots; retain cursor membership independently from realized controls.
4. Deliver the real sandboxed Playnite/collection slice, followed by remaining
   migration features and the pre-WIDGE-293 comparison from the plan.

Evidence: ignored `artifacts/winui-shell/` (build binlogs, UIA/launch output, screenshots,
source-reference excerpt) and `artifacts/winui-state/` (focused tests/binlog).

## Controller and external-content foundation follow-up

- `scripts/Build-OverlayPlatform.ps1` builds only the existing ABI v4 DLL. Release
  build and 24-export inspection passed. It neither initializes hardware nor
  installs the copied GameInput redistributable. Third-party ViGEm source emits
  existing encoding warnings.
- `OverlayPlatformClient` now provides exact ABI structs, initialized outputs,
  stdcall imports/callbacks, SafeHandle lifetime and serialized access. Its 15
  focused fake-native tests pass; they cover callback/disposal and failure paths,
  not physical delivery.
- The WinUI input pump and opt-in controller validation page build with zero
  warnings/errors. User authorized stopping native candidate PID 30152; its logs
  were preserved under `artifacts/winui-controller/`. It had no controller-isolation
  command-line flag. WinUI PID 26144 receives connected GameInputVisibleLease frames.
  Physical navigation/single-A/Guide acceptance is pending. Do not run the old
  native candidate concurrently with this controller owner.
- External-surface validation uses WinUI WebView2 with trusted literal animated
  content. Actual screenshots show a XAML button and ContentDialog above the
  external surface. Four scripted checks pass: ready, pointer-open, Escape focus
  restoration, and reopen. The widget-local modal component is NOT implemented by
  this fixture; neither actual playback nor pinning/rounded clipping is proven.
- Packaged desktop launch arguments are consumed from both activation data and the
  process command line. Project-mode winapp launches now explicitly pass
  `-p Platform=x64` after adding a managed project reference.
- Removed the unrelated template systemAIModels capability. The shell exposes only
  x64 until the native boundary gains supported additional architectures.

### Physical controller feedback and activation correction

Follow-up source audit found a separate confirmed input-starvation defect: the
WinUI pump publishes window state each poll, while the native setter reset its
edge tracker on every unfocused publication. Each background-visible read then
only primed, producing no A/navigation edge despite a connected visible lease.
The setter now applies a shared tracker transition operation: actual hide or
focus loss retires history; unchanged visible/unfocused state preserves it.
The pump can continue publishing current ownership without a duplicate managed
state cache. Isolation close/selection behavior is unchanged.

The focused production-DLL executable passes 119 checks, including an actual
exported-setter regression and deterministic A/D-pad/stick press/repeat/release
checks. The identical executable against the previous DLL fails specifically at
`repeated background state must not re-prime the production input tracker`.
Evidence: `artifacts/winui-controller/idempotent-state-*.log`. The requested full
`-PlatformInteropTestsOnly` selector stopped first at the preexisting
`GameInputQueryRuntimeTests` device-enumeration/runtime-identity check; that gate
is reported separately, not waived or repaired. The focused executable was then
compiled/run directly against the newly built production DLL.

The replay fake does not model native window-state resets, so its earlier pass
could not catch this defect. Foreground activation/restore policy and duplicate
WinUI/native physical gamepad delivery remain separate unaccepted gates. No
additional physical check was requested while the user was away. The corrected
DLL is in `src/OverlayHost/out/Release`; rebuild the WinUI platform artifact
before the next candidate launch.

User reported: Guide hide/show works, but native navigation/A required clicking a
button first. This is a failed controller-native startup gate, not acceptance.
No further physical checks are requested while the user is away.

The validation page previously called Focus during Loaded and ignored failure.
It now queues focus entry after loading/activation/show, verifies the framework
result, and preserves an already focused descendant. Cold-launch automated check
(PID 23444) observed First focused without any click, Right moved to Second,
Enter changed the shared command count to Second: 1. Native status reported
connected GameInputVisibleLease and entry retained. Keyboard automation does not
prove physical controller delivery; keep the original failure open until an
end-to-end native-frame/activation test and later physical acceptance establish it.
The validation process was closed via Shell.Close after checks.

## Controller replay and virtualized collection follow-up

`--replay-controller` now supplies deterministic native-shaped frames through the
same PlatformInputPump/OverlayPlatformSession, actual WinUI focus traversal,
command path and Guide hide/show handler as the live fixture. It does not load the
native DLL or read hardware. Cold focus, Right, one A, hide/show, retained Second
focus and a second A passed with no injected mouse or keyboard. This narrows the
startup-focus check but does not supersede the user's physical delivery failure.

A `KeyedObservableCollection<T>` preserves per-key binding wrappers within a data
generation and emits granular standard notifications. Twelve new state tests pass
(27 combined). Current code does not claim atomic notifications or constant-time
arbitrary reorders. See its README for limits.

`--validate-collection` connects this model to WinUI's higher-level ItemsView with
UniformGridLayout and ItemContainer. No custom virtualizing layout/focus engine.
With 1,000 items, direct visual-tree inspection counted 13 realized containers at
the top and 14 at the bottom. Scrolling reached Item 999, appending reached Item
1029, and payload update changed the existing container's title. These are
functional observations, not frame-time or full-product acceptance.

**Failing regression:** focus Item 0, then send fixture F7 to prepend 30 records
without transferring focus to a toolbar button. UIA retained the same focused
item/container identity but reported it offscreen with zero bounds afterward.
The focused item must remain visible under our cursor contract. Evidence is in
`artifacts/winui-collection/prepend-before.json`, `prepend-after.json`, and
`prepend-search.json`. Investigate documented ScrollView anchoring and keyed
logical index mapping before implementing the admission policy. Do not mark the
collection complete or mask this by invoking ScrollIntoView from a test.

Transport correction: current Playnite is a full-trust application widget, not a
sandboxed worker. Use its real application path for UI parity and a real dotnet
worker (e.g. Clock) for the separate AppContainer proof. Existing managed bridge
facade is reusable but needs typed host effects/media/artwork correlation extensions;
see `winui-widget-transport.md`. Prior plan references to sandboxed Playnite are
not accurate descriptions of its manifest.
A bounded follow-up set ScrollView.VerticalAnchorRatio to 0.5 (documented item
anchoring instead of the extent-edge special case). The same prepend still left
Item 0 offscreen. That experimental setting was removed; it is not a fix.
`anchor-before.json` / `anchor-after.json` retain the result. Next inspect logical
key/index mapping and native ItemsView bring/anchor lifecycle with a repeatable
regression, rather than accumulating unproven scroll settings.

## Real bridge and installed Clock round trip

`OwnedBridgeProcess` owns a random-pipe bridge child with explicit installation,
settings and installed-catalog roots. It drains bounded diagnostics, observes
early exit, and disposes the session before awaiting/terminating only its child.
Startup cleanup preserves the original connection/cancellation error. Transport
shutdown now interrupts blocked writers, write-gate and capacity waiters and
drains active requests before disposing synchronization; concurrent disposal
shares one task. Eighteen session tests pass, including the stalled-peer cases.

The first real catalog request found an outdated client contract: the producer
returns `isComplete` alongside revision/widgets. The facade now preserves that
field and tests pending-to-complete discovery. A real Power initial snapshot
(read-only, no action) succeeded and the owned bridge exited after disposal.

Shared framing, catalog descriptors and style DTOs now live in
`WidgetBridge.Contracts`. The client dependency graph includes only Contracts,
Protocol, SDK and Styling; it no longer pulls the executable, runtime, broker,
catalog or providers into WinUI. Wire shapes/namespaces are unchanged. Managed
consumers need rebuilding because types moved assemblies. Nine focused bridge
checks passed, including sandbox/full-trust managed session paths. The full-trust
check initially lacked its required Release fixture; building it resolved setup.

The Clock source was packaged and installed through wrail into the isolated
`artifacts/winui-clock/catalog`, with a separate settings profile. A new opt-in
WinUI fixture uses the real bridge/catalog/worker and validated declarations;
it never loads widget code in the frontend. Ten UIA checks passed: first snapshot,
cold focus without injected input, real generic worker process, three Refresh
actions each advancing exactly one snapshot, stable focused control through all
updates, and frontend/bridge/worker exit on close. The worker exit log records
cooperative stop and exit code zero. Final screenshot was visually inspected.
Frontend analyzer build: zero warnings/errors.

This is deliberately a **Clock-only integration fixture**, supporting its four
node kinds and basic typography, not the production renderer or theme migration.
Unsupported structure fails visibly. The shipping adapter, controller shortcuts,
media/artwork, scopes, modals and collection semantics remain incomplete. No
physical controller acceptance was inferred from UIA or deterministic replay.

Pixel-check caveat: while idle, both Clock and the existing shell/replay showed
UIA state without rendered pixels. A harmless synthetic Escape restored shell
pixels; the normal transparent Clock subsequently rendered correctly. Temporary
opaque-window experiments were removed. This suggests idle desktop presentation
suspension, but its cause is not proven; do not classify blank captures as a
widget rendering defect without checking actual presentation state. The scripted
capture sends Escape only after its no-input initial-focus assertion.

Further collection investigation tried public `StartBringItemIntoView` and
`IKeyIndexMapping` (recognized by `ItemsSourceView.HasKeyIndexMapping`). Neither
provided correct prepend focus/viewport retention in the tested combinations.
All unsuccessful adapters/scroll corrections were removed. Source experiments
remain only in `artifacts/winui-collection/anchor-experiment`; no claimed fix.

Evidence: `artifacts/winui-bridge/` contains build/test binlogs and real bridge
smoke output; `artifacts/winui-clock/results.json`, `clock-verified.png` and
`profile/overlay.log` contain the real worker/UI round-trip evidence. All changes
remain on the isolated migration branch; no main merge, push or release.

## Native control retention and typed host events

`Presentation/WidgetViewPresenter` is the shared beginning of the declaration
adapter. It creates standard WinUI controls, retains them by scope/element/kind/
item ancestry within a worker owner, and reconciles changed child membership.
WinUI still owns measurement, layout, painting, directional focus and control
behavior; this is not a custom layout/virtualization/frame scheduler. The real
Clock fixture now uses it, removing its separate four-kind renderer. The native
controller fixture/replay also uses the same presenter for entry, directional
focus and activation instead of creating its own test buttons.

Currently supported: Stack, Row, ordinary Scroll, Text, Button, standard
ActionSurface, Progress, LoadingIndicator and Spacer, with basic resolved font
size. Control identity survives ordinary content updates and reparenting. Worker
replacement creates fresh controls and retires saved command tokens. Inactive
scope controls cannot dispatch; returning to a scope can restore its still-valid
focus. Entry work is coalesced and ordinary updates do not take focus from outside
the widget. This remains an incomplete adapter: full styling/themes, explicit
focus groups/neighbors/shortcuts, images/icons, modals, collections, media,
responsive layout and the remaining controls still need implementation. Unsupported
node/layout families fail before membership mutation; they are not substitutes
for migrated product features. WinUI object creation/property failure is not an
atomic transaction guarantee; the surface owner must show its failed state.

Sixteen scripted UI checks pass through the shared presenter: authored initial
focus on the second button, single activation, insertion/reparenting with stable
control and focus, scope switch/return, inactive-scope rejection, external focus
retention, disabled-target fallback, new-owner initial focus/new identity and
rejection of a retained retired command, and two deliberate presses while the
first admission remains pending. WinUI command execution does not introduce a
second single-flight queue over the worker's action policy. Real Clock round trip still passes all
ten checks. Native-shaped controller replay passes through the shared presenter,
including Guide hide/show and retained focus. These remain automated checks, not
physical controller acceptance. Final frontend analyzer build has zero warnings
and errors; evidence is under `artifacts/winui-controls/`.

The managed session now publishes typed catalog/appearance revision notifications
and host effects. It accepts the actual producer's initiation timestamp and
activation target fields, rejects stale/replayed effects, preserves host-private
native identity and supplies authority revalidation after UI dispatch. Unknown
optional effects are explicitly Unsupported. Retirement/restart boundaries prevent
old effects from reviving. The host must still enforce its visible-session policy
and revalidate HWND/process/class identity; receiving a typed event does not
execute it. Twenty-seven session tests pass, including exact producer shapes,
malformed target rejection, stale/replayed effects, retirement and monotonic
revision notifications. No service/provider dependencies were reintroduced.

### GridView comparison: retention remains unresolved

The opt-in `--validate-gridview` fixture tests standard GridView/ItemsWrapGrid over
the same keyed ItemsSource, without custom offsets/anchor compensation. It also
fails prepend and leading-eviction retention. Final isolated run: seven checks
pass, four fail. Item0 after prepend keeps its logical focus but moves y=4 to
y=1180 at offset zero. Removing thirty leading items while Item600 is focused
leaves its container offscreen and the viewport advances to Item628. Append and
same-key updates pass independently; visual-tree container counts remain bounded.

This rules out a control swap as the collection fix. The fixture and
`scripts/Test-WinUiGridView.ps1` preserve the regression, not an accepted production
collection. Evidence and six inspected screenshots are in
`artifacts/winui-gridview/isolated-cases/`. Resolve the collection admission/data
virtualization and anchoring contract before wiring it to Playnite. No compensating
scroll code or unsuccessful adapter was added to the shared presenter.

## Sparse indexed data and artwork demand identity

`Collections/IndexedItemsSource<T>` now implements WinUI IList/IItemsRangeInfo for
one exact-count query and one consumer. It keeps sparse stable binding slots,
requests visible/tracked pages asynchronously with bounded concurrency, validates
query/request/index/key identity, and releases payload without structural item
removal. A new query replaces the source. The range reader remains trusted internal
code that must honor cancellation and bound its work; SDK/worker range transport,
retry UI and the production widget path are not connected yet.

The new eight-check `--validate-indexed` integration run passes. Native ListView
range callbacks were observed directly. One million logical rows used at most
96 resident slots and 21 realized containers without enumerating the full source.
Down/up navigation remained usable while an adjacent buffer request was held.
Releasing it increased completed loads while preserving focus 600000, y=20 and
scroll offset 43200172 exactly. Return traversal evicted distant payload and a
later deep visit performed another load while Count stayed unchanged. Screenshot
inspection and analyzer build passed. This demonstrates native data virtualization
and admission behavior, not production-widget frame pacing or controller acceptance.
Evidence: `artifacts/winui-indexed/authority/`.

An additional native anchoring experiment did not provide a reliable cursor fix.
An explicit AnchorRequested preference and interior ratio initially preserved a
top item, but broader cases still displaced it after prepend. All experimental
code was removed; `artifacts/winui-collection/anchor-selection/` retains source and
observations. No guessed offset correction or layout retry loop was introduced.

The SDK/service design direction and real provider capability analysis are in
`winui-indexed-collections.md`. YouTube's finite browse snapshot is an indexed
candidate. Playnite needs final display filtering normalized before advertising
its indexed count. Spotify's unqualified live search remains discovered/cursor
data. Arbitrary query membership changes and opaque before-origin insertion remain
distinct unresolved cases; the indexed cache path does not claim to solve them.

Artwork correlation now uses unique demand IDs with runtime/presentation authority,
so cancellation, reordering and late same-handle replies cannot satisfy replacement
demands. Local admission/completion has a configurable timeout and exact authority
is rechecked around decoding. The old 512-character diagnostic-text limit no longer
truncates/rejects normal Base64 artwork; the encoded-artwork limit applies instead.
All 34 session tests pass, including seven new demand/race/timeout cases. Server-side
decoding still lacks per-demand cancellation; already-sent transport requests remain
bounded/correlated until reply or shutdown. This distinction is intentional and
documented, not a claim that local cancellation stops provider work.


## Worker-side indexed declaration foundation

The migration SDK now registers immutable exact-count queries and renders only
requested bounded ranges. Protocol 60 declarations carry no inline items. Shared
range validation resolves the actual main/pinned parent and validates its source,
scope, occurrence keys, response correlation, presentation ownership and bounds.
Normal parent snapshots still reject inline indexed payloads. Item declaration
lists are frozen with the same helper used by retained collection declarations;
source/collection/key-scoped IDs distinguish independent placements.

The SDK limits actual provider tasks to four per source, including cancelled or
timed-out tasks until they terminate. Query/content changes retire old readers;
late content and cancelled rendering cannot publish. The public API baseline is
updated. The 128-check SDK console suite passes, including six new indexed groups.
This is not an end-to-end production collection: bridge/session demand, row
leases and actions, native templates, and widget adoption remain outstanding.


### Concurrent range transport and controller correction

Worker/process-client range reads now have a separate four-request lane, explicit
exact-demand cancellation and bounded teardown. Slow range reads leave the serial
input/render/lifecycle path available. Original response correlation survives
cancellation until its terminal reply drains; cancellation never targets a
replacement worker. Both worker trust tiers share this implementation. Six new
runtime scenarios and nine existing focused regressions pass. SDK API compatibility
passes all fourteen tests; generated protocol parity passes 140 constants. Bridge
and WinUI analyzer builds pass without warnings/errors. No indexed row action or
artwork authority is enabled by this transport alone.

The visible-but-unfocused controller defect has an isolated regression: the WinUI
pump publishes window state each poll, but the native setter previously reset input
tracking on every unfocused publication. The setter now retires tracking only on a
real hide or focus-loss transition. The production DLL passes 119 focused ABI/policy
checks; the same executable against the prior DLL fails the repeated-background-
state regression. The broader native selector stopped at the existing GameInput
query-lifetime/device-enumeration check before this executable; that result remains
separate and uncorrected. Evidence is in `artifacts/winui-controller/`. The platform
artifact was rebuilt and copied by the subsequent WinUI build. Physical Guide
reopen/navigation/A acceptance remains pending; no validation window was launched.


## Bridge/session indexed demand and native focus policy

The bridge now routes indexed reads and exact cancellation independently from its
ordinary per-widget FIFO. It bounds reads to eight total/four per widget, captures
an already-running worker ordinal without startup/recovery, releases the operation
gate during provider work and holds a publication lease through actual completion.
Cancellation waits for read admission, eliminating cancel-before-registration.
Five new bridge scenarios pass, including real bridge-pipe-to-worker traffic;
eleven existing focused dispatcher/retirement/session regressions also pass.

The presentation-session API owns demand identity, timeout, capacity and exact
frame/projection cancellation. Its forty-seven tests pass. New coverage includes
out-of-order replies, replacement authority, hidden surfaces, disposal, preserved
data across unrelated snapshots and modal opening, and stale-frame cancellation
that cannot affect a replacement frame. Callers still own surface lifetime tokens.
The result remains data only: no row action/artwork lease or native item template
has been enabled yet. This connection is not production-widget acceptance.

The shared WinUI presenter now maps authored neighbors (including group targets)
to native XYFocus properties. Native directional entry honors remembered/default
group children, and searches stay within the active scope. One-shot group requests
can wait for data, are not replayed on ordinary updates, and retire on newer user
navigation, activation, pointer input or explicit withdrawal. Worker replacement
clears old memories. Fifteen real WinUI focus-policy checks and all sixteen prior
presenter checks pass; analyzer builds pass. Test windows were closed. These tests
exercise native UI and synthetic navigation, not physical controller acceptance or
realized lazy-row focus, which remain outstanding.


## Captured item semantics in the SDK

The indexed source options now require a typed captured-item `OnAction` callback
and optionally supply a captured artwork resolver. Internal semantic leases retain
bounded frozen rows and resolve the current parent input-owner path at admission. Shared logical binding
resolution preserves row versus ancestor shortcuts, menu option availability,
nested scopes, pinned ownership and modal input suppression. Typed item execution
uses the existing serial action queue with query-aware repeat identity; no second
queue was added. Already-admitted actions retain their original values when visual
or data leases retire. Artwork lookup remains separately bounded and rejects late
retired results without discarding valid parent artwork merely because a modal is
active.

Validation: 134/134 SDK checks, 14/14 API compatibility checks, 6/6 indexed-runtime
and 5/5 indexed-bridge regressions. The first runtime invocation used `dotnet` with
the DLL, which made its self-spawning fixture attempt to launch dotnet as a worker;
rerunning the built test executable passed. This was an invocation correction,
not a production-code fix. No candidate was installed/launched in this pass.

The semantic lease API is currently internal to the worker SDK. Runtime, bridge
and session delivery/release/interaction routes plus native item templates and
first-party widget conversion remain incomplete. Existing range data still grants
no remote row action/artwork authority. The migration remains active and unmerged.


## Worker/runtime semantic lease transport

The actual worker pipe now supports acquisition/release, typed item input and
independent artwork requests. An internal disposable client lease owns the exact
captured process session, prevents use after disposal and never starts/reaches a
replacement worker. Cancellation after delivery and failed reply writes reclaim
worker retention. Range/artwork lanes share bounded cancellation/drain mechanics
while retaining independent four-request budgets; input remains on the existing
serial queue. Release and cancellation acknowledgements are strictly typed.

A code review corrected the distinction between retained data and current input:
leases retain immutable row data, not a frozen parent shortcut path. Input carries
a current worker snapshot sequence and scope. Page-level bindings are resolved
from that snapshot; stale input cannot execute a changed page command. The bridge
must still compare the user's origin frame with its current declarations before
forwarding that sequence. An admitted action may itself replace its query without
invalidating its admission acknowledgement.

Twelve indexed-runtime scenarios pass (six new lease scenarios), including real
worker acquisition/invocation/artwork, cancellation/reload, release during artwork,
worker replacement, late cancellation and failed delivery. Ten existing focused
action/teardown scenarios, five indexed-bridge tests, forty-seven presentation
session tests and the 134 SDK checks also pass. The replacement-worker test first
attempted unload while Interactive; its setup now performs the required Background
transition before unload. No production workaround was added for that test error.

Bridge/session semantic-lease tables and native interactive item templates are
still unimplemented. Existing bridge range reads remain data-only. No candidate
was installed/launched and nothing was merged to main.

Final transport review found that a rejected duplicate acquisition cancelled its
original successful demand and released the existing lease. Terminal worker
rejections now bypass withdrawal; cancelled or unadmitted successful replies still
withdraw their exact demand. A real-worker regression verifies the original row
action and artwork remain valid. All thirteen indexed runtime checks pass, and the
WinUI analyzer checkpoint build passes without warnings or errors.

## Interactive indexed data path and native content refresh

Bridge and presentation-session ownership now connect the runtime's semantic
leases end to end. The registry validates origin/current row and parent bindings,
keeps unchanged data across modals, and retires exact owners on query/projection/
worker changes. Active input/artwork operations keep registrations alive until
they drain. The session exposes disposable immutable row ranges with typed input
admission and artwork APIs. Independent provider/cancellation/control admission
and bounded release batches prevent bulk eviction from saturating the bridge;
separate release admission/reply deadlines cover a stalled ordinary control lane.

Validation: 64 session tests, 13 indexed bridge scenarios (including both pipes
and a real worker), four existing dispatcher and eighteen registry scenarios pass.
The full WinUI analyzer build has zero warnings/errors. New fixture errors were
corrected without changing production behavior: opaque test artwork lacked an
accessibility label, and an end-to-end test used the shortcut label overload
instead of the explicit `actionId` argument.

The native indexed source now owns/retires asynchronous page lifetimes and
refreshes content without replacing logical slots. Thirteen native UI checks
pass, including seven dispatcher lifetime scenarios. At index 600,000, refreshed
content preserved focus and exact scroll offset; the million-row source retained
96 slots and 21 native containers with no enumeration. This is a source fixture,
not production-widget performance evidence.

Evidence: `artifacts/winui-indexed/lease-final-*.log`,
`artifacts/winui-indexed/lease-complete-build.log`,
`artifacts/winui-indexed/native-ownership-ui/`, and
`artifacts/winui-session-leases/admission-tests.log` with corresponding binlogs.
Validation windows were closed. No controller physical acceptance was requested.

Next: connect these leases to native item templates, constrain collection layout,
validate navigation to unrealized items, and adopt real YouTube/Playnite sources.
The remaining styles, modals, media, pinned surfaces, animations and shell work
are still required for the full migration. Nothing was merged to main.

## Native interactive collection templates

Owned row ranges are now connected to the shared WinUI presenter through native
ListView/GridView controls and compiled item templates. Rich row contents reuse
the ordinary declaration renderer; native containers own interaction and focus.
Rows receive the bridge's computed styles, and opaque artwork shares bounded
provider admission with range loading. Basic native Grid auto/star layout keeps
fill collections constrained. Content replacement retains old image pixels until
new artwork is decoded, and realized row resource work drains during disposal.

An actual semantic-controller probe established that pure spatial focus search
could lose moves at realization boundaries (60 Down inputs reached item 49).
The collection adapter now resolves the logical target, coalesces frame bursts,
and asks WinUI to realize/scroll/focus its native container. Current probes reach
item 60 after 60 moves, item 50 after reversing 20 moves from 70, and preserve the
grid column. Window close now enters the same asynchronous cleanup path from
both the close button and system close; test bridge processes terminate cleanly.

Validation uses `--indexed-validation-pipe` with the test assembly's
`--serve-indexed-validation` mode, never a physical controller owner or installed
community widget. The full session suite passes 71 tests; indexed bridge checks
pass 15 tests. Existing native controls/focus/source checks pass 16/15/13 tests.
The native widget script covers eleven checks, including actual invocation count,
styled artwork, deep refresh, forward/reverse/burst navigation, cancellation of
superseded focus and GridView.
Evidence is under `artifacts/winui-indexed/widget-ui-checkpoint/`,
`templates-checkpoint-build.log`, `worker-checkpoint-tests.log`, and
`artifacts/winui-session-leases/styles-final-tests.log`.

The presenter remains incomplete: full styles/pseudo-state presentation, live
theme changes for retained leases, grouped collections, logical collection focus
requests, modals, popups, media, pinned surfaces and production-shell integration
are not proven. Production YouTube/Playnite sources still need conversion and
physical/performance validation. The collection document records concrete YouTube
playback and SDK contract requirements discovered in this pass.

## Logical collection entry and public author testing

Protocol 61 extends the existing one-shot group entry with an exact indexed query
occurrence. The host waits for native realization and verifies the loaded item key;
stale generations and wrong keys cannot focus another row. Initial collection focus,
explicit native neighbors and remembered entry focus native item containers. A
bounded identity-only history survives removed page controls. Row and parent actions
receive worker-generated logical focus metadata. Public indexed author test helpers
exercise real SDK acquisition, queued actions, cancellation and artwork paths.

Validation: 14 indexed SDK tests, 15 indexed bridge tests, 13 native widget checks
(including a ten-case logical-focus sequence with deliberately delayed row data),
and 15 existing native focus-policy checks pass. The WinUI analyzer build is clean.
Protocol header and SDK API baseline are regenerated; 14 SDK compatibility tests
pass. Native evidence is under `artifacts/winui-indexed/focus-disabled-final-ui/`
and `focus-policy-regression/`. One fixture initially used reserved View for a
test command; changing that fixture to RightStick restored valid worker startup.

Physical review confirmed navigation after Guide reopen but found a Guide problem
after Alt+Tab. The window remained alive and visible behind another application.
The validation shell now hides only while it owns foreground; otherwise Guide
requests show/activation. Live logs proved Guide delivery while both Activate and
a direct foreground request failed to reacquire the visible window. The native
host's existing bounded foreground acquisition is now shared through platform
ABI 5, with same-process/thread HWND validation and actual ownership confirmation.
Thirteen native ownership checks and nineteen managed tests pass. The WinUI caller
uses that adapter. A live physical Guide trace now confirms reactivation from a
visible background window to the WinUI process, then successful hiding on the next
press. The user confirmed activation/navigation/A/hiding, then reported that the
focus cue disappeared after Alt+Tab. Foreground acquisition now leaves control
focus to each frontend: the legacy host focuses its HWND, while WinUI explicitly
reasserts Keyboard focus on its existing leaf. Controller replay passes with
HasKeyboardFocus true on the retained Second button. The user then physically
confirmed that the focus highlight and input both work after Alt+Tab/Guide.
This closes the validation-shell activation/focus defect, not production controller
integration for all widget surfaces. Earlier controller
replay passed cold entry, navigation, one action per press, hide/show and retained
focus. Bounded asynchronous diagnostics preserve native Guide delivery and actual
foreground/visibility; activation is never inferred from a show request alone.
Evidence: `artifacts/winui-controller/shared-activation-physical.log`. The legacy
host now calls the same adapter but was not rebuilt during this checkpoint; the
installed native product remains unchanged.

Full migration remains incomplete. Grouped collections, production widget adoption,
complete styling/themes, modal/popup integration, media/pinned surfaces, global
animations and the production shell still require implementation and validation.

## Native grouped range demand

Executed grouped/flat/adapted probes confirm that CollectionViewSource preserves
indexed inner access but hides IItemsRangeInfo from ListViewBase. The small
NativeGroupedRangeView adapter restores direct range admission while delegating
the actual ICollectionView behavior to WinUI. One flat indexed query supplies
all group slices, avoiding per-group retention and provider budgets.

The three modes pass 7/9/10 UI checks, with five additional adapter contract
scenarios. At 30,000 logical items, deep focus/refresh/reversal required no full
enumeration, with 17 observed native containers and a peak of 96 data slots.
This is correctness evidence; real widget artwork and controller performance
remain unproven. See `winui-grouped-collections-assessment.md` and
`artifacts/winui-grouped/checkpoint/`. Public grouped declarations, production
row/header templates and first-party adoption remain next work.

## Grouped declarations connected to worker rows

Protocol 62 and the SDK's `.Grouped(...)` now describe bounded contiguous groups
over one flat indexed query. The production collection presenter uses compiled
header templates, native observable group slices and the proven range adapter.
List and grid mode share the same leases; partial/empty groups preserve meaningful
controller targets. Header text changes retain view/focus/offset. SDK structural
updates now transport both group metadata and indexed source descriptor changes
atomically; the prior differ omitted descriptor changes.

Validation: 15 indexed SDK checks, 15 indexed bridge checks, 14 SDK compatibility
checks and 17 real-worker native UI checks pass. Native tests include seven grouped
focus/header cases and sixty list moves across group headers. Analyzer build is
clean; API baseline and protocol header are regenerated. Evidence:
`artifacts/winui-grouped/worker-checkpoint-ui/`, `sdk-groups-tests.log`,
`bridge-tests.log`, `compatibility-tests.log`. An earlier run observed an unexpected
viewport enlargement before grouped tests and failed its size assertion; a targeted
window/layout inspection and fresh full run did not reproduce it. That failed run
is retained under `worker-ui/`; it is not classified as a proven grouping defect.

The public group metadata and production row/header integration are complete for
this contract, but widget conversion and full theme/animation/overlay parity remain
outstanding. The new Playnite captured-service query path is undergoing its own
tests and has not replaced the installed widget's cursor UI.

## Playnite captured application queries

The application service now captures immutable ordered query values and exposes
bounded random-range projection with captured artwork authorization. Projection
does not populate the old retained-cursor artwork registry. Catalog admission and
four-slot artwork work are independent, sharing the existing bounded byte cache.
Disposal cancels/drains these lanes and rejects late legacy projection. The existing
Bridge integration, query rules and cursor UI are preserved while conversion proceeds.

All 28 PackageRuntimeTests pass, including nine new tests for random access,
immutable values, authority, eviction, independent refresh, concurrency,
cancellation and retirement. Evidence: `artifacts/winui-playnite-capture-retirement-tests.log`.
This is service correctness evidence, not WinUI Playnite appearance/performance.
Next: final widget projection, captured actions and logical modal return targets,
then complete presenter support and actual overlay integration.

## Pending indexed focus across presentation changes

Review found that rebuilding a collection for layout/group partition changes
cancelled an already-consumed exact entry while its row was loading. The collection
now retains that logical intent when its source/query owner remains unchanged and
restores it after rebuilding native presentation. Query replacement, disabled
scope, request withdrawal and superseding input retain their cancellation behavior.

The real-worker sequence now includes delayed exact entry during list/grid changes
and regrouping; all 17 UI checks pass with twelve logical-entry cases. The 15
indexed bridge tests and clean WinUI analyzer build also pass. Evidence:
`artifacts/winui-grouped/entry-checkpoint-ui/` and `entry-final-bridge-tests.log`.
A test fixture initially used a reserved D-pad shortcut; changing its test-only
command to X restored valid worker startup. A separate interrupted artifact write
was an inspection/file-sharing conflict, not a UI result; the clean full run is
the accepted evidence.

## Retained native background and focus-presentation checkpoint

Native presentation surfaces now consume the shared logical source policy. The
background uses an ImageBrush so artwork intrinsic size cannot enlarge the collection
viewport. Focus fragments are presentation-only subtrees. Indexed contributions retain
bounded data through the existing range source, independently of visual realization;
content refresh updates the same slots and removal releases demand.

Artwork identity includes both the range lease and exact row key. A real-worker
regression verifies adjacent rows sharing one lease and handle decode different images,
and that a delayed former row cannot replace the newly selected artwork. Missing
artwork clears obsolete pixels. Session artwork authorization now includes authored
focus/default fragments.

Validation: analyzer build clean; 73 session tests, 15 indexed bridge tests, 18 native
worker UI checks (including eight surface cases), ten standalone surface-policy checks,
16 explicit retention checks and seven existing ownership checks passed. Screenshot
review confirms the fixture's constrained list and presentation placement; it is not
a production-theme or performance acceptance. Evidence: artifacts/winui-surfaces/.

This checkpoint implements basic selection, retention and layout. Full WRSS styling,
depth, animation and production-widget adoption remain incomplete. Test windows closed.
Parallel work continues in separate Playnite, shared-controls and styling/motion
worktrees; changes integrate only into codex/winui3-frontend, never main.

## Native glyph adapter checkpoint

ControllerGlyph and semantic Icon declarations now use native FontIcon controls.
Controller prompts reuse the existing Kenney Xbox/PlayStation fonts and PromptFont
Guide fallback, with their licenses packaged; no system font registration or new
controller reader is introduced. Host family changes update existing presenters,
including realized fragments, without changing worker snapshots. Unknown input
family retains the last observed family. Semantic icons use Segoe Fluent Icons;
package SVG transport/presentation and final themed glyph sizing remain outstanding.

Seven native glyph checks pass across all 45 declared controller/semantic symbols,
including family changes, accessible labels, fallback font selection and noninteractive
behavior. Packaged font rendering was inspected in artifacts/winui-surfaces/glyphs-fixed.png.
The unstyled validation gallery is not production-theme acceptance.

## Parallel integration pass, 2026-09-28

- Production Playnite Browse now uses a complete captured indexed query instead of
  its retained cursor window. Query/authority/source publication is coordinated;
  exact game targets and logical modal return survive content updates. The integrated
  widget suite passes 149 tests; the application lane also passes 28 runtime tests.
  Home still uses its previous rail and awaits conversion. This sample requires the
  indexed frontend; it has not been installed over the native product.
- Native Select passes 17 lifecycle/input checks. A shared normalized-button entry
  gives popups precedence, sends indexed actions through owned row leases, and uses
  the existing worker input pipeline for ordinary shortcuts. The real-worker input
  probe passes its three checks.
- Widget-local modals pass 21 standalone native checks and screenshot review after
  correcting fixture geometry/default panel fill. The real-worker indexed-parent
  probe is still under investigation: one run did not admit its first activation;
  standalone success does not close the indexed-parent gate.
- Motion policy preserves global settings and uses native composition batches.
  Nine policy tests and 18 actual compositor/focus checks pass. Production section,
  modal and focus wiring remains incomplete; no smoothness acceptance is claimed.
- The real Clock worker still passes ten transport/action/focus/shutdown checks.
  Its presenter now binds artwork/session resources and retires before the bridge.

All changes remain on codex/winui3-frontend. Main, installed native packages and
controller ownership are unchanged. Shared styling and indexed-modal investigation
continue in their isolated lanes. No production WinUI candidate is ready yet.

## Integration validation follow-up and open row lifetime defect

Native styles pass 24 checks after desktop ThemeSettings and resource ownership
corrections; the supported subset and omissions are recorded in winui-styles.md.
The combined indexed test passed 20 checks before strengthening the final rendering
assertion. Screenshot inspection showed the final replacement-query row could have
focus but no text/artwork. The new 25th modal assertion reproduces that defect.

A diagnostic run found the row loaded, its semantic lease current, its payload key
correct, but its Content null. The failure is not missing game data. Delaying Unloaded
retirement did not resolve all repeated-modal cases. A stable parent-layer experiment
also failed the full lifecycle sequence and was removed from production changes;
its patch is retained under artifacts/winui-surfaces/stable-parent-experiment.patch.
The integration source retains the strengthened failing regression and bounded row
metadata diagnostics. Do not call the combined modal gate green or launch a physical
candidate based only on the earlier 24 modal checks.

Evidence: styled-combined-ui/17-indexed-modal.json, modal-row-diagnostic.json,
row-lifetime-ui/17-indexed-modal.json and modal-entry-ui/17-indexed-modal.json under
artifacts/winui-surfaces. The earlier intermittent first-A observation is also still
unresolved. Continue by inspecting the native item-template/rendered-row lifetime,
including why a live row can lose its presenter while its data owner remains valid.
Main and the installed native product remain untouched.

## Indexed modal lifetime and keyboard follow-up, 2026-09-28

The missing row content was caused by queued native Unloaded notifications arriving
after the same row was loaded again. Row retirement now checks its current IsLoaded
state; explicit disposal and genuine unload still retire it. Temporary per-row/image
tracing was removed after confirming the event ordering.

The subsequent repeated-modal timeout was a different defect, not missing artwork:
the live Image contained its decoded source, but the next A arrived during a content
refresh while the displayed row's lease was retired. The collection now retains one
bounded activation intent for the same focused item/action, then uses the replacement
lease. Navigation, another button, hiding, changed scope/query/action/availability,
or a two-second expiry cancel it. SDK/session stale-lease checks remain unchanged.

Fixture row images now use yellow/dark checkerboards distinct from the blue parent
background. Modal assertions verify decoded 8x8 cover images with nonzero displayed
size, and timeout diagnostics identify the actual pending condition. Visual inspection
of the final replacement-query row confirms both text and checker artwork.

Integration validation: analyzer and worker builds have zero warnings/errors; 16
indexed bridge tests, all 20 combined native worker checks (including all 25 modal
assertions), and 11 dedicated deferred-activation checks pass. One combined attempt
was interrupted by another worktree's package deployment and was inconclusive; its
replacement run passed. Evidence: artifacts/winui-surfaces/combined-row-fixed/,
integrated-activation/, and integrated-indexed-tests.log. Scripts wait for initial
worker readiness and preserve the original failure when cleanup finds an exited app.

Native TextEntry/controller keyboard is integrated with guarded commit/cancel,
native password semantics, local edit state, authority revocation and live prompt
fonts. Its 26 native checks also pass on the integration branch. Details and remaining
physical/production/theme validation are in winui-text-entry.md. These are correctness
fixtures, not production-widget performance or full migration acceptance. Main and
the installed native product remain untouched.

## Playnite Home indexed adoption, 2026-09-28

Home now uses captured indexed membership and demanded row rendering, with no Home
cursor/load-page path. It preserves the manual/title-match prefix, provider ordering,
unavailable saved entries, captured action/artwork ownership, and exact deep-item modal
return. Same-membership updates retain logical query identity; refresh explicitly
retires obsolete launch evidence. Author notes and migration-specific tests are updated.

All 155 Playnite widget tests pass on the integration branch (zero failures/skips).
Evidence: artifacts/winui-surfaces/home-integrated-tests.log and its unique build log.
This does not validate the actual Home rail's WinUI rendering or performance; production
presentation support and shell integration remain outstanding. No sample was installed
over the native frontend, and no changes were merged into main.

## Shell production checkpoint, 2026-09-28

Production pins and semantic memory are integrated. Three native presenters and a
bounded 256-entry semantic history have separate ownership and retirement rules.
The real forced-eviction driver reached an offline Playnite Library because Playnite
was not running, so that production restoration gate remains unverified. Presenter
recreation fixtures and the 46-check managed shell suite passed at their checkpoints.

Shell chrome now uses resolved theme roles and native tray templates. Controller
symbols use the existing `wrail-controller-glyph` class independently from hint text.
All 23 native chrome checks pass, including current theme/focus state, controller
family, high contrast, text scaling and stable narrow-layout guide height. Five
actual production tray menu/reorder/dismissal checks pass with a freshly published
bridge runtime. The screenshot was inspected. Evidence: `artifacts/winui-shell/`
`chrome-glyph-result.json`, `chrome-glyph-final.png`, and `tray-chrome-production/`.
These automated results do not establish physical controller or performance acceptance.
## Shared input and typography follow-up, 2026-09-28

Priority is shared behavior across widgets. Additional pin teardown/display commits
are deliberately deferred and remain off this integration branch.

Native typography now applies line height, line caps, clipping, display casing and
scaled character spacing through one reversible adapter, including button/select/
text-entry labels and indexed-fragment focus states. Native validation passed 164
style, 104 control, 36 package-icon and 16 ordinary-control checks in the isolated
lane. The original source string survives casing/style updates and removal.

The controller trace showed a visible background overlay accepting stick movement.
The input pump now separates sampling from delivery, admits ordinary frames only
with foreground ownership, and primes held state on reacquisition. The 30-test
platform-client suite and native controller replay passed. The recorded physical
trace did not show duplicate gamepad-key events; that reported symptom remains a
physical acceptance item. The WinUI gamepad-key ownership boundary is integrated: 26 native injected-key
checks pass, including positive no-owner controls, single semantic activation and
movement, keyboard input, menus, selects, dialogs and retirement. The combined
build also passed all 164 native style checks and controller replay. This validates
the duplicate-route prevention mechanism, not the cause of the original physical symptom.

Physical follow-up: the user reported "Finished—single steps now" on candidate
`f1f5557e` (PID 43788). Single-step input is accepted for that test; the original
duplicate cause was not captured. The next shared work covers production shell
geometry, contextual guide, passive system status and theme refresh. Pin follow-ups
remain deferred. No change is merged into main.

## Combined production shell follow-up, 2026-09-28

Production geometry now uses one work-area shell with stationary guide/rail bands,
native icon-only rail items, conditional recovery chrome and the actual XAML
viewport for DPI sizing. Passive clock/date/connectivity status is attached to
visibility, fullscreen, narrow-layout and disposal lifetimes. Its wrapper stays
transparent and never participates in controller focus.

The section-to-tray defect was corrected by capturing focus and beginning the
publication transaction before outgoing controls are detached. The real Playnite
regression passes 13 Home/Library, Categories and explicit tray-entry checks on
the combined shell. The two appearance follow-ups preserve pending structural
focus restoration and avoid retrying failed data pages on a style-only update.

Combined evidence under `artifacts/winui-shell`: 82 native geometry checks,
63 managed shell tests, 12 native status checks, 169 native style checks and
15 real Bridge/worker theme checks pass; analyzer builds are clean. These are
automated correctness checks. Contextual guide recovery/menu validation and
physical acceptance of the combined shell remain separate, as do performance,
memory, accessibility, packaging and the deferred pinned-window work.

The semantic guide now includes recovery and tray-menu ownership. Its isolated
combined-shell checks pass: 66 managed, 87 production, 29 shell-chrome, 56 context-menu
and five real tray checks. That change is integrated as `e10a09d4`.

Final review/qualification remains open. A geometry-changing theme can leave grid
capacity stale when padding changes without outer SizeChanged; a native regression
is prepared. The real four-widget eviction run also failed exact viewport restoration:
the correct game returned, but its focused screen Y changed from 524 to 572 pixels.
Evidence: `artifacts/winui-shell/combined-eviction-01`. Use the isolated config at
`artifacts/winui-shell/eviction-production-01/shell-options.json` with
`--shell-no-controller --replay-shell-input`; the destinations are
`widgetrail.samples.ytmusic`, `settings`, and `media-sessions`. The earlier warm
profile lacks the fourth widget and cannot establish forced eviction. Do not call
this checkpoint physically ready or relax the viewport assertion while these
investigations remain open.

Both defects are now corrected. The padding correction passes 171 native style
and 21 retained-theme checks. The grid's native measurement item remains demanded
within the existing bounded collection lifetime, so recreation does not settle at
a placeholder-derived row height. Preview-before-focus recreation passes 22 list
and 23 grid checks. After integration, the real four-widget Playnite eviction and
modal-return sequence passes all five checks with unchanged focused tile geometry
(`artifacts/winui-shell/eviction-integrated-02`). Analyzer build is clean.

The first combined replay attempt used no-controller mode while Task Manager kept
foreground ownership; UIA correctly refused focus transfer. It is recorded as a
test setup failure, not a new widget-navigation defect. Repeating with the ordinary
production activation adapter acquired foreground and completed the eviction
sequence without any physical controller action. Resource-observation scope and
limitations are documented in `winui-performance-observation.md`.

The current priority is complete first-frame widget switching: the old shell hid
outgoing content before incoming content was laid out. Readiness/commit separation
and the remaining radial chooser are being implemented in independent lanes.
Visual quality, real switching latency, memory, accessibility and packaging remain
qualification requirements; these correctness checks do not finish the migration.

## Switching, artwork and Release qualification, 2026-09-28

The combined branch retains outgoing widget pixels until incoming native layout
is ready, includes the configured radial chooser, and sizes asynchronous artwork
decodes for the current presentation. Superseded selections are cancelled; input
admission and drawable lifetime are separate. Shared JSON metadata and updated
stable Windows SDK projections permit a trimmed Release publish without warning
suppression. Detailed boundaries are in `winui-widget-switch-readiness.md`,
`winui-artwork-demand.md`, and `winui-release-readiness.md`.

Combined automated evidence: 73 managed shell checks, 199 native style checks,
21 complete-widget switching checks, and four radial scale/text cases with 101
native assertions each and actual icon-pixel checks. The radial matrix now uses
the real supported 50–125% interface-scale range; its earlier nominal 200% case
was clamped and is not 200% coverage. All five real Playnite eviction/modal checks
pass again with identical focused tile geometry before and after restoration.
Evidence: `artifacts/winui-shell/combined-switch-01/retention/`.

Context-menu behavior and screen capture pass (56 checks). Embedded-media behavior
passes all 101 checks, including real WebView2/adapter transport. Media screen
capture during the popup phase was refused by the foreground guard; direct window
capture was blank and does not establish visual parity. Preserve that as an open
pixel-qualification gate, not a rendering diagnosis. An optional Debug-only
`--validation-platform-activation` uses the existing production adapter for native
fixture activation; it forwards no controller input to the fixture. It requires
exclusive adapter ownership and is excluded from shipping builds.

Visual review of the actual Library also exposed centered fixed-height poster
badges. The native regression reproduced that the poster adapter aligned inner
content rather than the layout wrapper that owns the authored height. A shared
poster correction now passes its reproduced regression and the full 200-check
style suite; real Library screenshots confirm the strips sit at the bottom.
No widget-specific style workaround was needed. The five Playnite checks pass
in Debug, Release, and the hash-verified trimmed/ReadyToRun publish. Installer/
bootstrap, broader Release scenarios, performance, complete accessibility, media
pixels and deferred pinned-window qualification remain open. Nothing is merged
to main, and all owned automated test windows have been closed.

## Passive media capture and launch bootstrap

The stable media pixel gate is now complete for the sealed fixture: passive,
foreground-verified client-area captures show live media in fullscreen and the
native dropdown above it. All 101 behavior checks pass; no production rendering
workaround was added. See `artifacts/winui-shell/media-readonly-capture-01/`.

Production launch now resolves its own payload without a shell-options file;
Debug's gallery is explicit. Native checks verified config-free Playnite startup
and missing-installation recovery without a Bridge child. All 78 managed shell
checks pass. `winui-launch-bootstrap.md` records exact behavior and the remaining
deployment/startup work, including the mutable runtime boundary required by the
current AppContainer grants. Broader media motion/device recovery and performance
qualification remain open.

## Autonomous lifecycle and shared-control follow-up

The integrated branch now uses native-compatible process election before XAML,
supports quiet `--hidden` startup, and shares one resolved launch configuration
between election and the shell. Windows activation tests pass 17 duplicate-launch
and cleanup checks; seven hidden-start/show/reopen checks pass. Invalid launch
configuration still displays service-free recovery. No startup entry or installed
native overlay was replaced.

That work reproduced a real shutdown deadlock in native icon rasterization.
Started native render/readback operations now drain on the live dispatcher before
window closure. All six visible/hidden interruption cases exit normally (42
checks), and the existing 36 native package-icon checks pass. The analyzer builds
are clean, and trimmed/ReadyToRun Release publish succeeds.

Outgoing popup ownership now follows input revocation during widget switching:
21 Select, 28 TextEntry and 60 context-menu behavior checks pass. Indexed native
containers receive one completed admission notification instead of five repeated
policy refreshes. All 83 managed shell and 21 native retained-theme checks pass.
The latest real Playnite replay could not obtain Windows foreground ownership;
it is not a passing replay or evidence of smoother physical scrolling. Detailed
paths and limits are recorded in the feature-specific maintainer documents.

Offline external-content package construction passes 17 checks. Subsequent
development registration/activation now proves a real external frontend, matching
package identity, default payload resolution and an AppContainer bundled worker.
Both probe registrations and all owned processes were cleaned up. Signed delivery,
clean-machine runtime provisioning and transactional update/rollback remain open.
The shared-control review corrections are now implemented: Select/TextEntry expose
native accessible current values, and controller editing respects Unicode text
elements. The updated validation passes 89 managed checks, 24 native Select and
38 native TextEntry checks, plus an external UIA value read. The analyzer build
and trimmed/ReadyToRun Release publish pass. Details and evidence are in
`winui-external-content-deployment.md`, `winui-select-control.md` and
`winui-text-entry.md`.
Broader performance/accessibility/media recovery and deferred pinned qualification
remain open. This checkpoint does not finish the migration; nothing is merged
to main, and no physical checks were requested while the user was away.

The subsequent deployment pass selects bundled .NET only in the Bridge child's
environment. The real external-content probe confirms both Bridge and the
AppContainer worker loaded the staged .NET 8.0.31 CLR; 251 managed session checks
and the analyzer-enabled trimmed publish pass. Windows App Runtime provisioning,
signed delivery and update/rollback remain open. A 30-second cold-hidden Release
observation recorded one process, about 97.5 MiB steady private commit and no
observed steady CPU usage; it does not establish warmed or scrolling performance.
Details are in `winui-launch-bootstrap.md` and `winui-performance-observation.md`.

## Retained presentation, input readiness and runtime payload follow-up

Preview activation now publishes its admitted frame before binding capture, and
temporary capacity failures retry on compatible publications. Eleven native
checks cover cold/replacement capture frames, retention, capacity recovery and
successful suspension/teardown.

Interactive widget publication now includes acknowledged input ownership before
waiting for unrelated outgoing cleanup. That cleanup remains serialized and
awaited; hide/foreground/selection changes still revoke admission. The new native
worker barrier reproduced the old visible-but-inert interval. All 27 switch checks
and 94 managed shell checks pass, including incoming actions during cleanup,
focus preservation and supersession/hide/reopen. See the switching document for
the exact lifecycle boundary and evidence.

The shared appearance follow-up fixes retained high-contrast palette refresh,
Reduced Transparency on animated selection surfaces, and a reproduced artwork
zoom race that could retain undersized decodes. All 206 native style checks pass
on the combined build. The source fixes do not add synchronous layout or polling.

Offline Windows App Runtime staging now computes NuGet content hashes from the
signed archive and verifies the complete Microsoft package/dependency/license
matrix. It passes 95 checks and 14 adversarial cases; actual runtime/license
installation remains unimplemented. The earlier trimmed Release warmed-hide
observation could not obtain foreground ownership, so it produced no warmed
performance verdict. Both attempts exited normally and removed their registrations.

The current combined frontend passes analyzer-enabled trimmed/ReadyToRun Release
publish (`artifacts/winui-release-probe/retained-ux-publish`). Its new isolated
external-content deployment passes 17 stage checks and actual package activation,
AppContainer Media Sessions startup, loaded private CLR path and cleanup checks
(`artifacts/winui-deployment-evaluation/retained-ux-runtime-16/`). No test process
or temporary probe registration remains. The migration is still in progress:
signed installer/provisioning/update/rollback, broader performance and media/OS
accessibility qualification, deferred pins and final physical acceptance remain.
Main and the installed overlay were not changed.

### Scoped background and style corrections — 2026-09-29

The latest isolated candidate uses shared Playnite Home/Library background ownership
and per-declaration decoded-image retention. See `presentation-retention.md` and
`winui-artwork-demand.md` for semantic versus pixel lifetime. No main integration.

Host style corrections implement WRSS circle/pill shapes, center icon-only button
content, honor finite maximum sizes when assigning automatic grid tracks, and
project authored paint/corners onto the stock ListViewItemPresenter states. Focus
outline offsets now match native stroke geometry and allow outside extents.
Shared depth roles use neutral side edges. The YouTube Music and Now Playing
accent-filled Play buttons use contrasting inset focus ink.

Evidence: `artifacts/winui-shell/scoped-background-20260929/`: 236 native style
checks (including selector pixels, circle resize, glyph alignment and bounded
session-strip layout); 43 native surface checks; 28 state tests; 157 Playnite
checks; 25 PlatformSettings checks; 29 Media Sessions checks; 28 standalone
YouTube Music checks. Final frontend build succeeds without warnings. Some new
fixtures initially failed protocol validation or exact DIP equality; corrected
fixtures respect accessibility names, explicit scroll axes and 125% pixel rounding.

Playnite 0.2.104 and YouTube Music 0.3.26 were packaged, validated and selected only
in the isolated candidate catalog. The candidate's Now Playing style was resealed,
and its Bridge updated for shared theme roles. Real shell screenshots cover tray,
Playnite Home/Library/details and YouTube Music. No Windows media session was active,
so transport geometry is covered by native fixtures; live playback and overall
physical acceptance remain separate. Test windows have been closed normally.

### Shared UX and controller parity — 2026-09-29

Current candidate corrections remain on `codex/winui3-frontend`, off main.
Distribution/CLI/installer work is deferred until widget functionality and UX are accepted.

- Free right-stick scrolling settles focus after 120ms neutral onto a ready visible
  target in the same native scroll owner and active scope. Partially visible current
  focus remains; explicit navigation continues from the settled item without a rewind.
- Down at the nonmodal widget boundary returns to the switcher. Hidden native tray
  fallback cannot overwrite widget interaction ownership. Reopen paints retained
  content while fresh lifecycle admission is pending.
- Rail and radial dispatch current `ViewSnapshot.QuickActions` through the existing
  DashboardQuickAction input contract under Visible lifecycle. Catalog quick actions
  remain separate. Current-view hints/menu commands now expose the same actions.
  Input preserves runtime/snapshot/scope/sequence and actual ingress origin; UIA is
  never relabeled as a physical capability gesture.
- Whole-widget content is clipped to the shell's themed corner radius. Native grouped
  headers consume the shared `.wrail-section-header__title` style through a bounded,
  frozen optional Bridge GroupHeader record. WRSS direction reaches native tracks.
- Widget switching resizes from the prior presented extent to the new extent in
  140ms before speed scaling, with the selected bottom anchor fixed. Panel and
  content share one compositor batch, with no slide-up or opacity effect.
  Equal extents do not animate. The global toggle, speed and accessibility policy remain
  authoritative. Outgoing section subtrees stay under local declaration owners,
  preserving modal/scroll clipping rather than painting above the whole presenter.
- Playnite scopes its foreground pages beneath a stable cinematic presentation scope.
  The earlier same-ID change alone was insufficient because RenderCore subsequently
  applied a changing navigator scope around that background. Categories/Hidden declare
  the same artwork owner as well. Playnite 0.2.105 includes this and the roomier dialog
  with opaque raised surface, separate title/hints and aligned metadata columns.
- Popup metrics now account for interface zoom outside the transformed widget tree.
  Native Menu/Toggle/SubItem state resource families share theme brushes and focused
  row fill. Guide gaps are 8 DIPs on both sides of its stable row, centered vertically.
- Now Playing uses bounded content height; session-selection accessibility wording
  is clear and input-device-neutral. Its updated binary/styles are resealed in the
  isolated candidate. Gallery remains installed.

Evidence: `artifacts/winui-shell/shared-ux-20260929/`.
Passed: 253 native style checks; 46 surface checks; 38 shell/popup checks; 34 combined
switch/lifecycle/dashboard checks; 252 session tests; 159 Playnite tests; 97 managed
shell tests; 11 motion tests; 31 Media Sessions tests. Native indexed direct probe
passes 3 scroll/focus/viewport assertions against the real worker/lease path.
The broad indexed keyboard script was interrupted when the test window lost foreground;
its new scroll case was then run via UIA invocation without keyboard injection. F17
was replaced by an explicit probe control in that script. Early new-fixture failures
included missing modal scopes, premature entry/BringIntoView observations and inherited
BoldText state; fixture setup now makes those dependencies explicit.

The opt-in `--validate-shared-ux` production gate requires `--shell-no-controller`.
It passes 6 checks using the actual installed Playnite and SDK Gallery: three Home/
Library replacements retain the same painted background with no unload/blank samples,
and Night Drive retains widget ownership and regains exact focus across hide/reopen.
Screenshots inspect actual Gallery, music headers, Playnite dropdown/dialog and Now
Playing empty state. Real playback was not started; controller feel, dashboard media
capability gestures and visual acceptance still require the user's physical check.
All owned frontend test windows are closed; no change was merged to main.

### Slider settlement, passive pinned focus and SVG sharpness — 2026-09-29

Active work remains on `codex/winui3-frontend`; no main integration. User authorized
closing candidate PID 4776 and requires automatic candidate launch when finished.

- Slider: restored the native host's 150ms trailing settlement, final A/B flush,
  bounded two-second acknowledgement protection and 16-value echo history. Native
  focus disengagement cannot dispatch an old entry value. Input retirement clears
  unsent work; dispatch failures are checked against their exact owner/generation.
- Pinned focus: child paint previously followed retained XAML focus even after
  interaction ended. A weak per-XamlRoot presentation policy suppresses focused and
  pressed styling/native outlines while passive, without moving logical focus or
  disabling controls. Reentry uses the existing remembered-focus path. The attempted
  focus-parking workaround was rejected because it could reacquire foreground.
- SVG: geometry and DPI/zoom density are corrected, but native fine-detail pixels
  still failed (4.0 contrast versus 89.7 direct reference). Follow-up rendering fix
  is in progress; do not mark sharpness validated from layout/raster sizes alone.

Evidence: `artifacts/winui-shell/slider-settle-20260929/`. Combined build 02 succeeds
with zero warnings/errors. Shell state suite 127 passed; final native slider 36
passed; shared native styles 305 passed; actual saved Spotify pin production
placement/opacity/focus checks 23 passed. Native peer pointer fixture needs harness
investigation: first foreground guard rejection, then unexpected phase/click counts.
The production pin test confirms retained exact control and suppression without
foreground theft. No real provider seek/playback/volume action was invoked.

### Final validation and candidate checkpoint

The SVG pixel failure is resolved: stream-backed SvgImageSource did not rerasterize
when dimensions changed after SetSourceAsync. OriginalColor inline art now uses
Image and decodes admitted bytes at the required DPI/zoom bucket before swapping
sources. No blank frame during upgrade; one bounded in-flight preparation. Native
fine-detail contrast is 91.1 versus 89.7 direct reference (previously 4.0).
All 48 package-icon checks pass. Actual installed Spotify wordmark was captured
and visually inspected at 144x40 physical pixels: `spotify-logo-production.png`.

The strengthened peer fixture exposed a real window activation defect: changing
extended styles through the old helper refreshed the HWND without NOACTIVATE.
The shared frame helper now updates extended styles only when changed and refreshes
with NOACTIVATE/NOZORDER/NOOWNERZORDER. The peer gate passes all 33 checks, including
real pointer pass-through, View-style exit, AltTab-style deactivation, remembered
focus and no foreground theft. Actual production pin gate passes all 23 checks
again with this correction. The earlier unexplained pointer result is retained;
run/publication identity and serialized advances now guard the fixture.

Final build 03: zero warnings/errors. Full focused evidence: 127 shell tests,
36 native slider checks, 305 shared style checks, 48 package-icon checks,
33 native pin checks, 23 production pin checks. Evidence lives under
`artifacts/winui-shell/slider-settle-20260929/`, including `svg-direct-image/`.
Automated checks did not change real playback/seek/volume. Physical provider
acceptance remains pending. Main is untouched; no merge, push or installer work.

Physical candidate launched via project-mode winapp run with the existing physical-options.json profile; PID 57472, left running for user validation.

### Active media widget/pin corrections — 2026-09-29

User is still testing candidate PID 57472: keep it running until they finish.
Do not replace the live frontend/package payloads or run native UI probes yet.

Requested: YouTube Music pin artwork, narrower default dimensions and shortcuts;
align ordinary YouTube Music/Spotify sizes; themed Spotify transport buttons;
fix Spotify Play focus jumping in BOTH ordinary and pinned layouts; audit saved
pin restart lifecycle; cyclic dropdown navigation. Work assigned by file ownership:
widget samples/styles/tests, shared pinned lifecycle/focus, and Select navigation.
Source implementation/isolated managed preparation can proceed while testing.
Dropdown controller traversal now wraps boundedly and skips unavailable options;
native fixture adds top/bottom wrap and single-enabled-option checks.

Media/pin checkpoint: candidate PID 57472 crashed at 2026-09-29T15:56:59Z after
Spotify unpin / YouTube Music play / pin. Preserved `crash-57472.log` identifies
ObjectDisposedException 0x80000013 from WidgetFocusDecoration.Loaded -> UpdateGeometry
-> Visual.Size. Loaded can deliver a captured callback after a preceding root
callback disposes the focus visual. Retirement now marks disposed before release;
geometry/visibility/unload ignore retired callbacks. Native regression and the
full 306 style checks pass. No blanket XAML exception handler was added.

Text-action clarification: user means Refresh / Play here / other text actions,
not transport redesign. Spotify text/page/settings-row buttons now use themed fills,
visible borders, 44-DIP minimum height and 14-DIP type. Final package is 0.3.83;
YouTube Music is 0.3.33. Both ordinary widgets prefer 840x580. Fresh compact pins
are Spotify360x360 and YT360x440; saved placement/opacity remain untouched.

Spotify reproduction uses state-relative Pausing/Resuming restrictions with both
initial playback states and both surfaces. Pending toggle stays enabled/busy;
a status-only acknowledgement with still-stale opposite-state restrictions retains
busy presentation until the bounded existing reconciliation obtains provider state.
It does not manufacture permissions or change command admission. Full managed
Spotify suite77/77 passes; native real-declaration focus sequence40 checks passes.
YT managed30/30 passes, including artwork and pinned shortcut routing. Dropdown
native27 checks pass, including cyclic skipping and single-enabled-option behavior.

Saved pin restoration passed actual fresh startup6 checks plus3 each for missing
widget, removed layout and YT with no current media. Restoration admits fresh
session authority, remains passive and leaves preference bytes unchanged. A pin
still keeps its widget in Visible lifecycle, so its normal provider updates continue.

Candidate catalog staged both new packages through disable/install/select/enable.
The old inactive YT0.3.25 directory was moved intact to this evidence directory to
stay within the catalog's eight-version bound; current/previous versions retained.
Native rendered geometry/captures are being finalized; no physical candidate is
currently running. No main merge or installer work.

Final media/pin result: all requested source fixes are staged in the isolated
candidate. Spotify0.3.83 / YT Music0.3.33 are selected and enabled. Final frontend
build11 has zero warnings/errors. Native media geometry gate64 checks passed;
captures for both main players and compact pins were inspected. This gate uses
real widget snapshots/resolved styles with deterministic artwork and empty browse
viewports, so it validates player/chrome geometry rather than provider browsing.
Initial fixture failures were incorrect external-image transport, an arbitrary
100px expectation versus Spotify's96px authored art, and incomplete indexed-viewport
substitution; those harness errors were corrected without changing product layout.
Actual installed Spotify confirms840x580 and the new Play here button surface.
Actual production pin workflow23 checks passes with the final package.

Complete focused evidence under `artifacts/winui-shell/media-pins-20260929/`:
managed Spotify77/77 and YT30/30; native Select27, shared styles306 (including
retired Loaded crash regression), Spotify real-declaration busy/focus40,
player/pin geometry64, restore6+3+3+3, production pin23. All owned native test
windows closed. Real provider playback/seek/volume was not invoked by tests.
User's saved dimensions/opacity and physical profile are preserved. Work remains
off main; no push/merge/installers. Physical playback and the exact crash sequence
still need user acceptance on the relaunched candidate.

Physical candidate relaunched and left running: PID12628, existing physical-options.json profile, shared controller activation enabled. User acceptance pending.

### Active optional-artwork and popup navigation correction — 2026-09-29

Physical candidate12628 remains running until the user finishes testing. Latest
transient recovery report is captured at16:23:10Z: shell-handled0x80190190 from
NativeArtworkStream.OpenAsync / HTTP400 for YouTube Music. Image demand incorrectly
called presenter.ReportFailure, replacing the whole widget with recovery; later
publication can clear it. Source now isolates expected HTTP/I/O/format/WIC image
failures, retains existing pixels/empty slot and logs widget/node/type/HRESULT
without asset URL. Retired authority handling remains; unexpected host defects
still propagate. Native fixture covers400, missing/corrupt image, no retry storm,
retained focus and usable actions, replacement recovery, and unexpected failure.

User reports wrapping absent in tray/radial. Previous change only covered SDK
Select. Command ownership is intentionally distinct, but traversal duplication
was unnecessary. NativeMenuFocus now centralizes bounded cyclic traversal for
Select, widget context menus, and tray/radial command popup. Each caller still
validates its own authority/lifetime. Context regression updated; production
--validate-popup-cycle gate exercises synthesized D-pad through both rail/radial
menu routes without invoking a command. Isolated output build02 is clean;
native validation and candidate replacement wait on user finishing current test.

Optional-artwork/popup pass completed. User authorized closing physical12628.
Final native build04 clean (zero warnings/errors). Passed artwork38, Select27,
context menu62, and actual production rail/radial controller-popup8 checks.
Evidence: `artifacts/winui-shell/artwork-failure-20260929/`.

The HTTP400 failure now stays at the optional artwork boundary: retained pixels
and focus/actions survive, no shell recovery message, no repeated same-size request
on unchanged snapshots, and replacement artwork recovers. Expected diagnostics
record widget/node/type/HRESULT without source URLs; unexpected host defects still
propagate. Native fixtures cover HTTP400, I/O, invalid encoded image and unexpected
InvalidOperationException. A missing ImageFit in the new fixture was corrected.

All three popup owners share NativeMenuFocus (bounded cyclic traversal, actual
native focus, disabled/collapsed-item skip), while snapshot/element/host command
validation remains distinct. Context fixture now keeps its ambiguity hint mounted
to test ownership independently of Loaded timing. Production popup fixture waits
for radial anchor layout and prior flyout dismissal; it tests D-pad routing in
both modes without executing commands. Its earlier radial timeouts were fixture
entry timing, not claimed product fixes. No widget package changes, main merge or
installer changes in this pass. Physical acceptance remains pending.

Physical candidate relaunched through project-mode winapp, PID49132, existing physical profile/controller adapter. Left running for user testing.

### Active context-menu indicator migration — 2026-09-29

User requested original focused row/tile top-right controller badge. Native reference:
DeclarativeRenderer.DrawContextMenuIndicator / ContextMenuIndicatorBounds:26DIP,
5DIP inset, radius6,20DIP semantic glyph, no border/hit/focus target, available actions
and current action-surface focus only, suppress partial viewport clipping. Original
renderer tests cover glyph family, authored X/Y, scale inheritance and unavailable
ancestors/actions.

WinUI source now has a shared zero-measure WidgetContextIndicator native overlay.
Ordinary action buttons attach inside existing depth template; indexed native
containers use an overlay around their row presenter. Badge stays under the same
control scale/clip and reads native computed brushes/glyph family. Shared style
adapter gates focus/pinned root presentation; declaration/lease admission gates
scope and availability. No SDK or widget changes. Native fixture covers placement,
measurement, glyph/family/theme changes, disabled/busy actions/ancestor, passive
input/pin policy, clipping and focus movement. Genuine indexed coverage remains to
be added/run. User approved closure; candidate had been restarted as PID58592
(same physical-options profile), then was closed normally. No physical candidate
currently running; build/native validation underway; relaunch when complete.

Context-menu indicator migration completed. Cause: native DrawContextMenuIndicator
was host-rendered decoration; WinUI had migrated context-menu dispatch and guide
hints but omitted this focused-item visual. WidgetContextIndicator now serves both
ordinary ActionSurface buttons and realized indexed containers, preserving the
native dimensions, authored Menu/X/Y shortcut, controller family, and theme ink.
It adds no measured size, input target, or accessible control. Scope, current
lease, host input admission, passive-pin focus presentation, and clipping gate it.
Indexed placement uses the actual outer control origin: native content alignment
can add offsets beyond padding. Layout observation runs only while that indexed
badge is requested; retirement removes handlers and decoration.

Final build07: zero warnings/errors. Passed 16 ordinary badge checks, 20 genuine
deep indexed-row/context checks (row75, input withdrawal, popup handoff, refreshed
leases/query replacement/modal scope), and 62 existing context-menu checks.
Indexed screenshot inspected for glyph placement and unchanged row geometry.
Evidence: `artifacts/winui-shell/context-indicator-20260929/`. Test setup corrections:
disabled state is invalid on a Stack, and refreshed row content may correctly reuse
the same native badge with a new current lease. Neither was a production defect.
No widget packages or SDK contracts changed; all work remains off main. Physical
controller acceptance remains for the relaunched candidate.

Physical candidate relaunched through project-mode winapp as PID2392 with the existing physical profile and controller adapter; left running for user testing.

Embedded completion physical candidate launched as PID60252 with the existing physical profile/controller adapter; left running. Embedded Media Sample enabled, YouTube Video disabled.

### Renderer retirement: replacement CI/build entrypoints — 2026-09-30

Build/CI lane added `Build-WinUiVerification.ps1`: explicit audited restore with
NoRestore support; fresh platform and preview native builds; synthetic controller,
foreground/process and preview policy gates; trimmed Release WinUI publication
with explicit native inputs; native hash, PRI, self-contained .NET10 and retired
artifact exclusion checks. No registration or launch. Actual wrapper pass is
`artifacts/winui-shell/renderer-retirement-20260930/ci-verification01/verification.json`
and sibling `ci-verification01.log`. Preview policy15/diagnostic2021 checks pass;
platform suites plus foreground/process checks pass. Existing ViGEm encoding
warnings remain; managed frontend publication reports no errors.

CI no longer installs Rust or invokes the old host build. Manifest now represents
WinUI shell/motion, platform client, shared state and deployment policies as well
as existing managed widget/provider/session contracts. Native bridge verification
creates a fresh managed installation with trusted Settings and sealed bundled
packages through `Test-WidgetBridgeRuntime.ps1`; its execution remains pending.
Protocol parity retains the actual extracted native generated header consumer.
Runner selftest and NuGet build contract passed before the final parent-owned
manifest cleanup; rerun runner selftest after that cleanup. Current building,
architecture, dependency and contributor/reference pointers updated. Display
restore guard now requires an explicit installation root; it was not executed.
No commits, deployment, candidate actions or machine setting changes by this lane.

CI/bridge follow-up: corrected Settings catalog serialization to always emit an
array (single configured worker previously unwrapped by PowerShell), and corrected
source-hash references for non-RID shared project outputs. The fresh installation
now passes Settings icons, cold catalog, embedded playback loop and Spotify payload
coherence. Bridge aggregate `bridge-verification02.log` is171/178: remaining failures
are indexed authority, trusted artwork demand1/0, full-trust fixture worker exit,
Playnite Home focus expectation, appearance12/16, hover action result/error and
virtual window50/57. These are retained as non-green evidence, not waived or fixed
by weakening production checks. No further bridge aggregate rerun by this lane.
Affected managed verification runs through the actual manifest/runner under
`renderer-retirement-20260930/managed-verification`.

Final replacement-gate results: NuGet build contract and bounded runner selftest
pass. Protocol parity regenerated the moved header through its existing generator:
it was stillv62 while managed contract isv64; only CurrentVersion, discovered/grid
feature versions and grid bounds changed. Parity now verifies147 constants,
1 shortcut-repeat rule and4 availability rules. Audio48, Network31, docs197,
PlatformClient67, Deployment47, WidgetUi.State28, Motion15 and Shell161 pass.
Evidence: `managed-verification/20260930T233615Z-0f144798/verification-result.json`
and earlier sibling runs. The native+trimmed gate ran before this additive generated
header synchronization; consumed placement constants and ABI did not change.

Docs gate repairs map all10 new public SDK files to existing indexed-collection,
WinUI-grid, navigation and pinning topics, preserve current rail/radial screenshot
coverage, and restore the community-services entry link. First-party conformance
is5/6: exact manifest/catalog/package maximum tests pass; the workflow still expects
Games & Apps item text in its parent snapshot rather than demanding the indexed
collection. This is retained as a test-adoption gap pending investigation. Bridge
aggregate remains171/178 as recorded above. Neither gate is declared green and no
assertion was weakened. All build slots released; candidate lifecycle remains root-owned.

### 2026-09-30 — Games & Apps conformance follows indexed presentation

The Games & Apps workflow harness assumed Library tiles were serialized inside
its parent snapshot. It now acquires bounded indexed leases, verifies the rendered
row, and dispatches launch through indexed controller admission with the exact
lease, item, input scope, and parent sequence. Catalog curation remains on its
current bounded parent-tree path. Fresh-worker persistence checks require the
saved indexed row to become interactive. Responsive navigation assertions target
exactly one compact navigation destination instead of counting its expanded copy.
No production authority guards changed; all broker effects use the simulated backend.

Validation under `artifacts/winui-shell/renderer-retirement-20260930/`:

- `conformance-indexed01.log`: full default conformance **6/6** passed.
- `conformance-games-restart02.log`: installed add/launch/fresh-worker persistence passed.
- `conformance-indexed03.binlog`: final source builds with zero warnings/errors.
- `conformance-evidence02/`: Games & Apps exported five authoritative snapshots
  and one add/remove workflow trace. Indexed ranges are retained separately from
  parent snapshots; membership and unrelated-row preservation are asserted.

The optional exporter still records a separate Settings gap:
`WidgetProcessException: Widget worker exited with code 1 before connecting.`
That mode is not claimed fully qualified; its exact gap is retained in
`conformance-evidence02/evidence-index.json`. Optional legacy Playnite acceptance
modes still contain eager-parent-row assumptions (including library/category,
owned/offline and running-app paths); those modes were not run or migrated in this
bounded batch. They do not invalidate the current default conformance result.
