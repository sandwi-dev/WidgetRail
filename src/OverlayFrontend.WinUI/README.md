# WinUI frontend development

The production frontend owns XAML controls, composition, controller navigation,
focus restoration, pinned surfaces, embedded media and themed accessibility.
The x64 C++ platform/preview DLLs handle Windows APIs that remain native.

## Build and verify

Run `scripts/Build-WinUiVerification.ps1` from the repository root. It builds both
native DLLs, executes safe native policy tests and publishes the trimmed Release
frontend with those exact binaries. It neither launches nor registers an app.
See [Building from source](../../docs/maintainers/building.md) for prerequisites
and [Build execution](../../docs/maintainers/build-execution.md) for audited restore.

Launch the complete unpackaged payload directly, or use the local workspace
`dotnet run` workflow in [Building from source](../../docs/maintainers/building.md).
No project-mode package registration is required. Only one controller-owning
overlay may run at a time.
Use `--shell-no-controller` for synthetic UI validation. Development fixtures
require a build with `EnableWidgetValidation=true`; release publication disables them.

## Isolated widget workspace

`--shell-config="<absolute-json-path>"` selects `InstallationRoot`, `SettingsRoot`,
`InstalledCatalogRoot`, and optional `InitialWidgetId`. All roots must be absolute.
The shell owns a real WidgetBridge and consumes admitted worker snapshots.
`scripts/New-WinUiBundledWorkspace.ps1` creates a fresh, sealed bundled installation
and shell configuration. It does not modify the installed profile.

The presenter reconciles controls, virtualizes indexed collections and retains
focus by semantic identity when a target remains visible and focusable. Controller
routing distinguishes widget, rail/radial, transient popup and pinned scopes.
Pinning and playback have explicit lifetimes; hiding the main overlay does not
end passive media. Shutdown retires capture/presentation resources before ending
the owned bridge and child processes.

Managed policies run in `WinUiShell.Tests`, `WinUiMotion.Tests`,
`OverlayPlatformClient.Tests`, `WidgetPresentationSession.Tests` and related SDK/
bridge suites. Native `Test-WinUi*.ps1` probes inspect actual XAML behavior;
controller, real-provider and display acceptance remain physical checks.

## Historical fixture notes

The notes below record early migration probes and their evidence limits. They
are not the current feature-completeness checklist; use the migration progress
document and current source/tests for remaining work.

## Foundation validation modes

Development-only argument `--validate-external-surface` exercises static trusted
HTML in WebView2 and a WinUI ContentDialog above it. It accepts no widget URLs,
scripts or credentials. `scripts/Test-WinUiExternalSurface.ps1 -AppPid <pid>` checks
navigation readiness, pointer delivery, modal reopening and focus restoration.
Inspect its screenshots independently. This does not prove video playback,
provider policies, widget-local dialog positioning, clipping or pinning.

`--validate-controller` initializes the existing native input session and exposes
three test commands. Guide hides a foreground validation window and brings a
hidden/inactive window forward through the shared native activation adapter; native directional
frames request WinUI focus traversal; A invokes the same command as clicking the
focused test button. This validation page is NOT the production shortcut router.
View+Menu's additional native observer, widget scopes, analog scrolling, slider
semantics and duplicate framework gamepad delivery still need integration/proof.

The input pump is serialized on the WinUI dispatcher. Normal navigation polling
runs only while visible; legacy Guide polling runs only when native policy requests
it. Native callbacks only enqueue bounded work. A foreground check uses process
identity so WinUI-owned popup HWNDs are recognized. No second GameInput/XInput
reader or widget IPC is introduced in the frame handler.

The development page logs connection/read-path status at most four times a second
in a UI label, rather than mutating UI on every read. Shutdown disposes the platform
session and closes WebView2. Keep physical acceptance separate from these fixtures.

Controller diagnostics are written asynchronously to
`%LOCALAPPDATA%\WidgetRail\WinUI\diagnostics\controller.log` (plus one bounded
`.previous` segment). The log records native registration/device/Guide messages,
accepted Guide source, requested and actual window visibility, foreground process
identity, failure and disposal. It does not log ordinary controller frames. Use
these boundaries to distinguish missing native events from activation failure;
a connected-controller label alone does not prove Guide delivery or foreground ownership.
Foreground acquisition does not assign keyboard focus to the outer HWND. WinUI
restores Keyboard focus on its retained native leaf after window activation;
logical group/collection memory is not reset for that operation.

Controller replay: launch with `--replay-controller`. It uses a deterministic
IOverlayPlatformNative test backend rather than hardware, but passes frames through
the real pump, session, scope traversal, command and hide/show paths. Read UIA
Controller.Replay for PASS/FAIL. It never clicks/focuses controls from the replay
scenario; entry focus is owned by the window/page lifecycle. It does not prove
GameInput delivery or duplicate native/framework gamepad handling.

