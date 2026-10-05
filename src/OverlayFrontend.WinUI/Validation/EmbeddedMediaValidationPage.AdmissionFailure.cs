using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class EmbeddedMediaValidationPage
{
    private async Task CheckFailedSnapshotAdmissionAsync()
    {
        ownerChecks = true;
        parkingLayer = new();
        ((StackPanel)Content).Children.Add(parkingLayer);
        mediaOwner = new(session!, parkingLayer) { Diagnostic = (_, code) => diagnostics.Add(code) };
        mediaOwner.SetHostState(Descriptor.Id, false, false);
        ownerPresenter = new() { Session = session, MediaOwner = mediaOwner };
        viewport.Children.Add(ownerPresenter);
        var frame = await SnapshotAsync();
        ownerPresenter.Apply(frame);
        await Until(() => ownerPresenter.IsLoaded && OwnerElement<WidgetMediaViewport>("Widget.viewport-0").ActualWidth > 0);
        Check(resolveCount == 0, "inactive fixture retains a real media viewport without admitting a browser");

        rejectRefresh = true;
        await ReplyAsync(0, "widget-invalidated", new { widgetId = Descriptor.Id, revision = 1 });
        await Until(() => session!.GetState(Descriptor.Id)?.Failure is not null);
        Check(session!.GetState(Descriptor.Id)?.LastGood?.Authority == frame.Authority,
            "rejected refresh preserves the prior displayed frame for failure presentation");
        mediaOwner.SetHostState(Descriptor.Id, true, false);
        for (var index = 0; index < 5; ++index) { mediaOwner.Refresh(); await Task.Delay(25); }
        Check(resolveCount == 0 && mediaOwner.ResidentCount == 0 && mediaOwner.BrowserCreationCount == 0 && diagnostics.Count == 0,
            "failed snapshot cannot re-admit retained media or spin despite repeated native refreshes");

        rejectRefresh = false;
        ownerPresenter.Apply(await SnapshotAsync());
        await Until(() => mediaOwner.IsReady(Descriptor.Id));
        Check(session.GetState(Descriptor.Id)?.Failure is null && resolveCount == 1 && mediaOwner.BrowserCreationCount == 1,
            "fresh valid snapshot recovers with exactly one media admission and browser");
        mediaOwner.SetHostState(Descriptor.Id, false, false);
    }
}
