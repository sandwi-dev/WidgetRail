# Scrolling diagnostics

The production frontend uses WinUI layout and virtualized collections. The retired
native renderer's `--scroll-diagnostics` and `--scroll-diagnostics-quiet` flags,
ring-buffer format and `Analyze-ScrollDiagnostics.ps1` script are no longer
supported. Do not use old native-renderer captures to characterize this frontend.

## Investigate a scrolling problem

Record the app version, widget/package version, active theme, interface/text scale,
monitor DPI and the exact controller sequence. Distinguish a missing provider
page from a realized item that fails to receive focus, and from a reveal request
that fails to scroll. Include whether it happens after an update, modal, widget
reload or overlay reopen.

Use [Diagnostics and recovery](../maintainers/diagnostics-and-recovery.md) for
the supported diagnostics entry points. For author-side checks, validate the
snapshot and representative collection item roots using the
[WinUI authoring preflight](../reference/winui-authoring.md). Inspect query identity,
range/discovery completion and stable item IDs before changing viewport behavior.

Maintainer investigations can use the frontend's Debug validation routes and
opt-in layout/focus diagnostics; see the
[frontend README](../../src/OverlayFrontend.WinUI/README.md). These are diagnostic
build facilities, not installer command-line promises. A meaningful regression
check verifies native scroll offsets and focus after layout has completed.

Keep captures bounded and identify the exact workload and build. Process memory
or a managed fixture alone does not measure scrolling smoothness. See
[performance guidance](../maintainers/performance.md).
