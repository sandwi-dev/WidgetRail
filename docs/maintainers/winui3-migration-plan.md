# WinUI 3 migration plan

Status: proposed delivery plan, 2026-09-27. WinUI 3 is the primary migration
direction selected in discussion. This planning task does not start implementation,
install dependencies, change the running candidate, or authorize a release.
The [Compose plan](compose-migration-plan.md) is retained as a fallback assessment,
not a second implementation track.

## Objective and constraints

Replace WidgetRail's custom UI engine with WinUI 3 end to end: shell, widget
controls, layout, virtualization, focus, text, UI Automation, and visual animation.
Keep C# for widget and frontend authoring, with C++ retained for useful Windows
integration. Preserve WidgetRail's controller-first design and themes rather than
reproduce the current renderer internally or adopt an unmodified desktop theme.

Preview permits breaking SDK, protocol, style and package changes. Sandboxed
community widgets remain mandatory. Existing C# business logic, providers,
permissions and state helpers are reusable where appropriate. They need not be
rewritten merely because presentation changes. Windows is the target; adding
other platforms is not part of this migration.

Success is better, predictable interaction with less custom UI machinery. Merely
replacing Direct2D calls with XAML while preserving our layout/focus/preparation
engine would not achieve that objective. WinUI's reputation is not performance
evidence: the integrated product must pass the gates below.

## Target architecture and ownership

```text
Windows controller, activation and native platform APIs
                         |
                 small C++ adapter
                         |
WinUI 3 frontend (C# / trusted compiled XAML and controls)
  shell, widget UI, focus, virtualized controls, themes, motion, UIA
                         |
      authenticated versioned local protocol / service client
                         |
.NET broker and provider services
  catalog, permission checks, worker lifecycle, resource authority
                         |
AppContainer widget workers / approved full-trust application workers (C#)
```

Initially retain the existing out-of-process broker/worker topology. Sharing C#
does not require merging worker code or potentially blocking service work into the
UI process. Later service consolidation needs measured value and a separate review.
Do not simultaneously redesign provider APIs or controller backends without a
specific integration need.

| Current area | Migration decision |
|---|---|
| `OverlayHost` rendering, Taffy layout, text layout and preparation scheduler | Replace with WinUI/XAML layout, controls, realization and frame scheduling |
| Native geometry-based UI focus and accessibility trees | Replace with framework focus and control automation peers; retain semantic action authority |
| Native animation presenter | Replace ordinary UI animation with XAML transitions / `Microsoft.UI.Composition`; no competing transform owner |
| Guide/View+Menu, device routing, activation, display/window APIs | Extract the relevant code behind a narrow adapter; preserve supported backend behavior |
| `WidgetBridge`, `WidgetRuntime`, `WidgetWorkerHost`, `WidgetCatalog` | Keep service/sandbox responsibilities; remove native renderer-specific preparation |
| `PlatformBroker`, `Windows*Provider`, `WidgetApplicationRuntime` | Preserve useful capability/provider and full-trust application behavior |
| `WidgetProtocol`, `WidgetSdk`, presentation helpers | Version a supported declarative subset of WinUI; retain C# authoring and business helpers, with one thin frontend adapter for both trust levels |
| `WidgetStyling` / WRSS | Move to semantic tokens and bounded component overrides; port accepted themes explicitly |
| Artwork, media, capture | Preserve security/session responsibilities; select framework-compatible presentation and cache ownership |
| Settings, credentials, private widget data | Preserve user meaning and authority; explicit reversible data migration |
| Installer and release tooling | Extend existing Windows packaging for WinUI/Windows App SDK runtime requirements |

Proposed new frontend project: `src/OverlayFrontend.WinUI`. Proposed shared trusted
controls project: `src/WidgetUi.WinUI`. Names are planning placeholders, not a
requirement to create layers without a consumer. Keep Windows-specific native
interop in the existing platform interop area where feasible. Widget SDK projects
must not require community workers to create XAML objects or load WinUI.

WinUI owns the UI thread, layout and visual tree. Interop callbacks enqueue events
onto a controlled dispatcher; they do not synchronously enter arbitrary UI code.
Native handles, COM objects and callbacks have explicit ownership and shutdown.
One component owns each HWND and composition subtree. Do not graft visuals from
incompatible compositor instances into WinUI's internal tree.

