# Avalonia Overlay Prototype — AVP-001

This is an isolated feasibility process, not a production migration. It uses
.NET 10 and pinned stable Avalonia 12.1.1 packages. It does not reference the
production renderer, layout/focus/accessibility engines, GBSS, Widget SDK,
GameInput/SDL, or remote widget surfaces.

## Architecture

- `MainWindow` is one transparent, borderless, topmost, no-taskbar Windows
  shell. Its client size is stable while the experiment is open.
- `PrototypeShellView` owns one stationary tray and one clipped content region.
  Page transitions happen between Avalonia controls inside that region; the
  HWND is not resized between pages.
- `NavigationCoordinator<TPage>` applies a latest-wins generation and
  cancellation policy. The admitted page remains present while an asynchronous
  destination is loading, and only a completed destination is cross-faded.
- Four independent Avalonia pages exercise long text, missing artwork,
  buttons, sliders, and an explicitly named `ScrollViewer` containing a
  sixteen-item application list.
- `PrototypeLifecycle` cancels visible work when hidden. The ordinary evidence
  route starts hidden, shows and switches the real window, samples visible
  idle cost, hides it again, and exits explicitly.
- Standard Avalonia controls and `AutomationProperties` provide UIA. There is
  no parallel accessibility tree.

## Bounded focused verification

From the repository root:

```powershell
powershell -NoProfile -File .\experiments\AvaloniaOverlayPrototype\scripts\Verify-Avp001.ps1 -TimeoutSeconds 180
```

The command builds the isolated solution in Release and runs only its focused
MSTest.Sdk 4.3.2 component/Windows UIA suite. Each child command is killed and
reported if it exceeds the supplied bound. The tests cover the 3 logical work
areas × 3 scales × 4 pages, emitted Avalonia containment, stationary tray,
retained loading, transition supersession, directional focus ownership,
slider/Enter/Back ownership, real `ScrollViewer` round-trip, hidden lifecycle
and in-flight suspension,
the transparent/topmost/no-taskbar window contract, and a real
Windows UIA smoke for Window/Button/Slider focus, Invoke, and RangeValue.

After committing a clean milestone, retain exact-runtime measurements with:

```powershell
powershell -NoProfile -File .\experiments\AvaloniaOverlayPrototype\scripts\Measure-Avp001.ps1 -TimeoutSeconds 90
```

The script publishes a copied framework-dependent `win-x64` runtime and runs
one ordinary bounded Windows lifecycle. Outputs remain under
`experiments/AvaloniaOverlayPrototype/artifacts/avp001` and are intentionally
ignored by Git so evidence can name the exact commit that already exists.

## Visible planner launch

After measurement/publish, launch the exact copied build:

```powershell
& .\experiments\AvaloniaOverlayPrototype\artifacts\avp001\runtime-win-x64\AvaloniaOverlayPrototype.exe
```

Arrows move through standard focusable controls; Left/Right stays with a
focused slider; Enter invokes buttons; Escape or B returns through page history
and then closes/reopens the content surface while leaving the tray stationary.
The normal process remains open until its window/process is closed. No
credentials, privileged installation, or undocumented window manipulation are
used.

## AVP-001 limits

The retained bounds/UIA/frame records are deterministic feasibility evidence,
not proof of physical visual quality. The planner/user still owns the visible
transparency, focus-ring, animation, mixed-DPI, and no-black-frame verdict on
the accepted Release. GPU timing is unavailable without a separately assigned
ETW/PresentMon lane. The 250 MiB private-memory value is retained as an initial
comparison target, not enforcement code. Evidence also states whether the run
remains below the user's roughly 500 MiB unacceptable region; misses are
reported without speculative prototype tuning. There is no Ready AVP-002
assignment.
