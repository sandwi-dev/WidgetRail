# Native collection profile

> Historical native-renderer profiling evidence. Playnite Home/Library now publish
> indexed WinUI collections. `WRAIL_PLAYNITE_LAYOUT_OUTPUT` emits `.indexed.json`
> parent/range fixtures for those pages; they are not compatible with the old
> eager `ScrollWorkloadProbe`. Use `Test-WinUiPlaynitePresentation.ps1` for acquired
> native row geometry and the shared UX gate for session-backed page behavior.
> The independent `Native-Details-Geometry.renderer.json` fixture remains valid.

2026-09-26. Adoption baseline: `a05e0279`, plus the adjacent
`CollectionWorkloadProfile.inl` probe. This is an offscreen CPU/resource comparison,
not live-overlay frame rate, compositor timing, provider latency or acceptance.

## Method

Production widget tests export synthetic snapshots with resolved Bridge/WRSS styles.
The native probe uses DirectWrite, Taffy and Direct2D WIC drawing with deterministic
artwork decoding. Each case runs in a separate process, serially, at 980×700 DIPs
and 125% scale. Depth, focus styles and composition capture remain enabled.

For each exported collection, retain a prefix of its existing synthetic items;
keep IDs, contents, resolved styles and surrounding page intact. Remove provider
window/loading metadata for this finite dataset. Compare native realization with
the same tree converted to eager Scroll/ResponsiveGrid. Sizes tested:

- Spotify queue: 16, 32 and 50 items.
- Games and Apps library: 16, 32 and 64 items.
- Playnite Browse: 16, 64 and 150 items.

Each phase attempts 96 directional focus moves or 7.3-DIP scroll deltas, reversing
every 24 attempts. Output records actual moved/blocked counts; blocked attempts
still redraw, so small-window distributions include boundary frames. Focus uses
the host's navigation and retained-focus planning. Only unrealized targets run
preparation slices. Scroll uses `PlanPreparedFreeScroll`. Preparation uses an
explicit four-item/1.5-ms soft slice budget; individual work may overrun. The
probe executes pending slices consecutively and records their count, without
pretending to measure timer/frame scheduling latency. Drawing is acknowledged
only after successful EndDraw. Every focused target must remain revealed.

`input-cpu` includes navigation/planning, preparation slices, drawing and frame
acknowledgement. Frame/preparation times are renderer counters. Memory is peak
process private-commit growth after graphics setup, including allocator/driver
retention; active raster bytes are also reported separately. Neither is a precise
GPU residency total or an aggregate cache budget.

## Initial results

One run, rounded; retain the raw distributions when comparing later changes.
Games and Apps had 84 moved/12 blocked focus attempts in both paths; Spotify and
Playnite had 96 moved focus attempts. All three had 96 moved scroll attempts.

| Fixture | Mode | Focus prepared nodes p95 | Focus CPU p95 | Scroll CPU p95 | Peak private growth |
|---|---|---:|---:|---:|---:|
| Spotify 50 | Eager | 792 | 13.42 ms | 6.74 ms | 111.2 MiB |
| Spotify 50 | Realized | 246 | 8.22 ms | 6.94 ms | 98.6 MiB |
| Games and Apps 64 | Eager | 3,617 | 152.56 ms | 8.79 ms | 84.8 MiB |
| Games and Apps 64 | Realized | 917 | 11.75 ms | 7.76 ms | 85.0 MiB |
| Playnite 150 | Eager | 12,644 | 139.55 ms | 17.41 ms | 160.9 MiB |
| Playnite 150 | Realized | 1,301 | 29.32 ms | 14.39 ms | 160.3 MiB |

Realization reduces heavy focus preparation substantially. Scroll results are
mixed and process memory is not consistently lower. Playnite still has material
preparation cost. Do not close the remaining scheduling/local-preparation work
or infer a latency guarantee from these comparisons. Investigate repeated outer
layout/focus-follow passes and per-slice preparation before expanding caches.

Earlier `profile-*` logs used unconditional focus preflight and omitted retained
focus planning; those are superseded by `routed-profile-*` logs. They must not be
used as the production-routing comparison.

## Retained-placement follow-up

Placement reuse removes repeated outer layout when the committed measured window
covers the new demand. Eligibility includes exact containing-block constraints and
the admitted logical scroll range. Stateful comparisons caught and corrected both
constraint drift and provider limits that previously depended on realized rows.

The same largest-window workload after this change (`retained-range-profile-*`):

| Fixture | Focus preparation p95, before → after | Focus CPU p95, before → after | Scroll CPU p95, before → after |
|---|---:|---:|---:|
| Spotify 50 | 2.02 → 0.44 ms | 8.22 → 8.87 ms | 6.94 → 5.98 ms |
| Games and Apps 64 | 4.23 → 1.41 ms | 11.75 → 12.08 ms | 7.76 → 8.60 ms |
| Playnite 150 | 11.61 → 1.16 ms | 29.32 → 27.79 ms | 14.39 → 12.36 ms |

These sequential single-run samples show substantially less layout preparation,
but not a uniform end-to-end win. Playnite's focus prepared-node p95 drops from
1,301 to 489; its frame p95 is still 22.52 ms in this software-target workload.
Painting/finalization and aggregate retention remain separate improvement areas.
Do not attribute small differences across runs to one cause without more evidence.

## Reproduce

Set `WRAIL_COLLECTION_LAYOUT_OUTPUT` while running the Spotify/Games and Apps test
executables to export their fixtures; Playnite uses `WRAIL_PLAYNITE_LAYOUT_OUTPUT`.
Build the native probe with `build.ps1 -ScrollWorkloadFixture <fixture>` (which also
runs the default scroll workload), then run each profile serially:

```powershell
& src/OverlayHost/out/Release/ScrollWorkloadProbe.exe <fixture> --profile 50 realized
& src/OverlayHost/out/Release/ScrollWorkloadProbe.exe <fixture> --profile 50 eager
```

Choose a count no larger than the exported item window. `--compare` independently
checks visible geometry/content pixels against eager rendering at three widths
and two scales, excluding estimated scrollbar thumbs. A profile is not a
replacement for those correctness checks or the cursor lifecycle tests.

`--compare-retained` is the stateful full-layout comparison for placement reuse;
unlike the eager comparison, both sides share the same logical/estimated window
and compare all pixels, including scrollbar thumbs. It also asserts that reuse
actually occurred, rather than passing by falling back on every frame.
