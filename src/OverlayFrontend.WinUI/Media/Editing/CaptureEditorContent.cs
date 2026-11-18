using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Media.Editing;

internal sealed class CaptureEditorContent : IHostDialogContent, IDisposable
{
    private readonly StackPanel body = new() { Spacing = 8, HorizontalAlignment=HorizontalAlignment.Stretch };
    private readonly ScrollViewer viewport;
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly CaptureCropView? crop;
    private readonly MediaPlayerView? player;
    private readonly Slider? start, end;
    private readonly WidgetResponsiveGrid? trimControls;
    private readonly Button? previewSelection;
    private readonly Button reset;
    private readonly double mediaAspect;
    private readonly double initialContentWidth;
    private VideoTrimState? trim;
    private Slider? adjusting;
    private readonly CaptureEditAcceleration acceleration = new();
    private bool refreshing, saving, disposed;
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
    internal bool CanSave => !saving && (crop is { Ready: true } image && image.State.Changed || trim is { Changed: true } range && range.LengthTicks <= TimeSpan.FromMinutes(10).Ticks);
    internal HostCaptureEditRecipe Recipe => crop is { } image
        ? new(new(image.State.QuarterTurns, image.State.Box.X, image.State.Box.Y, image.State.Box.Width, image.State.Box.Height), null)
        : new(null, new(trim!.StartTicks, trim.EndTicks));
    public FrameworkElement View => viewport;
    public event Action? InteractionChanged;
    internal event Action? Changed;
    public IReadOnlyList<ControllerGuideHint>? Guide => crop?.Adjusting == true
        ? (ControllerGuideHint[])[new(ControllerPrompt.DPad, "Adjust crop"), new(ControllerPrompt.LeftBumper, "Previous handle", ControllerButton.LeftBumper),
            new(ControllerPrompt.RightBumper, "Next handle", ControllerButton.RightBumper), new(ControllerPrompt.A, "Done", ControllerButton.A), new(ControllerPrompt.B, "Done", ControllerButton.B, Required: true)]
        : adjusting is not null
        ? (ControllerGuideHint[])[new(ControllerPrompt.DPadHorizontal, "Adjust time"), new(ControllerPrompt.LeftBumper, "Back " + CoarseLabel, ControllerButton.LeftBumper),
            new(ControllerPrompt.RightBumper, "Forward " + CoarseLabel, ControllerButton.RightBumper), new(ControllerPrompt.A, "Done", ControllerButton.A), new(ControllerPrompt.B, "Done", ControllerButton.B, Required: true)]
        : (ControllerGuideHint[])[new(ControllerPrompt.A, FocusedSlider() is not null ? "Adjust time" : "Select", ControllerButton.A), new(ControllerPrompt.B, "Cancel", ControllerButton.B, Required: true)];

