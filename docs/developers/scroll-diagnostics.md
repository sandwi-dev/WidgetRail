# Scrolling performance captures

The native host supports an opt-in, bounded scrolling trace. It does not change
cursor admission, retention, scrolling, layout, or cache budgets.

Ordinary launches omit high-frequency presentation logs. `--scroll-diagnostics`
enables those logs for a controlled comparison; `--scroll-diagnostics-quiet`
records the same trace with ordinary quiet logging.

Launch the candidate host with `--show --scroll-diagnostics`. Reproduce a short
scrolling sequence, then close the overlay with its normal controller shortcut.
The host saves `%LOCALAPPDATA%\WidgetRail\diagnostics\scroll-<pid>.log` when the
overlay closes and again on process shutdown. Reopening continues the same trace;
copy a capture before repeating a different experiment.

The buffer retains the latest 8,192 records, with at most 1,024 bytes per record.
There is no trace-file IO in the scrolling path. The header reports discarded
records, recording failures, recording cost, and ordinary synchronous logging
cost. Startup/background events can consume buffer space; use short captures.
Opaque hashes correlate nodes, images, and decoded variants. The trace does not
contain titles, descriptions, artwork URLs, credentials, or provider responses.

```powershell
./scripts/Analyze-ScrollDiagnostics.ps1 -Path '<capture path>'
```

## Records and interpretation

- `frame-plan` identifies full/bounded redraw plans, page-update impacts, retained
  refresh pixels, menu repainting, surface replacement, and transport promotion.
- `frame-commit` records each successful ordinary composition frame, including
  draw, BeginDraw, resource setup, EndDraw and commit timings. This excludes
  background-only opacity commits. It is not the display's measured frame rate.
- `render` separates preparation, layout, text measurement, style resolution,
  presentation, node drawing, image lookup and bitmap-upload time. Timings are
  nested. `work=-1` means no matching incremental plan; other values correspond
  to `IncrementalPresentationWork`. Image hit/miss counts cover image lookups;
  `image-miss` details are limited to eight per render.
  `work=4` is scroll-only projection of cached content geometry, without layout.
- `bitmap-create`, `bitmap-eviction`, and `decoded-eviction` correlate resource
  variants, sizes, budgets, and protection status. Protection is the cache's
  current protection set, not proof that an image was visibly displayed.
- `image-request`, `decoded-ready`, and `decoded-failed` record actual requests
  and their decoded outcomes, including budget rejection and decoded dimensions.
- `item-geometry` and `text-geometry` sample up to 18 records each on snapshot
  changes or every 250 ms. They include drawing coordinates, clipping, text
  origins/line counts, and scale, without text contents. Coordinate changes
  during normal scrolling are expected; compare a stable item across a page
  update with its scroll-offset adjustment before attributing jitter.
- `scroll` records actual committed right-stick offset changes and whether a
  pending full repaint displaced the incremental plan.
- `refresh-state` records the caller's source line, snapshot sequence, and state
  transition. It counts host state changes, not API requests.
- `image-ready` and `invalidate` correlate artwork completions with retained/full
  repaint requests. Not every completion requires a repaint.

## Controlled comparisons

Keep the theme, window size, game ordering and input gesture the same:

1. Scroll back and forth inside loaded content without crossing a page boundary.
2. Cross one boundary, then reverse through it several times.
3. Repeat the same route after artwork has loaded.
4. Repeat at 100% and 125% overlay scale, capturing each separately.

To compare ordinary logging overhead, restart with
`--show --scroll-diagnostics-quiet`. This suppresses only named high-frequency
presentation/debug log categories; errors, lifecycle, and pagination logs remain.
The same bounded trace still runs and records how many messages were suppressed.
Compare normal and quiet runs before claiming that diagnostic logging explains
stutter. A normal launch without either switch creates no scroll trace.
