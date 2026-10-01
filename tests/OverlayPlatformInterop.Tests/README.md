# OverlayPlatformInterop focused tests

Run the deterministic native boundary suite with:

```powershell
scripts\Build-OverlayPlatform.ps1 -Configuration Release -TestPolicy
```

The suite covers ABI/version rejection, Guide show/hide debounce, controller
prime/repeat/device-loss/reconnect/trigger/chord state, work-area containment,
foreground target ownership, and idempotent callback shutdown. It does not
require a controller; physical Guide and controller behavior remains a manual
integration check for the final product candidate.

`scripts/Build-OverlayPlatform.ps1 -NoRestore -TestForeground` builds the platform
and runs the independent foreground-boundary tests using hidden windows only.
They never initialize a controller owner or change foreground. They cover invalid
output/owner, foreign-process, wrong-thread, hidden, destroyed and retired windows.
The resulting `ForegroundAcquisitionTests.exe --activation` is a separate opt-in
desktop check that briefly shows a test window and compares the returned boolean
with actual foreground process ownership. Windows may deny that request; physical
Guide activation remains its own acceptance check.

Window-state regression checks exercise repeated visible/unfocused publication
through the production DLL setter and assert that only the actual focus-loss
transition primes the tracker. Deterministic raw-state policy checks cover A,
D-pad/stick presses, held repeats, releases, and hide/reopen priming. They require
no controller gestures. The separate GameInput query-lifetime executable still
uses the installed runtime and device enumeration; its failures are not proof
that the deterministic controller policy failed.
