using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private bool presentationInputEnabled = true;
    internal bool FocusEffectsActive => !disposed && presentationInputEnabled && presentationActive;

    // Drawable content and input ownership have different lifetimes during a
    // switch. Disabling input must not tear down media or retained item pixels.
    internal void SetPresentationInputEnabled(bool enabled)
    {
        if (!enabled && presentationInputEnabled) preparingForegroundReturn = null;
        if (presentationInputEnabled && !enabled) RememberFocus();
        presentationInputEnabled = enabled;
        foreach (var adapter in nativeStyles.Values) adapter.FocusDecoration?.RefreshSettledVisibility();
        RefreshBrowsers();
        RefreshMediaPlayers();
        RefreshContextIndicators();
        IsHitTestVisible = enabled && presentationActive;
        if (!enabled)
        {
            foreach (var binding in bindings.Values)
                if (binding.Children is WidgetActionPanel face) face.EndLighting();
            CancelHeldCollectionSelection();
            ClearFocusPreview(); ClearDeferredPreviews();
            CancelPassiveScrollFocus();
            scrollGesture = null;
            directionalScroll = null;
            ResetSliderValues(); SetSliderAdjustment(null);
            DismissTransientControl();
        }
        else ResumeSuspendedCollectionReturn();
    }

    internal sealed record ForegroundReturn(WidgetElementIdentity Surface, WidgetElementIdentity Target, string Process, Windows.Foundation.Rect Bounds,
        bool HadArtwork);

    internal ForegroundReturn? CaptureForegroundReturn()
    {
        if (disposed || XamlRoot is null || (FocusedBinding() ?? RememberedBinding()) is not { } binding ||
            declarations.GetValueOrDefault(binding.Identity.Id)?.Node.ForegroundBackgroundProcessKey is not { } process)
            return null;
        // Resolve the declared ancestor once. The rendering callback then looks up
        // only these two identities even on a large widget tree.
        for (var parent = declarations[binding.Identity.Id].ParentId; parent is not null; parent = declarations[parent].ParentId)
        {
            if (declarations[parent].Node.Kind != WidgetRail.WidgetProtocol.ViewNodeKind.BackgroundSurface) continue;
            if (declarations[parent].Node.ForegroundBackgroundProcessKey != process ||
                bindings[parent].Element is not WidgetPresentationSurface background) return null;
            var bounds = ReturnBounds(binding);
            return bounds.Width > 0 && bounds.Height > 0 ? new(bindings[parent].Identity, binding.Identity, process, bounds,
                background.ArtworkSource is not null) : null;
        }
        return null;
    }

    private ForegroundReturn? preparingForegroundReturn;
    internal void PrepareForegroundReturn(ForegroundReturn? previous) => preparingForegroundReturn = previous;

    // The hidden preparer has no native focus. Project its own current card's
    // contributions so artwork and layout can resolve before the card takes input.
    private Binding? PreparedForegroundSource()
    {
        if (preparingForegroundReturn is not { } prior || activeScope != prior.Target.Scope ||
            !bindings.TryGetValue(prior.Surface.Id, out var surface) || surface.Identity != prior.Surface ||
            declarations[surface.Identity.Id].Node.ForegroundBackgroundProcessKey != prior.Process ||
            !bindings.TryGetValue(prior.Target.Id, out var binding) || binding.Identity != prior.Target ||
            declarations[binding.Identity.Id].Node.ForegroundBackgroundProcessKey != prior.Process) return null;
        return binding;
    }

    private Windows.Foundation.Rect ReturnBounds(Binding binding) => binding.LayoutElement.TransformToVisual(this)
        .TransformBounds(new(0, 0, binding.LayoutElement.ActualWidth, binding.LayoutElement.ActualHeight));

    internal bool HasForegroundReturn(ForegroundReturn prior)
    {
        // A changed route or game gets its normal first layout immediately.
        if (activeScope != prior.Target.Scope) return true;
        if (!bindings.TryGetValue(prior.Surface.Id, out var game) || game.Identity != prior.Surface ||
            game.Element is not WidgetPresentationSurface bg) return true;
        if (declarations[game.Identity.Id].Node.ForegroundBackgroundProcessKey is not { } current) return false;
        if (current != prior.Process) return true;
        if (!bindings.TryGetValue(prior.Target.Id, out var binding) || binding.Identity != prior.Target ||
            binding.Element.XamlRoot is null) return false;
        var bounds = ReturnBounds(binding);
        return Math.Abs(bounds.Y - prior.Bounds.Y) < 1 && Math.Abs(bounds.X - prior.Bounds.X) < 1 &&
            Math.Abs(bounds.Height - prior.Bounds.Height) < 1 && Math.Abs(bounds.Width - prior.Bounds.Width) < 1 &&
            (bg.IsForegroundRevealed || !prior.HadArtwork || bg.ArtworkSource is not null);
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
    internal static async Task WaitAsync(WidgetViewPresenter presenter, CancellationToken cancellationToken,
        WidgetViewPresenter.ForegroundReturn? foregroundReturn = null)
    {
        if (!presenter.DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Wait for presentation on its native dispatcher.");
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        long waitUntil = Environment.TickCount64 + 350;
        void Rendering(object? sender, object args)
        {
            try { if (presenter.HasPreparedLayout && (foregroundReturn is null || Environment.TickCount64 >= waitUntil ||
                presenter.HasForegroundReturn(foregroundReturn))) completion.TrySetResult(); }
            catch (Exception error) { completion.TrySetException(error); }
        }
        CompositionTarget.Rendering += Rendering;
        try { await completion.Task.WaitAsync(cancellationToken); }
        finally { CompositionTarget.Rendering -= Rendering; }
    }
}
