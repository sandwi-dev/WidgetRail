using Microsoft.UI.Xaml;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Previews;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Includes the active widget. Native controls are retained, not worker leases
    // or background artwork/capture demand. Eviction is deliberately bounded.
    private const int RetainedSurfaceLimit = 3;
    private readonly Dictionary<string, RetainedWidgetSurface> retainedSurfaces = new(StringComparer.Ordinal);
    private long surfaceUse;

    private sealed class RetainedWidgetSurface(BridgeWidgetDescriptor descriptor,
        WidgetViewPresenter presenter, WindowPreviewRenderer? previews)
    {
        internal BridgeWidgetDescriptor Descriptor { get; set; } = descriptor;
        internal WidgetViewPresenter Presenter { get; } = presenter;
        internal WindowPreviewRenderer? Previews { get; } = previews;
        internal long LastUse { get; set; }
    }

    private static bool SameSurfaceOwner(BridgeWidgetDescriptor first, BridgeWidgetDescriptor second) =>
        first.Id == second.Id && first.InstanceId == second.InstanceId &&
        first.RuntimeGeneration == second.RuntimeGeneration &&
        first.PresentationGeneration == second.PresentationGeneration &&
        first.PackageContentDigest == second.PackageContentDigest;

    private async Task SuspendWidgetSurfaceAsync()
    {
        if (surface is null) return;
        surface.SetAutomaticFocusEnabled(false);
        previewRenderer?.SetVisible(false);
        await surface.SetPresentationActiveAsync(false);
        surface.Visibility = Visibility.Collapsed;
    }

    private async Task SelectWidgetSurfaceAsync(string id)
    {
        var descriptor = owner!.Session.GetTarget(id).Descriptor;
        if (retainedSurfaces.TryGetValue(id, out var retained) && !SameSurfaceOwner(retained.Descriptor, descriptor))
        {
            await RetireWidgetSurfaceAsync(id);
            retained = null;
        }
        if (retained is null)
        {
            // Dispose before allocating so rapid tray browsing cannot exceed the
            // native surface budget even while asynchronous retirement drains.
            while (retainedSurfaces.Count >= RetainedSurfaceLimit)
            {
                var oldest = retainedSurfaces.MinBy(pair => pair.Value.LastUse);
                await RetireWidgetSurfaceAsync(oldest.Key);
            }
            var previews = CreatePreviewRenderer();
            var presenter = new WidgetViewPresenter
            {
                Session = owner.Session, MediaOwner = mediaOwner, WindowPreviews = previews,
                DispatchActionAsync = InvokeAsync, EnsureInteractionAsync = EnsureInteractionAsync,
                Visibility = Visibility.Collapsed,
            };
            presenter.Failed = error =>
            {
                if (ReferenceEquals(surface, presenter)) ReportFailure(error);
                else System.Diagnostics.Trace.WriteLine("Inactive WinUI widget retirement: " + error.GetType().Name);
            };
            presenter.SetAutomaticFocusEnabled(false);
            await presenter.SetPresentationActiveAsync(false);
            retained = new(descriptor, presenter, previews);
            retainedSurfaces.Add(id, retained);
            WidgetSurfaces.Children.Add(presenter);
        }
        retained.LastUse = ++surfaceUse;
        surface = retained.Presenter;
        previewRenderer = retained.Previews;
        activeWidget = id;
        publication = 0;
        surface.SetAutomaticFocusEnabled(false);
        surface.ApplyAppearance(Appearance, systemUi.AnimationsEnabled);
    }

    private async Task ResumeWidgetSurfaceAsync(WidgetPresentationFrame next)
    {
        if (surface is null) return;
        surface.Apply(next);
        if (activeWidget is { } id && retainedSurfaces.TryGetValue(id, out var retained))
            retained.Descriptor = next.Descriptor;
        surface.Visibility = Visibility.Visible;
        await surface.SetPresentationActiveAsync(true);
    }

    private async Task RetireWidgetSurfaceAsync(string id)
    {
        if (!retainedSurfaces.Remove(id, out var retained)) return;
        retained.Presenter.SetAutomaticFocusEnabled(false);
        retained.Previews?.SetVisible(false);
        WidgetSurfaces.Children.Remove(retained.Presenter);
        if (ReferenceEquals(surface, retained.Presenter)) { surface = null; previewRenderer = null; }
        try { await retained.Presenter.DisposeAsync(); }
        finally { if (retained.Previews is not null) await retained.Previews.DisposeAsync(); }
    }

    private async Task DisposeWidgetSurfacesAsync()
    {
        // Attempt every retirement even if one optional resource fails to drain.
        List<Exception>? errors = null;
        foreach (var id in retainedSurfaces.Keys.ToArray())
        {
            try { await RetireWidgetSurfaceAsync(id); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        if (errors is not null) throw new AggregateException(errors);
    }
}
