# Hosted browser

The protocol, SDK declaration, Browser intent receiver and host-owned WebView2
surface are implemented. Browser ships in both Production and Developer editions.
It is also a reusable SDK element; widgets do not implement their own WebView.

```csharp
UI.WebBrowser(new WebBrowserDocument(
    "guide.page", navigationId, "https://example.com/guide", "Game guide"),
    BrowserInteractionMode.InteractOnFocus, "guide.browser");
```

This is a reusable host component, available to widget authors as an ordinary
element. Its native navigation chrome is shared so each widget does not need to
reimplement browser input, history and current-page status. The Browser sample
provides address entry and an intent handler; it does not render HTML itself.

The declaration requires protocol 66. Keep the document ID stable across
compatible presentations. Advance the navigation ID only for a new authored
navigation, including deliberately reopening the same URL. Re-rendering must not
reload a page, erase its history or move its scroll position. Native history and
page contents stay host-owned and are not sent to the widget.

URLs are bounded to 2,048 characters and must be absolute HTTP(S), without
credentials, surrounding whitespace or control characters. IDs follow the normal
identifier contract. Navigation IDs are positive integers up to 2^53 - 1. An
accessible label is required and bounded to 256 characters. The element has no
widget action, script, HTML, DOM, native-handle or host-object payload. Navigation
changes carry resource and authority impact in atomic presentation updates.

The Browser widget handles `widgetrail.web.open` and opts that mapping into
passive delivery. It starts on a local blank page with bookmark/history access. An
accepted intent or native address entry opens a website. Invalid requests preserve
the existing document.
Its address entry accepts up to 2,048 characters. Extended text entry requires
protocol 66; existing SDK fields keep their 96-character default and all commits
remain bounded by the individual control declaration.

The host lazily creates a profile isolated by widget and account. Private storage
is the ordinary-browser default; persistent storage is explicit. Up to four root
browser sessions and two hosted popup controllers can be resident. The owner
retires them with the worker/document lifetime.
Main and pinned presentations borrow the same controller; crossing XAML roots
waits for the native child's unload/load lifecycle. Native history and navigation
survive compatible widget updates.
While a visible pin exists, it owns the page regardless of main-widget input
readiness. Opening the main widget shows a pin notice and a View hint, without
moving or recreating the page. Unpinning returns the retained page to the main
widget. Input ownership and native placement are deliberately independent.

The required `interactionMode` SDK argument selects both entry and Back behavior:
`ActivateToInteract` requires A to enter and B to leave; `InteractOnFocus` starts
browsing when the element receives admitted input focus and lets B follow normal
widget/tray routing. There is no separate Back flag. LS toggles between the page and native toolbar. Left stick moves the pointer, right stick scrolls,
A presses the mouse button, holding it while moving the stick drags, and releasing
it releases the mouse button. D-pad jumps geometrically between webpage controls. Toolbar left/right navigation selects commands, and LS or Down returns to the page. LB/RB navigate history, X reloads/stops, Y edits the current address,
LT/RT zoom out/in (including held repeat), A opens editable fields, and
Menu opens page actions at the pointer; Open externally remains in the toolbar. The press used to enter Interact never clicks the page.
Opening a dialog or losing input cancels a held pointer outside the page.

### Directional jumps

While interacting with page content, D-pad moves the controller circle to a nearby
control in that direction, considering alignment and distance. A still performs
the ordinary native click; jumping never activates a button or opens the keyboard.
Left-stick movement immediately resumes free pointing. Right stick retains scrolling.
At a scrollable list's edge, D-pad scrolls within the list before moving into another
pane. There is no wrap to the opposite edge of the page.

The shared implementation applies to browsers, webapps, provider documents,
hosted pop-outs, pins and expanded views. No new SDK declaration is needed. Native
HTML controls, links, meaningful interactive ARIA roles, hidden-input label
proxies and open shadow roots provide the most reliable targets. Hidden, disabled,
inert and occluded controls are excluded; nested clickable ancestors do not steal
navigation from their child controls. Held A keeps drag behavior and suppresses jumps.
Keyboard focusability (`tabindex`) alone does not make a pointer target: scroll
regions, lists, resize separators and hover-only rows are skipped unless they
also expose an action. Explicit click attributes and pointer-cursor affordances
remain fallbacks for custom controls. Ordinary jumps must advance from the actual
pointer position, including when only part of a control is clickable.
Pointer-cursor decorations inside an actionable parent share that parent's
navigation geometry; they do not become extra targets or change the source
rectangle. Descendants with their own interactive semantics or explicit click
attributes remain independent controls.

