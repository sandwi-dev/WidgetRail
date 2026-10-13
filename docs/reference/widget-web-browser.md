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
it releases the mouse button. D-pad also moves the pointer. Toolbar left/right navigation selects commands, and LS or Down returns to the page. LB/RB navigate history, X reloads/stops, Y edits the current address,
LT/RT zoom out/in (including held repeat), R3 edits the selected text field, and
Menu opens externally. The press used to enter Interact never clicks the page.
Opening a dialog or losing input cancels a held pointer outside the page.
HTML drag-and-drop uses Chromium's drag interception and delivery only for the
controller-owned gesture. Drag data is bounded, stays inside its browser, and
cannot contain files. Ordinary physical mouse handling is retained outside that
gesture. This does not implement dragging desktop files into or out of WidgetRail.

Zoom uses WebView2's native Chromium touchpad pinch gesture anchored at the
controller pointer, with requested steps bounded to 100â€“250%. The header shows
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
The address/text dialog consumes B to cancel and resumes interaction; preparation
is cancellable too, and late responses cannot reopen cancelled edits.

Address and page text use the existing themed controller text-entry dialog.
Page text supports ordinary editable inputs, textareas, passwords and rich
contenteditable controls, including fields in open shadow roots. Password entry
starts empty and replaces the password without importing an existing secret.
Rich editing inserts at the captured selection through native browser input,
preserving surrounding formatting and mention nodes. Apply updates the field;
Apply + Enter is an explicit, separate submission action. Cross-origin frame
editing still requires compatibility validation. A
native remote-object reference identifies the exact field. Commit checks that
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
revoke saved decisions or clear all website data for the selected profile.
Downloads, external URI schemes, basic authentication and bad certificates remain
denied at this implementation checkpoint. Popup routing is configured separately
from ordinary link routing, as described below. Password saving, autofill, host
objects, web messages, developer tools, script dialogs and default context menus
remain disabled pending the file/dialog/context workstream. The host
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
and associated labels), and reuses the existing validated read/commit path. Clicking
elsewhere does not reopen an older focused field. R3 remains available to reopen
editing. Mouse clicks and keyboard Tab navigation do not trigger this dialog.

Automatic editing includes supported ordinary fields, passwords and rich editors.
Read-only controls are excluded. Cross-origin frame editing remains unverified. New
navigation, a newer gesture, input loss or cancelling preparation prevents late
opening; replacing the captured field rejects its commit.

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

### Popups and authentication

`ExternalLinks` controls ordinary user-initiated top-level link navigation. It is
not an origin allowlist; redirects can cross origins for authentication.
`Popups` independently controls new-window requests:

- ReplacePage navigates the current page; it cannot preserve an opener relationship.
- Hosted creates a bounded child in the same widget/account profile, preserving
  `window.opener`, `postMessage` and `window.close`. Its toolbar always identifies
  the origin and provides Close. B returns from child toolbar to child page, then
  closes the child and returns to the existing parent.
- SystemBrowser opens HTTP(S) popup URLs through the existing Windows handoff.
  Windows does not share the hosted profile or opener; auth flows requiring them
  should use Hosted.

Each root can host one popup; the host permits at most two globally. Nested and
non-user-initiated popups are rejected. Hosted about:blank startup is supported
for authentication flows that navigate the new window afterwards. Non-web schemes
remain unsupported. Closing, stopping, clearing a profile or retiring the root
drains child initialization and native resources. Narrow toolbars keep address
and Close visible with a controller-scrollable command row.

### Screen sharing and notifications

A visible interactive webapp can request screen sharing through the standard
website API. WidgetRail asks for a one-shot confirmation identifying the widget
and exact requesting HTTPS origin before allowing the Windows capture picker.
No persistent screen-share permission is stored. Hidden pages cannot summon the
picker. Closing the page cancels pending consent. The Windows picker and actual
selected-window sharing remain compatibility/physical acceptance checks; the
native cancellation path is verified without capturing any display.

Notifications use WebView2's default native notification UI after website
permission is granted. A local fixture verifies that the website receives its
`show` event. Notification interaction and real Discord behavior still require
validation. This supports document notifications while the webapp is resident;
WebView2 does not support push notifications while WidgetRail is closed.
