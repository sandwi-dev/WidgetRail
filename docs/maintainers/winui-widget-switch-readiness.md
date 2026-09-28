# WinUI widget switching

The shell distinguishes the requested widget, the native presenter being prepared,
and the committed widget. A request immediately revokes outgoing action/focus
admission. It leaves the committed native tree, extent, background and live regions
visible until a replacement can be published. The bridge moves the outgoing widget
to Visible during that interval, and to Background after commit.

Incoming presenters stay parented in a full-size native Grid. Each presenter has
its final widget extent and alignment; its opacity stays zero during preparation.
The committed background has its own extent. Switching therefore does not resize
the old page or unload/reparent the new page at commit. Content, selected identity,
surface hints, backdrop policy and final geometry change in one dispatcher call.

Readiness means an accepted SDK frame has native layout and a transient
CompositionTarget.Rendering opportunity. This accepts all valid SDK trees,
including an empty root or a loading/error view. It does not classify leaf types,
wait for remote artwork/media, or run a permanent frame callback. This milestone
is **not evidence of pixels reaching the display**; native raster/screen checks
remain separate verification.

New selection and hide cancel pending preparation. Obsolete requests cannot
publish. The existing transition semaphore still serializes bridge/lifetime
mutation, while cancellation removes obsolete waiters. The surface cache remains
bounded to three native trees, including displayed and staged content. Same-ID
incarnation changes preserve the outgoing tree until its replacement commits.
Failures retain the committed page and expose the shell's recovery control.

The native tray's GettingFocus/GotFocus pair can straddle an asynchronous switch.
The shell records the selection epoch at GettingFocus so completion of an older
focus entry cannot act as a fresh selection request. Explicit latest tray entry
continues through the existing FocusTray route.

## Diagnostics and checks

`SwitchDiagnosticsPath` is an optional absolute output path in shell options.
It uses a bounded asynchronous writer: requested, preparing, layout-ready,
committed, superseded, hidden-or-retired, timed-out and failed milestones have
monotonic timestamps and elapsed times. Disabled diagnostics add no disk work.
No widget content or credentials are recorded.

Build `tests/WidgetSwitchFixture`, then use `scripts/New-WinUiSwitchFixture.ps1`
with an existing isolated Bridge installation and a fresh output directory. The
script copies the runtime into the new fixture and creates isolated catalog and
settings roots. It does not modify the user's installed overlay or settings.
The slow fixture stays below the worker's two-second request deadline.

Launch the Debug WinUI frontend with `--shell-no-controller`, the generated
`--shell-config=<absolute shell.json>` and
`--validate-widget-switches=<absolute result.json>`. The native probe exercises
the production session/worker path, delayed cold/cached/recreated switches,
rapid reversal, bounded retention, valid empty/loading trees, worker failure,
hide/reopen, dynamic updates and same-ID incarnation replacement. It compares
outgoing raster pixels during preparation and observes native render callbacks;
these checks complement, rather than replace, a screen recording of presentation.
