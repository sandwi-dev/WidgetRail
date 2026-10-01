# Retained renderer preparation

> Historical native-renderer profiling evidence. Playnite Home/Library now publish
> indexed WinUI collections. `WRAIL_PLAYNITE_LAYOUT_OUTPUT` emits `.indexed.json`
> parent/range fixtures for those pages; they are not compatible with the old
> eager `ScrollWorkloadProbe`. Use `Test-WinUiPlaynitePresentation.ps1` for acquired
> native row geometry and the shared UX gate for session-backed page behavior.
> The independent `Native-Details-Geometry.renderer.json` fixture remains valid.

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

## Cursor-page layout admission

Structural snapshot changes still pass through full semantic preparation and the
existing collection admission, anchor, focus and presentation authorities. They
can now reuse Taffy layout nodes underneath that full path. The renderer owns two
bounded layout sessions: one for the initial parent estimate and one for the
parent-relative correction pass. Sharing one session between those passes would
repeatedly invalidate their differing constraints. Local surface measurement and
modal layout keep the stateless path.

Stable native IDs establish node identity, not measurement validity. Each session
reconciles current styles and child relationships, removes absent nodes, and updates
callback indices to the current input buffer. An exact intrinsic-input proof covers
leaf kind, text, resolved style, controller prompt, button content/state lanes and
pixel scale. Changed inputs mark measurement dirty; Taffy separately validates
available constraints and propagates dirty descendants. Unknown leaf kinds and
unversioned generic layout callbacks always remeasure. The layout engine also
includes its fallback measurement width and responsive mode in that validity check.
New intrinsic leaf behavior must extend the proof alongside `MeasureLeaf`, or opt
out of reuse. Color/style changes currently invalidate conservatively.

The Rust session owns only layout state and numeric current-buffer indices. It
retains no C++ callback, snapshot, action or buffer pointer. The ABI validates the
complete tree before reconciling it, including disconnected cycles; changed child
relationships are detached before reparenting. Failed layout or render passes
discard their reusable state. Package/instance changes, widget retirement and
renderer resource retirement also release sessions. Storage follows the currently
admitted bounded tree, and each text-measurement proof keeps at most 32 constraint
queries before falling back to fresh measurement.

This is measurement/layout reuse, not provider prefetch or fixed-height item
substitution. Content-sized music rows remain content-sized. Responsive grid tracks
are resolved through the existing full algorithm, including partial rows and logical
start columns. Placement, clipping, scroll extent, navigation and accessibility are
published from the current result. No SDK or wire contract changes are required.
Full style/tree preparation and grid track resolution can still cost time on page
arrival; this does not promise frame-budget admission or eliminate provider waits.

`ScrollWorkloadProbe --admission` compares production widget fixtures against a
stateless reference, including append, prepend, eviction, reorder/reset, missing
anchors, changed same-key content, display/text scale, inherited surfaces, modal
entry/exit and failed-frame recovery. It checks geometry, input/accessibility order
and raster pixels. `disableRetainedLayoutForTesting` disables only the new layout
sessions so the reference cannot accidentally share their measurement reuse.

## Retained composition pixels

Composition paint bands have stable IDs derived from their parent, first paint
operation and node identity. `CompositionPaintIdentity.inl` captures exact,
bounded dependencies alongside native painting: resolved immutable styles,
operation order, ancestor geometry/clips, text and visual state, controller family,
optimistic slider values, cursor reset identity, widget/resource authority and
pixel scale. It does not retain old widget trees or rely on a collision-prone hash.
New visual properties must be added to this dependency contract with the painter.

Unchanged bands reuse immutable bitmap pixels and their pool leases. Input,
accessibility, focus and scroll geometry still come from the current snapshot.
Only successful render passes publish the next paint cache. Inactive bands retire
on the next frame; widget retirement and resource-domain replacement clear the
cache. The existing 256-node/64-MiB scene and pool bounds remain; each exact value
signature is limited to 64 KiB and retained identity metadata to 4 MiB.

Ready artwork uses weak identity references to immutable decoded images. This
avoids retaining another copy of decoded image storage. A changed/evicted source
invalidates its band. Pending and failed artwork, package-icon demand, loading
indicators and background-surface transitions keep their original rendering paths;
they cannot freeze demand or animation behind a cached placeholder.

