# WinUI frontend development

Work in the isolated migration checkout. Native platform builds currently support
Windows x64 only. The installed/native candidate is a separate product process;
never run both controller owners simultaneously.

## Build

1. Run `scripts/Build-OverlayPlatform.ps1` to build the platform DLL without Taffy
   or the old renderer. This does not initialize hardware or install drivers.
2. Build `src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj` with x64 platform
   and a unique MSBuild binlog. Use the installed Microsoft WinUI skill analyzer.
3. Launch through `winapp run` in project mode with `--no-build --arch x64
   -p Platform=x64`. Explicit Platform avoids output-path ambiguity introduced by
   project references. Do not launch the packaged executable directly.

The default page is an honest foundation placeholder, not a migrated widget.
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
