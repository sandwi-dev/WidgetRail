# WinUIEx

Version: 2.9.3 (NuGet dependency; not vendored source).
Repository: https://github.com/dotMorten/WinUIEx
Package source commit: 72f2975d2a237c0d7ad1113fe617d5894e66feb6
License: MIT, retained verbatim in LICENSE.

Used by the WinUI frontend for TransparentTintBackdrop. It supplies the windowing
integration missing from a plain transparent SystemBackdrop brush; WidgetRail does
not copy its native message interception or DWM implementation. The ordinary UI
remains owned by WinUI. See docs/maintainers/winui3-hosting-decision.md for validation.
