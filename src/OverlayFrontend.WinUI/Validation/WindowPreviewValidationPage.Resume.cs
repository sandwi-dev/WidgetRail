using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Previews;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WindowPreviewValidationPage
{
    private async Task ValidatePresenterResumeAsync()
    {
        renderer!.Failed += error => serverFailure = error;
        presenterRoot = new() { Id = "root", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[
            new() { Id = "resumed.preview", Kind = ViewNodeKind.WindowPreview, WindowId = "owned.preview",
                PreviewAspectRatio = 1.6, ImageFit = ImageFit.Cover, AccessibilityLabel = "Resumed preview" }] };
        widgetPresenter = new() { Session = session, WindowPreviews = renderer,
            Width = 300, Height = 170, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        canvas.Children.Add(widgetPresenter);
        await widgetPresenter.SetPresentationActiveAsync(false);
        fixtureFrame = await session!.EstablishPresentationAsync(session.GetTarget(Descriptor.Id), WidgetLifecycleState.Interactive);
        widgetPresenter.Apply(fixtureFrame);
        Check(FindPreviewSlot(widgetPresenter)?.Content is null, "inactive preparation does not start a preview surface");
        await widgetPresenter.SetPresentationActiveAsync(true);
        Check(FindPreviewSlot(widgetPresenter)?.Content is WindowPreviewSurface, "cold activation binds preview after publishing its frame");
        preview = (WindowPreviewSurface)FindPreviewSlot(widgetPresenter)!.Content;
        await Until(() => preview.InspectNative().State == 2 && preview.InspectNative().Frames > 0);
        Check(true, "cold presenter receives actual native capture frames");
        widgetPresenter.Apply(fixtureFrame);
        Check(ReferenceEquals(preview, FindPreviewSlot(widgetPresenter)!.Content), "compatible publication retains the active preview surface");
        await ValidatePreviewCapacityRecoveryAsync();

        var previous = preview;
        await widgetPresenter.SetPresentationActiveAsync(false);
        await previous.Completion;
        var oldSource = source!;
        source = new Window { Title = "WidgetRail replacement owned capture source",
            Content = new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.Green) } };
        source.AppWindow.Resize(new(320, 200));
        source.Activate();
        App.Window.Activate();
        targets["owned.preview"] = NativeIdentity(WinRT.Interop.WindowNative.GetWindowHandle(source)).ToTarget("owned.preview");
        oldSource.Close();
        fixtureFrame = await session.EstablishPresentationAsync(session.GetTarget(Descriptor.Id), WidgetLifecycleState.Interactive);
        widgetPresenter.Apply(fixtureFrame);
        await widgetPresenter.SetPresentationActiveAsync(true);
        Check(FindPreviewSlot(widgetPresenter)?.Content is WindowPreviewSurface, "reactivation binds a replacement target under the same logical window ID");
        preview = (WindowPreviewSurface)FindPreviewSlot(widgetPresenter)!.Content;
        Check(!ReferenceEquals(preview, previous), "replacement target does not reuse retired capture identity");
        await Until(() => preview.InspectNative().State == 2 && preview.InspectNative().Frames > 0);
        Check(true, "replacement target receives capture grants and native frames");
        await widgetPresenter.SetPresentationActiveAsync(false);
        await preview.Completion;
        // InspectNative is the last published snapshot, not a post-disposal query.
        // The callback root is released only after successful native slot removal.
        Check(!preview.HasNativeCallbackRoot && !preview.HasDemand && !preview.IsBindingTimerRunning &&
            FindPreviewSlot(widgetPresenter)?.Content is null, "suspension retires native capture");
        Update("PresenterResume");
    }

    private async Task ValidatePreviewCapacityRecoveryAsync()
    {
        var fillers = new List<WindowPreviewSurface>();
        var waiting = new WidgetWindowPreview();
        try
        {
            for (var index = 0; index < 63; ++index) fillers.Add(renderer!.CreateSurface("owned.preview", ImageFit.Cover));
            var declaration = presenterRoot!.Children[0];
            waiting.Configure(renderer, fixtureFrame!, declaration);
            Check(waiting.Content is null, "capture capacity leaves an unbound native placeholder");
            renderer!.RemoveSurface(fillers[^1]);
            await fillers[^1].Completion;
            fillers.RemoveAt(fillers.Count - 1);
            waiting.Configure(renderer, fixtureFrame!, declaration);
            Check(waiting.Content is WindowPreviewSurface, "compatible publication retries a temporary capture-capacity rejection");
        }
        finally
        {
            waiting.Dispose();
            foreach (var item in fillers) renderer!.RemoveSurface(item);
            await Task.WhenAll(fillers.Select(item => item.Completion));
        }
    }
}
