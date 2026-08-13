# AVP-004 controller/platform dependency decision

AVP-002's `Vortice.XInput` experiment is retired from the candidate and project
dependencies. AVP-004 consumes the accepted production extraction,
`OverlayPlatformInterop` ABI v1.

The native component owns the existing Microsoft GameInput Guide callback,
background/exclusive visibility policy, legacy Guide compatibility quarantine,
controller device/sample lifecycle, dead-zone/repeat/neutral gating, foreground
targeting, and safe placement. It creates no window, renderer, transport, or
focus tree. Avalonia provides one HWND, drains typed events, and routes semantics
through one focus/action state.

This avoids a second C# GameInput package/COM owner, avoids copying production
platform policy, and preserves the already reviewed native behavior. The
managed ABI layout is asserted against the native header; physical device and
Guide behavior remain mandatory planner/user evidence.