## Early platform choices: prove these before bulk migration

### Overlay window and native media

Start with a WinUI-owned top-level window and documented HWND/AppWindow integration.
Test borderless/topmost placement, foreground activation, hide/show, multiple
monitors, DPI changes, taskbar/Alt+Tab policy and controller ownership. Test genuine
per-pixel desktop transparency and hit testing, not just a transparent XAML brush,
Mica or acrylic backdrop. Preserve the current supported windowed/borderless game
scope; migration does not promise exclusive-fullscreen overlays.

If standard hosting cannot meet the overlay requirements, evaluate WinUI 3's
`Microsoft.UI.Xaml.Hosting.DesktopWindowXamlSource` in a native HWND as a bounded
alternative. Its existence is not proof of transparency or media compatibility.
Do not mistake older UWP XAML Islands guidance for WinUI 3 support. Choose one
hosting model after the gate; do not maintain two production shells indefinitely.
Undocumented composition-root modifications are not the default solution.

Test WebView2 playback and a live window preview in that actual shell immediately,
including a pinned window. Prefer supported WinUI controls and compositor interop
where they satisfy the requirements. Existing media sessions may need a new
presentation adapter rather than a direct reuse of their old visual target.

Microsoft documents external-content limitations: compositor effects cannot
necessarily sample media/WebView2 pixels, and transparent WebView2 cannot simply
reveal arbitrary XAML content behind it. Verify menus/dialogs above media, rounded
clipping, transforms, resize, hide/resume, pin/unpin, device loss and capture resource
lifetime. A basic playing video is insufficient. Do not introduce routine full-frame
CPU readback or silently remove depth, clipping, pinning or overlay transparency
to declare this gate passed. Record any necessary tradeoff explicitly before ports.

### Standard controls before lower-level building blocks

Use virtualized `ListView` for music rows and `GridView` for game/app posters first.
Customize shared item containers/templates while retaining supported virtualizing
panels (`ItemsStackPanel` / `ItemsWrapGrid`) and a bounded viewport. Avoid wrapping
these controls in an unconstrained outer scroll container or replacing their panel
with a nonvirtualizing layout. Verify container counts grow with the viewport and
buffer, not the entire available collection.

`ItemsRepeater` provides realization and layout building blocks; it is not a complete
focus/selection/interaction policy. Although directional keyboard navigation is
enabled by default, do not assume it provides the full controller experience.
Use it only for a concrete layout need that standard controls cannot meet, through
one tested shared component. No new custom virtualizing layout or spatial focus
engine is planned as part of the initial migration.

## SDK, presentation and interaction contracts

### WinUI-aligned declarations and one shared adapter

Both sandboxed and full-trust workers publish the same serializable, bounded UI
declarations and semantic action IDs over the process boundary. Both use the same
SDK, protocol and trusted frontend adapter. Full trust changes what the worker can
access and execute; it does not provide a separate in-process UI extension path.
Neither trust level sends live WinUI controls or executable UI code to the frontend.

The SDK should closely mirror the supported WinUI control and layout model rather
than invent a toolkit-neutral equivalent. Follow WinUI names, values and semantics
where practical: for example, `Grid`, row/column definitions with Auto/star/fixed
sizing, row/column spans, alignment and spacing. Arbitrary compositions of supported
controls remain possible; arbitrary custom layout algorithms are not part of the
worker contract. WinUI performs layout, focus traversal and virtualization.

The adapter validates declarations, creates controls, assigns supported properties,
connects semantic actions, and updates/reuses controls by stable identity. It is
an IPC-to-control adapter, not a second layout, focus or rendering engine. Trusted
compiled XAML supplies reusable templates and themes; dynamic widget trees can be
assembled programmatically in C#. Widget authors do not need to write XAML or load
WinUI in their worker process.

Maintain an explicit allowlist of components and properties, with supported defaults,
value limits, version requirements and documented differences from WinUI. Generate
repetitive schema/property mappings and conformance cases from that allowlist where
useful; do not automatically expose the entire WinUI API. Framework upgrades must
not silently expand the wire contract or change its supported semantics. Include
property-reset/removal and recycled-container tests as well as initial creation.

Workers do not provide executable controls, assemblies, native handles or markup extensions.
Do not pass widget-supplied XAML to `XamlReader.Load`; XAML is a frontend implementation
format, not the widget wire format for either trust level. Callbacks, unrestricted
bindings/reflection, resource loading and thread/platform objects are not mirrored
merely because WinUI exposes them.