List membership uses DOM ownership, not just overlapping screen rectangles.
After scrolling, the host retains the scrolled region and original source, then
scans that region's subtree. Scroll distance follows meaningful list items,
articles and table rows, including text-only rows. Oversized rows advance in
bounded portions; unknown markup uses a smaller viewport-relative step. When
newly revealed content has no action, the circle rests near the advancing edge
inside the scroller; it does not jump into an unrelated header. Scrolling can
reposition a newly revealed target above the pointer's old screen position while
still advancing through the content in the requested direction.

Holding D-pad defers webpage hover until release, while the circle glides between
destinations. The previous page hover is retained so an already-open menu remains
navigable; intermediate targets do not receive mouse-move events. A immediately
settles the circle and synchronizes the native pointer before clicking. Left-stick
takeover restores immediate movement and hover, without a trail or interpolation.
Animation never sends intermediate page input. System reduced motion
disables glide. Timing
constants are centralized in `Input/PointerMotionOptions.cs`; no widget option is
required. Native chooser windows retain their existing pointer presentation.

The host uses bounded, on-demand geometry scans in isolated worlds, and traverses
only frames owned by that WebView. It returns positions rather than page text or
field values, and releases temporary DOM references/sessions after each request.
The current control and nearby DOM branches are inspected before the broader
scan, so an earlier large subtree cannot starve a later toolbar. If the broader
scan is truncated, every discovered target remains eligible under the normal
direction/distance/alignment ranking, including diagonal destinations. Local
discovery is a priority, not an eligibility boundary. There is no mandatory row
or column overlap. The source control is identified explicitly,
independent of scan order.
No scans run during neutral controller polling. Cross-origin and separate-process
frames are supported; rotated/skewed/mirrored/perspective frame transforms and
depths beyond eight are skipped. Large documents, closed-shadow internals without
host semantics, canvas controls and unmarked click handlers may require free
pointing. Native browser/Windows chooser dialogs retain their existing controls;
they do not use webpage geometry navigation.
HTML drag-and-drop uses Chromium's drag interception and delivery only for the
controller-owned gesture. Drag data is bounded, stays inside its browser, and
cannot contain files. Ordinary physical mouse handling is retained outside that
gesture. This does not implement dragging desktop files into or out of WidgetRail.

Zoom uses WebView2's native Chromium touchpad pinch gesture anchored at the
controller pointer, with requested steps bounded to 100–250%. The header shows
the scale reported by the browser; its percentage button resets to 100%. Input
coordinates use the visual viewport so pointer targeting remains accurate while
zoomed. Native viewport boundaries can limit anchoring near page edges.
The existing controller guide shows the actions that fit, with its complete help
text retaining the remaining mappings. The compact header uses the active host
theme and retains mouse/keyboard buttons; steady-state status text is removed.

The Browser sample uses `InteractOnFocus`. A visible passive pin never grants
input merely because its page is ready. The A gesture that enters the widget is
consumed by the shell, including its release. Focus loss cancels held page input.
Mode selection is required in `UI.WebBrowser(document, interactionMode, id)`;
missing or unknown modes in protocol declarations are rejected.
The address dialog consumes B to cancel its draft. A live page keyboard consumes B
to close while keeping edits. Preparation is cancellable too, and late responses
cannot reopen cancelled edits.

Address and page text use the existing themed controller text-entry dialog.
Page text supports ordinary editable inputs, textareas, passwords and rich
contenteditable controls, including fields in open shadow roots. Password entry
shows only masks for existing text and sends incremental editing commands.
Rich editing inserts at the captured selection through native browser input,
preserving surrounding formatting and mention nodes. Send Enter is an explicit,
separate action. Same-origin and cross-origin iframe editing is validated with synthetic pages, including frame reloads.
A native remote-object reference identifies the exact field. Every command checks that
the document and focus are still current and the field remains connected and
editable. Navigation, loss of input and worker retirement revoke the edit; a
replacement field with the same HTML ID never inherits it. Fixed host scripts
and generated JSON are used internally; widget code receives no page values,
remote objects or scripting capability.

