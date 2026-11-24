# Semantic controller guide in the WinUI shell

The guide is a read-only view of the active input surface. SDK authors continue to
use shortcut labels, primary actions and context actions. No separate guide declaration
or widget-specific host mapping is required.

`WidgetViewPresenter.CaptureControllerGuide` reads the active scope and exact focused
ordinary element or admitted indexed row. Indexed rows append their item declaration
to the collection's logical owner path, as input admission does. Missing/loading/retired
rows advertise no item actions. The existing shared `ControllerShortcutResolver`
determines nearest binding and disabled/busy ownership. Unlabeled or unavailable
nearest owners never expose a different ancestor binding.

Context action discovery is shared with actual dispatch. A focused surface wins;
otherwise exactly one eligible scoped container may own a context button. A declared
menu whose actions are all unavailable consumes its button without advertising an
option or falling through to a shortcut. Popups suppress parent hints; their primary
selection action and host Back/Close remain. Modal input scopes exclude parent actions.

The shell continues to own Back/Close. Native guide buttons send normalized buttons
through `RouteButtonAsync`; they do not capture action ids, retain item leases, create
an input queue, or take focus from the widget. Existing root gamepad-key suppression
also covers the guide.

The view preallocates a bounded 16 native cells. WinUI measures the full styled labels
and glyphs. Optional hints fit as whole units; LT/RT and LB/RB pairs fit together. Host
Back/Close are reserved. Invisible cells remain measured to preserve height across
input context changes, and are removed from hit testing and the accessibility content
view. Theme/text scaling and Xbox/PlayStation glyphs use the shared chrome roles.

## Shell-owned contexts

Recovery replaces widget hints only while the widget domain is interactive. It
advertises A Retry when Retry is available, otherwise Back/Close only. While the
tray owns input, a background recovery panel does not replace normal tray hints.
An open tray command menu advertises Select/Back/Close and suppresses normal
Open/Reorder/Commands hints. Both explicit dismissal and native flyout closure
restore the appropriate guide. Guide buttons retain the existing normalized
input dispatcher; no second action path was introduced.

## Validation

On overlay reopen, a virtualized row can still show the right content after the
worker has retired its authority to handle commands. The WinUI host checks the
lease in `WidgetIndexedRows` when reusing a demanded page. It renews expired
pages in place and waits for the focused row to accept input before publishing
its replacement guide. The cache keeps the original slots during renewal.

A bounded history window can also lose its query while a newer snapshot is
travelling from the bridge to WinUI. If a speculative range read is rejected,
the host falls back to ordinary row loading and its row-level failure handling.
A failed commit still reports an error.

| Consumers | Applicability and verification |
| --- | --- |
| YouTube Music | Reproduced the guide dropping to Back and Close on a focused song. The native reopen scenario samples the guide across transitions and invokes the restored Options action. |
| Discord | The native Messages fixture checks published keys and visible positions during history pagination. The configured 20-message adjacent page can be prepared before a scroll while only the first 20 messages are displayed. |
| Playnite, Spotify, YouTube Video, FullApplicationWidget, PoB Companion, Capture Library, Games & Apps, Settings | Use the shared indexed renderer. Source reviewed. No widget changes required. |
| Non-indexed views and widget authoring templates | No page lease handling to migrate. |

The Windows App SDK analyzer build passed with no warnings.
Validated on the combined production-shell baseline: 66 managed shell tests,
87 native production checks (including five recovery-guide assertions), 29
native shell-chrome checks, 56 context-menu checks, and five production tray
checks. Production tray testing invokes the native guide Back button through
the shared dispatcher and verifies the ordinary tray guide returns. Native
screenshots and JSON results are under `artifacts/guide-context` in the guide
worktree. An initial driver attempt to focus a MenuFlyoutItem via UIA failed
focus confirmation; the final driver preserves the existing native menu Invoke
route and passed. No product fix was needed for that driver limitation.