Use direct mappings for supported standard controls, such as `StackPanel`, `Grid`,
`TextBlock`, `Button`, `Slider`, `ListView` and `GridView`. Add WidgetRail components
where they provide product behavior: poster tiles, controller hints, navigation
rails, cursor collections, widget-local dialogs and authorized media slots. These
compose framework controls rather than define competing layout semantics. Include
automation names/roles, error states and explicit unsupported-feature responses.
Keep schema and validators shared in managed code where possible. Unknown
component/property versions must fail clearly rather than silently render a broken UI.

Collections expose stable keys, typed immutable item data and bounded declarative
item templates/variants. Materialize controls on demand via trusted templates or
element factories. Do not deserialize/create a live XAML subtree for every loaded
item, perform provider I/O in template selection, or synchronously ask the worker
for UI during measure. Template bindings support explicit fields and bounded
variants, not reflection paths or a general executable expression language.

### State and data ownership

- Workers own business state, provider I/O, cursor tokens and actions.
- Frontend owns realized controls, current UI focus, scroll position and transient
  feedback; WinUI owns layout and rendering of those controls.
- Broker owns identity, consent, capabilities, resources and process lifetime.
- Updates carry widget/session, parent page/scope, collection generation and
  revision. Validate off the UI thread; apply coherent bounded changes on it.
- Preserve stable item identity and unchanged items when applying data updates.
  Do not replace `ItemsSource` or issue a collection Reset for every page/status
  update. Select a keyed incremental collection adapter and verify which change
  notifications the pinned WinUI version supports; do not assume range updates.
- Bound UI-thread mutation cost without exposing half-applied business state.
  Measure page application before adding a custom scheduler. Do not copy our
  native prepare/ready/commit protocol into XAML as a second layout lifecycle.
- Use bounded authenticated IPC, correlation, backpressure and cancellation.
  Coalescing superseded state must not drop actions or acknowledgements.
- Local focus, scrolling and button feedback do not require a worker roundtrip.
  Remote actions remain tied to their original session/target and fail safely if
  that authority expires.

Cursor support must cover before/after paging, unknown totals, partial rows,
filter/sort resets, page eviction and stale responses. WinUI virtualization does
not implement those provider semantics. Retain the displayed anchor and pending
focus target while changing the data window. Frontend reports stable-key demand
and retention needs; workers may not evict those targets without a coordinated
replacement. Keep data retention bounded without requiring geometry for every item.
Do not equate ListView incremental loading with complete bidirectional cursor support.

### Controller focus and scrolling

Route GameInput/XInput/other supported backends through one existing normalized
input path. Preserve multi-controller handling, dead zones, press/release/repeat,
Guide activation and focus-loss/disconnect cancellation. Deliver each semantic
navigation event once; prevent both injected key input and custom routing from
handling the same event. Programmatic traversal can use WinUI `FocusManager` and
control APIs; verify the exact path on real devices.

Use framework focus traversal and bring-into-view behavior. Configure scopes and
explicit neighbors where product boundaries require them: rail/content, tray,
search, grid columns and modal containment. Focus and item selection are distinct;
do not let default selection visuals recreate the stale double-highlight problem.
Check asynchronous realization and scrolling before deciding a target is absent.

Left-stick/D-pad tap/hold behavior remains ordinary directional navigation first.
Right stick scrolls the owning control through its supported scrolling API, with
a defined focus reconciliation policy on release. At a loading edge, stop only
movement into unavailable content; reversing toward existing content responds
immediately. Never replay accumulated repeats when a page arrives. Optional
Nuvio-style held-D-pad continuous scrolling is a later setting, not a dependency.

### Dialogs, local presentation and actions

Use a shared widget-local modal layer rather than a screen-centered desktop dialog
window. An in-tree overlay with framework controls is the default. `ContentDialog`
is usable only if its placement, XamlRoot ownership, focus and multiple-widget
behavior meet this contract. Test pointer blocking and accessibility as well as
controller containment. Context menus/select flyouts must remain above the correct
surface and respect scaling, scrolling and native-media layering.

