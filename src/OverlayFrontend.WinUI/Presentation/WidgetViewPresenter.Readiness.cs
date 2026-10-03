using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private bool presentationInputEnabled = true;

    // Drawable content and input ownership have different lifetimes during a
    // switch. Disabling input must not tear down media or retained item pixels.
    internal void SetPresentationInputEnabled(bool enabled)
    {
        if (presentationInputEnabled && !enabled) RememberFocus();
        presentationInputEnabled = enabled;
        RefreshBrowsers();
        RefreshMediaPlayers();
        RefreshContextIndicators();
        IsHitTestVisible = enabled && presentationActive;
        if (!enabled)
        {
            directionalScroll = null;
            ResetSliderValues(); SetSliderAdjustment(null);
            DismissTransientControl();
        }
    }

    internal bool HasPreparedLayout => !disposed && !applying && presentationActive && frame is not null &&
        IsLoaded && ActualWidth > 0 && ActualHeight > 0;
}

/// <summary>
/// A transient native layout/render opportunity, not remote-data readiness or
/// evidence that pixels reached the display. Empty/loading/error SDK trees are valid.
/// </summary>
internal static class WidgetPresentationReadiness
{
    internal static async Task WaitAsync(WidgetViewPresenter presenter, CancellationToken cancellationToken)
    {
        if (!presenter.DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Wait for presentation on its native dispatcher.");
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Rendering(object? sender, object args)
        {
            try { if (presenter.HasPreparedLayout) completion.TrySetResult(); }
            catch (Exception error) { completion.TrySetException(error); }
        }
        CompositionTarget.Rendering += Rendering;
        try { await completion.Task.WaitAsync(cancellationToken); }
        finally { CompositionTarget.Rendering -= Rendering; }
    }
}
