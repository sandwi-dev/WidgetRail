using System.Diagnostics;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using PresentationSession = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Previews;

/// <summary>One widget's displayed preview demand and permission renewal. Pixels never enter managed code.</summary>
internal sealed class WindowPreviewRenderer : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly PresentationSession session;
    private readonly ulong hostWindow;
    private readonly CancellationTokenSource lifetime = new();
    private readonly WindowPreviewCaptureService captureService;
    private readonly NativePreviewEngine engine;
    private readonly List<WindowPreviewSurface> surfaces = [];
    private readonly HashSet<WindowPreviewSurface> deferredRoots = [];
    private readonly Task renewal;
    private WidgetPresentationFrame? displayed;
    private bool enabled = true;
    private bool pauseRenewal;
    private bool retired;
    private Task? disposal;
    internal event Action<Exception>? Failed;
    internal WindowPreviewRenderer(WindowPreviewCaptureService captureService, NativePreviewEngine engine, PresentationSession session, ulong hostWindow)
    { this.captureService = captureService; this.engine = engine; this.session = session; this.hostWindow = hostWindow; renewal = RenewAsync(); }

    // Must be the exact frame currently displayed by the trusted presenter.
    internal void Apply(WidgetPresentationFrame frame) { lock (gate) { if (!retired) displayed = frame; } }
    internal WindowPreviewSurface CreateSurface(string id, ImageFit fit)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(retired, this);
            if (surfaces.Count >= 64 || displayed?.WindowPreviews.GetValueOrDefault(id) is not { } target)
                throw new InvalidOperationException("The displayed window preview is unavailable or exceeds capture capacity.");
            var surface = new WindowPreviewSurface(engine, session, target, hostWindow, fit);
            surface.SetOwnerVisible(enabled);
            surfaces.Add(surface); return surface;
        }
    }
    internal void RemoveSurface(WindowPreviewSurface surface)
    {
        lock (gate) { surfaces.Remove(surface); deferredRoots.Add(surface); }
        try { surface.Dispose(); }
        finally { _ = RetireSurfaceAsync(surface); }
    }
    private async Task RetireSurfaceAsync(WindowPreviewSurface surface)
    {
        await surface.Completion.ConfigureAwait(false);
        if (!surface.HasNativeCallbackRoot) lock (gate) deferredRoots.Remove(surface);
    }
    internal void SetVisible(bool value)
    {
        WindowPreviewSurface[] revoke;
        lock (gate) { enabled = value; revoke = surfaces.ToArray(); }
        foreach (var surface in revoke) surface.SetOwnerVisible(value);
    }
    internal void PauseRenewalForValidation(bool pause) { lock (gate) pauseRenewal = pause; }
    internal void ResetDeviceForValidation() => System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(PreviewNative.ResetDevice(engine));
    private async Task RenewAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(400));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token).ConfigureAwait(false))
            {
                WidgetPresentationFrame? frame; WindowPreviewSurface[] active;
                lock (gate) { frame = enabled && !pauseRenewal ? displayed : null; active = surfaces.Where(surface => surface.HasDemand).ToArray(); }
                if (frame is null || active.Length == 0) continue;
                try
                {
                    // Start before the request: this native deadline is strictly
                    // no later than the session's two seconds after grant issuance.
                    var started = Stopwatch.GetTimestamp();
                    var grant = await session.RefreshWindowPreviewPermissionsAsync(frame, lifetime.Token).ConfigureAwait(false);
                    var deadline = checked(started + 2 * Stopwatch.Frequency);
                    foreach (var surface in active) surface.ApplyGrant(grant, deadline);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
                catch (Exception error)
                {
                    foreach (var surface in active) surface.Revoke();
                    Failed?.Invoke(error);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
    public ValueTask DisposeAsync()
    { lock (gate) return new(disposal ??= DisposeCoreAsync()); }
    private async Task DisposeCoreAsync()
    {
        WindowPreviewSurface[] owned;
        lock (gate) { if (retired) return; retired = true; owned = surfaces.Concat(deferredRoots).Distinct().ToArray(); surfaces.Clear(); deferredRoots.Clear(); }
        lifetime.Cancel();
        Exception? failure = null;
        foreach (var surface in owned)
        {
            try { surface.Revoke(); surface.Dispose(); }
            catch (Exception error) { failure ??= error; }
        }
        try { await Task.WhenAll(owned.Select(surface => surface.Completion).Append(renewal)); }
        finally
        {
            captureService.Retire(this, owned);
            lifetime.Dispose();
        }
        if (failure is not null) throw failure;
    }
}
