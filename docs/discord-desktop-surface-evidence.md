# Discord Desktop visual surface feasibility

Date: 2026-10-27. Read-only source investigation for the
[desktop integration plan](discord-desktop-integration-plan.md).
No Discord process was restarted, patched, attached through CDP, or controlled
through the UI. No profile, credential or account database was read. No host
implementation or live visual acceptance is claimed.

## Verdict

There is a concrete no-reencode route for incoming video **inside a Discord-owned
renderer/window**: the inspected desktop renderer creates a native Discord stream
and assigns it to an HTML video element. Use that existing native-video component
and its reference-counted sink lifetime in a dedicated Discord pop-out first.
Do not copy frames to a WidgetRail WebView, create a second WebRTC connection, or
assume the browser receiver implementation can be transplanted unchanged.

For the full Discord page, first qualify a temporary lease of the existing
frameless main BrowserWindow, coordinated within the WidgetRail content rectangle.
This preserves the original renderer, its preload, native media binding, normal
keyboard/IME and accessibility. Moving its WebContents to another window is a
more invasive alternative, not an established shortcut. Current desktop code
retains many references to `mainWindow.webContents`.

Both recommendations still need generated-window and then approved live-client
proof. Coordinated windows are not a supported XAML embedding API: clipping,
z-order, modal layering, foreground and restoration can invalidate this route.
The current `UI.WindowPreview` is not sufficient for either interactive full
Discord or guaranteed continuous high-quality video.

## Source provenance and findings

### Native video path

The public cached bundle `public-web.33a8a649a2fda558.js` has SHA256
`5B2AFCF1DEA50E7E6E98E460CF8DBA00101FF37C4610363F7002719DE4F7BB1C`.
AST-extracted modules are under ignored
`artifacts/discord-desktop-provider-20261027/`; they were parsed, not executed.
The loaded desktop renderer's exact build has not been verified against this
cache. Module IDs are evidence locators, not proposed production bindings.

| Source location | Observed behavior | Consequence |
| --- | --- | --- |
| Module229209, lines10-44 | Checks `window.createDiscordStream`, constructs the stream, registers a direct native sink, and reference-counts consumers. Last release removes the sink. | Acquire through the existing wrapper/component; do not install a second uncoordinated sink registry. |
| Module206959, lines1444-1503 | Native Video component creates a muted HTML video, assigns acquired `.stream` to `srcObject`, and clears/releases it on pause or unmount. | Incoming picture need not be re-encoded. Audio stays in Discord's normal stream audio context. |
| Module206959, lines1458-1486 and2199-2202 | Reports physical container dimensions through the provider's resize hook. | Preserve native receiver demand/size adaptation rather than forcing a fixed browser-track size. |
| Module206959, lines1569 and2309-2310 | Native active-sink changes are forwarded to current media connections. | Lifetime/visibility determines receive demand. An extra video element alone is not proof that background demand stays active. |

Installed native wrapper inspected at
`%LOCALAPPDATA%/Discord/app-1.0.9265/modules/discord_voice-1/discord_voice/index.js`,
SHA256 `C06FC0D68FBEB6B139E10AE98810918CB91512325E221922FE0DE9B85A9CB8C1`:
lines259-343 expose regular/direct video sink bookkeeping and a one-frame CPU
readback helper. The native `.node` binaries were not loaded by the investigator.
This wrapper corroborates direct sink lifetime but does not establish the current
stream identity or a successful cross-window rendering transfer.

`getNextVideoOutputFrame` is not the preferred transport: it is a single-frame
readback API with timeout and would encourage a CPU screenshot loop. The preferred
route keeps native stream rendering within Discord, without an added encoder.

### Installed window model

Read-only installed package inspection found app1.0.9265 declares Electron42.7.1
in its resource package metadata. This is a build declaration, not a runtime
`process.versions` measurement. Do not assume newest Electron documentation is an
exact compatibility contract.

Installed desktop core archive:
`%LOCALAPPDATA%/Discord/app-1.0.9265/modules/discord_desktop_core-1/discord_desktop_core/core.asar`,
SHA256 `922C22E53ECEB3A62FDFA51240EBBBB55E8E75CBC03CAA6B711F6193448FA48E`.
The installed archive's provenance was not independently compared against a clean
vendor release; these findings apply to its observed bytes only.

