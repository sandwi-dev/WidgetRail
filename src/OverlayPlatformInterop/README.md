# OverlayPlatformInterop

`OverlayPlatformInterop` is the version-2 native presentation-platform boundary.
It owns the single production Microsoft GameInput instance, supported Guide
callback, quarantined legacy Guide adapter, controller device/sample lifecycle,
repeat and neutral priming, Guide debounce, foreground-target memory, and safe
work-area placement entrypoint.

The boundary never creates a window, renderer, focus tree, widget transport, or
managed input reader. Its caller supplies the presentation HWND lifecycle and
marshals event callbacks onto that window's UI thread. ABI structures carry
`structSize` and `abiVersion`; callers require
`WRAIL_OVERLAY_PLATFORM_ABI_VERSION`.

Shutdown is idempotent. It stops and unregisters GameInput callbacks, closes
callback admission, waits for in-flight callbacks, clears queued events, then
releases native owners. The legacy XInput Guide ordinal remains private to
`OverlayHost/GuideInputCompatibility.*`.

## Opt-in controller isolation

Controller isolation is disabled by default. Start the long-lived Release host
with `OverlayHost.exe --controller-isolation`; the retired enable, status,
disable, and recover helper commands are rejected. The native platform boundary
then owns one dedicated routing thread, the selected physical GameInput reader,
one ViGEm Xbox 360 target, and the exact HidHide policy delta. Rendering and
window-message work do not schedule gameplay forwarding.

Startup accepts exactly one known physical gamepad and rejects unavailable,
ambiguous, unknown, or virtual-output identities before it writes HidHide
policy. It journals the exact local policy delta before applying it. A prior
local journal is restored only when the current policy still proves that the
delta is ours; foreign additions, inverse policy, corrupt local records, and
the retired Guardian-era journal all fail closed.

The routing thread serializes ordinary reports against neutral transitions.
Opening the overlay drains the pre-transition input boundary and submits neutral
before local overlay input is admitted. Closing retains neutral until the
release barrier completes, then resumes from a current reading. A bounded local
edge queue preserves controller taps and Guide edges for the host; it cannot
backpressure game-facing output. Disconnect, stale readings, output failure, or
queue corruption retire only the owned target.

Normal OverlayHost shutdown stops callback admission and the routing thread,
neutralizes and removes only its ViGEm target, then restores only the journaled
HidHide delta. Application crash survival and forwarding after OverlayHost exit
are intentionally not guarantees of this application-lifetime design; the next
startup can attempt bounded local policy recovery.

HidHide and ViGEmBus must already be installed. This mode never selects another
controller automatically, changes unrelated policy entries, or grants widgets
device, driver, or virtual-controller authority.