The toolbar provides Back, Forward, Reload/Stop and Open externally. External
opening uses the existing release-aware handoff, including returning from pinned
interaction to the main foreground owner first.

Supported website permissions use host prompts identifying the widget and exact
HTTPS origin, with Allow once, Always allow and Block choices. Website controls
manage permissions or clear all website data for the selected profile. A visible
browser widget also exposes **Website permissions** in its tray menu. Microphone,
camera and the other supported permissions are always listed with explicit
**Ask next time / Allow / Block** choices, including when no decision was saved.
Changing a permission reloads pages in that profile and ends active calls; it
does not clear login data. Ask also resets a temporary decision.
Uploads and downloads use origin-labelled confirmation and Windows file pickers.
Script dialogs and standard context menus use the existing controller dialog
framework. External URI schemes, HTTP basic authentication and invalid certificates
remain denied; use the external browser for those unsupported authentication
workflows. Popup routing is configured separately from ordinary link routing.
Password saving, autofill, host objects, web messages and developer tools remain
disabled. The host
sends only fixed browser commands; page contents and scripting APIs never cross
widget IPC. Use Open externally for unsupported site workflows. A browser process
failure has a visible recovery message; A reopens the page with a new controller.
A failed authentication popup closes on recovery so the user can retry the action
from its unchanged parent page.

The browser is separate from the sealed embedded-media adapter and does not
change that adapter's origin or resource restrictions. Synthetic checks cover
long addresses, stale authority, pin transfer, focus, native history and pointer
clicks; real-site/controller usability remains a physical acceptance gate.


Physical mouse and keyboard input use WebView2 while its surface is interactive.
The native page participates in Tab focus and receives editing/navigation keys;
the host does not reinterpret its arrow keys as controller pointer movement.
Neutral controller polling leaves keyboard focus and mouse hover alone. Actual
controller activity returns to the existing controller route, while physical mouse
activity hides the controller cursor and ends its held pointer gesture. Passive
pins still require the normal interaction step before accepting any input.


A completed controller A-click on an ordinary editable text field opens the same
controller keyboard automatically. The press alone never opens it, and dragging
or selecting text while holding A suppresses automatic editing. The host hit-tests
the completed click, resolves the exact focused field (including open shadow roots
and associated labels). Clicking elsewhere does not reopen an older focused field;
clicking the same field again reopens its keyboard. Mouse clicks and keyboard Tab
navigation do not trigger this dialog.

Browser fields use live editing: native incremental insert/key commands update the
page as you type, preserving its selection, undo and rich-editor nodes. B/Done
close the keyboard while retaining those edits; Send Enter explicitly sends an
Enter key to the website. The host shows a bounded preview and receives only masks
for existing password text. Widget `UI.TextEntry` fields, address entry and website
text prompts remain buffered until confirmed. This does not add per-keystroke SDK
events or expose page contents to widgets.

Automatic editing includes supported ordinary fields, passwords and rich editors.
Read-only controls are excluded. Frame editing supports up to eight nested frames, including separate renderer processes. Rotated, skewed, mirrored or perspective-transformed frame ancestors are excluded because the pointer cannot be mapped reliably by this path. New
navigation, a newer gesture, input loss or cancelling preparation prevents late
opening; replacing the captured field rejects further live commands.

## Local bookmarks and history

The title/address toolbar is part of the host-owned browser element. It provides
Save/Remove bookmark, Bookmarks, and History. The lists use native themed dialogs
with controller focus, A to open, and B to close. Closing a list restores toolbar
focus; opening a saved page returns to page interaction. Clearing history requires
confirmation. Bookmarks are bounded to 100 and recent unique pages to 200.

URL/title metadata persists under the active settings profile's
`WebBrowser/Library/<widget-id-hash>.json`. Widgets cannot read this metadata via
IPC. Restricted provider-attribution documents are excluded. Named accounts and
persistent profiles use separate library files; the original private/default
profile keeps its existing library filename. Website cookies follow the declared
Storage policy and never cross widget IPC. Rapid URL updates are coalesced and
writes are serialized and replaced atomically. Failed or non-HTTP(S) pages are not
recorded. `WebBrowserDocument.StartPage` (`about:blank`, protocol 73) loads no site
and is neither recorded nor bookmarkable. Other `about:` URLs remain unsupported.


## Address and search entry