Details belongs to its parent page and selected game. Each opening has a distinct
identity; focus Play/Install once, preserve focus during ordinary updates, and
restore a valid parent target/viewport when dismissed. Preserve the latest accepted
behavior of retaining details across overlay hide/reopen. Parent/worker replacement
invalidates the modal. Pinned surfaces do not need dialogs, but unsupported requests
must be handled safely without a crash or stealing focus from another surface.

Open the modal shell from existing game data immediately. Fetch descriptions,
achievements and activity asynchronously; their delay cannot block opening or B.
Retain poster cover, completion-status dropdown, X game options, Menu additional
options, Y refresh and RS search-focus entry as explicit product tests.

### Themes, animation and artwork

Preserve theme-agnostic styling through semantic colors, text, spacing, shape,
depth and state tokens. Map trusted theme resources to WinUI resources/control
styles. Port the existing accepted themes, controller glyphs and accessibility
variants. High contrast and text scaling remain functional, not cosmetic checks.
A default Fluent appearance is not a substitute for the accepted WidgetRail design.

Version the author style contract. Allow bounded component overrides and supported
focused/pressed/selected states. A WRSS importer may cover documented properties,
but arbitrary legacy renderer behavior is not a compatibility requirement. Never
accept executable ResourceDictionary content from a community theme. Unsupported
features get migration diagnostics rather than silent fallback.

Global animation settings remain authoritative. Widgets declare intent and stable
identity; shared components select transitions, speed and reduced-motion behavior.
Preserve accepted Slide section and Zoom dialog defaults. Use framework/composition
animations for eligible transforms and opacity, with explicit cancellation and
interruption. Layout changes still require layout work. Do not keep the old animation
presenter or animate the same transform independently in XAML and native code.

Artwork fetching remains capability-checked and host-owned. Use opaque identities,
bounded asynchronous decoding, target sizes and cancellation. Choose a single cache
owner per asset path; do not keep old decoded/native GPU caches plus equivalent
WinUI copies by default. Recycled containers must release/reset image subscriptions,
focus state, commands and animations so one game's content never appears on another.

## Delivery phases

These are proposed work packages, not Plane tickets created by this planning task.
Each ends with a reviewable artifact, executable where applicable, and evidence.

| Package | Deliverable | Exit gate |
|---|---|---|
| WU-01: baseline and product contract | Archived pre-WIDGE-293/current builds and matching widgets; feature inventory, normalized-input replay scenarios, resource budgets | Complete repeatable baseline; no gaps in critical traces |
| WU-02: shell and controller foundation | Pinned .NET/Windows App SDK build, WinUI window, native adapter, rail and basic settings, packaged smoke build | Real transparency/hit testing, activation/show/hide, input ownership, two controllers, DPI/monitor behavior and basic-UI memory pass |
| WU-03: media, pinning and accessibility | WebView2, live preview and pinned window in that shell; themed dialog/menu over them; automation peers | Z-order/clipping/lifetime and device recovery pass; Narrator/NVDA/UIA work; hosting model selected |
| WU-04: safe widget frontend and SDK | WinUI-aligned allowlisted declarations, thin shared adapter, WidgetRail components/templates, theme resolver and broker client | Sandboxed and full-trust workers use the same UI path; mapping/reset/recycling conformance passes; malformed/stale/oversized input rejected; crash/restart and capability isolation verified; no eager UI tree per item |
| WU-05: Playnite end to end | Bridge-backed Home/Library, search/filter, virtualized posters, details/actions and artwork | Full-host controller/loading/modal suite and physical behavior pass; measured against pre-WIDGE-293 |
| WU-06: music and dynamic updates | YouTube Music and Spotify, rails/lists/queue, playback and pinned views | Frequent updates, variable-height rows, artwork and playback do not reset focus or interrupt traversal |
| WU-07: product feature completion | Games and Apps, remaining built-ins/samples, tray/guide, settings, keyboard, popup controls and gallery | Feature inventory complete across controller, pointer, keyboard, themes, UIA and error states |
| WU-08: author and theme migration | Supported WinUI subset and differences, WidgetRail components, SDK examples/templates, version requirements, theme conversion diagnostics, rebuilt packages | Author can build a widget using C# declarations without XAML, host-specific focus workarounds or different UI contracts per trust level |
| WU-09: qualification and cutover | Repeated comparative report, install/upgrade/rollback tests, physical acceptance | All product/resource gates pass before main/release integration |
| WU-10: old UI retirement | Remove unused native renderer/layout/focus/preparation/animation code and associated dependencies | No production route depends on old UI; native services/media/input still pass tests |

