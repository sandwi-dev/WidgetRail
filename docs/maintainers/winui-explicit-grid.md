# Explicit WinUI grid projection

`ViewNodeKind.Grid` with `GridLayout` creates a plain
`Microsoft.UI.Xaml.Controls.Grid`. Rows and columns map directly to native
`RowDefinition` and `ColumnDefinition`: Auto, device-independent Pixel lengths,
and weighted Star lengths, including minimum and maximum constraints. Empty axes
use WinUI's implicit single Star track. Row and column spacing come from the
layout declaration; WRSS flex direction, grow, justify, and gap do not translate
or reorder explicit tracks. Normal theme paint, padding, typography, and authored
element dimensions continue to apply.

Direct child `GridCell` placement sets native row, column, and span on the actual
layout owner. For animated/decorated elements that owner is the existing motion
wrapper, not its inner control. Container changes clear attached values that the
new parent no longer owns. Existing Grid positions are updated directly, avoiding
reset-and-reapply churn on every publication. Reused controls cannot carry stale
spans into rows, scroll panels, posters, or responsive grids. Collapsed children retain
their declared slots rather than shifting siblings into different cells.

Updating tracks reuses the native Grid, definitions, and child controls. Switching
between explicit and responsive Grid replaces the incompatible container but
retains compatible logical children and existing presenter focus restoration.
An ordinary Grid without `GridLayout` remains the existing bounded responsive
grid. Indexed collections retain their native virtualized controls and can be
placed in finite Star tracks; the explicit grid itself is a static layout panel,
not a new collection implementation.

## Validation

`WidgetStylesValidationPage.ExplicitGrid.cs` adds a native fixture to the shared
style suite and supports `--validate-styles --explicit-grid-only`. It checks native
fixed/Auto/weighted-Star measurement, min/max constraints, real wrapper spans,
resizing with retained focus, cell movement, definition removal, implicit tracks,
responsive/row conversion, and a finite scroll viewport that resizes in place.

Build and native execution are coordinated by the root task. This document does
not claim physical controller acceptance or performance measurements.

## Integrated source checkpoint

SDK Gallery 0.1.24 adopts the API in its Controls page, with a local-only action
that swaps star weights while preserving control and focus IDs. All 10 Gallery
checks, 144 SDK checks and 272 presentation-session checks pass. Three authoring
guide examples compile and validate. Debug and trimmed Release frontend builds
are analyzer-clean. Evidence: artifacts/winui-shell/explicit-grid-20260929/.
The archive is staged but not installed; it needs the matching protocol 64 runtime.
The native geometry/focus fixture is compiled but pending the exclusive frontend
slot. No claim of rendered geometry or smoothness acceptance follows from these
source/build results.