Y or the address toolbar opens the shared text-entry dialog. A new page starts
with an empty value rather than `about:blank`. HTTP(S) URLs navigate directly;
bare domains, IP addresses and localhost use HTTPS by default. Plain text opens
an escaped Google search. File, script and OS-protocol URLs are not navigation
sources. An empty submission leaves the current page unchanged.

LB is labelled Previous and RB Forward; they navigate browser history without
changing B's ordinary widget/tray behavior.

## Webapp configuration (protocol 81)

```csharp
var page = new WebBrowserDocument("webapp.session", 1,
    "https://discord.com/app", "Discord")
{
    Options = WebBrowserOptions.WebApp with { ProfileKey = "personal" }
};
UI.WebBrowser(page, BrowserInteractionMode.InteractOnFocus, "webapp.page");
```

The CLI uses the same API:

```text
wrail new widget Discord --template webapp --url https://discord.com/app
```

`WebBrowserOptions.WebApp` is a preset, not a separate element or rendering engine.

| Option | Ordinary default | Webapp preset |
| --- | --- | --- |
| Chrome | Toolbar | OnDemand (LS reveals controls without resizing the page) |
| Storage | Private | Persistent |
| ProfileKey | default | default |
| Background | Automatic | KeepAlive |
| ExternalLinks | InWidget | OtherOriginsInWindows |
| Popups | ReplacePage | Hosted |

Profile keys use 1–64 lowercase letters, digits, dots, dashes or underscores.
They are account labels, never paths. Profiles are namespaced by widget identity
and storage type. No widget receives cookies, tokens, native browser objects or
website permission authority. Provider documents keep their restricted defaults.

KeepAlive requests bounded host/worker retention even when the widget is hidden
or parked. It does not grant background input or unrestricted widget execution.
Stop session unloads the page, ending connections and calls while keeping saved
login data. The tray reports running/disconnected sessions and offers Stop.
Native fake-device media persistence is verified; real Discord calls and full-shell
pin/recovery behavior remain acceptance checks.

Hidden browser pages request WebView2's `MemoryUsageTargetLevel.Low` after 15
seconds continuously hidden. This shared policy also covers hosted pop-outs and
provider documents; a visible pin remains at Normal. Normal is restored before
showing the page. Quick switches cancel the delayed reduction, and repeated
hidden layout updates do not restart its deadline. The delay is defined in
`BrowserSurface.HiddenMemoryDelay`.

Low is a best-effort working-set reduction, not suspension or a memory limit.
Scripts, connections, notifications, calls and screen sharing remain allowed;
there is no website-specific call detection. Memory may be paged back in as
needed, so lower resident memory does not imply lower committed memory or CPU.
The host does not call `TrySuspendAsync`. Unsupported/failed optimization leaves
the page running and logs one diagnostic per surface. Widget authors need no
additional option or lifecycle callback.

### Popups and authentication

`ExternalLinks` controls ordinary user-initiated top-level link navigation. It is
not an origin allowlist; redirects can cross origins for authentication.
`Popups` independently controls new-window requests:

- ReplacePage navigates the current page; it cannot preserve an opener relationship.
- Hosted creates a bounded child in the same widget/account profile, preserving
  `window.opener`, `postMessage` and `window.close`. LS reveals its on-demand
  toolbar, which identifies the origin and provides Close. B returns from child toolbar to child page, then
  closes the child and returns to the existing parent.
- SystemBrowser opens HTTP(S) popup URLs through the existing Windows handoff.
  Windows does not share the hosted profile or opener; auth flows requiring them
  should use Hosted.

Each root can host one popup; the host permits at most two globally. Nested and
non-user-initiated popups are rejected. Hosted about:blank startup is supported
for authentication flows that navigate the new window afterwards. Non-web schemes
remain unsupported. Closing, stopping, clearing a profile or retiring the root
drains child initialization and native resources. When revealed, narrow toolbars keep address
and Close visible with a controller-scrollable command row.

For widgets that support full-widget pinning, a new hosted popup first asks
**Open pinned / Open in widget / Cancel**. The host defers the native request
until the choice is made and places the existing child directly at its destination.
It does not flash the child in the parent before pinning. Both placements use
on-demand controls. The optional Remember checkbox stores the choice per widget,
account profile and requesting top-level website origin (including popups from
its embedded frames). This makes the choice manageable from that website's own
controls. It applies to hosted popup windows
generally, including authentication windows; the host does not infer their purpose.
Unknown requesting origins do not offer Remember. **Website controls → Pop-out
placement** offers Ask each time / Open in widget / Open pinned. Cancel never saves
a choice. Preferences survive restarts; a remembered pin preference still asks
before replacing an existing pin, with a direct **Replace pin** action.

