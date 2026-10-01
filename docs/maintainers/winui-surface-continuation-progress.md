# WinUI surface continuation — 2026-09-29

## Scope and safety

Continuation of the WinUI-first migration on `codex/winui3-frontend`. This pass
audits background-surface ownership, decoded artwork presentation, and compositor
lifetime. No real widget/provider actions, setting changes, candidate shutdown,
or candidate launch were performed by this workstream. Native test scheduling and
physical candidate ownership remain with the coordinating task.

## Implemented: background blends survive native layout resize

`WidgetArtworkCrossfade` used `SizeChanged` to call `ShowLatest()`. That canceled
the current native blend and immediately painted the logical latest image. If a
newer focus image was waiting behind the active blend, resize skipped directly to
that pending image. A content-sized widget changing extent therefore introduced
an abrupt background change even though both decoded image layers remained valid.

Removed that resize callback. XAML already rearranges the two native `Border` /
`ImageBrush` layers. The artwork recipe animates opacity only: it has identity
translation/scale and no size-dependent clip, so native resize does not invalidate
its compositor batch. Unload, accessibility/motion preference changes, explicit
clearing, and disposal still settle/cancel normally. The two painted images plus
one pending decoded reference remain the maximum retained set. No new renderer,
frame callback, layout timer, image cache, SDK declaration, or widget change.

Extended `PresentationSurfaceValidationPage.Motion.cs` to hold an active blend
and a distinct pending image across both width and height changes. It verifies
the native layer extent, unchanged active batch and painted sources, normal
completion rather than cancellation, ordered pending replacement, and release of
old image references. The old fixture asserted immediate snap on resize; that
assertion has been replaced with the intended native-layout behavior.

## Audit findings with no changes

- Background ownership already follows the nearest declared surface and its
  logical scope. Reparenting across a presentation boundary, replacing runtime
  authority, and removing the surface revoke its source memory.
- Retaining decoded background paint is separate from retaining request/input
  authority. A missing or pending replacement does not clear a retaining surface.
  Existing retention fixtures cover section replacement, nested scope isolation,
  unavailable artwork, and opt-out behavior.
- Poster entrance fades already use a dedicated composition layer so they do not
  animate authored image opacity or foreground controls. Ordinary same-source
  updates do not replay entrance. Presentation suspension, binding retirement,
  unload, and appearance changes cancel poster motion.
- Artwork decoding already uses native asynchronous target-sized decode,
  bounded source metadata, demand cancellation, logical authority admission,
  and optional-image failure isolation. No competing custom painting mechanism
  was introduced.

## Validation and acceptance

Code review complete. Coordinating task must build and run the native
`--validate-surfaces` fixture before claiming validation. No new physical
smoothness acceptance has occurred, and this bounded correction does not establish
that every list/grid/widget is free of stutter.