| Archive entry and byte offset | Source finding |
| --- | --- |
| `/bundle.js`,180132 | Main window is a frameless BrowserWindow with mainScreenPreload, context isolation and desktop native bindings. |
| `/bundle.js`,186418 | Main WebContents handles created child windows and runs Discord popout setup. |
| `/bundle.js`,141982 | A document-PiP path exists, but is driven through the desktop's own trigger; availability does not prove it can select our target stream or satisfy pin layout. |
| `/bundle.js`,79920 | A frame-view implementation uses WebContentsView inside the original Electron window, with restricted callers/URLs. Its input path uses a debugger connection. |

Do not adopt the frame-view helper merely because its name sounds suitable. It
handles activity frames, not an arbitrary foreign WinUI surface, and its private
restrictions/debugger path do not satisfy the proposed production contract.

### Existing host constraints

| Current repository source | Confirmed boundary / required owner |
| --- | --- |
| [WindowPreview.cs](../src/WidgetSdk/WindowPreview.cs), lines5-50 | Explicitly view-only and permission-bound; minimized/protected/resource-limited sources may fall back. |
| [WindowPreviewSurface.cs](../src/OverlayFrontend.WinUI/Previews/WindowPreviewSurface.cs), constructor | Hit testing is off and accessibility is Raw. Capture demand depends on owner/viewport/element visibility. This is not live foreign input routing. |
| [WindowPreviewPolicy.h](../src/WinUiWindowPreviewNative/WindowPreviewPolicy.h) | Output is capped at960x540; source size/count/memory bounded. Existing safety limits cannot simply be removed to claim stream parity. |
| [PinnedWidgetWindow.cs](../src/OverlayFrontend.WinUI/Shell/PinnedWidgetWindow.cs), SetInteraction/ReconcileOrder | Host owns passive click-through/no-activation, z-order, native pin chrome and opacity. A Discord window needs equivalent host-coordinated lifetime, not independent always-on-top flags. |
| [PlatformInputPump.cs](../src/OverlayFrontend.WinUI/Input/PlatformInputPump.cs), lines147-210 | External foreground normally dismisses overlay. Auxiliary controller ownership is a separate exact-window check and route. |
| [ForegroundDismissalPolicy.cs](../src/OverlayFrontend.WinUI/Input/ForegroundDismissalPolicy.cs) | A blanket exemption for the entire Discord process would be too broad for one leased surface and could keep overlay open over unrelated Discord dialogs/windows. |
| [MainWindow.ForegroundReturn.cs](../src/OverlayFrontend.WinUI/MainWindow.ForegroundReturn.cs) | Prior-app restoration avoids replacing its destination with owned pins/browser UI. Add leased Discord windows to this ownership decision, not to the previous external destination. |
| [NativeBrowserDialogController.cs](../src/OverlayFrontend.WinUI/Input/NativeBrowserDialogController.cs), Begin | Existing admission grants a specific auxiliary process foreground eligibility while the host owns focus. Reuse the authority pattern, not the dialog-specific lifecycle verbatim. |
| [NativeDialogPointerInput.cs](../src/OverlayFrontend.WinUI/Input/NativeDialogPointerInput.cs), Receive | Controller pointer routing is foreground-validated and bounded to an admitted target; it is not a full text/IME/accessibility implementation. |

## Qualified next implementation shape

1. **Acquire:** adapter supplies an exact Desktop main/popup identity to the host
   via the authenticated companion connection. Broker verifies PID/creation/HWND
   generation and maps it to an opaque lease. Arbitrary widget-supplied handles
   are not admitted.
2. **Incoming video:** create or acquire a Discord-owned popout for the exact native
   stream incarnation. Prefer the existing native Video component/refcount wrapper
   and provider resize/demand reporting. Keep video muted. Do not duplicate the
   desktop's stream audio. Preserve an already open user popout and its ownership.
3. **Full UI:** temporarily position the original frameless main window in the
   widget's content region. Keep its original WebContents/preload intact. Record
   bounds, visibility, minimize/maximize, z-order, route and any scoped style
   changes; restore only state still owned by the lease. Suppress or correctly
   reconcile Discord's window-bounds persistence during borrowed placement.
4. **Passive versus interactive:** use nonactivating/click-through presentation
   for passive video. Full UI gets actual admitted foreground for keyboard/IME;
   controller pointer input routes to that exact window with a reserved B/Guide
   return. Release all synthetic held buttons before transfer.
5. **Layering:** host owns presentation above/below pin chrome and native dialogs.
   Hide or clip content at page transition/modal boundaries and apply fresh DPI
   geometry. Do not let independently topmost Discord windows cover host menus.
