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
The version-1 managed ABI uses only fixed-width `uint32_t` scalars for Boolean
fields, parameters, return values, and out values (`0` is false; `1` is true),
and the public header asserts every managed-facing structure size and critical
offset. Focused verification links through the generated import library and
loads the built DLL rather than compiling a private copy of its implementation.

Shutdown is idempotent. It stops and unregisters both GameInput callbacks,
atomically closes callback admission and accounts entrants under one mutex,
waits for any callback already in flight to leave the opaque owner, clears
queued events, and only then marks shutdown complete and permits destroy to
release the owner. Concurrent shutdown callers wait for that completed state.

The legacy XInput Guide ordinal remains implemented only by
`OverlayHost/GuideInputCompatibility.*` and is consumed privately by this
boundary. It is not part of the public ABI.
