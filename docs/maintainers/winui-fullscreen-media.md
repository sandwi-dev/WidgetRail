# WinUI host-owned fullscreen media

The existing SDK action `host.embeddedMediaSession.enterFullscreen` remains the
entry declaration. The widget declares `OverlayFullscreen` capability; it does
not hold fullscreen state. A trusted host presentation moves the existing admitted
browser between its ordinary viewport and an aspect-fit fullscreen viewport.

## Session admission

`EnterEmbeddedMediaFullscreen` accepts an exact session-published displayed frame,
its captured action and the resident admitted document. It applies the same
origin/current binding checks as ordinary actions, including scope, availability
and action identity. The host action never becomes a worker action. Normalized
controller shortcuts resolve through `ResolveEmbeddedMediaFullscreenInput`, which
uses the same shortcut resolver and input-origin validation as ordinary input.

The returned `WidgetMediaPresentation` is an opaque receipt. Its capability/scope
epoch is captured at frame publication. Compatible metadata, aspect-ratio and
playback updates retain it. Removing the capability, changing scope, removing or
replacing the document, runtime replacement and session retirement invalidate it
permanently. Removing then reintroducing a capability cannot resurrect an old
receipt or admit input captured before removal, even when frontend dispatch skips
the intervening publication. Document lifetime remains independent: leaving a
capable route may close fullscreen without restarting the browser or audio.

These checks do not prove native ownership. Before entry the frontend must prove
the exact document is resident in its visible ordinary viewport, that interaction
is admitted and no pinned presentation owns it. Hide, widget switch, deactivation
and pinned takeover retire the frontend request. B returns to the ordinary widget;
host playback controls use the existing adapter with no invented widget command
sequence.

## Native ownership and shell

The shell now provides one `MediaFullscreenView` with a native aspect-fit viewport,
responsive native buttons and semantic controller glyphs. The existing media owner
moves the same browser into that destination and returns it on exit; it neither
resolves the package again nor constructs another controller. The main widget stays
mounted but disabled behind the fullscreen surface. Existing display placement and
zoom policies supply the available work area; ordinary shell chrome returns on exit.

B returns to the widget, View returns to the tray, X toggles playback and triggers
seek using the declaration's bounded step. A activates the focused native guide
button. Releases from host-owned presses remain consumed after exit, so they cannot
activate a newly revealed widget shortcut. Pointer/keyboard controls call the same
host operations. Pausing and seeking use command-sequence zero; resuming uses the
existing trusted arm/activate handshake. The package's monotonic command lineage
is not advanced by host controls, and busy commands are consumed without a queue.

Validation: 189 session tests pass, including 13 fullscreen admission cases for
compatible updates, genuine frames, unavailable bindings, shortcut resolution,
scope/capability removal and reappearance, document retirement and disposal. This
does not establish physical fullscreen acceptance. The integrated native media
fixture passes 99 checks, including the actual fullscreen view, aspect fit, narrow
guide layout, controls inside the real window, same-browser transfer, trusted
pause/resume, capability loss, input deactivation and hide/reopen. Inspected
fullscreen/popup screenshots and results are in `artifacts/winui-media/fullscreen-visible/`.
The shell policy suite passes 24 tests and the analyzer build is clean. Real provider
fullscreen, production shell routing/placement and physical controller acceptance
remain to be verified together. Pinned ownership transfer remains separate work.