    internal CaptureEditorContent(CaptureAttachment media, string trustedPreviewUri, Func<bool> current, double availableHeight, double availableWidth)
    {
        mediaAspect=(double)media.Width/media.Height;
        initialContentWidth=Math.Clamp(availableWidth-100,280,800);
        viewport = new ScrollViewer { Content = body, MaxHeight = Math.Clamp(availableHeight - 190, 240, 780),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsTabStop = false, Padding = new(0, 0, 12, 0),
            MaxWidth=800,HorizontalContentAlignment=HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(viewport, "Capture.Edit.Content");
        viewport.SizeChanged += (_, _) => UpdateLayout();
        body.SizeChanged+=(_,_)=>UpdateLayout();
        if (media.ContentType == "image/png")
        {
            crop = new(media.Width, media.Height, new BitmapImage(new Uri(trustedPreviewUri)));
            crop.Changed += Update; crop.InteractionChanged += () => InteractionChanged?.Invoke();
            body.Children.Add(crop);
            var tools = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 150, ItemHeight = 48, MaximumRowsOrColumns = 5 };
            tools.Children.Add(Button("Rotate left", "RotateLeft", () => crop.Rotate(false)));
            tools.Children.Add(Button("Rotate right", "RotateRight", () => crop.Rotate(true)));
            tools.Children.Add(Button("Original ratio", "OriginalRatio", () => crop.SetAspect((double)crop.State.Width / crop.State.Height)));
            tools.Children.Add(Button("Square", "Square", () => crop.SetAspect(1)));
            tools.Children.Add(Button("Free crop", "FreeCrop", () => crop.SetAspect(null)));
            foreach(Control tool in tools.Children)tool.Margin=new(0,0,8,4);
            body.Children.Add(tools);
        }
        else
        {
            trim = new(TimeSpan.FromSeconds(media.DurationSeconds).Ticks, Math.Min(TimeSpan.FromMilliseconds(100).Ticks, TimeSpan.FromSeconds(media.DurationSeconds).Ticks));
            player = new();
            AutomationProperties.SetAutomationId(player,"Capture.Edit.Player");
            var definition = MediaPlayerDefinition.FromCapture(media);
            player.Apply(_ => Task.FromResult(new HostMediaPlayerSource(definition, trustedPreviewUri, Width: media.Width, Height: media.Height, DurationSeconds: media.DurationSeconds)), definition, current, this);
            player.SetActive(true, true); player.InteractionChanged = () => InteractionChanged?.Invoke();
            body.Children.Add(player);
            start = TimeSlider("Start", "Start", 0, media.DurationSeconds);
            end = TimeSlider("End", "End", media.DurationSeconds, media.DurationSeconds);
            trimControls = new() { ColumnSpacing = 12, RowSpacing = 4 };
            AutomationProperties.SetAutomationId(trimControls,"Capture.Edit.TrimControls");
            trimControls.Children.Add(start);trimControls.Children.Add(end);
            trimControls.Configure([start,end],240,2);
            trimControls.SizeChanged+=(_,_)=>UpdateLayout();
            body.Children.Add(trimControls);
            start.ValueChanged += (_, _) => { if (!refreshing) { trim = trim.WithStart(TimeSpan.FromSeconds(start.Value).Ticks); Update(); player.SeekForEdit(TimeSpan.FromTicks(trim.StartTicks).TotalSeconds); } };
            end.ValueChanged += (_, _) => { if (!refreshing) { trim = trim.WithEnd(TimeSpan.FromSeconds(end.Value).Ticks); Update(); player.SeekForEdit(Math.Max(0, TimeSpan.FromTicks(trim.EndTicks).TotalSeconds - .1)); } };
            previewSelection=Button("Play selection", "Preview", () => player.PreviewEditRange(TimeSpan.FromTicks(trim.StartTicks).TotalSeconds, TimeSpan.FromTicks(trim.EndTicks).TotalSeconds));
        }
        body.Children.Add(summary);
        body.Children.Add(progress);
        reset=Button("Reset edits", "Reset", () => { if (crop is not null) crop.Reset(); else { trim = trim!.Reset(); Update(); player!.SeekForEdit(0); } });
        if(previewSelection is not null)
        {
            var actions=new Grid{ColumnSpacing=12};
            actions.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
            actions.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
            actions.Children.Add(previewSelection);actions.Children.Add(reset);Grid.SetColumn(reset,1);
            previewSelection.XYFocusRight=reset;reset.XYFocusLeft=previewSelection;
            body.Children.Add(actions);
        }
        else body.Children.Add(reset);
        body.GotFocus += (_, _) => InteractionChanged?.Invoke();
        UpdateLayout();
        Update();
    }
    private void UpdateLayout()
    {
        if(disposed)return;
        if(viewport.XamlRoot is {} root)viewport.MaxHeight=Math.Clamp(root.Size.Height-190,240,780);
        if(player is null||start is null||end is null||previewSelection is null||trimControls is null)return;
        // Native horizontal stretch follows the dialog's finite content width.
        // Never write ActualWidth back into Width: that prevents a shrunken
        // auto-sized dialog from growing on a later layout pass.
        var width=body.ActualWidth>0?body.ActualWidth:initialContentWidth;
        var paired=width>=492;
        player.Height=Math.Clamp(width/mediaAspect,160,Math.Max(160,Math.Min(460,viewport.MaxHeight-(paired?168:236))));
        // Native XY navigation follows the authored rows. Adjustment remains
        // owned by the existing slider/player modes; footer navigation stays
        // with HostChoiceDialog and its native focus search.
        start.XYFocusRight=paired?end:null;
        end.XYFocusLeft=paired?start:null;
        start.XYFocusDown=paired?previewSelection:end;
        end.XYFocusUp=paired?null:start;
        end.XYFocusDown=reset;
        previewSelection.XYFocusUp=paired?start:end;
        reset.XYFocusUp=end;
    }
    private Slider TimeSlider(string label, string id, double value, double maximum)
    {
        var slider = new Slider { Header = label, Minimum = 0, Maximum = maximum, Value = value, SmallChange = .1, LargeChange = 10, StepFrequency = .1,
            HorizontalAlignment = HorizontalAlignment.Stretch, UseSystemFocusVisuals = true };
        AutomationProperties.SetAutomationId(slider, "Capture.Edit." + id); AutomationProperties.SetName(slider, label + " time in seconds");
        slider.LostFocus += (_, _) => { if (ReferenceEquals(adjusting, slider)) { adjusting = null; InteractionChanged?.Invoke(); } };
        slider.KeyDown += (_, args) => { if (args.Key == Windows.System.VirtualKey.Escape && adjusting is not null) { adjusting = null; InteractionChanged?.Invoke(); args.Handled = true; } };
        return slider;
    }
    private static Button Button(string label, string id, Action action)
    {
        var button = new Button { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, MinHeight = 44,
            Padding = new(12, 6, 12, 6), HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Center };
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetAutomationId(button, "Capture.Edit." + id); button.Click += (_, _) => action(); return button;
    }
    private Slider? FocusedSlider() => viewport.XamlRoot is not null && FocusManager.GetFocusedElement(viewport.XamlRoot) is Slider focused && (focused == start || focused == end) ? focused : null;
    private double CoarseSeconds => Math.Max(10, Math.Round(TimeSpan.FromTicks(trim?.DurationTicks ?? 0).TotalSeconds / 20));
    private string CoarseLabel => CoarseSeconds >= 60 ? $"{CoarseSeconds / 60:0.#} min" : $"{CoarseSeconds:0} sec";
    private bool PlayerFocused()
    {
        if (player is null || viewport.XamlRoot is null) return false;
        for (var node = FocusManager.GetFocusedElement(viewport.XamlRoot) as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, player)) return true;
        return false;
    }
    public bool Move(FocusNavigationDirection direction, bool repeated = false)
    {
        if (crop?.Move(direction, repeated) == true) return true;
        if (adjusting is { } slider)
        {
            if (direction is FocusNavigationDirection.Left or FocusNavigationDirection.Right)
            {
                var step = acceleration.Step((int)direction, repeated, Environment.TickCount64, .1,
                    CaptureEditAcceleration.TrimFastStep(slider.Maximum));
                slider.Value = Math.Round((slider.Value + (direction == FocusNavigationDirection.Left ? -step : step)) * 10,
                    MidpointRounding.AwayFromZero) / 10;
            }
            else { adjusting = null; InteractionChanged?.Invoke(); return false; }
            return true;
        }
        return PlayerFocused() && player!.MoveFocus(direction, repeated);
    }
    public bool Handle(ControllerButton button, ControllerEventPhase phase)
    {
        if (phase != ControllerEventPhase.Pressed)
            return phase == ControllerEventPhase.Released && player?.Handle(button, phase) == true;
        acceleration.Reset();
        if (crop?.Handle(button) == true) return true;
        if (adjusting is { } slider)
        {
            if (button is ControllerButton.A or ControllerButton.B) { adjusting = null; InteractionChanged?.Invoke(); return true; }
            if (button is ControllerButton.LeftBumper or ControllerButton.RightBumper) { slider.Value += button == ControllerButton.LeftBumper ? -CoarseSeconds : CoarseSeconds; return true; }
        }
        if (button == ControllerButton.A && FocusedSlider() is { } target) { adjusting = target; InteractionChanged?.Invoke(); return true; }
        return PlayerFocused() && player!.Handle(button, ControllerEventPhase.Pressed);
    }
    public void ResetInput() { acceleration.Reset(); adjusting = null; crop?.ResetInput(); player?.ResetEditorInput(); Input.PointerCaptureLifetime.ReleaseWithin(viewport); }
    internal void ShowSaving(double? value)
    {
        if (!saving)
        {
            saving = true; ResetInput(); player?.SetActive(false, false);
            // Keep the dialog's Cancel action outside this disabled content.
            viewport.IsEnabled = false;
            InteractionChanged?.Invoke();
        }
        progress.Visibility = Visibility.Visible;
        progress.IsIndeterminate = value is null;
        if (value is { } fraction) progress.Value = Math.Clamp(fraction * 100, 0, 100);
        summary.Text = value is { } number ? $"Saving copy… {Math.Clamp(number * 100, 0, 100):0}%" : "Saving copy…";
    }
    private void Update()
    {
        refreshing = true;
        try
        {
            if (trim is { } range)
            {
                start!.Value = TimeSpan.FromTicks(range.StartTicks).TotalSeconds; end!.Value = TimeSpan.FromTicks(range.EndTicks).TotalSeconds;
                start.Header = "Start · " + Format(range.StartTicks); end.Header = "End · " + Format(range.EndTicks);
                summary.Text = "Selection: " + Format(range.LengthTicks) + (range.LengthTicks > TimeSpan.FromMinutes(10).Ticks ? " · Shorten to 10 minutes or less to save." : " · Saved as a new MP4. Trimming re-encodes the selection.");
            }
            else summary.Text = "Save copy creates a new PNG. The original stays unchanged.";
        }
        finally { refreshing = false; }
        Changed?.Invoke();
    }
    private static string Format(long ticks) => TimeSpan.FromTicks(ticks).ToString(@"h\:mm\:ss\.f");
    public void Dispose() { if (disposed) return; disposed = true; ResetInput(); crop?.Dispose(); player?.Dispose(); }
}
