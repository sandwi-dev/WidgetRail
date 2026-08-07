# Controller/Input Feasibility Probe

This bounded native diagnostic tests what Windows input paths observe while an overlay-like window owns foreground focus. It is evidence gathering for the controller containment gate; it is not an input blocker and does not claim that observing an API proves what every game will do.

It records:

- Foreground-window and application activation transitions.
- GameInput gamepad state, including whether readings arrive in foreground/background.
- GameInput Guide/Share system-button callbacks.
- GameInput generic controller-button labels and rising edges, including a
  strictly labeled Guide fallback for `XboxGuide`, `IconHome`, `Home`, and
  `Guide`. Unlabeled extra buttons are logged but never treated as Guide.
- XInput state changes from all four user slots.
- The dynamically loaded XInput ordinal-100 `GetStateEx` result, clearly
  labeled undocumented and diagnostic-only. This can reveal the otherwise
  hidden `0x0400` Guide bit but is not a supported production API contract.
- HID joystick/gamepad/multi-axis Raw Input registered with `RIDEV_INPUTSINK`, including whether reports arrive while the probe is in the background.

GameInput is loaded dynamically. If `GameInput.h` was unavailable at build time, or the runtime DLL is absent at run time, the other diagnostics continue and the log identifies the limitation.
The probe prefers `GameInputRedist.dll` and falls back to the inbox
`GameInput.dll`, matching the production package loader on systems where the
redistributable is the newer runtime.

For a bounded byte-level investigation of the currently observed 8BitDo
device, add `--raw-hid-diff-2dc8-3106`. This opt-in mode records at most 128
bytes and 128 changed reports per matching `VID_2DC8/PID_3106` device. It does
not interpret a byte as Guide or establish a production mapping.

## Build

From PowerShell:

```powershell
cd tools\InputProbe
.\build.ps1 -Configuration Release
```

Prerequisites are the Visual Studio **Desktop development with C++** workload and a Windows 10/11 SDK containing `GameInput.h`. The script discovers Visual Studio Build Tools/Community and the newest installed SDK, then produces `build\Release\InputProbe.exe`. It deliberately fails with a specific message when a partial Visual Studio installation has compiler binaries but no C++ headers. CMake metadata is also provided for IDEs and environments where CMake is on `PATH`.

## Basic run

Every run ends after 120 seconds unless another bounded duration is supplied:

```powershell
.\build\Release\InputProbe.exe --mode overlay --focus-policy exclusive --duration 120
```

- Guide toggles the overlay window if Windows delivers the GameInput system
  callback or exposes the same physical control as one of the four documented
  Guide/Home generic-controller labels.
- `F8` is the deterministic toggle fallback.
- `Escape` exits early.
- The full log path is printed and shown in the probe window.
- `--duration 0` disables the timeout and should only be used for an intentionally supervised session.

Focus policies:

- `default`: no explicit background/exclusive flags.
- `no-background`: asks GameInput not to deliver input to this process while it is in the background.
- `exclusive`: asks GameInput for exclusive ordinary, Guide, and Share input while this process is foreground. This is the proposed overlay behavior, but it only governs GameInput clients.

## Two-process containment test

The best local proxy for an underlying game is a second copy of the probe. Start the observer first, then the overlay in a separate PowerShell window:

```powershell
.\build\Release\InputProbe.exe --mode observer --focus-policy default --duration 180 --log observer.log
.\build\Release\InputProbe.exe --mode overlay --focus-policy exclusive --duration 180 --log overlay.log
```

With the overlay visibly foreground, press `A`, `B`, `LB`, `RB`, move both sticks, and press Guide once. Compare the same timestamp interval in both logs.

- `GAMEINPUT` in overlay but not observer is evidence that the exclusive policy affects another GameInput client.
- `RAWINPUT ... foreground=no` in observer is direct evidence that a background Raw Input sink still receives that controller's HID reports.
- `XINPUT ... foreground=no` in observer shows that XInput polling still sees those state transitions in this setup.
- No Raw Input entry does **not** prove containment: some Xbox controllers are hidden from generic HID Raw Input or exposed through a different stack.

## Manual matrix

Use the same wired/wireless controller, port, probe binary, button sequence, and 180-second duration for every row. Save logs with descriptive names.

| Case | Xbox Game Bar | Steam | Overlay focus policy | What to record |
| --- | --- | --- | --- | --- |
| A | Off; disable `Open Xbox Game Bar using this button` | Fully exited | `exclusive` | Baseline Guide callback, ordinary GameInput exclusivity, observer Raw Input/XInput |
| B | On with Guide shortcut enabled | Fully exited | `exclusive` | Whether Guide opens both products or prevents the probe callback; ordinary input behavior |
| C | Off | Running; Guide chord enabled | `exclusive` | Whether Steam consumes/reacts to Guide and whether ordinary input still reaches observer |
| D | On with Guide shortcut enabled | Running; Guide chord enabled | `exclusive` | Collision behavior, callback ordering, and which UI gains foreground |
| E | Off | Fully exited | `default` | Control run showing behavior without the exclusivity request |
| F | Off | Fully exited | `no-background` | Minimize the probe and verify its own GameInput stream stops while Raw Input/XInput may continue |

For each case:

1. Start the background observer, then the overlay probe.
2. Confirm the overlay says `Probe foreground: YES`.
3. Press, with one-second pauses: `A`, `B`, `LB`, `RB`, D-pad directions; move each stick; squeeze each trigger.
4. Press Guide once. Note whether Game Bar, Steam, the probe, or more than one reacts.
5. Use `F8` to restore the probe if Guide was intercepted, repeat `A/B/LB/RB`, then let both runs end.
6. Record controller model, connection type, Windows build (`winver`), GameInput runtime file version, Steam version/channel, and Game Bar version beside the logs.

Repeat at least the baseline and collision cases with a representative game as the underlying foreground app. Prefer a game with a visible controller test/input screen, then compare its observed actions with the probe logs. Test borderless/windowed and Fullscreen Exclusive separately; a normal topmost window is not expected to cover every Fullscreen Exclusive title.

## Interpretation and limitations

- GameInput focus policy is scoped to GameInput. It is not a universal user-mode filter for Raw Input, XInput, direct HID, Steam Input, virtual controllers, or vendor APIs.
- The observer approximates an underlying client but cannot reproduce every engine's registration flags or timing.
- Raw HID hashes intentionally avoid interpreting vendor-specific report layouts. They demonstrate delivery, not semantic button identity.
- Guide availability varies by controller, transport, Windows configuration, and competing clients.
- The probe neither injects into games nor installs a driver, service, hook, or virtual controller.
- A credible go/no-go decision requires real-game tests and should report unsupported input paths honestly rather than inferring universal suppression.
