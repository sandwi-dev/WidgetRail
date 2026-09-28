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

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed class SliderControlValidationPage : Page, IAsyncDisposable
{
    private readonly WidgetViewPresenter presenter = new();
    private readonly TextBlock status = new() { Text = "Slider validation waiting for layout" };
    private readonly List<string> checks = [];
    private readonly List<WidgetActionRequest> actions = [];
    private long sequence;
    private double minimum = 10, maximum = 21, value = 10, step = 3;
    private bool disabled, alternate;
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
            Apply(); await Task.Delay(200); Focus("direct");
            var direct = (Slider)Find(presenter, "Widget.direct")!;
            Check(direct.Value == 10 && actions.Count == 0, "initial snapshot sets native value without an action");
            presenter.MoveFocus(FocusNavigationDirection.Right);
            Check(direct.Value == 13 && actions[^1].Action.RequestedValue == 13, "right steps from minimum-anchored range");
            presenter.MoveFocus(FocusNavigationDirection.Right); presenter.MoveFocus(FocusNavigationDirection.Right);
            presenter.MoveFocus(FocusNavigationDirection.Right);
            Check(direct.Value == 21 && actions[^1].Action.RequestedValue == 21, "nonmultiple maximum remains reachable");
            var count = actions.Count;
            presenter.MoveFocus(FocusNavigationDirection.Right);
            Check(actions.Count == count && FocusId == "Widget.direct", "range boundary consumes direction without duplicate action");
            presenter.MoveFocus(FocusNavigationDirection.Left);
            Check(direct.Value == 19 && actions[^1].Action.RequestedValue == 19, "left from endpoint returns to minimum-anchored grid");
            direct.Value = 17;
            Check(direct.Value == 16 && actions[^1].Action.RequestedValue == 16, "native pointer or UIA value is quantized through shared SDK range");
            presenter.ActivateFocused();
            Check(actions[^1].Action.ActionId == "activate", "direct slider optional A action dispatches");
            count = actions.Count;
            value = 13; Apply();
            Check(ReferenceEquals(direct, Find(presenter, "Widget.direct")) && direct.Value == 13 && actions.Count == count,
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
            presenter.MoveFocus(FocusNavigationDirection.Right);
            Check(actions[^1].Action.SourceElementId == "adjust" && actions[^1].Action.RequestedValue == -130, "active adjustment steps value");
            presenter.MoveFocus(FocusNavigationDirection.Up);
            Check(FocusId == "Widget.adjust", "active adjustment consumes perpendicular direction");
            Check(await presenter.HandleControllerButtonAsync(ControllerButton.B), "B exits adjustment before widget shortcut");
            Check(await presenter.HandleControllerButtonAsync(ControllerButton.B, ControllerEventPhase.Released), "B release remains consumed after exiting adjustment");
            presenter.MoveFocus(FocusNavigationDirection.Right);
            Check(FocusId == "Widget.after", "B exit restores normal navigation");
            Focus("adjust"); await presenter.HandleControllerButtonAsync(ControllerButton.A);
            presenter.ResetPressedStyles();
            presenter.MoveFocus(FocusNavigationDirection.Right);
            Check(FocusId == "Widget.after", "host hide cancels adjustment mode");
            Focus("adjust"); await presenter.HandleControllerButtonAsync(ControllerButton.A);
            Focus("direct"); Focus("adjust");
            presenter.MoveFocus(FocusNavigationDirection.Right);
            Check(FocusId == "Widget.after", "focus departure cancels adjustment mode");
            Focus("adjust"); await presenter.HandleControllerButtonAsync(ControllerButton.A);
            alternate = true; Apply(); await Task.Delay(50);
            alternate = false; Apply(); await Task.Delay(50); Focus("adjust");
            presenter.MoveFocus(FocusNavigationDirection.Right);
            Check(FocusId == "Widget.after", "scope replacement cancels adjustment mode");
            count = actions.Count; disabled = true; Apply(); direct.Value = -120;
            Check(actions.Count == count, "disabled slider rejects native action dispatch");
            await presenter.DisposeAsync();
            if (failure is not null) throw failure;
            status.Text = $"Passed {checks.Count} slider checks"; WriteResult(new { result = "passed", checks });
        }
        catch (Exception error) { status.Text = "Slider validation failed: " + error.Message; WriteResult(new { result = "failed", checks, error = error.ToString() }); }
    }
    private void Focus(string id) => ((Control)Find(presenter, "Widget." + id)!).Focus(FocusState.Keyboard);
    private string FocusId => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject element ? AutomationProperties.GetAutomationId(element) : "";
    private void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
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
            Focus = adjust ? new() { Right = "after" } : null, IsDisabled = disabled };
        var snapshot = new ViewSnapshot { WidgetInstanceId = "slider.instance", Sequence = ++sequence,
            ActiveInputScopeId = alternate ? "dialog" : "page", InitialFocusId = alternate ? "other" : "direct",
            Root = new() { Id = "page", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[Slider("direct", false), Slider("adjust", true),
                new() { Id = "after", Kind = ViewNodeKind.Button, Text = "After", ActionId = "after" },
                new() { Id = "dialog", Kind = ViewNodeKind.Stack, InputScopeId = "dialog", Children = (ViewNode[])[
                    new() { Id = "other", Kind = ViewNodeKind.Button, Text = "Other", ActionId = "other" }] }] } };
        var descriptor = new BridgeWidgetDescriptor { Id = "slider", Name = "Slider validation", InstanceId = snapshot.WidgetInstanceId,
            RuntimeGeneration = "runtime", PresentationGeneration = "presentation", Icon = WidgetGlyph.Connection, PackageContentDigest = "" };
        var errors = ViewSnapshotValidator.Validate(snapshot);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors.Select(error => error.Path + ": " + error.Message)));
        presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
            snapshot.WidgetInstanceId, sequence, snapshot.ActiveInputScopeId), descriptor, SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)), new Dictionary<string, BridgeNodeRenderStyles>()));
    }
    private static void WriteResult<T>(T result)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
        Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "slider-controls-result.json"), JsonSerializer.Serialize(result));
    }
    public ValueTask DisposeAsync() => presenter.DisposeAsync();
}
