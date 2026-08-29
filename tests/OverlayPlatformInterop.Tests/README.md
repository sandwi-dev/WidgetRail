# OverlayPlatformInterop focused tests

Run the deterministic native boundary suite with:

```powershell
src\OverlayHost\build.ps1 -Configuration Release -PlatformInteropTestsOnly
```

The suite covers ABI/version rejection, Guide show/hide debounce, controller
prime/repeat/device-loss/reconnect/trigger/chord state, work-area containment,
foreground target ownership, and idempotent callback shutdown. It does not
require a controller; physical Guide and controller behavior remains a manual
integration check for the final product candidate.
