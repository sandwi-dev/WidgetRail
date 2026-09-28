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
                if (widget == activeWidget && code is "media-session-capacity" or "media-document-admission-failed")
                    ReportFailure(new InvalidOperationException(code == "media-session-capacity"
                        ? "The maximum of four media sessions is already open."
                        : "The widget's media document could not be admitted."));
            },
        };
        ReconcileMediaHostState();
    }

    private void ReconcileMediaHostState() => mediaOwner?.SetHostState(activeWidget,
        !retired && visible && !switching, !retired && visible && !switching && interactive && foreground);
}