An already open hosted child also provides **Pin pop-out** in its LS toolbar and
as a tray pin choice. Replacing a pin uses the same
single-pin placement/opacity controls. The existing child moves into that pin;
its URL is not opened a second time and its opener/profile stay intact. The
parent remains usable. B leaves pinned interaction; it does not close the child.
**Return pop-out to widget** removes the pin and restores the live child to its
parent. **Close pop-out**, parent-session Stop, profile changes and widget
retirement end the child. An ordinary parent navigation retains the normal
browser opener/window semantics until its session is replaced or stopped.

The host initializes root and child controllers in its durable main XamlRoot
before borrowing a visible slot. This keeps native controller lifetime independent
of disposable pin windows. Pin/expanded transfers share the same detach/load
routine, including all attached child controllers. No extra author API, URL
cloning, site selectors or media extraction are involved. This supports actual
hosted new-window pop-outs, not arbitrary in-page media or Picture-in-Picture.

### Screen sharing and notifications

A visible interactive webapp can request screen sharing through the standard
website API. WidgetRail asks for a one-shot confirmation identifying the widget
and exact requesting HTTPS origin before allowing WebView2/Chromium's native
sharing chooser. No persistent screen-share permission is stored. Hidden pages
cannot summon the chooser. Closing the page cancels pending consent.

Small or pinned viewports can clip Chromium's chooser, so the host temporarily
expands the existing browser through its shared presentation layer. Choosing a
source or cancelling automatically restores the original widget/pin and interaction.
A browser already expanded before the request stays expanded.

The chooser uses the browser's familiar pointer controls: left stick moves the
controller circle, A clicks (hold to drag), right stick scrolls, and B cancels.
There are no chooser-specific D-pad/bumper keyboard mappings. Physical mouse and
keyboard remain available. Widget controller actions are suspended while the
native dialog owns input. Its pointer transport shares browser motion/deadzone
policy and is independent of the particular dialog's lifecycle adapter.

The host searches the requesting WebView2 process's windows for the actual sharing
chooser using native accessibility structure. An active share creates a separate
indicator window, so the number of browser windows is not treated as dialog identity.
Webpage accessibility subtrees and window titles are not inspected. Unknown or failed
detection keeps the modal input/placement boundary active; it does not mean closed.
After admission, observations and cancellation bind to the native HWND and the
chooser's accessibility runtime identity. Process-scoped native window events
request rechecks; a bounded accessibility fallback covers Chromium's reused HWNDs
and absent events. Two successful absent observations while the host is visible
confirm dismissal. Hidden-host accessibility absence is not closure evidence.
Diagnostic state transitions include identity, foreground, pointer visibility,
frame-delivery counts and cancellation reasons without website content.

The circle uses the browser's diameter and theme colors in a tiny nonactivating,
click-through native visual above the chooser. Controller mouse messages target only
the admitted native root/children at the circle's virtual position; they do not move
the physical mouse or replace the Windows cursor. Desktop mouse movement or mouse
button changes hide the circle; controller activity shows it again. Keyboard and
gamepad-related Windows input do not cancel a held controller click. The physical
mouse keeps its own location.
The pointer transport, motion policy and visual are reusable independently of the
sharing-specific accessibility detector. An outstanding controller press is cancelled
on disconnect, lost focus or teardown without leaving a global mouse button held.
The native dialog retains mouse hit testing even while webpage controller input
is suspended. A bounded completion handoff lets Chromium finish activating the
selected source before WidgetRail restores placement/focus. Leaving a still-open
chooser for another application pauses its controller input while retaining the
dialog session; foreground loss does not mean the dialog closed.

An overlay hide request first cancels the chooser while its native host remains
visible. Successful native Close/Escape requests are not treated as proof of
closure. The expanded placement and modal input boundary remain until absence is
confirmed. Reopening cancels a pending overlay hide and reconciles the outstanding
chooser before enabling webpage input. Native host visibility transitions pause
observation and invalidate stale results. Browser foreground eligibility is
explicitly granted while WidgetRail owns foreground; no unrelated app is forcibly
activated to obtain it.

