# WinUIEx

Version: 2.9.3 (NuGet dependency; not vendored source).
Repository: https://github.com/dotMorten/WinUIEx
Package source commit: 72f2975d2a237c0d7ad1113fe617d5894e66feb6
License: MIT, retained verbatim in LICENSE.

The WinUI frontend uses CompositionBrushBackdrop and WindowMessageMonitor.
WidgetRail's TransparentWindowBackdrop adapts the upstream DWM configuration
and erase-background handling. It releases each acquired window DC, borrows the
stock black brush, and deletes the blur region after each DWM configuration.
