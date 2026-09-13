# WidgetRail app icon

Selected concept A: three mint widget tiles above a rail on a navy rounded square.
The editable source is `widgetrail.svg`. It is project-owned artwork under MIT.

Run `pwsh -NoProfile -File scripts/New-ApplicationIcon.ps1` from the repository
root on Windows to regenerate the checked-in PNG and ICO. The generator reads
the rounded rectangles directly from the SVG and supersamples each ICO size:
16, 20, 24, 32, 40, 48, 64, 96, 128 and 256 pixels. Transparent corners are retained.
No image-generation service is needed for production asset regeneration.

The icon is embedded in the native host and artwork decoder, the managed bridge,
Settings worker and shared widget worker, and Inno Setup. Host window classes
also expose large and small icons. Start menu and uninstall entries already use
the host executable. Community widget artwork and Microsoft WebView2 processes
keep their own identity. This does not change Task Manager process grouping.
