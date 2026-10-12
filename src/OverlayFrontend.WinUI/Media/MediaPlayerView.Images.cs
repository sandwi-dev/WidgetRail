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
        ZoomMode = ZoomMode.Enabled, MinZoomFactor = .1f, MaxZoomFactor = 8,
        HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Enabled,
        IsTabStop = true, UseSystemFocusVisuals = true, Visibility = Visibility.Collapsed,
    };
    private readonly Grid imageTools = new() { ColumnSpacing = 8, RowSpacing = 8, Padding = new(8), Visibility = Visibility.Collapsed };
    private readonly Button zoomOut = new() { Content = "−" };
    private readonly Button zoomIn = new() { Content = "+" };
    private readonly Button imageFit = new() { Content = "Fit" };
    private readonly Button imageActual = new() { Content = "100%" };
    private readonly TextBlock imageZoom = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
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
            UpdateImageZoomLabel();
        };
        imageControls = [zoomOut, zoomIn, imageFit, imageActual, imageExpand];
        Label(zoomOut, "Zoom out", "MediaPlayer.Image.ZoomOut");
        Label(zoomIn, "Zoom in", "MediaPlayer.Image.ZoomIn");
        Label(imageFit, "Fit image", "MediaPlayer.Image.Fit");
        Label(imageActual, "Actual image size", "MediaPlayer.Image.ActualSize");
        Label(imageExpand, "Expand image", "MediaPlayer.Image.Expand");
        AutomationProperties.SetName(imageViewport, "Image · use triggers to zoom and directions to pan");
        AutomationProperties.SetAutomationId(imageViewport, "MediaPlayer.Image.Viewport");
        foreach (var control in imageControls)
        {
            control.MinHeight = 44; control.MinWidth = 44; control.IsTabStop = true;
            control.UseSystemFocusVisuals = true;
            imageTools.Children.Add(control);
            control.GotFocus += (_, _) => { rememberedControl = control; InteractionChanged?.Invoke(); };
        }
        imageTools.Children.Add(imageZoom);
        imageTools.SizeChanged += (_, _) => LayoutImageTools();
        imageViewport.ViewChanged += (_, _) => UpdateImageZoomLabel();
        imageViewport.PointerWheelChanged += (_, args) =>
        {
            if (!input) return;
            ZoomImage(args.GetCurrentPoint(imageViewport).Properties.MouseWheelDelta > 0 ? 1 : -1);
            args.Handled = true;
        };
        imageViewport.PointerPressed += (_, args) =>
        {
            // Leave touch manipulation/pinch to ScrollViewer's native gesture path.
            if (!input || args.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse ||
                !args.GetCurrentPoint(imageViewport).Properties.IsLeftButtonPressed) return;
            imageViewport.Focus(FocusState.Pointer);
            imageDrag = args.GetCurrentPoint(imageViewport).Position;
            imageDragOffset = new(imageViewport.HorizontalOffset, imageViewport.VerticalOffset);
            imageViewport.CapturePointer(args.Pointer); args.Handled = true;
        };
        imageViewport.PointerMoved += (_, args) =>
        {
            if (imageDrag is not { } start || !input) return;
            var point = args.GetCurrentPoint(imageViewport).Position;
            imageViewport.ChangeView(Math.Max(0, imageDragOffset.X + start.X - point.X), Math.Max(0, imageDragOffset.Y + start.Y - point.Y), null, true);
            args.Handled = true;
        };
        imageViewport.PointerReleased += (_, args) =>
        {
            if (args.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse) return;
            imageDrag = null; imageViewport.ReleasePointerCapture(args.Pointer);
        };
        imageViewport.PointerCaptureLost += (_, _) => imageDrag = null;
        zoomOut.Click += (_, _) => ZoomImage(-1);
        zoomIn.Click += (_, _) => ZoomImage(1);
        imageFit.Click += (_, _) => FitImage();
        imageActual.Click += (_, _) => ActualImageSize();
        imageExpand.Click += async (_, _) => await ToggleExpandedAsync();
    }

    private void LayoutImageTools()
    {
        var columns = Math.Clamp((int)((imageTools.ActualWidth - 16) / (80 * Math.Max(1, imageActual.FontSize / 14))), 2, 6);
        if (imageTools.ColumnDefinitions.Count == columns) return;
        imageTools.ColumnDefinitions.Clear(); imageTools.RowDefinitions.Clear();
        for (var i = 0; i < columns; i++) imageTools.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (var i = 0; i < (imageTools.Children.Count + columns - 1) / columns; i++) imageTools.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var i = 0; i < imageTools.Children.Count; i++) { Grid.SetColumn((FrameworkElement)imageTools.Children[i], i % columns); Grid.SetRow((FrameworkElement)imageTools.Children[i], i / columns); }
    }

    private Windows.Foundation.Point? imageDrag;
    private Windows.Foundation.Point imageDragOffset;
    private long lastImagePan;
    internal bool PanImage(short x, short y)
    {
        if (!input || !IsImage || XamlRoot is null || !ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), imageViewport) ||
            imageViewport.ScrollableWidth < 1 && imageViewport.ScrollableHeight < 1)
        { lastImagePan = 0; return false; }
        var now = Environment.TickCount64;
        var elapsed = lastImagePan == 0 ? 0 : Math.Clamp((now - lastImagePan) / 1000d, 0, .05);
        lastImagePan = now;
        static double Axis(short raw) => Math.Abs(raw / 32768d) < .18 ? 0 : Math.Sign(raw) * (Math.Abs(raw / 32768d) - .18) / .82;
        imageViewport.ChangeView(Math.Clamp(imageViewport.HorizontalOffset + Axis(x) * elapsed * 700, 0, imageViewport.ScrollableWidth),
            Math.Clamp(imageViewport.VerticalOffset - Axis(y) * elapsed * 700, 0, imageViewport.ScrollableHeight), null, true);
        return true;
    }
    private double ImageFitScale => resolved is { Width: > 0, Height: > 0 }
        ? Math.Min(Math.Max(1, image.Width) / resolved.Width, Math.Max(1, image.Height) / resolved.Height) : 1;
    private sealed record ImageViewportMemory(bool Fit, double Scale, double X, double Y);
    private ImageViewportMemory? RememberImageViewport()
    {
        if (!IsImage || resolved is not { Width: > 0, Height: > 0 }) return null;
        var zoom = imageViewport.ZoomFactor;
        var scale = ImageFitScale;
        var x = (imageViewport.HorizontalOffset + imageViewport.ViewportWidth / 2 - (image.Width - resolved.Width * scale) * zoom / 2) / (resolved.Width * scale * zoom);
        var y = (imageViewport.VerticalOffset + imageViewport.ViewportHeight / 2 - (image.Height - resolved.Height * scale) * zoom / 2) / (resolved.Height * scale * zoom);
        if (imageViewport.ScrollableWidth < 1) x = .5;
        if (imageViewport.ScrollableHeight < 1) y = .5;
        return new(Math.Abs(zoom - 1) < .01, scale * zoom, x, y);
    }
    private void RestoreImageViewport(ImageViewportMemory? memory)
    {
        if (memory is null || !IsImage || resolved is not { Width: > 0, Height: > 0 }) return;
        if (memory.Fit) { FitImage(); return; }
        var scale = ImageFitScale;
        var zoom = (float)Math.Clamp(memory.Scale / scale, imageViewport.MinZoomFactor, imageViewport.MaxZoomFactor);
        var x = ((image.Width - resolved.Width * scale) / 2 + memory.X * resolved.Width * scale) * zoom - imageViewport.ViewportWidth / 2;
        var y = ((image.Height - resolved.Height * scale) / 2 + memory.Y * resolved.Height * scale) * zoom - imageViewport.ViewportHeight / 2;
        imageViewport.ChangeView(Math.Max(0, x), Math.Max(0, y), zoom, true);
    }
    private void ActualImageSize()
    {
        if (!input || !IsImage) return;
        imageViewport.MinZoomFactor = .1f;
        imageViewport.MaxZoomFactor = (float)Math.Max(8, 8 / ImageFitScale);
        var zoom = (float)Math.Clamp(1 / (ImageFitScale * (XamlRoot?.RasterizationScale ?? 1)), imageViewport.MinZoomFactor, imageViewport.MaxZoomFactor);
        imageViewport.ChangeView(null, null, zoom, true);
    }
    private void UpdateImageZoomLabel() => imageZoom.Text = $"{ImageFitScale * imageViewport.ZoomFactor * (XamlRoot?.RasterizationScale ?? 1):P0}";

    private void FitImage() { imageViewport.ChangeView(0, 0, 1, disableAnimation: true); UpdateImageZoomLabel(); }
    private void ZoomImage(int direction)
    {
        if (!input || !IsImage) return;
        var old = imageViewport.ZoomFactor;
        var next = Math.Clamp(old * (direction > 0 ? 1.2f : 1 / 1.2f), imageViewport.MinZoomFactor, imageViewport.MaxZoomFactor);
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
        else if (ReferenceEquals(focused, imageActual)) ActualImageSize();
        else FitImage();
    }
    private bool MoveImageFocus(FocusNavigationDirection direction)
    {
        if (!input || XamlRoot is null) return false;
        var focused = FocusManager.GetFocusedElement(XamlRoot);
        if (ReferenceEquals(focused, imageViewport) || ReferenceEquals(focused, this))
        {
            if (imageViewport.ZoomFactor <= 1.01)
                return direction == FocusNavigationDirection.Down ? imageFit.Focus(FocusState.Keyboard) : IsExpanded;
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
        return IsExpanded;
    }
    private bool HandleImage(ControllerButton button, ControllerEventPhase phase)
    {
        if (button is not (ControllerButton.A or ControllerButton.LeftTrigger or ControllerButton.RightTrigger or ControllerButton.LeftBumper or ControllerButton.RightBumper or ControllerButton.RightStick)) return IsExpanded;
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
