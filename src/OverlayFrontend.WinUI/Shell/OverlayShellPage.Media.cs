using WidgetRail.OverlayFrontend.WinUI.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private EmbeddedMediaOwner? mediaOwner;

    private void InitializeMediaOwner()
    {
        if (owner is null || mediaOwner is not null || retired) return;
        mediaOwner = new(owner.Session, MediaParking)
        {
            Diagnostic = (widget, code) =>
            {
                System.Diagnostics.Trace.WriteLine("WinUI embedded media: " + code);
            },
            FailureChanged = widget =>
            {
                if (widget == activeWidget && owner?.Session.GetState(widget) is { Failure: null, LastGood: { } frame })
                    ShowPresentationStatus(frame.Descriptor.Name);
            },
        };
        ReconcileMediaHostState();
    }

    private void ReconcileMediaHostState() => mediaOwner?.SetHostState(activeWidget,
        !retired && visible && !switching, !retired && visible && !switching && interactive && foreground);

    private void ShowPresentationStatus(string widgetName)
    {
        if (retired) return;
        var code = activeWidget is { } id ? mediaOwner?.GetFailure(id) : null;
        Status.Text = code switch
        {
            null => widgetName,
            "media-session-capacity" => "Four media sessions are already open. Close one before opening another.",
            "media-document-admission-failed" => "The widget's media could not be loaded. Retry to restart this widget.",
            _ => "The widget's media stopped unexpectedly. Retry to restart this widget.",
        };
        Retry.Visibility = code is not null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }
}
