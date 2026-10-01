# Production shell geometry

Production and validation now use separate roots. Validation keeps the title,
card and `Shell.Close` control. A production shell attaches its Frame directly to
the existing native scale root, with no validation header or common padded card.
It keeps a conditional recovery panel for connection/empty/error states; success
does not consume a status row. Exception details remain in diagnostics.

One transparent WinUI HWND covers the selected monitor work area. This keeps
native XAML popup/focus ownership together rather than copying the previous
renderer’s multiple chrome HWNDs. Native Grid anchors the rail and guide at the
bottom independently of a widget’s preferred content size. Content grows upward
within the remaining bounded area. Center/left/right settings align the content,
guide and rail inside that work-area shell. Monitor DPI and user interface zoom
remain separate; outside margins stay monitor DIPs.
The remembered foreground HWND selects only the monitor. Native content sizing
waits for this window's actual XAML viewport after placement, so a DPI-unaware
foreground app cannot supply a false 96-DPI layout scale.

`ProductionShellGeometry` owns only shell capacities/reservations. Normal guide,
guide-to-rail, rail and content-gap bands are 58/46/76/3 design DIPs. Very short
viewports reduce empty bands before producing a negative content area. Native
WinUI continues to measure all child layouts and intrinsic content. No authored
layout is replicated in host geometry.

The horizontal native ListView retains selection, virtualization, context menus,
focus restoration and reordering. Rail items are icon-only squares, preferred
64 DIPs, shrinking to 44 to fit the catalog before using explicit previous/next
controls. Native ScrollIntoView realizes/focuses page targets. Icons scale with
their tile. Accessible names and tooltips retain widget identity. Up and A enter
the selected widget through the existing selection path. A reserved side slot
(`RailStatusHost`) accepts the independent passive status view; centered rails
reserve equal side space so status does not shift their anchor.

The widget surface receives its existing appearance fill and the resolved panel
corner radius. The guide and rail envelope stay transparent; individual native
items receive their own theme roles. Transparent empty shell space dismisses the
overlay, while widget, guide, rail, recovery and native popup interactions retain
their own routes. The existing backdrop/activation and input adapter remain.

`Overlay.Shell` is the stable UI Automation root. Its Name identifies the current
widget and HelpText carries the existing diagnostic snapshot. Production scripts
use `Close-WinUiTestShell.ps1`, which verifies the explicitly supplied process owns
the frontend HWND and posts WM_CLOSE into normal owned shutdown. No invisible
debug button or process kill is retained for testing.

## Validation and remaining parity

The pure shell suite covers rail/status capacity, content constraints and short
viewports. `--validate-production-shell` uses the actual shell XAML/presenter and
checks stationary guide/rail bounds across Compact/Wide/FillAvailable surfaces,
three zoom levels and three placement settings; it also checks native square
items, overflow focus, recovery visibility and accessible icon identity.
Provider-backed shell/reorder/cleanup checks remain distinct from that fixture.

Recorded gates: analyzer build clean; 49 managed shell tests; 82 native geometry
checks; five real catalog tray/menu/reorder checks; six production catalog,
selection, screenshot and owned-process cleanup checks. Native keyboard Up was
observed entering widget interaction and a desktop mouse click dismissed an empty
shell region; the hidden frontend then completed normal WM_CLOSE cleanup.
Capture-screen screenshots were inspected. Evidence: `artifacts/product-shell`.
Initial driver repairs corrected PowerShell's unsupported `nint` type name,
explicit HWND selection with the separate backdrop, and hidden-window discovery.
The fixture's label assertion now checks effective ancestor visibility. These
were test-driver/fixture failures, distinct from the guide-width correction found
by inspecting production pixels. Synthetic touch delivery was inconclusive and
is not counted as physical pointer acceptance.

The semantic guide and passive status view are now integrated; their contextual
input and observation lifetimes are documented in `winui-semantic-controller-guide.md`
and `winui-shell-status.md`.
Radial switching, native bottom selection-marker styling and physical controller,
mixed-monitor and live-surface acceptance remain explicitly outstanding. This
checkpoint does not claim complete product parity and makes no pinned changes.