Selected-window video delivery and track stopping have been verified with an
opaque synthetic window using simulated analog/button controller frames through
the real native chooser. The repeat fixture keeps earlier streams alive across
share → cancel change → share, and verifies dragging, scrolling, circle presentation,
mouse/controller handoff, unchanged physical cursor position and automatic return.
It also interleaves Windows keyboard/gamepad-key events with held A, clicks native
Share/Cancel using A, and toggles audio on/off on a cancelled request (no audio
stream is started). B cancellation is available in the driver through `-CancelWithB`.
`-ForegroundReturn` exercises temporary focus loss; `-HideReopen -Cancel` holds
native cancellation long enough to verify that reopening cannot prematurely
collapse the webapp or return input to it. Controller-log failures are reported
in the durable frontend error log, with a one-time startup writer-health record.
These checks are distinct from physical-controller acceptance.

Notifications use WidgetRail's own presentation after website permission is
granted. The host handles WebView2 notification events; Windows notification UI is
not used. Per-widget tray preferences select Banner + list, List only, or Off.
Ordinary widgets use the same coordinator and presentation through the
[widget notification service](widget-notifications.md), with explicit retention,
interaction, artwork and overlay-visibility options. Web notifications retain the
default policy below.
Quiet passive banners last four seconds without taking focus or making a sound.
While the overlay is visible, the bell beside the clock updates its pending-count badge instead. The badge counts all pending entries, including ones already shown. A burst can
update the current banner without extending its deadline; an eight-second quiet
interval follows. Website replacement tags are scoped to widget/profile/origin.

The bell opens a compact panel above it. The tray-menu Notifications command remains
available. Cards show the widget icon, source/domain, receipt time, title and body.
The panel offers A to open the exact originating page (including its existing pin),
X to dismiss a focused entry, B to close and restore focus, and secondary Clear all.
New arrivals do not move the selected entry.
Expiry hides only the banner. Pending entries remain until opened, dismissed or
withdrawn by the website. A stale page is labeled Session ended. Opening it enters the owning widget normally
and removes the entry after a successful open, without invoking stale page callbacks.
If the widget is unavailable, the entry is removed and the panel shows a brief inline
message; no warning dialog or additional confirmation is required. The bounded inbox keeps up to 256 entries and
reports overflow rather than silently expiring old items. Preferences survive
restarts; actionable pending notifications are session-only.

Native callbacks, list actions, banners and main/pin activation are covered by
synthetic fixtures. Actual Discord emission and conversation navigation remain
physical acceptance checks. Only document notifications emitted while the webapp
is resident are supported; website-specific in-page toasts, reply/call actions and
push while WidgetRail is closed are not provided by this API.

### Website messages and page actions

Alerts, confirmations and leave-page questions identify the requesting website
and use the host chooser. Website text prompts use the controller keyboard, with
the full bounded message in its scrollable body and an explicit Submit action.
B cancels. Navigating away, hiding or retiring the page cancels outstanding
dialogs. A confirmed Stop/clear-data action accepts beforeunload so a website
cannot veto the user's decision to end its session.

Menu sends a native right-click at the controller pointer. Website-owned context
menus remain website UI. Standard Chromium menus use the themed host chooser,
including submenus, without exposing native command IDs to widget code.

### Files

Ordinary HTML file inputs use Windows Open dialogs after a confirmation showing
the widget and requesting frame's origin. Multiple selection is supported up to
32 files. Cancelling preserves an existing selection; replacing the input or
navigating while the picker is open invalidates the selection. Windows pickers
currently require mouse/keyboard. They block overlay shortcuts while open.
Directory uploads and File System Access API workflows are not advertised.

Downloads require a confirmation and a Windows Save destination for each file.
Windows overwrite confirmation and WebView2's file security checks remain in
place. Files are never opened automatically. Up to two downloads per surface can
run; Website controls lists running downloads with cancellation. Stopping the
session, clearing its profile or retiring it cancels remaining downloads. A
website requesting repeated downloads receives a separate permission prompt;
allowing it does not bypass per-file save confirmation.

File paths stay between the native picker and its originating browser controller.
They are not widget IPC results or SDK file grants. A completed user selection
does not depend on the timing of native-picker foreground restoration, but still
requires the same live, visible document and uncancelled request. Picker
cancellation closes only the application's owned common dialog and waits for its
native completion before releasing the modal input scope.
