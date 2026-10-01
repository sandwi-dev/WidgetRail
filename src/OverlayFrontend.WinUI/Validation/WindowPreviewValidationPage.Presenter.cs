using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Previews;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WindowPreviewValidationPage
{
    private WidgetViewPresenter? widgetPresenter;
    private ViewNode? presenterRoot;

    private async Task PresenterCommandAsync(string command)
    {
        if (command == "Presenter")
        {
            canvas.Children.Remove(preview!);
            renderer!.RemoveSurface(preview!);
            await preview!.Completion;
            widgetPresenter = new() { Session = session, WindowPreviews = renderer,
                Width = 300, Height = 170, Margin = new(40, 30, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            canvas.Children.Insert(0, widgetPresenter);
        }
        var previous = FindPreviewSlot(widgetPresenter!);
        var previousCapture = previous?.Content as WindowPreviewSurface;
        var target = command == "PresenterReplace" ? "wrong.pid" : "owned.preview";
        var remove = command == "PresenterRemove";
        presenterRoot = new() { Id = "root", Kind = ViewNodeKind.Stack, Children = remove ? [] :
            (ViewNode[])[new() { Id = "presented.preview", Kind = ViewNodeKind.WindowPreview, WindowId = target,
                PreviewAspectRatio = 1.6, ImageFit = ImageFit.Cover, AccessibilityLabel = "Owned presenter preview" }] };
        var next = await session!.EstablishPresentationAsync(session.GetTarget(Descriptor.Id), WidgetLifecycleState.Interactive);
        fixtureFrame = next;
        widgetPresenter!.Apply(next);
        if (remove)
        {
            await previousCapture!.Completion;
            Check(FindPreviewSlot(widgetPresenter) is null, "removed preview retires its native layout slot");
            await Until(() => invalid.All(item => item.InspectNative().ActiveCount == 0));
            Check(true, "removed preview releases capture before session disposal");
            return;
        }
        await Until(() => FindPreviewSlot(widgetPresenter)?.Content is WindowPreviewSurface);
        var current = FindPreviewSlot(widgetPresenter)!;
        preview = (WindowPreviewSurface)current.Content;
        if (command == "PresenterUpdate")
            Check(ReferenceEquals(previous, current) && ReferenceEquals(previousCapture, preview),
                "compatible publication preserves layout and native capture identities");
        if (command == "PresenterReplace")
        {
            Check(ReferenceEquals(previous, current) && !ReferenceEquals(previousCapture, preview),
                "target replacement retains layout slot but retires old capture identity");
            await previousCapture!.Completion;
            await Until(() => preview.InspectNative().Error < 0 && preview.InspectNative().ActiveCount == 0);
        }
        else
        {
            await Until(() => preview.InspectNative().State == 2 && preview.InspectNative().Frames > 0);
            Check(current.ActualWidth > 0 && current.ActualHeight > 0 && !current.IsTabStop && !current.IsHitTestVisible,
                "preview declarations receive native geometry without consuming controller or pointer input");
        }
    }

    private static WidgetWindowPreview? FindPreviewSlot(DependencyObject parent)
    {
        if (parent is WidgetWindowPreview preview) return preview;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); ++index)
            if (FindPreviewSlot(VisualTreeHelper.GetChild(parent, index)) is { } child) return child;
        return null;
    }
}
