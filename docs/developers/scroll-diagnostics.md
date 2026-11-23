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

## Opt-in production pipeline trace

The `WidgetRail-PresentationPipeline` EventSource reports numeric range/artwork
timings at Informational level. Verbose level additionally reports controller-owned
native scroll requests and `ViewChanged` observations. These events are disabled
unless a diagnostics collector enables them. They contain a process-local gesture
number and geometry, never widget titles, message contents, paths or account IDs.

Enable provider `WidgetRail-PresentationPipeline:0xffffffffffffffff:5` in a bounded
EventPipe recording of the actual frontend. Use
`scripts/Summarize-PresentationPipelineTrace.ps1` to read the saved `.nettrace`
with the collector's TraceEvent assembly. The report retains existing stage
summaries and adds per-gesture request/update counts, observed coordinate ranges
and raw scroll events. Event 2 has phase 1 (requested destination after ChangeView)
or 2 (native viewport observation); flags are accepted=1, animated=2,
intermediate=4 and native-wheel ownership=8. The requested coordinates in phase 2
equal the observed position; they are not another scroll command.

First establish that the intended Library actually moved in the captured interval.
A warm viewport may scroll without fetching data, so zero range/artwork events
does not prove an idle or healthy UI. Conversely, scroll events do not establish
which widget was visible; record workload/build context separately. `ViewChanged`
timestamps and offset spans are not displayed frames, GPU timings or measured FPS.
Avoid simultaneous UIA polling, screenshots and builds during timing collection.
