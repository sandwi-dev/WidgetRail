using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task PresentationLayersAsync(string directory)
    {
        var root = new Grid { Width = 800, Height = 620 };
        var page = new WidgetViewPresenter { Width = 760, Height = 580 };
        root.Children.Add(page);
        var fullscreen = new MediaFullscreenLayer(); fullscreen.Attach(root);
        host.Children.Add(root);
        Exception? failure = null; page.Failed = error => failure = error;
        var appearance = AppearanceSettings.Default with { Motion = MotionPreference.Full };
        page.ApplyAppearance(appearance, true);
        NativeTextScaleScope.Set(page, 1.5);
        page.SetSurfaceCornerRadius(24);
        var path = Path.Combine(directory, "pattern.mp4");
        var definition = new MediaPlayerDefinition(MediaPlayerSource.LocalFile(path), new(Muted: true, Loop: true, AutoPlay: true));
        var shell = new Border { Width = 200, Height = 100, Background = new SolidColorBrush(Microsoft.UI.Colors.Lime), Visibility = Visibility.Collapsed };
        PresentationLayers.Find(page)!.Add(shell, PresentationLayer.ShellOverlay);
        WebView2? browser = null;
        MediaPresentationStage? embedded = null;
        try
        {
            Render(false); page.UpdateLayout();
            var media = Find<MediaPlayerView>("Widget.layer.video", root)!;
            Load(media); await Wait(() => media.IsPlaying); await Task.Delay(200);
            var native = media.PageVideo;
            var parent = native.Parent; var unloads = 0; native.Unloaded += (_, _) => ++unloads;
            var plane = root.Children.OfType<MediaPresentationStage>().Single();
            Check(Canvas.GetZIndex(plane) == (int)PresentationLayer.ContentMedia, "Inline native video uses the content-media band");

            Render(true); await Wait(() => root.Children.OfType<PresentationPortal>().Any());
            var portal = root.Children.OfType<PresentationPortal>().Single();
            var layer = (WidgetModalLayer)portal.Content;
            if (layer.OpeningMotion is { } opening) await opening;
            await Task.Delay(80);
            Check(Canvas.GetZIndex(portal) > Canvas.GetZIndex(plane), "Dialog scrim and panel paint above background resident video");
            Check(unloads == 0 && ReferenceEquals(native.Parent, parent) && media.IsPlaying, "Opening a dialog does not detach or restart background playback");
            Check(Find<Button>("Widget.layer.cancel", root)!.Focus(FocusState.Keyboard), "Portaled dialog accepts native focus");
            Check(NativeTextScaleScope.Find(Find<Button>("Widget.layer.cancel", root)!)?.Value == 1.5,
                "Portaled controls inherit the owning widget's text scale");
            Check(WidgetViewPresenter.ViewportFor(Find<Button>("Widget.layer.cancel", root)!) == (page.ActualWidth, page.ActualHeight),
                "Portaled content uses the widget viewport for responsive policy");
            Check(Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual((UIElement)VisualTreeHelper.GetChild(portal, 0)).Clip is Microsoft.UI.Composition.CompositionGeometricClip,
                "Portal carries the widget's rounded surface clip in addition to viewport clipping");
            Check(AutomationProperties.GetAutomationId(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject) == "Widget.layer.cancel", "Dialog focus remains owned by its presenter");
            Check(!Find<Button>("Widget.layer.open", root)!.Focus(FocusState.Keyboard) ||
                AutomationProperties.GetAutomationId(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject) != "Widget.layer.open", "Inactive parent scope rejects focus while dialog is open");
            var modalMedia = Find<MediaPlayerView>("Widget.layer.modal-video", root)!;
            Load(modalMedia); await Wait(() => modalMedia.IsPlaying);
            var dialogPlane = root.Children.OfType<MediaPresentationStage>().Single(stage => !ReferenceEquals(stage, plane));
            Check(Canvas.GetZIndex(dialogPlane) > Canvas.GetZIndex(portal), "Video declared inside the dialog belongs above its dialog panel");
            Check(Canvas.GetZIndex(shell) > Canvas.GetZIndex(dialogPlane), "Shell overlays remain above dialog-owned media");
            shell.Visibility = Visibility.Visible;
            await Task.Delay(200); shell.Visibility = Visibility.Collapsed;

            // Match the embedded-media renderer's resident WebView contract and
            // exercise the actual WinUI browser composition surface locally.
            browser = new WebView2 { IsTabStop = false };
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(directory, "layer-webview"), null);
            var slot = new Grid { Width = 240, Height = 135 };
            layer.Children.Add(slot);
            embedded = fullscreen.CreateMediaStage(16d / 9);
            embedded.SurfaceHost.Children.Add(browser); embedded.SetSlot(slot);
            await browser.EnsureCoreWebView2Async(environment);
            browser.NavigateToString("<html><body style='margin:0;background:#00aacc;color:white'>Resident embedded document</body></html>");
            await Task.Delay(250);
            Check(Canvas.GetZIndex(embedded) == (int)PresentationLayer.WidgetDialogMedia, "Embedded WebView derives its layer from the same dialog scope");
            Check(embedded.VideoBounds.Width > 0 && embedded.Visibility == Visibility.Visible, "Embedded document is positioned in the dialog viewport");
            var webParent = browser.Parent;
            embedded.EnterFullscreen();
            Check(Canvas.GetZIndex(embedded) == (int)PresentationLayer.FullscreenMedia, "Fullscreen promotes resident media through the central policy");
            Check(Canvas.GetZIndex(fullscreen) > Canvas.GetZIndex(embedded) && Canvas.GetZIndex(shell) > Canvas.GetZIndex(fullscreen), "Fullscreen controls and shell overlays have ordered bands above media");
            embedded.SetSlot(slot);
            Check(ReferenceEquals(browser.Parent, webParent) && Canvas.GetZIndex(embedded) == (int)PresentationLayer.WidgetDialogMedia,
                "Returning embedded media restores its dialog band without reattachment");
            embedded.Dispose(); embedded = null; browser.Close(); browser = null; layer.Children.Remove(slot);

            page.Width = 660; page.UpdateLayout(); await Task.Delay(100);
            Check(Math.Abs(portal.ActualWidth - page.ActualWidth) < 1 && portal.Clip is RectangleGeometry,
                "Portal follows widget resizing and keeps a widget-local clip");
            page.Visibility = Visibility.Collapsed; await Task.Delay(50);
            Check(portal.Visibility == Visibility.Collapsed, "Hiding the owner hides the external paint portal");
            page.Visibility = Visibility.Visible; await Task.Delay(80);
            Check(portal.Visibility == Visibility.Visible, "Showing the owner restores the same portal");

            // Stop testing the dialog video before closing; closing must also
            // dispose it while retaining the underlying native decoder.
            Render(false);
            Check(!page.HasClosingModal && !root.Children.OfType<PresentationPortal>().Any(),
                "A native-bearing dialog retires safely without unsynchronized native opacity animation");
            Check(MediaPlayerView.ResidentPlayers == 1 && media.IsPlaying && unloads == 0, "Dialog teardown keeps only the background decoder and playback");
            Render(true, false); await Task.Delay(350); Render(false);
            Check(page.HasClosingModal && root.Children.OfType<PresentationPortal>().Count() == 1,
                "Closing animation retains the dialog in its ordered root layer");
            await Wait(() => !page.HasClosingModal);
            Check(!root.Children.OfType<PresentationPortal>().Any(), "Closing retires the portal and its input registrations");
            Render(true); await Task.Delay(100);
            await page.SetPresentationActiveAsync(false);
            await page.DisposeAsync(); await Task.Delay(50);
            Check(!root.Children.OfType<PresentationPortal>().Any() && !root.Children.OfType<MediaPresentationStage>().Any(),
                "Owner retirement leaves no orphan dialogs or native planes");
            if (failure is not null) throw failure;
            await File.WriteAllTextAsync(Path.Combine(directory, "layers.json"), JsonSerializer.Serialize(new { passed = true, checks }));
        }
        catch (Exception error)
        { await File.WriteAllTextAsync(Path.Combine(directory, "layers.json"), JsonSerializer.Serialize(new { passed = false, checks, error = error.ToString() })); throw; }
        finally { embedded?.Dispose(); browser?.Close(); await page.DisposeAsync(); host.Children.Remove(root); }

        void Load(MediaPlayerView view) => view.Apply(_ => Task.FromResult(new HostMediaPlayerSource(definition, new Uri(path).AbsoluteUri)), definition, () => true);
        void Render(bool modal, bool containsMedia = true)
        {
            var background = new ViewNode { Id = "layer.main", Kind = ViewNodeKind.Stack, InputScopeId = "main", Children = (ViewNode[])[
                new() { Id = "layer.video", Kind = ViewNodeKind.MediaPlayer, MediaPlayer = definition, AccessibilityLabel = "Background video" },
                new() { Id = "layer.open", Kind = ViewNodeKind.Button, Text = "Open dialog", ActionId = "open" }] };
            var dialog = new ViewNode { Id = "layer.dialog", Kind = ViewNodeKind.Stack, InputScopeId = "dialog", Children = (ViewNode[])[
                new() { Id = "layer.cancel", Kind = ViewNodeKind.Button, Text = "Cancel", ActionId = "cancel" },
                new() { Id = "layer.modal-video", Kind = ViewNodeKind.MediaPlayer, MediaPlayer = definition, AccessibilityLabel = "Dialog video" }] };
            if (!containsMedia) dialog = dialog with { Children = dialog.Children.Take(1).ToArray() };
            var tree = modal ? new ViewNode { Id = "layer.modal", Kind = ViewNodeKind.ModalLayer, Children = (ViewNode[])[background, dialog] } : background;
            var styles = new Dictionary<string, BridgeNodeRenderStyles>
            {
                ["layer.main"] = Compute("stack { height: 540px; background: #203040; padding: 16px; }", "layer.main", "stack"),
                ["layer.video"] = Compute("media-player { height: 400px; }", "layer.video", "media-player"),
                ["layer.dialog"] = Compute("stack { width: 500px; height: 400px; background: #602030; padding: 24px; }", "layer.dialog", "stack"),
                ["layer.modal-video"] = Compute("media-player { height: 240px; }", "layer.modal-video", "media-player"),
            };
            page.Apply(CreateFrame(tree, styles, modal ? "dialog" : "main")); page.UpdateLayout();
        }
    }
}
