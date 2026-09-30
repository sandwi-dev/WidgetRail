using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    partial void ConfigureFocusTrace(WidgetViewPresenter presenter, string widgetId)
    {
        if (switchDiagnostics is not null)
            presenter.FocusTrace = snapshot => switchDiagnostics.Write(
                $"focus widget={widgetId} selection={selectionVersion} interactive={interactive} switching={switching} {snapshot}");
    }
}
