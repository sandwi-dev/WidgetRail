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

The host lazily creates an InPrivate profile isolated by widget, retains at most
four browser controllers, and retires them with the worker/document lifetime.
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
The address/text dialog consumes B to cancel and resumes interaction; preparation
is cancellable too, and late responses cannot reopen cancelled edits.

Address and page text use the existing themed controller text-entry dialog.
Page text supports ordinary editable input and textarea controls up to 2,048
characters, including fields in open shadow roots. Passwords, cross-origin frame
editors and rich contenteditable controls use the external browser for now. A
native remote-object reference identifies the exact field. Commit checks that
the document and focus are still current and the field remains connected and
editable. Navigation, loss of input and worker retirement revoke the edit; a
replacement field with the same HTML ID never inherits it. Fixed host scripts
and generated JSON are used internally; widget code receives no page values,
remote objects or scripting capability.

The toolbar provides Back, Forward, Reload/Stop and Open externally. External
opening uses the existing release-aware handoff, including returning from pinned
interaction to the main foreground owner first.

Site permissions, downloads, external URI schemes, basic authentication and bad
certificates are denied. User-initiated HTTP(S) popups stay in the same browser;
other popups are suppressed. Password saving, autofill, host objects, web messages,
developer tools, script dialogs and default context menus are disabled. The host
sends only fixed browser commands; page contents and scripting APIs never cross
widget IPC. Use Open externally for unsupported site workflows. A browser process
failure has a visible recovery message; Reload widget creates a new lifetime.

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

Automatic editing retains the existing supported fields: ordinary text/search/URL/
email/telephone inputs and text areas, up to 2,048 characters. Password fields,
read-only controls, rich editors and cross-origin frames do not trigger it. New
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
IPC. Restricted provider-attribution documents are excluded. Page cookies remain
in the existing in-private WebView profile. Rapid URL updates are coalesced and
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
