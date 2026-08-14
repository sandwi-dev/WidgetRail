# AVP-004-REDESIGN responsibility map

| Concern | Retained owner | Avalonia candidate responsibility |
| --- | --- | --- |
| Catalog, package discovery and installed Community merge | Existing `WidgetBridge` / catalog/runtime | Display every returned descriptor in one dynamic stationary tray. |
| Widget processes, trust, lifecycle, providers and domain behavior | Existing bridge/runtime/catalog/domain implementations | Request typed lifecycle changes through `WidgetPresentationSession`; never load a widget or domain assembly into Avalonia. |
| Authenticated transport and stale authority | Existing bridge protocol plus accepted `WidgetPresentationSession` | Publish typed state on the UI scheduler and submit the exact current authority; no parallel schema or transport. |
| Outer geometry | Native monitor/work-area placement plus one Avalonia HWND | Choose one work-area-relative shell geometry. Snapshot surface hints select only compact/standard/wide content behavior; they never move the HWND, tray, or guide. |
| Semantic rendering | Existing typed `WidgetProtocol.ViewSnapshot` | One closed component compiler maps node kind, orientation/axis, responsive visibility, grid keys, and advanced preset/slot to Avalonia ControlThemes, standard UIA, and virtualized collections. Authored style classes and bridge-computed geometry are not presentation roles. |
| Scrolling | Typed semantic Scroll/Grid ownership plus the shell page boundary | Give each axis one owner. A direct vertical Scroll in a root stack is hoisted with its siblings into one recycling page-level list; otherwise the explicit semantic owner or one outer page owner scrolls. No nested catch-all scroller. |
| Action authority | Snapshot node/action declarations plus managed session validation | Revalidate current source-node/action/value/text shape, then send exact runtime/session/snapshot/scope authority. Controls never become authority. |
| Guide, controller device lifecycle, repeat/neutral, foreground and placement | Accepted native `OverlayPlatformInterop` ABI v1 | Supply the single Avalonia HWND; marshal events to the UI dispatcher and route them through the shared shell/session. |
| Focus and shell policy | Avalonia FocusManager/XYFocus and presentation services | Own an explicit tray/content/modal state machine, component-zone spatial search, direct tray cycling, content entry/exit, slider behavior, and per-widget focus-persistence memory. |
| Transition and reduced motion | Avalonia `TransitioningContentControl` / `CrossFade` | Transition content only. Tray, guide, modal and status stay outside the presenter; admission remains latest-wins and releases superseded visual/artwork ownership. |
| Composition | Explicit manual root in `MainWindow` | Own one bridge child, one managed coordinator, one shell, and one native interop handle with bounded teardown. |

No compiler or renderer branch names a widget/package/provider/element/text/
style class or known tree shape. The only specialized layout switch consumes
the protocol's closed `WidgetAdvancedPresentationKind`, preset, and slot enums.
Raw semantic IDs remain
attached to controls; UIA IDs use canonical base64url of the exact widget/node/
collection tuple with a bounded, non-lossy contract.

## Composition decision retained from AVP-003

The seven-sample AVP-003 comparison measured direct
`Microsoft.Extensions.DependencyInjection` 10.0.10 against manual composition.
DI added about 20.7 ms to the tiny composition call and 163,766 published bytes;
the memory difference was noise-sized. AVP-004 still has one application
lifetime and explicit ownership, so manual composition remains clearer and no DI
package is retained. The tracked comparison remains
[evidence/composition-comparison.json](evidence/composition-comparison.json).
