using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    private readonly ScrollViewer imageViewport = new()
    {
        ZoomMode = ZoomMode.Enabled, MinZoomFactor = 1, MaxZoomFactor = 8,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Enabled,
        IsTabStop = true, UseSystemFocusVisuals = true, Visibility = Visibility.Collapsed,
    };
    private readonly StackPanel imageTools = new() { Orientation = Orientation.Horizontal, Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly Button zoomOut = new() { Content = "−" };
    private readonly Button zoomIn = new() { Content = "+" };
    private readonly Button imageFit = new() { Content = "Fit" };
    private readonly Button imageExpand = new() { Content = new SymbolIcon(Symbol.FullScreen) };
    private Control[] imageControls = [];
    private string ImageActionLabel => XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is Control focused && imageControls.Contains(focused)
        ? AutomationProperties.GetName(focused) : "Fit image";

    private void InitializeImages()
    {
        imageViewport.Content = image;
        imageViewport.SizeChanged += (_, _) =>
        {
            image.Width = Math.Max(1, imageViewport.ActualWidth);
            image.Height = Math.Max(1, imageViewport.ActualHeight);
        };
        imageControls = [zoomOut, zoomIn, imageFit, imageExpand];
        Label(zoomOut, "Zoom out", "MediaPlayer.Image.ZoomOut");
        Label(zoomIn, "Zoom in", "MediaPlayer.Image.ZoomIn");
        Label(imageFit, "Fit image", "MediaPlayer.Image.Fit");
        Label(imageExpand, "Expand image", "MediaPlayer.Image.Expand");
        AutomationProperties.SetName(imageViewport, "Image · use triggers to zoom and directions to pan");
        AutomationProperties.SetAutomationId(imageViewport, "MediaPlayer.Image.Viewport");
        foreach (var control in imageControls)
        {
            control.MinHeight = 44; control.MinWidth = 44; control.IsTabStop = true;
            control.UseSystemFocusVisuals = true;
            imageTools.Children.Add(control);
            control.GotFocus += (_, _) => InteractionChanged?.Invoke();
        }
        zoomOut.Click += (_, _) => ZoomImage(-1);
        zoomIn.Click += (_, _) => ZoomImage(1);
        imageFit.Click += (_, _) => FitImage();
        imageExpand.Click += async (_, _) => await ToggleExpandedAsync();
    }

    private void SizeCard(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return;
        card.Width = width; card.Height = height;
    }

    private void FitImage() => imageViewport.ChangeView(0, 0, 1, disableAnimation: true);
    private void ZoomImage(int direction)
    {
        if (!input || !IsImage) return;
        var old = imageViewport.ZoomFactor;
        var next = Math.Clamp(old * (direction > 0 ? 1.2f : 1 / 1.2f), 1, 8);
        var x = (imageViewport.HorizontalOffset + imageViewport.ViewportWidth / 2) * next / old - imageViewport.ViewportWidth / 2;
        var y = (imageViewport.VerticalOffset + imageViewport.ViewportHeight / 2) * next / old - imageViewport.ViewportHeight / 2;
        imageViewport.ChangeView(Math.Max(0, x), Math.Max(0, y), next, disableAnimation: true);
    }
    private void ActivateImageControl()
    {
        var focused = FocusManager.GetFocusedElement(XamlRoot);
        if (ReferenceEquals(focused, zoomOut)) ZoomImage(-1);
        else if (ReferenceEquals(focused, zoomIn)) ZoomImage(1);
        else if (ReferenceEquals(focused, imageExpand)) _ = ToggleExpandedAsync();
        else if (HasDialog && focused is Button && !imageControls.Contains(focused)) expandedDialog?.Hide();
        else FitImage();
    }
    private bool MoveImageFocus(FocusNavigationDirection direction)
    {
        if (!input || XamlRoot is null) return false;
        var focused = FocusManager.GetFocusedElement(XamlRoot);
        if (ReferenceEquals(focused, imageViewport) || ReferenceEquals(focused, this))
        {
            if (imageViewport.ZoomFactor <= 1.01)
                return direction == FocusNavigationDirection.Down ? imageFit.Focus(FocusState.Keyboard) : HasDialog;
            var x = imageViewport.HorizontalOffset;
            var y = imageViewport.VerticalOffset;
            if (direction == FocusNavigationDirection.Left) x -= 48;
            if (direction == FocusNavigationDirection.Right) x += 48;
            if (direction == FocusNavigationDirection.Up) y -= 48;
            if (direction == FocusNavigationDirection.Down) y += 48;
            imageViewport.ChangeView(Math.Clamp(x, 0, imageViewport.ScrollableWidth), Math.Clamp(y, 0, imageViewport.ScrollableHeight), null, true);
            return true;
        }
        if (direction == FocusNavigationDirection.Up) return imageViewport.Focus(FocusState.Keyboard);
        var index = Array.IndexOf(imageControls, focused as Control);
        var next = index + (direction == FocusNavigationDirection.Left ? -1 : 1);
        if (direction is FocusNavigationDirection.Left or FocusNavigationDirection.Right && next >= 0 && next < imageControls.Length)
            return imageControls[next].Focus(FocusState.Keyboard);
        return HasDialog;
    }
    private bool HandleImage(ControllerButton button, ControllerEventPhase phase)
    {
        if (button is not (ControllerButton.A or ControllerButton.LeftTrigger or ControllerButton.RightTrigger or ControllerButton.LeftBumper or ControllerButton.RightBumper or ControllerButton.RightStick)) return HasDialog;
        if (phase == ControllerEventPhase.Released) return true;
        if (button == ControllerButton.LeftTrigger) ZoomImage(-1);
        else if (button == ControllerButton.RightTrigger) ZoomImage(1);
        else if (phase == ControllerEventPhase.Pressed)
        {
            if (button == ControllerButton.A) ActivateImageControl();
            if (button == ControllerButton.LeftBumper) FitImage();
            if (button == ControllerButton.RightBumper)
                (ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), imageViewport) ? (Control)imageFit : imageViewport).Focus(FocusState.Keyboard);
            if (button == ControllerButton.RightStick) _ = ToggleExpandedAsync();
        }
        return true;
    }
}
