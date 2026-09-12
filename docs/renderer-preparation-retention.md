# Retained renderer preparation (WIDGE-238)

Full snapshot transport no longer implies full native layout. After snapshot authority
and virtual-window admission succeed, the session compares the canonical documents
and computed styles and passes their changes through the existing presentation-impact
classifier. Unknown properties, structural edits and surface changes retain the
conservative full-render fallback. Recovery checkpoints do not borrow the prior view.

Pinned layout roots and their focus/input scope belong to the pinned surface owner.
Changes to those contents do not invalidate the ordinary widget. Layout IDs, names,
surface hints and other catalog metadata remain ordinary full-fallback changes. The
pinned coordinator still receives every admitted snapshot before ordinary rendering.
The wire protocol remains unchanged: pinned-content changes can still use a complete
checkpoint for transport; the host independently avoids unnecessary ordinary rendering.
The SDK reports `pinned_layout_content_changed` separately from catalog changes.

## Preparation ownership and invalidation

- Renderer-owned style entries compare exact base/focus/pressed styles, resolved parent
  and viewport dimensions, inherited background, root/parent fonts and accessibility
  policy. Custom contrast callbacks bypass reuse. Each node retains at most four
  contexts; inactive nodes are retired when the cache exceeds 4096 IDs.
- Renderer-owned DirectWrite plans are immutable and shared by measurement and paint.
  The key includes text, font family/weight/size, spacing, wrapping, trimming,
  transformation, alignment, line limits and both constraints. Changing the DirectWrite
  factory clears the cache. The LRU is bounded to 1024 plans and 131072 source/family
  characters. This does not change decoded-artwork or GPU-image budgets.
- Paint-only preparation rebinds current snapshot pointers and reuses styles without
  constructing layout elements. No pointer into an older semantic snapshot is cached.
- Text-only layout classification re-measures changed leaves at every distinct
  constraint used by the committed layout. It keeps geometry only if all intrinsic
  dimensions, line metrics and ink bounds match; otherwise existing safe-boundary
  layout propagation runs. Missing evidence falls back conservatively.
- Viewport, DPI/pixel scale, text scale, responsive viewport, root font and accessibility
  changes cannot consume a pending retained-layout plan. Theme styles are compared
  independently of semantic JSON. Paint-only snapshot admissions can retain the host's
  existing intrinsic surface measurement, subject to the same request and constraints.
- Consecutive uncorrelated pure visual events may combine only across an exact sequence
  chain in the same lifecycle/generation. All admissions and completion traces still
  happen. Correlated input/action results, restart, authority, layout and structural
  changes remain separate. Existing refresh-demand coalescing remains the request owner.

## Diagnostics

Slow-frame logging now splits style resolution, text measurement, layout computation,
snapshot comparison, update planning and drawing, with style/text cache hits and misses.
Layout time includes intrinsic text callbacks; the nested measurements must not be
summed as disjoint stages. Snapshot comparison and update planning happen before Render
and are reported separately from its total.

## Automated validation and performance

Comparison baseline: accepted main `108d0eae891bc08fccb6e5ed1b97936e82dbd3e4`.
Release builds on the same machine, using the same 60-track offscreen WIC/DirectWrite
workload (40 measured progress-update frames per mode). This measures native work,
not live Spotify FPS or controller latency.

| Workload | Baseline median preparation | Candidate median preparation |
| --- | ---: | ---: |
| Full render, warm resources | 8.897 ms | 0.565 ms |
| Classified paint-only update | 0.215 ms | 0.064 ms |

The candidate's full-render median complete workload frame was 1.239 ms and the
paint-only frame was 0.724 ms. Comparing a 60-track full snapshot with only pinned
playback content changed took 1.037 ms median (1.970 ms p95) and correctly required
no ordinary raster work. Assertions verify zero layout builds, style misses and
text-layout misses on stable paint-only playback frames, plus correct invalidation
when text scale changes.

Passed: renderer 6795 checks; native text layout 88 checks; style tests; native full
snapshot comparison; session/coalescing tests; SDK 115/115; Spotify 62/62; background
surface 12/12 (including 240 retarget continuity cases); controller navigation, focus,
slider, interaction, accessibility and composition suites. Renderer coverage includes
cursor offsets, paging, focus-follow, responsive layouts, themes and artwork retention.

Playnite follow-up: **82/82 passed** after repairing six baseline test assumptions.
No widget, SDK or host production behavior changed in this follow-up. Coverage now:

- Select option events originate from the published Select control and the fixture
  actually supplies the source names it selects. Secondary-route checks use their
  current headers; Hidden resolves saved identities without replacing the Home cursor.
- Browse reentry preserves navigator focus, including disabled focusable controls;
  committing a new search resets the collection and keeps focus on Search. Clear
  retains its own focus after becoming disabled, and restores the complete results.
- Cached display metadata cannot recreate games absent from current provider results
  or transfer hidden state to a different saved identity with the same title. Hidden
  assertions await the hidden resource, not an unrelated cursor.
- The paging race uses Home's published pagination action and the current page size.
  Separate cases cover an adjacent page merging after a newer favorite mutation and
  cancellation/replacement when a category change triggers refresh. Both preserve the
  newer organization authority; delayed writes have bounded waits and cleanup.

Build/test logs and unique managed binlogs are retained in the worktree's ignored
`logs` directory. Physical acceptance remains pending: Spotify playback while navigating
with D-pad, left stick and right stick, followed by Playnite Browse paging/scrolling.
No integration or closure before acceptance.
