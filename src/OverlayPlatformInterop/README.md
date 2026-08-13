# OverlayPlatformInterop

`OverlayPlatformInterop` is the version-1 native presentation-platform boundary.
It owns the single production Microsoft GameInput instance, supported Guide
callback, quarantined legacy Guide adapter, controller device/sample lifecycle,
repeat and neutral priming, Guide debounce, foreground-target memory, and safe
work-area placement entrypoint.

The boundary never creates a window, renderer, focus tree, widget transport, or
managed input reader. Its caller supplies the one presentation HWND lifecycle,
marshals event-availability callbacks onto that window's UI thread, and retains
shell action and semantic focus policy. All ABI structures carry `structSize`
and `abiVersion`; callers must require `GBA_OVERLAY_PLATFORM_ABI_VERSION`.

The legacy XInput Guide ordinal remains implemented only by
`OverlayHost/GuideInputCompatibility.*` and is consumed privately by this
boundary. It is not part of the public ABI.
