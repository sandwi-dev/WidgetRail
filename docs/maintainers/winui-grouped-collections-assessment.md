# Native grouped collection assessment

> Implementation record: checkpoint dates, temporary candidates, former source
> paths and pending-work statements below describe the stage recorded, not current
> release status. See [current platform status](current-platform-status.md) and
> [WinUI authoring](../reference/winui-authoring.md) for the implemented contract.

2026-09-27. Source inspection plus executed native probes on Windows App SDK 2.5.1
(WinUI package 2.3.9), at 125% Windows scaling. The default grouped source loses
range callbacks; a small native collection-view adapter restores them. No public
widget SDK grouping contract has been introduced yet.

## Required product behavior

YouTube Music Home currently renders contiguous section runs as a heading followed
by a `ResponsiveGrid` (minimum width 130, maximum four columns), all in one vertical
scroll surface. Section titles are not independent scrolling lists. Repeated titles
separated by another section are separate runs. Home tiles keep their artwork,
actions, context menus and song-only playback context.

Native `GridView` with horizontal `ItemsWrapGrid`, top group headers and a single
internal vertical `ScrollViewer` is the closest standard control shape. Its native
grouping must be assessed independently from item-data retention. Group headers
and UI virtualization alone do not imply that sparse data-source demand works.

## What the platform sources establish

Inspection uses microsoft/microsoft-ui-xaml commit
`1fdf51480ab1e5fe92b63d2e1c0b8d56c367049e`. This upstream source is evidence about
implementation, not proof that the installed Windows App SDK binary is identical.

1. `GroupedDataCollectionView.CalculateGroups` visits all **group descriptors**
   when the source is assigned. Its `GetAt` walks group counts and indexes into
   the matching group's items; it does not inherently materialize every item.
   `IndexOf` delegates to each group's vector, so inner constant-time occurrence
   lookup matters. A modest bounded set of Home sections fits this shape.
2. `CollectionViewGroup.GetObservableVector` preserves a supplied observable
   vector, or wraps `IBindableVector` plus `INotifyCollectionChanged`. Otherwise
   it manufactures a read-only collection by enumerating the source. Merely
   implementing `IEnumerable` is insufficient for sparse inner groups. Our
   `IList` + `INotifyCollectionChanged` is the intended non-copying path, subject
   to confirmation through the C#/WinRT projection in the installed runtime.
3. `ListViewBase.InitializeDataSourceItemsRangeInfo` queries the control's **direct
   ItemsSource** for `IItemsRangeInfo`. The inspected grouped collection-view
   hierarchy does not implement that interface or forward its callbacks to inner
   group sources. Assigning `CollectionViewSource.View` therefore cannot be assumed
   to retain the range callbacks previously delivered to a direct indexed source.
4. This matters in WidgetRail: `IndexedItemsSource` creates a stable slot on index
   access but deliberately schedules reads/eviction through `RangesChanged`.
   Sparse index access without range callbacks would leave placeholders unfilled
   and would not provide the data-retention lifecycle we need.

Official examples support the native layout choice but do not resolve point 3:
`winapp find-ui --id gallery-semanticzoom-1` uses `GridView`, grouped
`CollectionViewSource.View` and a header template; `gallery-gridview-2` uses
`ItemsWrapGrid.MaximumRowsOrColumns` for wrapping. Microsoft's data-virtualization
guidance describes direct `IList`/`IItemsRangeInfo` use, not grouped callback
forwarding. Its native IItemsRangeInfo integration test also assigns its source
directly to a ListView.

## Probe prepared

`src/OverlayFrontend.WinUI/Validation/GroupedCollectionValidationPage.cs` creates
three groups of 10,000 logical slots backed by the existing indexed source, an
80 ms asynchronous reader, native group headers and native `ItemsWrapGrid`.
Each group implements `IList`, `INotifyCollectionChanged` and `IItemsRangeInfo`;
it forwards only calls that actually reach it. There is no custom viewport scan,
range synthesis, nested list, full-data preload or custom layout masking platform
behavior. It publishes counts through `Grouped.Status` for UI Automation.

The constructor's `flatBaseline: true` binds the first source directly to the same
GridView. `useRangeAdapter: true` uses one flat 30,000-item indexed query, three
observable indexed slices and `NativeGroupedRangeView`. Each mode runs in a fresh
window, so previously fetched data cannot hide missing grouped demand. Launch with
`--validate-grouped`, `--validate-grouped-flat` or `--validate-grouped-adapted`.

The probe sequence is automated by `scripts/Test-WinUiGroupedCollection.ps1`:

