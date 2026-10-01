using WidgetRail.OverlayFrontend.WinUI.Development;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal Func<CancellationToken, Task>? PrepareDeveloperInspectorAsync { get; set; }
    internal string LastDeveloperNavigation { get; private set; } = "Navigate in the widget to inspect focus. F12 reopens the inspector.";

    internal DeveloperInspection? CaptureDeveloperInspection() =>
        !retired && visible && surface is { IsLoaded: true } current ? current.CaptureDeveloperInspection() : null;

    private void ConfigureDevelopmentInspection(WidgetViewPresenter presenter)
    {
        if (options.Development?.Inspector == true)
            presenter.NavigationObserved = value => LastDeveloperNavigation = value;
    }

    private async Task PublishDevelopmentReadyAsync()
    {
        if (options.Development is not { ProbeOnly: false } development) return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        await WaitReadyAsync();
        if (development.Inspector)
        {
            if (PrepareDeveloperInspectorAsync is null) throw new InvalidOperationException("Development inspector was not configured.");
            await PrepareDeveloperInspectorAsync(deadline.Token);
            await WaitReadyAsync();
        }
        development.PublishReady(inspectorReady: development.Inspector);

        async Task WaitReadyAsync()
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (retired || RecoveryVisible || activeWidget != development.WidgetId || requestedWidget != development.WidgetId)
                    throw new InvalidDataException("The requested development widget has not reached an admitted native presentation.");
                if (surface is { HasPreparedLayout: true, CurrentBinding: { IsCurrent: true } binding } &&
                    binding.Frame.Descriptor.InstanceId == development.InstanceId) return;
                // Opening a same-process inspector can change widget lifecycle.
                // Await the resulting current declaration instead of rejecting a
                // valid generation during that ordinary publication boundary.
                await Task.Delay(16, deadline.Token);
            }
        }
    }
}
