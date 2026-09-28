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

Window-state regression checks exercise repeated visible/unfocused publication
through the production DLL setter and assert that only the actual focus-loss
transition primes the tracker. Deterministic raw-state policy checks cover A,
D-pad/stick presses, held repeats, releases, and hide/reopen priming. They require
no controller gestures. The separate GameInput query-lifetime executable still
uses the installed runtime and device enumeration; its failures are not proof
that the deterministic controller policy failed.
