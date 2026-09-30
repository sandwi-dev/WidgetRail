using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class SliderControlValidationPage : Page, IAsyncDisposable
{
    private readonly WidgetViewPresenter presenter = new();
    private readonly TextBlock status = new() { Text = "Slider validation waiting for layout" };
    private readonly List<string> checks = [];
    private readonly List<WidgetActionRequest> actions = [];
    private long sequence;
    private double minimum = 10, maximum = 21, value = 10, step = 3;
    private bool disabled, alternate, busy;
    private bool shortcutBusy;
    private bool volumeGeometry, paddedVolumeGeometry;
    private Exception? failure;
    private Task? run;
    public SliderControlValidationPage()
    {
        AutomationProperties.SetAutomationId(status, "Slider.Status");
        Content = new StackPanel { Spacing = 12, Children = { status, presenter } };
        presenter.Failed = error => failure = error;
        presenter.DispatchActionAsync = request => { actions.Add(request); return Task.CompletedTask; };
        Loaded += (_, _) => run ??= RunAsync();
    }
    private async Task RunAsync()
    {
        try
        {
            if (Environment.GetCommandLineArgs().FirstOrDefault(arg => arg.StartsWith("--slider-pixels=")) is { } pixels)
            {
                await CaptureSliderPixelsAsync(pixels["--slider-pixels=".Length..]);
                return;
            }
            await FractionalVolumeAsync();
            actions.Clear();
            minimum = 10; maximum = 21; value = 10; step = 3;
            Apply(); await Task.Delay(200); Focus("direct");
            var direct = (Slider)Find(presenter, "Widget.direct")!;
            Check(direct.Value == 10 && actions.Count == 0, "initial snapshot sets native value without an action");
            presenter.MoveFocus(FocusNavigationDirection.Right);
            Check(direct.Value == 13 && actions.Count == 0, "slider steps immediately while provider dispatch waits for settlement");
            await Until(() => actions.Count == 1);
            Check(direct.Value == 13 && actions[^1].Action.RequestedValue == 13, "right steps from minimum-anchored range");
            presenter.MoveFocus(FocusNavigationDirection.Right); presenter.MoveFocus(FocusNavigationDirection.Right);
            presenter.MoveFocus(FocusNavigationDirection.Right);
            await Until(() => actions.Count == 2);
            Check(direct.Value == 21 && actions[^1].Action.RequestedValue == 21, "nonmultiple maximum remains reachable");
            var count = actions.Count;
            presenter.MoveFocus(FocusNavigationDirection.Right);
            Check(actions.Count == count && FocusId == "Widget.direct", "range boundary consumes direction without duplicate action");
            presenter.MoveFocus(FocusNavigationDirection.Left);
            await Until(() => actions.Count == count + 1);
            Check(direct.Value == 19 && actions[^1].Action.RequestedValue == 19, "left from endpoint returns to minimum-anchored grid");
            direct.Value = 17;
            await Until(() => actions.Count == count + 2);
            Check(direct.Value == 16 && actions[^1].Action.RequestedValue == 16, "native pointer or UIA value is quantized through shared SDK range");
            presenter.ActivateFocused();
            Check(actions[^1].Action.ActionId == "activate", "direct slider optional A action dispatches");
            count = actions.Count;
            value = 16; Apply();
            Check(ReferenceEquals(direct, Find(presenter, "Widget.direct")) && direct.Value == 16 && actions.Count == count,
                "authoritative value update preserves native control without echo");
            minimum = 100; maximum = 200; value = 130; step = 10; Apply();
            Check(direct.Minimum == 100 && direct.Maximum == 200 && direct.Value == 130, "disjoint larger range updates atomically");
            minimum = -200; maximum = -100; value = -140; step = 10; Apply();
            Check(direct.Minimum == -200 && direct.Maximum == -100 && direct.Value == -140, "disjoint smaller range updates atomically");
            Focus("adjust");
            presenter.MoveFocus(FocusNavigationDirection.Right);
            Check(FocusId == "Widget.after", "inactive adjustment slider uses authored spatial neighbor");
            Focus("adjust"); count = actions.Count;
            await presenter.HandleControllerButtonAsync(ControllerButton.A);
            Check(actions.Count == count, "A enters adjustment without activation command");
            var adjusting = (Slider)Find(presenter, "Widget.adjust")!;
            await Task.Delay(40);
            Check(adjusting.IsFocusEngaged && presenter.CaptureControllerGuide().Any(hint => hint.Button == ControllerButton.A && hint.Label == "Done") &&
                AutomationProperties.GetHelpText(adjusting).Contains("Adjustment active"),
                "A engages the native slider thumb with Done guidance and accessible adjustment state");
            Check(adjusting.UseSystemFocusVisuals && adjusting.FocusVisualPrimaryBrush is SolidColorBrush ink &&
                ink.Color == Windows.UI.Color.FromArgb(255, 171, 205, 239),
                $"authored whole-control focus hands off to the themed native thumb while adjusting (system={adjusting.UseSystemFocusVisuals}, ink={(adjusting.FocusVisualPrimaryBrush as SolidColorBrush)?.Color})");
            await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Repeated);
            await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
            Check(adjusting.IsFocusEngaged, "A repeat and release do not toggle adjustment twice");
            presenter.MoveFocus(FocusNavigationDirection.Right);
            await Until(() => actions.Count > count);
            Check(actions[^1].Action.SourceElementId == "adjust" && actions[^1].Action.RequestedValue == -130, "active adjustment steps value");
            busy = true; Apply(); count = actions.Count;
            presenter.MoveFocus(FocusNavigationDirection.Right);
            Check(adjusting.IsFocusEngaged && FocusId == "Widget.adjust" && actions.Count == count && adjusting.Value == -120,
                "pending seek retains focus and queues newer local adjustment without dispatching while busy");
            presenter.MoveFocus(FocusNavigationDirection.Left);
            busy = false; value = -130; Apply();
            presenter.MoveFocus(FocusNavigationDirection.Right);
            Check(adjusting.IsFocusEngaged && FocusId == "Widget.adjust" && adjusting.Value == -120,
                "settled seek resumes adjustment without another A press or navigation to a transport button");
            presenter.MoveFocus(FocusNavigationDirection.Right);
            Apply();
            Check(adjusting.Value == -110 && actions.Count == count, "remote updates cannot overwrite the latest unsettled local target");
            await presenter.HandleControllerButtonAsync(ControllerButton.A);
            Check(!adjusting.IsFocusEngaged && adjusting.Value == -110 && actions.Count == count + 1 && actions[^1].Action.RequestedValue == -110,
                "A Done flushes the latest target once without native disengagement rolling back to the first step");
            await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
            busy = true; Apply();
            Check(adjusting.Value == -110, "older busy provider echo cannot undo the final seek after Done");
            busy = false; value = -110; Apply();
            value = -130; Apply();
            Check(adjusting.Value == -110, "late acknowledgement of the earlier seek does not rubberband after the final seek settled");
            value = -100; Apply();
            Check(adjusting.Value == -100, "distinct external value remains authoritative after a local seek");
            await presenter.HandleControllerButtonAsync(ControllerButton.A);
            presenter.MoveFocus(FocusNavigationDirection.Up);
            Check(FocusId == "Widget.adjust", "active adjustment consumes perpendicular direction");
            Check(await presenter.HandleControllerButtonAsync(ControllerButton.B), "B exits adjustment before widget shortcut");
            Check(!adjusting.IsFocusEngaged && presenter.CaptureControllerGuide().Any(hint => hint.Button == ControllerButton.A && hint.Label == "Adjust"),
                "B clears native thumb engagement and restores Adjust guidance");
            // Native dependency-property changes enqueue style application.
            for (var attempt = 0; adjusting.UseSystemFocusVisuals && attempt < 50; ++attempt) await Task.Delay(20);
            Check(!adjusting.UseSystemFocusVisuals, "leaving adjustment restores the authored whole-control focus decoration");
            Check(await presenter.HandleControllerButtonAsync(ControllerButton.B, ControllerEventPhase.Released), "B release remains consumed after exiting adjustment");
            presenter.MoveFocus(FocusNavigationDirection.Right);
            Check(FocusId == "Widget.after", "B exit restores normal navigation");
            Focus("adjust"); await presenter.HandleControllerButtonAsync(ControllerButton.A);
            count = actions.Count;
            presenter.MoveFocus(FocusNavigationDirection.Right);
            presenter.ResetPressedStyles();
            await Task.Delay(200);
            Check(actions.Count == count, "host hide retires an unsettled seek instead of sending it after deactivation");
            presenter.MoveFocus(FocusNavigationDirection.Right);
            Check(FocusId == "Widget.after", "host hide cancels adjustment mode");
            Focus("adjust"); await presenter.HandleControllerButtonAsync(ControllerButton.A);
            Focus("direct"); Focus("adjust");
            presenter.MoveFocus(FocusNavigationDirection.Right);
            Check(FocusId == "Widget.after", "focus departure cancels adjustment mode");
            Focus("adjust"); await presenter.HandleControllerButtonAsync(ControllerButton.A);
            count = actions.Count;
            presenter.MoveFocus(FocusNavigationDirection.Right);
            alternate = true; Apply(); await Task.Delay(50);
            alternate = false; Apply(); await Task.Delay(50); Focus("adjust");
            await Task.Delay(150);
            Check(actions.Count == count, "scope replacement retires pending slider dispatch without resurrecting it on return");
            presenter.MoveFocus(FocusNavigationDirection.Right);
            Check(FocusId == "Widget.after", "scope replacement cancels adjustment mode");
            count = actions.Count; disabled = true; Apply(); direct.Value = -120;
            Check(actions.Count == count, "disabled slider rejects native action dispatch");
            disabled = busy = alternate = false; Apply(); await Task.Delay(50); Focus("after");
            var held = presenter.CaptureHeldAction(ControllerButton.RightTrigger);
            Check(held is { Available: true }, "repeatable authored trigger is admitted on the focused path");
            Check(presenter.CaptureHeldAction(ControllerButton.LeftTrigger) is null, "single-shot shortcut never arms a hold");
            shortcutBusy = true; Apply(); await Task.Delay(50);
            var waiting = presenter.CaptureHeldAction(ControllerButton.RightTrigger);
            Check(waiting is { Available: false } && Equals(held!.Value.Identity, waiting.Value.Identity),
                "busy shortcut retains semantic identity while withholding repeats");
            shortcutBusy = false; Apply(); await Task.Delay(50);
            Check(Equals(held!.Value.Identity, presenter.CaptureHeldAction(ControllerButton.RightTrigger)?.Identity),
                "ordinary snapshot updates do not cancel the same held action");
            Focus("direct");
            Check(!Equals(held.Value.Identity, presenter.CaptureHeldAction(ControllerButton.RightTrigger)?.Identity),
                "focus change retires captured held action authority");
            alternate = true; Apply(); await Task.Delay(50);
            Check(presenter.CaptureHeldAction(ControllerButton.RightTrigger) is null, "nested input scope blocks the old root shortcut hold");
            await presenter.DisposeAsync();
            if (failure is not null) throw failure;
            status.Text = $"Passed {checks.Count} slider checks"; WriteResult(new { result = "passed", checks });
        }
        catch (Exception error) { status.Text = "Slider validation failed: " + error.Message; WriteResult(new { result = "failed", checks, error = error.ToString() }); }
    }
    private void Focus(string id) => ((Control)Find(presenter, "Widget." + id)!).Focus(FocusState.Keyboard);
    private string FocusId => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject element ? AutomationProperties.GetAutomationId(element) : "";
    private void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
    private static async Task Until(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; ++attempt) { if (condition()) return; await Task.Delay(20); }
        throw new TimeoutException("Slider settlement did not complete.");
    }
    private static FrameworkElement? Find(DependencyObject root, string id)
    {
        if (root is FrameworkElement element && AutomationProperties.GetAutomationId(element) == id) return element;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i) if (Find(VisualTreeHelper.GetChild(root, i), id) is { } found) return found;
        return null;
    }
    private void Apply()
    {
        ViewNode Slider(string id, bool adjust) => new() { Id = id, Kind = ViewNodeKind.Slider, Minimum = minimum, Maximum = maximum, Value = value, Step = step,
            AccessibilityLabel = id, AccessibilityValue = value.ToString(System.Globalization.CultureInfo.InvariantCulture), ValueChangedActionId = "change",
            ActionId = adjust ? null : "activate", SliderInteractionMode = adjust ? SliderInteractionMode.ActivateToAdjust : null,
            Focus = adjust ? new() { Right = "after" } : null, IsDisabled = disabled, IsBusy = busy };
        var snapshot = new ViewSnapshot { WidgetInstanceId = "slider.instance", Sequence = ++sequence,
            ActiveInputScopeId = alternate ? "dialog" : "page", InitialFocusId = alternate ? "other" : "direct",
            Root = new() { Id = "page", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[Slider("direct", false), Slider("adjust", true),
                new() { Id = "after", Kind = ViewNodeKind.Button, Text = "After", ActionId = "after", IsBusy = shortcutBusy,
                    Shortcuts = (ControllerShortcut[])[new(ControllerButton.LeftTrigger, "single"),
                        new(ControllerButton.RightTrigger, "repeat", RepeatPolicy: ControllerActionRepeatPolicy.WhileHeld)] },
                new() { Id = "dialog", Kind = ViewNodeKind.Stack, InputScopeId = "dialog", Children = (ViewNode[])[
                    new() { Id = "other", Kind = ViewNodeKind.Button, Text = "Other", ActionId = "other" }] }] } };
        var descriptor = new BridgeWidgetDescriptor { Id = "slider", Name = "Slider validation", InstanceId = snapshot.WidgetInstanceId,
            RuntimeGeneration = "runtime", PresentationGeneration = "presentation", Icon = WidgetGlyph.Connection, PackageContentDigest = "" };
        var errors = ViewSnapshotValidator.Validate(snapshot);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors.Select(error => error.Path + ": " + error.Message)));
        presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
            snapshot.WidgetInstanceId, sequence, snapshot.ActiveInputScopeId), descriptor, SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)), SliderStyles()));
    }
    private Dictionary<string, BridgeNodeRenderStyles> SliderStyles()
    {
        if (pixelStyles is not null) return new() { ["adjust"] = pixelStyles };
        var focused = new Dictionary<string, BridgeComputedStyleValue>
        {
            ["outline-color"] = new() { Kind = WrssValueKind.Color, Text = "#abcdef" },
            ["outline-width"] = new() { Kind = WrssValueKind.Length, Text = "3px", Number = 3, Unit = "px" },
        };
        var basic = new Dictionary<string, BridgeComputedStyleValue>();
        if (volumeGeometry)
        {
            basic["width"] = new() { Kind = WrssValueKind.Length, Text = "160px", Number = 160, Unit = "px" };
            basic["height"] = new() { Kind = WrssValueKind.Length, Text = "44px", Number = 44, Unit = "px" };
            basic["padding"] = new() { Kind = WrssValueKind.LengthList, Text = paddedVolumeGeometry ? "8px 10px" : "0px" };
            foreach (var pair in basic) focused[pair.Key] = pair.Value;
        }
        return new() { ["adjust"] = new() { Base = basic, Focused = focused, Pressed = focused } };
    }
    private static void WriteResult<T>(T result)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
        Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "slider-controls-result.json"), JsonSerializer.Serialize(result));
    }
    public ValueTask DisposeAsync() => presenter.DisposeAsync();
}
