# WidgetRail app icon

Selected concept A: three mint widget tiles above a rail on a navy rounded square.
The editable source is `widgetrail.svg`. It is project-owned artwork under MIT.

Run `pwsh -NoProfile -File scripts/New-ApplicationIcon.ps1` from the repository
root on Windows to regenerate the checked-in PNG, ICO and WinUI package logos. The generator reads
the rounded rectangles directly from the SVG and supersamples each ICO size:
16, 20, 24, 32, 40, 48, 64, 96, 128 and 256 pixels. Transparent corners are retained.
No image-generation service is needed for production asset regeneration.

The WinUI frontend links the canonical ICO as both its executable icon and
`Assets/AppIcon.ico`, which is applied through `AppWindow.SetIcon`. Package tiles,
Start-menu logos and splash assets are generated from the same SVG; wide canvases
center the mark without stretching. Executable and package display metadata use
WidgetRail. The managed bridge, Settings worker and shared widget worker also
embed the ICO. Community widget artwork and Microsoft WebView2 processes keep
their own identity. This does not change Task Manager process grouping.