WU-01 precedes claims of improvement. WU-02/03 resolve high-risk platform assumptions
before a broad port. WU-04 can proceed after the shell foundation, but WU-05 needs
both platform viability and the real sandbox contract. WU-05 is the first integrated
product slice; it is not a trusted in-process fake widget. WU-06/07 follow its
acceptance gates, with WU-08 alongside. WU-09 is the release decision, and WU-10
removes obsolete UI only after replacement behavior is established.

If standard virtualized controls cannot satisfy the core browsing scenario,
identify the failing toolkit behavior before authoring a custom replacement. Do not
recreate our current UI framework inside WinUI or enter another open-ended sequence
of compensating patches without revisiting the architecture decision.

## Full-host verification and acceptance

### Comparators and reproducible workloads

Use executable baseline `092ee64d`, before WIDGE-293. The immediate integration
parent `41751085` adds assessment documents only. Current native comparator:
`20ef8dd9`, including modal correction `c3b7955f`. Record executable/package hashes,
settings, theme, controller mode, display/refresh, scale and toolchain for each run.
Use each host's compatible widget packages; do not pair an old host with newer
unsupported SDK contracts. Freeze a common data/artwork workload despite differences
in source/package versions.

Keep isolated configuration/package profiles or a verified backup/restore procedure.
The comparison uses the same visual complexity and cold/warm cache states, not
an empty WinUI grid against the populated native product.

Automated full-host tests inject normalized events at the same dispatch boundary
used by controllers. Run frontend, broker and actual sandboxed Playnite worker
against a controlled Bridge-compatible service. Vary delays, failures, stale replies
and artwork completion. This layer must not bypass input routing by calling layout,
focus or scroll internals directly. Complement it with real controller/device tests
and the live Playnite Bridge; normalized replay does not validate physical input.

Cover sustained up/down, deep scrolling, reversal while loading, left/right-stick
handoff, loading boundaries, partial rows, append/prepend/eviction, filter/reset,
background updates, 100/125/150/200% scale, repeated game-detail openings, hide/show,
worker replacement, native media and pinning. Test cold/warm artwork and motion
settings; qualification uses normal animation/depth enabled. Include the user's
ultrawide/125% environment and a conventional viewport.

### What to record

Correlate input receipt, target decision, provider request/reply, frontend update,
focus/scroll change and visible presentation using stable identities and monotonic
timestamps. Measure UI-thread stalls, collection-notification/template cost, GC,
process-tree CPU/private memory and GPU memory. Use ETW/PresentMon where applicable;
correlate actual visual change with recorded/video evidence. A successful action,
property update or render submission is not proof of the displayed response.

Use buffered capture with explicit loss accounting. A trace missing the relevant
interval is inconclusive. Record process baselines including service/worker costs,
not just the frontend process or .NET heap. Separate provider waits, framework
work, display scheduling and optional background playback.

### Proposed gates to freeze in WU-01

- Zero reproducible stuck navigation when a loaded reachable row exists, unintended
  header escape, stale highlight, wrong-item action/artwork, viewport jump, or input
  replay debt in the scenario matrix. Provider delays do not block reversal.
- Warm input-to-visible feedback and details-shell opening: p95 at most 100 ms.
  Optional metadata must load separately; record cold-start behavior independently.
- At 60 Hz during continuous motion: presented-frame interval p95 at most 16.7 ms,
  p99 at most 33.3 ms, and no recurring admission stalls above 50 ms. Characterize
  120/240 Hz separately; passing 60 Hz is not proof of high-refresh quality.
- Demonstrated improvement in the reported reversal/loading/modal scenarios against
  pre-WIDGE-293, and preserved behavior wherever that baseline already works well.
  Report median/p95/p99, worst events and variance across repeated runs, not just
  one favorable sample. Physical acceptance remains required.
- Establish visible-idle, hidden, library, media and peak memory budgets before
  bulk migration. Proposed starting ceiling: warmed total private committed memory
  within 25% of the equivalent pre-WIDGE-293 run, with GPU memory tracked separately.
  This is a proposed product limit, not measured WinUI behavior. Any budget change
  must be an explicit tradeoff before qualification, not retrospective justification.