Collection fixture: `--validate-collection` uses WinUI ItemsView/UniformGridLayout
with stable keyed binding entries. F7 prepends and F8 appends without transferring
focus to toolbar controls; use these to test page arrival while an item owns focus.
The current prepend case retains focus identity but loses viewport visibility: this
is an open regression, not accepted cursor behavior. The generic trusted template
uses bounded Binding paths (Value.Title) because its data is a generic entry; no
widget-supplied XAML, reflection path or custom layout code is loaded.

## Real worker integration fixture

`--widget-config=<absolute-json-path>` opts into a Clock-only round-trip fixture.
The JSON has four explicit string properties: `InstallationRoot` (built backend
distribution), `SettingsRoot` (isolated candidate profile), `InstalledCatalogRoot`
(isolated installed packages), and `WidgetId` (`widgetrail.samples.clock`). Package,
install and enable the existing Clock sample in that catalog using wrail; never
substitute the user's default catalog/profile for this fixture.

The fixture starts and owns a real WidgetBridge child and consumes its validated
snapshot. The shared `Presentation/WidgetViewPresenter` maps admitted declarations
to standard WinUI controls. Refresh
uses current snapshot/scope authority, and subsequent publications update existing
controls without replacing focus. The shared presenter also preserves controls
through structural insertion and reparenting; unsupported control/layout families
fail visibly. This incomplete migration adapter is not a supported new SDK
contract. Existing resolved font size is projected for basic readability;
complete style/state/theme migration remains outstanding.

Run `scripts/Test-WinUiBridgeWidget.ps1 -AppPid <pid>` after project-mode launch.
It verifies initial focus before injecting any keyboard/mouse input, three actual
worker Refresh round trips, stable control/focus identity and owned process cleanup.
It closes the fixture and records results plus a screenshot. Inspect the screenshot
separately. The capture sends harmless Escape after the initial-focus checks to
wake idle desktop presentation; no hardware-controller claim follows from this.

The client now depends on `WidgetBridge.Contracts`, not the backend executable.
Shared DTO namespaces and wire shapes are unchanged; managed consumers must rebuild.

`--validate-controls` exercises the same shared presenter without a worker or
hardware. `scripts/Test-WinUiControls.ps1` checks initial focus, membership changes,
scope gating/return, updates while external controls own focus, disabled-target
fallback and retired-control action rejection. Capture artifacts do not prove
the rest of the widget feature vocabulary or performance under large collections.
The presenter deliberately rejects native collection declarations rather than
expanding them into nonvirtualized StackPanels.

`--validate-gridview` is an independent native-control comparison for the unresolved
collection anchor issue. Its script currently reports failures for prepend and
leading eviction. It is retained as evidence; do not substitute it for the shipping
collection implementation or describe a bounded realized count as scroll correctness.

`--validate-indexed` exercises a native ListView over the internal sparse indexed
source. `scripts/Test-WinUiIndexedCollection.ps1` checks native range callbacks,
bounded realization/data, reversal while a buffer page is held, unchanged viewport
on its completion, and eviction/reload without deleting positions. It closes the
window after success. Real worker leases and item templates are exercised separately
by `--indexed-validation-pipe` and `Test-WinUiIndexedWidget.ps1`; see
[the collection contract](../../docs/maintainers/winui-indexed-collections.md)
for provider capabilities, SDK/service work and remaining adoption requirements.


`--validate-focus-policy` exercises the shared presenter's declarative focus policy
through native WinUI controls and `FocusManager`. Explicit neighbors are assigned
to native XYFocus properties, including neighbors naming remembered-child groups.
Spatial group entry honors the remembered child, then the declared default, then
an eligible descendant when the default is unavailable. Input scopes constrain
native directional search. Group requests are consumed once per worker, may wait
for ready content, and cannot replay on routine updates; withdrawal or subsequent
user navigation/activation/pointer input retires a deferred request. New worker
ownership clears remembered groups. This is focus policy over WinUI's target
selection, not a custom geometry/navigation renderer. Run
`scripts/Test-WinUiFocusPolicy.ps1`; it uses no controller hardware. The indexed
widget probe separately validates initial/exact/remembered entry, native neighbor
entry, wrong/stale targets, disabled items and cancellation during delayed reads.
Physical controller acceptance remains separate from these automated checks.

## Native context menus

The shared presenter renders SDK context actions with `MenuFlyout`. Menu, X and Y
are resolved from authored declarations: the focused action surface wins (Menu by
default), then a single visible container menu in the active input scope. A
nonfocusable controller-hint row can anchor the scoped menu. Ambiguous hints do
not open a popup. This does not replace or remap ordinary worker shortcuts.

An open menu owns normalized controller input ahead of Select, widget shortcuts
and modal dismissal. Native WinUI handles placement and popup rendering. Direction
boundaries remain inside the popup; disabled/busy actions are skipped. B dismisses
the menu and restores its exact opener when that owner remains current. The host
must call `DismissTransientControl` when hiding/deactivating the overlay, as it
already does for Select and text entry.

Each opening captures semantic owner identity, actions and scope. Unrelated
snapshots may advance; replaced owners/actions, disabled ancestors, modal scopes,
query replacement, unloaded anchors and stale indexed leases revoke the popup.
Indexed commands retain the exact row and use its existing lease admission API;
a selection is never replayed against a replacement lease. Revocation precedes
asynchronous action dispatch and the retention is released after admission.

