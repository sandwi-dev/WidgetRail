# WidgetRail app icon

Three rounded square tiles above a rail, with silver-green facets and a
near-black graphite background. The larger center tile and aligned bottom edges
preserve the original tile-rail identity.
The editable source is `widgetrail.svg`. It is project-owned artwork under MIT.

Install Node.js 18 or later, then run `npm.cmd ci --prefix scripts/branding` from the
repository root to install the pinned development-only SVG renderer. Run
`pwsh -NoProfile -File scripts/New-ApplicationIcon.ps1` to regenerate the
checked-in PNG, ICO and WinUI package logos. The generator renders the full SVG,
including gradients, clipping and shadows, directly at each ICO size:
16, 20, 24, 32, 40, 48, 64, 96, 128 and 256 pixels. Transparent corners are retained.
No image-generation service is needed for production asset regeneration. Node
and the renderer are not shipped with WidgetRail or required for normal builds.

The WinUI frontend links the canonical ICO as both its executable icon and
`Assets/AppIcon.ico`, which is applied through `AppWindow.SetIcon`. Package tiles,
Start-menu logos and splash assets are generated from the same SVG; wide canvases
center the mark without stretching. The startup indicator uses the canonical
PNG so it preserves the same appearance without relying on WinUI SVG filter
support. Executable and package display metadata use
WidgetRail. The managed bridge, Settings worker and shared widget worker also
embed the ICO. Community widget artwork and Microsoft WebView2 processes keep
their own identity. This does not change Task Manager process grouping.
