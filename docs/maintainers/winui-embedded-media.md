# WinUI embedded media

The sample and provider adapters keep the existing SDK contract: a sealed entry
document and resource inventory, allowed provider origins, typed commands and
correlated playback observations. No arbitrary URL, script or browser controller
is exposed to widget authors. WinUI owns placement and native controls; the
existing bridge/session/adapter logic owns admission and command lineage.

## Presentations and lifetime

One `EmbeddedMediaOwner` retains at most four resident sessions, including parked
ones. A session has one WebView2 and one document. Ordinary viewport, fullscreen
and compact pin borrow that browser; they do not create duplicate players.
Hide/switch parks ordinary media, while a pin remains visible. Scope/capability or
document retirement revokes its presentation receipt. Explicit restart creates a
fresh document; ordinary playback publications preserve it.

`EmbeddedMediaSurface.MoveTo` owns native handoff. Cross-XamlRoot transfer awaits
WebView2's real Unloaded event before attachment, permitting the native control to
disconnect its old composition target. Pending placement is coalesced to the latest
destination. Unpin waits for return/parking before destroying the peer. Retirement
revokes input immediately, unwinds browser callbacks, drains initialization and
placement, then closes the controller. No document reload or playback replay is
used to recover transfer pixels.

Passive pins use `AppWindow` plus `DesktopWindowXamlSource`. They avoid the implicit
WinUI Window first-activation visibility dependency observed with WebView2. The
shared pin coordinator retains placement, opacity, DPI/display reconciliation,
focus memory and explicit interaction. Native nonclient calculation gives the
whole window to XAML even when AppWindow retains WS_DLGFRAME. The existing gamepad
key boundary also covers the pinned XAML root.

## Input and observations

Trusted Play still uses the adapter's arm/activate handshake. CDP pointer input is
host-only and bounded to the intersection of the reported action and current CSS
viewport. WinUI DIP dimensions cannot validate CSS coordinates under scaling.
Disappearing geometry cancels the gesture, without destroying the document.

Ordinary Back and fullscreen buttons use exact displayed-action validation.
Compact input uses X for playback, LB/RB for declared previous/next and LT/RT for
bounded seek. B/View return to passive interaction. Native controls expose the
same routes, with optional navigation disabled when not declared. The main view
reports that media is pinned. Fullscreen from that view returns the browser from
its pin before entering fullscreen.

Command terminals retain exact sequence/media/preference validation. Unsolicited
observations can cross a compatible snapshot publication: the bridge requires a
retained origin inside the uninterrupted document-resource epoch. Document
removal/reappearance, foreign ownership and future origins remain rejected.

Saved compact placement is reapplied after the user opens the media widget and
its document is admitted; startup does not start its player or replay playback.
No SDK or widget-author migration is necessary for these host changes.

## Evidence and limits

Evidence: `artifacts/winui-shell/embedded-completion-20260929/`. Session269,
shell127, bridge2 and native media116 checks pass. The cross-root gate passes69,
including one core/document/audio instance, continuing playback, native peer close
and visible HTML pixels. Shared pinned-window regression passes33, including real
pointer pass-through and focus restoration. The actual installed local sample
workflow covers playback, slider settlement, preferences, both tracks, fullscreen,
pin controls/placement/opacity, unpin and repeated widget switching; the driver
requires video pixels in passive, interactive and returned-passive states.

These local checks do not establish real YouTube-provider acceptance or every
GPU/display configuration. YouTube Video remains disabled pending its provider
workflow qualification. The original reported EmbeddedBrowserWebView.dll crash
was not conclusively attributed from its module name alone.

References consulted: Microsoft UI XAML `DesktopWindowImpl.cpp` initial activation
visibility and WebView2.cpp `OnUnloaded`, `TryCompleteInitialization` and
`DisconnectFromRootVisualTarget`; installed Windows App SDK XML API documentation
for AppWindow and DesktopWindowXamlSource. Retrieved source snapshots are retained
in the evidence directory.
