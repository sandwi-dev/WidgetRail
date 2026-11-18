using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.System;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Media.Editing;

/// <summary>One crop state for drag handles, keyboard and controller. No widget-authored input bookkeeping.</summary>
internal sealed partial class CaptureCropView : Grid, IDisposable
{
    private readonly Canvas canvas = new() { Background = new SolidColorBrush(Colors.Transparent) };
    private readonly Image image = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Border crop = new() { BorderBrush = new SolidColorBrush(Colors.White), BorderThickness = new(2), IsHitTestVisible = false };
    private readonly Border[] masks = Enumerable.Range(0, 4).Select(_ => new Border { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(155, 0, 0, 0)), IsHitTestVisible = false }).ToArray();
    private readonly Border[] handles = Enumerable.Range(0, 8).Select(_ => new Border { Width = 12, Height = 12, CornerRadius = new(6), Background = new SolidColorBrush(Colors.White), BorderBrush = new SolidColorBrush(Colors.Black), BorderThickness = new(1), IsHitTestVisible = false }).ToArray();
    private readonly Button focus;
    private readonly TextBlock label = new() { TextWrapping = TextWrapping.Wrap };
    private double scale = 1, left, top;
    private ImageCropState state;
    private CropGrip grip;
    private readonly CaptureEditAcceleration acceleration = new();
    private bool adjusting;
    private uint? pointerId;
    private Point pointerStart;
    private ImageCropState? dragStart;
    internal ImageCropState State => state;
    internal bool Ready { get; private set; }
    private bool imageFailed;
    internal bool Adjusting => adjusting;
    internal event Action? Changed;
    internal event Action? InteractionChanged;

    internal CaptureCropView(int width, int height, ImageSource preview)
    {
        state = ImageCropState.Full(width, height);
        RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowSpacing = 8;
        canvas.Children.Add(image);
        image.ImageOpened += (_, _) => { Ready = true; Render(); Changed?.Invoke(); };
        image.ImageFailed += (_, _) => { Ready = false; imageFailed = true; Render(); Changed?.Invoke(); };
        image.Source = preview;
        foreach (var mask in masks) canvas.Children.Add(mask);
        canvas.Children.Add(crop);
        foreach (var handle in handles) canvas.Children.Add(handle);
        focus = new Button { Content = canvas, Padding = new(0), HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 180, Height = 300, UseSystemFocusVisuals = true };
        AutomationProperties.SetAutomationId(focus, "Capture.Edit.Crop");
        AutomationProperties.SetName(focus, "Crop selection");
        focus.Click += (_, _) => SetAdjusting(!adjusting);
        focus.GotFocus += (_, _) => InteractionChanged?.Invoke();
        focus.LostFocus += (_, _) => { if (pointerId is null) SetAdjusting(false); };
        focus.KeyDown += OnKeyDown;
        canvas.SizeChanged += (_, _) => { EndPointer(); Render(); };
        canvas.PointerPressed += PointerDown;
        canvas.PointerMoved += PointerMove;
        canvas.PointerReleased += PointerUp;
        canvas.PointerCaptureLost += (_, _) => EndPointer();
        canvas.PointerCanceled += (_, _) => EndPointer();
        Children.Add(focus); Grid.SetRow(label, 1); Children.Add(label);
        Unloaded += (_, _) => ResetInput();
        Render();
    }

    internal bool Handle(ControllerButton button)
    {
        if (!Ready || !ContainsFocus()) return false;
        if (button == ControllerButton.A) { SetAdjusting(!adjusting); return true; }
        if (!adjusting) return false;
        if (button == ControllerButton.B) { SetAdjusting(false); return true; }
        if (button is ControllerButton.LeftBumper or ControllerButton.RightBumper)
        { acceleration.Reset(); grip = (CropGrip)(((int)grip + (button == ControllerButton.RightBumper ? 1 : 8)) % 9); Render(); InteractionChanged?.Invoke(); return true; }
        return false;
    }
    internal bool Move(FocusNavigationDirection direction, bool repeated = false)
    {
        if (!adjusting || !ContainsFocus()) return false;
        var distance = acceleration.Step((int)direction, repeated, Environment.TickCount64, 2, 24);
        ApplyDirection(direction, Math.Max(1, (int)Math.Round(distance / scale)));
        return true;
    }
    internal void Rotate(bool clockwise) { ResetInput(); Set(state.Rotate(clockwise)); }
    internal void Reset() { ResetInput(); Set(state.Reset()); }
    internal void SetAspect(double? ratio) { ResetInput(); Set(state.SetAspect(ratio)); }
    internal void ResetInput() { EndPointer(); SetAdjusting(false); }
    public void Dispose() { ResetInput(); image.Source = null; }
    private bool ContainsFocus() => XamlRoot is not null && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), focus);
    private void SetAdjusting(bool value)
    {
        acceleration.Reset();
        if (adjusting == value) return;
        adjusting = value; Render(); InteractionChanged?.Invoke();
    }
    private void Set(ImageCropState next) { state = next; Render(); Changed?.Invoke(); }
    private void ApplyDirection(FocusNavigationDirection direction, int step)
    {
        var dx = direction == FocusNavigationDirection.Left ? -step : direction == FocusNavigationDirection.Right ? step : 0;
        var dy = direction == FocusNavigationDirection.Up ? -step : direction == FocusNavigationDirection.Down ? step : 0;
        Set(state.Adjust(grip, dx, dy));
    }
    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape && adjusting) { ResetInput(); args.Handled = true; return; }
        if (args.Key == VirtualKey.Tab) { ResetInput(); return; }
        if (!adjusting) return;
        var direction = args.Key switch { VirtualKey.Left => FocusNavigationDirection.Left, VirtualKey.Right => FocusNavigationDirection.Right,
            VirtualKey.Up => FocusNavigationDirection.Up, VirtualKey.Down => FocusNavigationDirection.Down, _ => FocusNavigationDirection.None };
        if (direction != FocusNavigationDirection.None)
        {
            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            ApplyDirection(direction, shift ? 10 : 1); args.Handled = true;
        }
        else if (args.Key is VirtualKey.PageDown or VirtualKey.PageUp)
        { grip = (CropGrip)(((int)grip + (args.Key == VirtualKey.PageDown ? 1 : 8)) % 9); Render(); args.Handled = true; }
    }
    private void PointerDown(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(canvas);
        if (!Ready || !point.Properties.IsLeftButtonPressed || pointerId is not null || scale <= 0) return;
        var box = state.Box;
        var x = (point.Position.X - left) / scale; var y = (point.Position.Y - top) / scale;
        var edge = 12 / scale;
        if (x < box.X - edge || x > box.Right + edge || y < box.Y - edge || y > box.Bottom + edge) return;
        var nearLeft = Math.Abs(x - box.X) <= edge; var nearRight = !nearLeft && Math.Abs(x - box.Right) <= edge;
        var nearTop = Math.Abs(y - box.Y) <= edge; var nearBottom = !nearTop && Math.Abs(y - box.Bottom) <= edge;
        grip = nearTop ? nearLeft ? CropGrip.TopLeft : nearRight ? CropGrip.TopRight : CropGrip.Top :
            nearBottom ? nearLeft ? CropGrip.BottomLeft : nearRight ? CropGrip.BottomRight : CropGrip.Bottom :
            nearLeft ? CropGrip.Left : nearRight ? CropGrip.Right : CropGrip.Move;
        focus.Focus(FocusState.Pointer);
        if (!canvas.CapturePointer(args.Pointer)) return;
        pointerId = args.Pointer.PointerId; pointerStart = point.Position; dragStart = state;
        SetAdjusting(true); args.Handled = true;
    }
    private void PointerMove(object sender, PointerRoutedEventArgs args)
    {
        if (pointerId != args.Pointer.PointerId || dragStart is null) return;
        var point = args.GetCurrentPoint(canvas);
        if (!point.Properties.IsLeftButtonPressed) { EndPointer(); return; }
        Set(dragStart.Adjust(grip, (int)Math.Round((point.Position.X - pointerStart.X) / scale), (int)Math.Round((point.Position.Y - pointerStart.Y) / scale)));
        args.Handled = true;
    }
    private void PointerUp(object sender, PointerRoutedEventArgs args)
    { if (pointerId == args.Pointer.PointerId) { EndPointer(); args.Handled = true; } }
    private void EndPointer()
    {
        if (pointerId is null) return;
        pointerId = null; dragStart = null; canvas.ReleasePointerCaptures();
    }
    private void Render()
    {
        var cw = canvas.ActualWidth; var ch = canvas.ActualHeight;
        var box = state.Box;
        label.Text = $"{box.Width} × {box.Height} px" + (adjusting ? $" · {GripName(grip)} · Arrows adjust; PgUp/PgDn choose handle; Esc done" : " · Drag edges to crop, or select the image to adjust");
        if (!Ready) label.Text = imageFailed ? "The image preview could not be opened. Cancel and try again." : "Opening image preview…";
        AutomationProperties.SetHelpText(focus, label.Text);
        if (cw <= 0 || ch <= 0) return;
        scale = Math.Min((cw - 20) / state.Width, (ch - 20) / state.Height);
        if (scale <= 0) return;
        left = (cw - state.Width * scale) / 2; top = (ch - state.Height * scale) / 2;
        image.Width = state.SourceWidth * scale; image.Height = state.SourceHeight * scale;
        Canvas.SetLeft(image, (cw - image.Width) / 2); Canvas.SetTop(image, (ch - image.Height) / 2);
        image.RenderTransformOrigin = new(.5, .5); image.RenderTransform = new RotateTransform { Angle = state.QuarterTurns * 90 };
        var x = left + box.X * scale; var y = top + box.Y * scale; var w = box.Width * scale; var h = box.Height * scale;
        Place(crop, x, y, w, h);
        Place(masks[0], left, top, state.Width * scale, y - top);
        Place(masks[1], left, y + h, state.Width * scale, top + state.Height * scale - y - h);
        Place(masks[2], left, y, x - left, h);
        Place(masks[3], x + w, y, left + state.Width * scale - x - w, h);
        var points = new[] { new Point(x, y + h / 2), new Point(x + w / 2, y), new Point(x + w, y + h / 2), new Point(x + w / 2, y + h),
            new Point(x, y), new Point(x + w, y), new Point(x, y + h), new Point(x + w, y + h) };
        for (var i = 0; i < handles.Length; i++)
        {
            var size = adjusting && (int)grip == i + 1 ? 18 : 12;
            handles[i].CornerRadius = new(size / 2d);
            Place(handles[i], points[i].X - size / 2d, points[i].Y - size / 2d, size, size);
        }
    }
    private static void Place(FrameworkElement element, double x, double y, double width, double height)
    { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); element.Width = Math.Max(0, width); element.Height = Math.Max(0, height); }
    private static string GripName(CropGrip value) => value switch { CropGrip.Move => "Move crop", CropGrip.TopLeft => "Top-left corner", CropGrip.TopRight => "Top-right corner",
        CropGrip.BottomLeft => "Bottom-left corner", CropGrip.BottomRight => "Bottom-right corner", _ => value + " edge" };
}