1. Build/analyze, then launch each mode through WinApp CLI. The validation window's
   existing asynchronous cleanup owns the page and its data sources.
2. Launch grouped mode at the same 125% scale as earlier collection checks. Wait
   for native settling, press F6, and capture `Grouped.Status` plus screenshot.
   Check headers, wrap direction, one scroll viewer, native container count,
   per-group index accesses/enumerations, range callbacks and completed loads.
3. F5 scrolls to group 1, item 6000 (flat mode: first group, item 6000). F6 inspects
   after settling; F8 attempts focus on that native container. F7 returns to the
   first item. Check local index -> flattened index mapping and focus boundaries.
4. Run flat baseline fresh and compare. If flat loads with bounded retention while
   grouped produces slots but zero callbacks/loads, that confirms the source-level
   limitation in the installed binary. Do not compensate inside the probe.
5. If grouped demand does work, compare distant eviction, reverse traversal,
   narrow/wide native wrapping, and F9 content refresh after focusing deep.
   Record focus/offset before and after; require no whole-inner-source enumeration.

The expected negative result is useful evidence. This probe is a data/lifecycle
check; its simple text tiles cannot prove production poster performance, complete
styling, controller navigation or accessibility parity.

## Runtime result and chosen adapter

The initial grouped probe had 30,000 logical items and eight realized controls,
zero item enumeration, zero range callbacks and zero loads. Deep native scrolling
and item focus worked, but did not change the missing-data result. The flat source
received range callbacks and loaded its rows normally.

`NativeGroupedRangeView` delegates `ICollectionView` to the real WinUI grouped view
and exposes `IItemsRangeInfo` at the direct ItemsSource boundary. Groups are slices
of **one flat query**, so native ranges can be forwarded unchanged (with defensive
clipping). There is no per-group provider/scatter layer or separate cache budget.
Currency, group headers, index lookup, realization, layout and scrolling remain
native. The adapter rejects structural changes to its immutable query and releases
only its event subscriptions/demand; the caller owns source disposal.

The adapted run focused flat item 16,000 in the second group, refreshed content
without changing focus/offset, then returned to item zero. It used one scroller,
17 native containers at those observation points, at most 96 data slots, and no
full enumeration. Retained data stayed bounded after reversal. This establishes
the data/realization integration, not production artwork performance or every
controller-navigation edge case.

Checkpoint checks: seven unadapted-grouped, nine flat-baseline, and ten adapted UI
checks pass. The adapted mode also runs five contract scenarios covering empty
groups, sentinel/overflow clipping, native event sender/cancellation, immutable
structure and disposal ownership. WinUI analyzer build: zero warnings/errors.
Evidence: `artifacts/winui-grouped/checkpoint/`, `contracts-build.log`, and its
unique binlog. The screenshots use the transparent validation shell over the
desktop, not a finished widget visual design.

## Next integration

Do not replace Home with nested independent scroll views or eagerly rendered grids
to claim migration success. Integrate the proven flat-query adapter with the real
semantic row leases. Add clear author-facing group metadata and retain existing
header styling, row action context, focus identity and native navigation across
partial group rows. Query replacement, group changes and window scaling require
additional tests before converting YouTube Home. The adapter is not a replacement
layout or focus engine.

## References

- [Data virtualization for ListView/GridView](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/listview-and-gridview-data-optimization)
- [UI virtualization and native panels](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/optimize-gridview-and-listview)
- [GroupedDataCollectionView implementation](https://github.com/microsoft/microsoft-ui-xaml/blob/1fdf51480ab1e5fe92b63d2e1c0b8d56c367049e/dxaml/xcp/dxaml/lib/GroupedDataCollectionView_Partial.cpp)
- [CollectionViewGroup source adaptation](https://github.com/microsoft/microsoft-ui-xaml/blob/1fdf51480ab1e5fe92b63d2e1c0b8d56c367049e/dxaml/xcp/dxaml/lib/CollectionViewGroup_Partial.cpp)
- [ListViewBase range admission and notifications](https://github.com/microsoft/microsoft-ui-xaml/blob/1fdf51480ab1e5fe92b63d2e1c0b8d56c367049e/dxaml/xcp/dxaml/lib/ListViewBase_Partial_DataVirtualization.cpp)
- [Microsoft's direct-source range tests](https://github.com/microsoft/microsoft-ui-xaml/blob/1fdf51480ab1e5fe92b63d2e1c0b8d56c367049e/dxaml/test/native/external/enterprise/datavirtualization/IItemsRangeInfoIntegrationTests.cpp)