`--validate-context-menu` exercises real native popup/controller routing with a
synthetic action sink, writing `context-menu-result.json` under the existing WinUI
diagnostics directory. The indexed worker fixture exposes
`IndexedWidget.ContextProbe` for exact row actions, refresh/query invalidation,
modal precedence and scroll/focus retention; its output is
`indexed-context-menu-result.json`. These are correctness checks, not production
widget performance measurements. Slider and live media/window-preview controls
remain separate migration work.

## Native sliders

SDK Slider and Scrubber declarations use native `Slider`, including native
pointer, keyboard and UIA range-value behavior. Controller Left/Right applies
`SliderMath` against the minimum-anchored step grid, consumes endpoint movement,
and sends bounded absolute `RequestedValue` through ordinary action authority.
Programmatic snapshot/range changes never echo actions. Direct sliders can also
have an authored A action; Up/Down remains normal focus navigation.

`ActivateToAdjust` remains a controller policy: A enters, Left/Right adjusts,
and A/B exits without firing an activation or parent Back action. Repeats and
release do not replay the exit. Scope replacement, focus departure and host
hide reset adjustment. Widgets still own the authoritative value; the frontend
owns native interaction only. Native `--validate-slider` covers these lifetimes,
endpoint quantization and disjoint range changes with a synthetic action sink.

## Package SVG icons

Manifest `OriginalColor` icons now use `SvgImageSource`/`ImageIcon`, including
standalone Icon, Button adornments and Select options. All bytes come from the
session's hash-checked bridge package resolver. Indexed fragments share their
existing parent session. Native glyph fallback appears immediately; removal,
rebind, popup dismissal and catalog replacement revoke pending publication.
Native controller-family fonts and accessible labels retain their prior meaning.

Icon bytes have a dedicated session cache (128 entries/2 MiB), separate from
artwork. Exact-authority concurrent requests share one fetch; canceling one
view does not cancel other waiters. The session admits 16 distinct pending
demands and at most four concurrent Bridge reads. Failures do not populate it.
Native controls retry transient availability/capacity/timeouts in bounded batches;
presentation reentry can rearm a failed batch. Malformed bytes and revoked
authority remain failures. Compatible label updates retain the ready SVG, while
asset or authority replacement clears it. Terminal failures record a bounded
reason/type/HRESULT in frontend diagnostics for tray, radial and widget icons.

ThemeTint uses WinUI's SVG renderer and `RenderTargetBitmap` on a noninteractive,
clipped preparation layer. Native PNG encoding transfers the unchanged raster
alpha to `LoadedImageSurface`; a compositor mask applies the current foreground.
No SVG attributes or pixels are recolored. Foreground brush/color/opacity and theme
updates only change the compositor color brush, with no capture or decode.

Masks are shared by admitted normalized content hash and size bucket per XAML
root. Retention is capped at 64 entries/8 MiB; preparation is serialized and admits
at most 16 pending assets. Visible icons hold leases; only idle completed entries
are evicted. Larger sizes and DPI changes acquire a higher-resolution bucket up
to the SDK's 512px limit, preserving the prior mask during preparation. Closing
icons releases their leases; XAML root teardown cancels preparation and releases
surfaces. OriginalColor remains a direct native SVG source.

`--validate-package-icons` covers real native sources/masks, original and tinted
Select adornments, alpha overlap, brush changes, Light/Dark theme changes,
replacement/cancellation and bounded size upgrades. Existing session/bridge tests
cover exact manifest authority independently. `--probe-svg-raster` records the
native capture variants that established the preparation path.

The shell tray uses `WidgetCatalogItemContent` inside the ordinary native
`ListViewItem` template. The reusable content supplies a 24-DIP icon and optional
label; it creates no focus target, controller reader or selection behavior. The
containing control owns the accessible name and tooltip. A future radial item can
hide the label and retain the same icon/content lifetime.

Catalog package icons resolve through `WidgetPresentationSession` with the
captured widget/instance/runtime/presentation/package identity and admitted asset
metadata. Unrelated catalog revisions may retry only that same identity. Item
recycling and unload cancel demand; late results cannot paint into another item.
Unchanged descriptors keep their native content, while icon-affecting catalog
changes update it. Theme tint and original color reuse the existing package-icon
pipeline and its semantic fallback. The package-icon fixture also exercises
recycled identity, unloaded/reloaded demand and native item focus/selection.

The optional `--validate-package-icons --probe-svg-mask` probe compares a simple
SVG alpha mask, a PNG alpha mask and a direct solid compositor visual in one
window. On the installed WinUI runtime, the SVG source painted but its mask target
was blank; the PNG mask and solid control painted green. A non-null mask brush
alone is not a passing rendering assertion. The comparison screenshot is retained
in local `artifacts/winui-shell/native-svg-mask-baselines.png`. This records the
observed capability boundary, not a claim about every future WinUI version.