The presenter reuses GPU raster contents when the immutable pixel lease matches,
and skips unchanged solid fills. It retains focus-atlas contents when both source
leases match, updating only the compositor effects/timeline. New pixels always
receive a new lease; leases must never be reused for mutated bitmap contents.
Raster placement and clipping remain independent of upload identity.

Capture-local pixel identity is separate from compositor placement. Moving an
unchanged text row or poster can retain its bitmap and GPU surface. Effective
clips are compared within the capture. Simple controls inside scroll containers
capture their full item pixels, with the live outer viewport clip applied by the
compositor; moving an item across that edge does not recapture its artwork.
Internal rounded tile masks remain in the capture. Complex subtrees (nested scroll,
transitions, presentation surfaces, live media, visible-overflow descendants,
subpixel-translated clips, or an outer rounded tile mask)
retain the conservative clipped-capture path.
Text, surfaces, artwork and tile masks rasterize in capture-local coordinates so
fractional DPI and baseline snapping cannot depend on the previous screen position.
Float cancellation noise within 0.0001 physical pixels of the layout grid is
normalized; other subpixel geometry is preserved.

Painter paths without translation-invariance coverage (including glyphs, sliders,
state cues and focus decorations) retain placement in their cache identity.
Stationary scroll chrome is split from content: a scrollbar captures only its
track, and hidden scrollbars do not allocate an empty viewport-sized raster.
Loading indicators retain their existing admission and painting behavior.
This is bounded per-band reuse, not a texture of the entire collection.
Existing cursor loading, anchor restoration, artwork protection and logical input
coordinates remain authoritative.

Capture traversal follows a per-band contribution index in authored order instead
of rescanning every sibling. Fully clipped visual subtrees are culled when their
overflow rules prove they cannot paint outside their bounds. Logical offscreen
navigation data remains available; revealability uses prepared parent links rather
than a fresh tree search per control. Physical-pixel projection subtracts snapped
scroll displacement before converting to DIPs, avoiding precision loss at deep
offsets. Capture-local zero is canonicalized so signed zero cannot invalidate pixels.
The full-item capture uses its pixel-aligned raster envelope rather than an extra
fractional shadow clip. Shadow padding must not become a changing paint dependency
as an otherwise unchanged item moves through the viewport.

For a retained immutable view, regular rows/grids also keep an index of eligible
item roots. Style/presentation work covers the viewport plus one item extent on
each scrolling edge. Beyond that horizon, the host reuses the root's immutable
style and retains logical layout/navigation while deferring its decorative
descendants. Buttons and clipped action surfaces qualify; nested interactive
children, nested scrolling, transformed ancestry, modal/presentation layers and
unusual decoration keep the full preparation path. Focused/pressed items are
always prepared. New snapshots, themes, scale changes and inspection rebuild or
validate the index through the existing layout authority. This is independent of
the item bitmap cache, and does not remove offscreen focus or accessibility data.

Continuous controller movement is coalesced by `ContinuousScrollFrames` and
consumed once immediately before `WM_PAINT`. It retains at most one bounded
kinetic interval, replaces unpainted movement on reversal, and discards stale-view
input. Reaching a loaded boundary blocks more movement until the admitted view
changes, direction reverses or a fresh gesture starts. Inputs received while
blocked do not accumulate distance. Held-D-pad focus settles after the boundary
frame commits, as well as after release. The existing scroll offset, pagination,
refresh and input-scope authorities still decide whether movement may apply.

## Diagnostics

Slow-frame logging now splits style resolution, text measurement, layout computation,
snapshot comparison, update planning and drawing, with style/text cache hits and misses.
Layout time includes intrinsic text callbacks; the nested measurements must not be
summed as disjoint stages. Snapshot comparison and update planning happen before Render
and are reported separately from its total.

Existing slow-frame records also report `paint-cache-hits`, `paint-cache-misses`
and `painted-bytes` for that render. GPU upload/reuse/atlas counters are cumulative
for the current compositor owner. Focus full/retained counters and
`last-focus-fallback` help explain conservative fallback; the last reason can refer
to an earlier focus change. These fields do not add per-frame logging.