6. **Retire:** unwatch, account/call change, stream restart, window destruction,
   host crash or lease expiry retires only our presentation. Restore the borrowed
   main window and return foreground through host authority. Never stop a user's
   call/broadcast as surface cleanup.

Borrowing the main window means it cannot remain simultaneously displayed in its
original desktop placement. This is an inherent single-surface constraint, not
an implementation detail to conceal. Source-level evidence cannot establish that
temporary borrowing preserves every Discord handler. If this disrupts normal
desktop use or fails restoration, reject it and assess a supported alternative.

## Supported Electron APIs versus assumptions

[BrowserWindow](https://www.electronjs.org/docs/latest/api/browser-window) documents
inactive show, focusability and ignoring mouse input. These support a cooperative
window experiment, not cross-process XAML embedding. Electron's parent-window
API expects Electron window objects, not a foreign WinUI HWND.

[WebContentsView](https://www.electronjs.org/docs/latest/api/web-contents-view)
can adopt existing WebContents, but only one view at a time. It does not guarantee
that moving the official client's main WebContents preserves application-owned
window references, dialogs or native media bindings. Defer this approach until
the simpler original-window lease has been tested and rejected for a concrete reason.

[webContents.sendInputEvent](https://www.electronjs.org/docs/latest/api/web-contents#contentssendinputeventinputevent)
requires the containing BrowserWindow to be focused. A hidden captured view with
input forwarded while the game stays foreground is not a documented substitute.
Physical keyboard/IME and screen-reader interaction should use the actual focused
Discord window. A cross-window UIA tree is not automatically part of the host's
XAML automation tree; accessibility needs explicit native verification.

[Offscreen shared-texture rendering](https://www.electronjs.org/docs/latest/tutorial/offscreen-rendering)
is a possible later compositor strategy, not a first implementation promise. It
requires native texture handling and does not prove that this client's native
video sink renders offscreen. Do not convert the existing client into an offscreen
renderer or create another signed-in client to evade the full-UI requirement.

Vencord source at commit `443688b5366a4d43e9868519f48a4544cd7520d4`
([native IPC registration](https://github.com/Vendicated/Vencord/blob/443688b5366a4d43e9868519f48a4544cd7520d4/src/main/ipcPlugins.ts))
registers plugin native exports directly. Our native methods must validate the
sender's exact WebContents/frame/origin and lease themselves. Native plugin access
does not automatically confer surface authority on every Discord page.

## Smallest meaningful next fixture

Before any installed-client modification, build one two-process generated fixture:
a WinUI host with existing pin/input/foreground machinery and an isolated Electron
client with a frameless main page, local video popout, input fields and modal/file
dialog test affordances. Pin the Electron runtime compatible with the declared
desktop version. No account, microphone, network media, user file capture or CDP.

The fixture should resolve a specific decision: **can a leased top-level Electron
surface behave as part of WidgetRail without reparenting, frame copying or broken
foreground?** It is not another throughput benchmark.

Required checks:

- Passive video above a generated game window remains moving and click-through
  with overlay hidden; no focus changes or duplicate audio.
- Overlay/full-page enter and B/Guide exit return to the original foreground app.
  Host does not dismiss merely because its exact leased window receives focus.
- Native host menus/dialogs appear above the leased surface; input cannot reach
  the obscured Discord page. Page transitions leave no orphan rectangle.
- Controller pointer, wheel, click/release and host text keyboard reach an admitted
  field; physical typing, selection, IME composition and accessibility work through
  real foreground rather than DOM-value mutation.
- Host/pin resize, DPI/monitor move, opacity and passive/interactive transitions
  apply coherently; ordinary desktop window placement is restored after release.
- Client/window/host crash or handle reuse retires the correct lease and leaves
  no held input, topmost content or accidental prior-app target.

Only after that generated fixture passes should the approved desktop plugin test
the existing native Video component and real full client. Required live checks
include simultaneous watching/sharing, main-window minimize, long overlay hide,
open-full-Discord/back, stream restart and user-owned popout coexistence. A local
generated `<video>` cannot prove Discord native sink continuity or cross-window
compatibility. Those remain explicit gates.

## Generated-fixture follow-up: common input/rendering observation

The generated fixture and detached stock `--validate-controls` comparison both
exposed their expected accessibility trees but failed to receive the selected
Computer Use button/keyboard action. Their captures showed underlying desktop
content. No borrow/restore scenario ran, and no native surface pass is claimed.
See `artifacts/discord-desktop/surface-run-1/interaction-result.json` and
`surface-run-detached-1/interaction-result.json`. All generated processes were
closed; the installed Discord client was not targeted.

Read-only static review found no main-window capture-exclusion call. Ordinary
`SuppressBorder` changes caption/DWM border policy, not input transparency. A
shared controller diagnostics file-rotation failure occurred in custom runs and
was caught safely, but the stock controls route does not create that replay pump.
Neither is established as the common cause. Likewise, Grid versus Page alone
cannot explain a stock Page failing in the same way.

One concrete isolation omission was corrected: the new custom flag is now in
`Program.Validation.cs`'s early fixture classification. That prevents default
production-profile election before custom validation configuration. The stock
route was already classified, so this is an independent correction, not an
explanation of its failed interaction.

The opt-in [ValidationWindowObserver](../src/OverlayFrontend.WinUI/Validation/ValidationWindowObserver.cs)
adds at most64 read-only observations, with no action/focus/style/capture changes:
own native handle and foreground; native style, alpha, affinity and cloaked state;
XAML Loaded/host-visible/size/opacity/hit-test chain; DIP and screen geometry/DPI;
the native root at the target button center; and routed pointer/key event counts
without key codes/text. This distinguishes a capture-only defect, mismatched tool
target, wrong coordinates, obscured window, missing foreground or invisible XAML
without invoking the control behind the UI's back.

The observer build published successfully to
`artifacts/discord-desktop/surface-frontend-observation-1`; its log is
`surface-observation-build.log` with a unique binary log. No new UI run was made
while adding it. Next is one coordinated stock-controls observation using
`--validation-window-observation=<fresh file>` and
`--validation-window-target=Widget.second`, inspecting startup data before input.
Choose the next action from those measurements instead of another blind retry.

## Executed handoff and native geometry (2026-10-27)

The user confirmed stock rendering and physically ran the generated Electron44
handoff. `surface-manual-44-1/result.json` records nine passed checks, including
real foreground transfer without host dismissal, then a failed derived-geometry
assertion. Restoration was not reached. Both generated processes exited; the
installed Discord client was left running and unchanged.

The hidden follow-up `surface-geometry-native-3/result.json` reads actual Win32
client and outer rectangles at125% DPI. Requested808x456 client pixels became
808x457 (fixed) or809x456 (resizable): within the existing two-pixel tolerance.
Electron getBounds/dipToScreenRect compounds rounding; GetWindowRect additionally
includes invisible resize borders. The validator now compares native client
pixels and records all geometry before assertions.

Saving/restoring Electron getBounds remains defective: original outer823x613
became826x614, with client805x604 becoming808x605. Both resizable policies drifted.
Exact native restoration now has its own assertion. Do not use hard-coded border
subtraction or loosen that gate. Host/companion-owned exact geometry and crash
restoration require qualification before adopting this surface strategy. All
changes and measurements remain in generated fixtures, not production contracts.

## Own-process native geometry result

The generated Node-API helper now passes35 checks in
`artifacts/discord-desktop/geometry-native-run-5/result.json`, using native build3
and Electron44.9.3. Exact physical client placement and original outer/client
restoration are verified for resizable windows. Fixed-size and minimum-size
refusals remain explicit negative cases. A generated host's termination closes
the local pipe and triggers explicit restoration while the test client stays
alive. No focus, input, capture or media is involved in these hidden checks.

Installed9266 `bundle.js` explicitly sets main-window `resizable:true` at194655,
and default minimum940x500 at189614. The representative interactive fixture now
uses resizable:true, while the hidden suite retains nonresizable failure and
minimum-policy cases. Host layout must honor the client's reported minimums.
This source observation does not prove full-client visual usability in a smaller
region, and the addon never clears application min/max constraints.

The native helper is guarded by process, thread and a per-window generation
marker. Strong JS ownership and explicit restore/disconnect/finish are required;
GC cleanup deliberately does not reposition a window because Electron resize
callbacks can re-enter JavaScript. Native placement has no retry loop, guessed
border offsets, focus changes or cross-process window authority.

The remaining production gate includes concurrent user/app layout reconciliation,
minimized/maximized state, monitor changes, modal/input behavior and actual Discord
video continuity. The new helper is isolated under SurfaceFixture/native; no
shipping host surface contract or installed Discord plugin has adopted it.
