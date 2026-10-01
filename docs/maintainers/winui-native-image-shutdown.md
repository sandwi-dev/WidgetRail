# Native icon preparation shutdown

Package icon masks use native SVG decode, `RenderTargetBitmap`, readback and a
composition image surface. A cancelled consumer wait is not proof that those
native operations have stopped. Closing XAML while a render worker still needs
the UI dispatcher can deadlock `AsyncImageFactory::Shutdown` against
`RenderTargetBitmapWaitExecutor`.

Before closing the final window on a dispatcher, after page/resource owners have
been retired, the window owner must await:

```csharp
await NativePackageIconTintCache.ShutdownAsync(DispatcherQueue);
```

This is required even when a page's cleanup reports an error. It must execute
before `Close()` and before dispatcher shutdown; it cannot repair shutdown after
XAML has already stopped dispatching. It never blocks the UI thread.

The boundary rejects new cache acquisition on that dispatcher, including late
requests for another XamlRoot. Every preparation has tracked completion before
native work starts, independently of whether a consumer still awaits it or its
cache entry remains retained. Shutdown cancels queued work and observes all these
completions. Started native render/decode/readback operations are awaited without
a cancellation wrapper; cancellation is checked between stages instead. Native
completion callbacks unwind through a dispatcher turn before staging is removed.
Only then are cached surfaces, staging content and synchronization owners released.
Repeated shutdown calls share one completion.

Owner unload starts the same retirement as a fallback, but final window shutdown
must still await the dispatcher-wide boundary. Per-icon decode failures retain
the existing semantic fallback; they do not skip draining native work.

The focused fixture uses `--validate-package-icons --validate-icon-shutdown` and
`--icon-shutdown-stage=render`, `pixels`, or `surface`. It cancels all consumer
waits with multiple preparations queued, starts shutdown during an observed
pending native operation, and checks native/preparation counts, UI dispatch,
staging/resource release, late admission rejection and idempotence. Its result
is `WidgetRail/WinUI/diagnostics/package-icon-shutdown-result.json` under local
application data. A driver must verify that the owned process exits normally
after these checks; a passing file alone is not shutdown proof.

The integrated fixture passes all three stages both visible and hidden: 42
checks total, with normal process exit in all six runs. The existing 36 native
package-icon checks also pass. Evidence is under `artifacts/winui-shell/` in
`icon-shutdown-integrated-01`, `icon-shutdown-hidden-02`, and
`icons-after-drain-01`. The original hidden-start/reopen shutdown deadlock has
been reproduced and its native/managed stacks retained in
`hidden-startup-native-01`; the corrected sequence exits normally in
`hidden-startup-integrated-02`.