The native renderer test executable accepts `--pipeline-workload` for the decorated
60-row comparison. It runs three repeats of full and retained-layout progress updates,
reporting raster footprint separately from bytes actually repainted. Use Release
for performance comparisons. This WIC probe excludes GPU upload/commit, provider
work and real artwork decoding; pair it with compositor pixel/counter checks and
physical testing rather than treating it as end-to-end FPS.

`--scroll-retention` compares vertical lists, artwork-backed grids and horizontal
rails at 100%, 125%, 150% and 200% scale against forced fresh captures. It checks
exact pixels, resizing, nested clipping, cursor eviction/reset and current focus
geometry. The reported timings exclude the comparison/replay work. The ordinary
suite also enforces pixel-reuse budgets. `OverlayChromeTests --focus-scroll-pixels`
checks real compositor placement and GPU reuse while interrupting a focus fade.

`--scroll-large-coordinates` adds 240-item/deep-offset cases at those scales.
`--scroll-capture-parity` compares full-item captures with conservative raster
clipping using flat, square tiles, including resizing, cursor eviction/reset,
authored translation and nested scrolling. Textured artwork, rounded masks and
depth are checked separately with byte-identical retained-versus-fresh captures;
the old truncated raster path can produce different antialias coverage at rounded
edges and is not a pixel identity oracle for those effects.
For production widget structures, export the Playnite layout fixtures with
`WRAIL_PLAYNITE_LAYOUT_OUTPUT` or the standalone YouTube Music tests' `--export-layout`.
The `.renderer.json` files include the production Bridge-resolved default theme
and package styles. Pass one to `build.ps1 -ScrollWorkloadFixture <path>` to build
and run the native `ScrollWorkloadProbe`. This probe has no renderer test geometry
instrumentation, honors the widget's surface hints, and substitutes deterministic
in-memory artwork. It reports near-start/deep scrolling and the largest changed
raster bands. It does not measure provider/network delay or display FPS.
The probe also reports `prepared-nodes` and `deferred-items` so viewport work can
be distinguished from raster reuse. Run the built probe with an additional
`--cadence` argument to exercise coalesced input, a loaded-range stop, synthetic
page arrival, reversal and scope cancellation using the same real-widget layout.

For physical evidence, launch with `--scroll-diagnostics-quiet`, exercise the
widget, then hide the overlay. The bounded in-memory trace is saved under
`%LOCALAPPDATA%/WidgetRail/diagnostics/scroll-<PID>.log`; use
`scripts/Analyze-ScrollDiagnostics.ps1` to summarize it. Inspect dropped records
before interpreting totals. CPU frame/submission times are not display FPS or GPU
execution time; no trace-file writes occur on the scrolling paint path.

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

## Poster artwork regression follow-up

Physical testing found that posters disappeared when moving focus between visible
rows, while scrolling to an offscreen row restored them. Retained preparation had
incorrectly used presence in the Taffy layout as a requirement for every paint node.
Poster images deliberately have no independent layout box: they paint into the tile's
box. The retained pass now prepares these images with their current semantic pointers
and cached style, using the same child classification as full layout preparation.

An offscreen pixel regression failed before the correction and passes afterward. It
checks every visible row through focus moves, right-stick movement in a nested scroll,
paint-only snapshot updates and local poster-content layout changes. Assertions also
keep artwork outside independent layout geometry and reject full layout rebuilds on
those retained paths. The renderer suite now passes **6952 checks**; its playback
workload remains about 0.57 ms full preparation / 0.064 ms paint-only preparation.

Build/test logs and unique managed binlogs are retained in the worktree's ignored
`logs` directory. Physical acceptance completed for performance and poster scrolling in candidate
`4eac4a42`. The user authorized integration and closure. Latest live Spotify capture
(176 logged frames) measured 4.83 ms median / 12.42 ms p95 draw time, compared with
70.77 ms / 78.94 ms in the earlier 61-frame capture. These are sampled interaction
windows, not a controlled continuous-FPS measurement. The separately observed raw
controller snapshot-authority race is follow-up work.