- No hidden repaint loop or monotonic memory growth over repeated 30-minute runs;
  verify retirement after widget unload, dialog dismissal, image churn and pinning.
- Built-in automation peers and shared custom-control peers expose appropriate
  roles/actions, names, focus events, virtualized items and modal boundaries.
  Verify Narrator/NVDA and UIA inspection, high contrast, keyboard and text scaling.
- Capability/sandbox, lifecycle, native media, input ownership and installer gates
  all pass. Performance alone cannot justify losing product functionality.

Unit and contract tests remain useful, but assertion count and equivalence between
two code paths do not establish these outcomes. A failing platform/product gate
must yield a specific defect or architecture decision before migration continues.

## Packaging, compatibility and retirement

Pin a supported stable Windows App SDK/.NET combination after checking support
policy, OS minimums and required APIs. C# frontend, SDK and services may target
different appropriate frameworks; avoid changing all library targets unnecessarily.
Start with the existing per-user Inno-based installation model. Evaluate unpackaged
WinUI startup and Windows App SDK framework-dependent versus self-contained
provisioning on clean machines, then choose one supported distribution path.

Framework-dependent deployment can share/service Windows App SDK runtime packages;
self-contained deployment improves version isolation/offline packaging but increases
size and can affect startup/memory, and the app owns runtime updates. .NET and
Windows App SDK deployment choices are separate. Do not switch to MSIX merely
because the frontend is WinUI; assess package identity only where an API requires it.
Update installer prerequisites, repair/uninstall/rollback and notices accordingly.

Version the UI protocol and package minimum-host requirements. Rebuild all bundled
and sample widgets; unsupported old UI packages get a clear migration requirement,
not a permanent native renderer fallback. Preserve private state, credentials and
permission consent independently from UI-package versions. Back up settings/layout
before conversion and test rollback to the native product without destructive
schema downgrades. Do not assume same-language migration means binary compatibility.

Author documentation must explain ownership: stable item keys and generation,
async providers, cancellation, page/collection state, local UI interaction, themes,
modal focus, capabilities and media lifetime. Include runnable examples and a
conformance gallery. Document direct WinUI mappings separately from WidgetRail
components, including unsupported APIs and deliberate semantic differences. Do
not promise toolkit neutrality or complete WinUI API parity. Authors should not
need XAML or access to WinUI internals, regardless of the worker's trust level.

Implementation stays on `codex/winui3-*` worktrees with a dedicated integration
branch. Preserve both native comparators and evidence. Do not merge the native
overhaul into main as a prerequisite. No main merge, push or release is part of
this planning task; existing task integration permissions do not substitute for
release authorization. The running candidate remains unchanged.

After qualification, remove the old UI implementation and its unused dependencies,
not independently used Windows input, media/capture, provider or sandbox code.
Maintain one production frontend. Compose remains a documented contingency only
if a concrete WinUI blocker requires revisiting the selected direction.

A reliable calendar estimate follows WU-02/03, especially true transparency and
external-content presentation. This is a product migration with SDK/theme ports,
not a language conversion or a few-hour renderer swap.

## Sources and limits

Repository basis: [platform architecture](platform-architecture.md),
[security boundary](security-and-trust.md), [previous toolkit assessment](ui-architecture-assessment.md),
[native changes](native-ui-evolution-assessment.md), [collection corrections](native-collection-transactions.md),
[modal regression](native-modal-ordering.md), and [installer contract](../../eng/installer/README.md).

Official documentation checked 2026-09-27:

- [ListView / GridView and virtualization](https://learn.microsoft.com/en-us/windows/apps/design/controls/listview-and-gridview).
- [ItemsRepeater responsibilities and limitations](https://learn.microsoft.com/en-us/windows/apps/design/controls/items-repeater).
- [Directional focus APIs](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.input.focusmanager.trymovefocus).
- [Windows App SDK composition and external-content limitations](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/composition).
- [WinUI windowing](https://learn.microsoft.com/en-us/windows/apps/develop/ui-input/windowing-overview).
- [WinUI 3 DesktopWindowXamlSource](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.hosting.desktopwindowxamlsource).
- [Accessibility and automation peers](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessibility).
- [Windows App SDK deployment models](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/deploy-overview).

No WinUI runtime performance, true overlay transparency or native-media compatibility
has been established by this planning task. Those are early executable gates, not
assumptions implied by choosing the framework.
