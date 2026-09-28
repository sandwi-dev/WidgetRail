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
sequence. The geometry, native transfer and shell/controller wiring are still
implementation work at this checkpoint.

Validation: 189 session tests pass, including 13 fullscreen admission cases for
compatible updates, genuine frames, unavailable bindings, shortcut resolution,
scope/capability removal and reappearance, document retirement and disposal. This
does not establish native transfer, playback or physical fullscreen acceptance.
