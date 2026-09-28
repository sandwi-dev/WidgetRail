using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Runs public SDK WithModal through the native presenter, without owning controller input.</summary>
internal sealed class ModalValidationPage : Page, IAsyncDisposable
{
    private readonly WidgetViewPresenter presenter = new();
    private readonly TextBlock status = new() { Text = "Modal checks pending", TextWrapping = TextWrapping.Wrap };
    private readonly Border widget;
    private readonly List<string> checks = [];
    private readonly List<WidgetActionRequest> actions = [];
    private long sequence;
    private long owner = 1;
    private string? modal;
    private string detailText = "Details";
    private Task? running;
    private Exception? failure;

    public ModalValidationPage()
    {
        AutomationProperties.SetAutomationId(status, "Modal.Result");
        widget = new Border { Width = 600, Height = 250, Child = presenter,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(20, 40, 0, 0) };
        Content = new Grid { Children = { status, widget } };
        presenter.Failed = error => failure = error;
        presenter.DispatchActionAsync = request => { actions.Add(request); return Task.CompletedTask; };
        Loaded += (_, _) => running ??= RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            Apply();
            await Until(() => Find("parent.scroll") is ScrollViewer { ScrollableHeight: > 0 });
            var parentScroll = (ScrollViewer)Find("parent.scroll")!;
            var parentButton = (Button)Find("game.12")!;
            parentButton.Focus(FocusState.Keyboard);
            parentScroll.ChangeView(null, 480, null, true);
            await Task.Delay(180);
            var offset = parentScroll.VerticalOffset;
            var extent = parentScroll.ScrollableHeight;
            var viewport = parentScroll.ViewportHeight;
            var parentWidth = parentScroll.ActualWidth;
            Check(offset > 0 && FocusedId == "Widget.game.12", "fixture begins at a scrolled parent item");
            var parentCommand = parentButton.Command!;
            var parentUnloads = 0;
            parentScroll.Unloaded += (_, _) => ++parentUnloads;

            modal = "details.game12"; Apply();
            await Until(() => FocusedId == "Widget.play" && Find(modal) is { ActualWidth: > 0 });
            var panel = (WidgetModalPanel)Find(modal)!;
            var modalLayer = AncestorLayer(panel);
            Check(((Border)modalLayer.Chrome).Background is SolidColorBrush { Color.A: 163, Color.R: 12, Color.G: 24, Color.B: 36 } &&
                modalLayer.Background is null, "authored modal veil paints the scrim above the parent instead of beneath it");
            await Until(() => modalLayer.OpeningMotion is not null);
            var opening = modalLayer.OpeningMotion!;
            Check(await opening.WaitAsync(TimeSpan.FromSeconds(2)) == WidgetMotionOutcome.Completed,
                "real presenter dialog opening completes on the native composition batch");
            Check(ReferenceEquals(parentScroll, Find("parent.scroll")) && ReferenceEquals(parentButton, Find("game.12")), "modal retains original parent controls");
            Check(Near(parentScroll.VerticalOffset, offset) && Near(parentScroll.ScrollableHeight, extent) &&
                Near(parentScroll.ViewportHeight, viewport) && Near(parentScroll.ActualWidth, parentWidth), "opening preserves parent viewport geometry and offset");
            Check(parentButton.IsEnabled && !parentButton.IsTabStop && !parentButton.IsHitTestVisible,
                "inactive parent remains visually enabled but cannot receive input");
            parentButton.Focus(FocusState.Keyboard);
            Check(FocusedId == "Widget.play", "programmatic focus cannot enter inactive parent scope");
            parentCommand.Execute(null);
            Check(actions.Count == 0, "retained parent command has no modal action authority");
            Check(AutomationProperties.GetIsDialog(panel), "dialog is exposed to automation");
            CheckLocalBounds(panel, "dialog is bounded inside corner-positioned widget");

            var modalScroll = (ScrollViewer)Find(modal + ".scroll")!;
            await Until(() => modalScroll.ScrollableHeight > 0);
            modalScroll.ChangeView(null, 200, null, true);
            await Task.Delay(100);
            Check(modalScroll.VerticalOffset > 0 && Near(parentScroll.VerticalOffset, offset), "dialog scroll is independent of parent");
            ((Button)Find("second")!).Focus(FocusState.Keyboard);
            await Task.Delay(100);
            var modalOffset = modalScroll.VerticalOffset;
            detailText = "Updated details"; Apply();
            await Task.Delay(100);
            Check(ReferenceEquals(panel, Find(modal)) && FocusedId == "Widget.second" && Near(modalScroll.VerticalOffset, modalOffset),
                "ordinary dialog update preserves control focus and scroll");
            Check(ReferenceEquals(opening, modalLayer.OpeningMotion), "ordinary dialog snapshot does not replay entrance motion");
            presenter.Enter(restoreNativeFocus: true);
            await Task.Delay(100);
            Check(FocusedId == "Widget.second", "host foreground restoration preserves dialog focus");
            for (var count = 0; count < 4; ++count) presenter.MoveFocus(FocusNavigationDirection.Left);
            Check(!FocusedId.StartsWith("Widget.game.", StringComparison.Ordinal), "controller boundary remains in dialog");

            ((Button)Find("picker")!).Focus(FocusState.Keyboard);
            presenter.ActivateFocused();
            await Until(() => presenter.HasTransientControl);
            Check(presenter.DismissTransientControl() && Find(modal) is not null, "Select dismisses before its owning modal");
            await Task.Delay(150);

            widget.Width = 330; widget.Height = 180;
            await Until(() => panel.ActualWidth < 330 && panel.ActualHeight < 180);
            CheckLocalBounds(panel, "dialog reclamps on small widget resize");
            widget.Width = 600; widget.Height = 250;
            await Until(() => panel.ActualWidth > 500 && panel.ActualHeight > 200);
            CheckLocalBounds(panel, "dialog expands again without stale small bounds");

            ((Button)Find("second")!).Focus(FocusState.Keyboard);
            modal = "details.game13"; Apply();
            await Until(() => FocusedId == "Widget.play");
            Check(!ReferenceEquals(panel, Find(modal)), "new modal scope gets fresh controls and initial focus");
            var retiredPlay = ((Button)Find("play")!).Command!;
            var retiredButton = (Button)Find("play")!;
            modal = null; Apply();
            Check(presenter.HasClosingModal, "closing retains only a noninteractive dialog visual while publishing the parent");
            await Until(() => FocusedId == "Widget.game.12");
            Check(ReferenceEquals(parentScroll, Find("parent.scroll")) && Near(parentScroll.VerticalOffset, offset),
                "closing returns exact parent focus and viewport");
            Check(parentUnloads == 0, "modal opening and closing keep the parent mounted in one native stage");
            retiredPlay.Execute(null);
            Check(actions.Count == 0, "retired dialog command cannot dispatch after close");
            retiredButton.Focus(FocusState.Keyboard);
            Check(FocusedId == "Widget.game.12", "closing dialog visual cannot regain native focus");
            parentCommand.Execute(null);
            await Until(() => actions.Count == 1);
            Check(actions[0].Displayed.Authority.SnapshotSequence == sequence && actions[0].Action.InputScopeId == "parent",
                "parent action during exit uses the new exact displayed frame and parent scope");
            actions.Clear();
            await Until(() => presenter.ModalExitPlayback is not null);
            Check(await presenter.ModalExitPlayback! == WidgetMotionOutcome.Completed,
                "native modal exit completes independently of restored parent focus");
            await Until(() => !presenter.HasClosingModal);
            Check(Find("details.game13") is null, "completed exit releases the old dialog tree");

            modal = "details.game13"; Apply();
            await Until(() => FocusedId == "Widget.play");
            Check(FocusedId == "Widget.play", "reopening removed dialog scope starts at initial focus");
            presenter.ActivateFocused();
            await Until(() => actions.Count == 1);
            Check(actions[0].Action.ActionId == "play" && actions[0].Action.InputScopeId == modal + ".scope", "dialog action dispatches once with its own scope");
            var firstReopened = Find(modal);
            modal = null; Apply();
            modal = "details.game13"; Apply();
            await Until(() => FocusedId == "Widget.play");
            Check(!presenter.HasClosingModal && !ReferenceEquals(firstReopened, Find(modal)),
                "rapid close and reopen discards the old exit and enters only the latest dialog");
            modal = null; Apply();
            await Until(() => presenter.HasClosingModal);
            presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
            Check(!presenter.HasClosingModal, "changing to reduced motion releases an in-flight dialog exit immediately");
            modal = "details.game13"; Apply();
            await Until(() => FocusedId == "Widget.play");
            modal = null; Apply();
            Check(!presenter.HasClosingModal, "reduced-motion close does not retain an outgoing dialog");
            modal = "details.game13"; Apply();
            await Until(() => FocusedId == "Widget.play");
            var priorPanel = Find(modal);
            presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
            ++owner; Apply();
            await Until(() => FocusedId == "Widget.play");
            Check(!ReferenceEquals(priorPanel, Find(modal)), "runtime replacement retires dialog identity");
            var reducedLayer = AncestorLayer(Find(modal)!);
            await Until(() => reducedLayer.OpeningMotion is not null);
            Check(reducedLayer.OpeningMotion!.IsCompletedSuccessfully && await reducedLayer.OpeningMotion == WidgetMotionOutcome.Completed,
                "global reduced-motion settings settle a fresh dialog without an animation timer");
            presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Full }, true);
            modal = null; Apply();
            Check(presenter.HasClosingModal, "fixture creates an exit for teardown validation");
            await presenter.DisposeAsync();
            Check(!presenter.HasClosingModal, "presenter teardown revokes and releases pending dialog exits");
            if (failure is not null) throw failure;
            status.Text = $"Passed {checks.Count} modal checks";
            WriteResult(new { result = "passed", checks });
        }
        catch (Exception error)
        {
            status.Text = "Modal validation failed: " + error.Message;
            WriteResult(new { result = "failed", checks, error = error.ToString() });
        }
    }

    private void Apply()
    {
        var parent = new WidgetView(UI.Stack("parent",
            UI.Text("Library", "heading"),
            UI.VerticalScroll("parent.scroll", Enumerable.Range(0, 30)
                .Select(index => (WidgetElement)UI.Button("Game " + index, "game." + index, "game." + index)).ToArray()))
            .InputScope("parent"), InitialFocusId: "game.12", ActiveInputScopeId: "parent");
        var view = modal is null ? parent : parent.WithModal(new(modal, detailText,
            UI.Stack("details.content", UI.Button("Play", "play", "play"), UI.Button("Second", "second", "second"),
                UI.Select("Completion", [new("none", "None", "none", true), new("done", "Done", "done")], "picker"),
                UI.Stack("long.description", Enumerable.Range(0, 60).Select(index =>
                    (WidgetElement)UI.Text($"Description line {index}", "line." + index)).ToArray())), "play", "dismiss"));
        var snapshot = view.CreateSnapshot("modal.instance", ++sequence);
        var descriptor = new BridgeWidgetDescriptor { Id = "modal", Name = "Modal validation", InstanceId = snapshot.WidgetInstanceId,
            RuntimeGeneration = $"runtime-{owner}", PresentationGeneration = "presentation", Icon = WidgetGlyph.Connection, PackageContentDigest = "" };
        var style = new Dictionary<string, BridgeComputedStyleValue> { ["background"] = new() { Kind = WrssValueKind.Color, Text = "rgba(12, 24, 36, 0.64)" } };
        var styles = modal is null ? new Dictionary<string, BridgeNodeRenderStyles>() : new Dictionary<string, BridgeNodeRenderStyles>
            { [snapshot.Root.Id] = new() { Base = style, Focused = style, Pressed = style } };
        presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, owner,
            snapshot.WidgetInstanceId, sequence, snapshot.ActiveInputScopeId), descriptor, snapshot,
            styles));
    }

    private void CheckLocalBounds(FrameworkElement panel, string name)
    {
        var location = panel.TransformToVisual(widget).TransformPoint(new(0, 0));
        Check(location.X >= 15 && location.Y >= 15 && location.X + panel.ActualWidth <= widget.ActualWidth - 15 &&
            location.Y + panel.ActualHeight <= widget.ActualHeight - 15, name);
    }
    private static bool Near(double first, double second) => Math.Abs(first - second) < 1;
    private static WidgetModalLayer AncestorLayer(DependencyObject element)
    {
        for (var current = VisualTreeHelper.GetParent(element); current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is WidgetModalLayer layer) return layer;
        throw new InvalidOperationException("Missing native modal layer.");
    }
    private string FocusedId => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused
        ? AutomationProperties.GetAutomationId(focused) : string.Empty;
    private FrameworkElement? Find(string id) => Find(presenter, "Widget." + id);
    private static FrameworkElement? Find(DependencyObject element, string id)
    {
        if (element is FrameworkElement candidate && AutomationProperties.GetAutomationId(candidate) == id) return candidate;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); ++index)
            if (Find(VisualTreeHelper.GetChild(element, index), id) is { } found) return found;
        return null;
    }
    private static async Task Until(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 150; ++attempt) { if (condition()) return; await Task.Delay(20); }
        throw new TimeoutException("Modal layout/focus did not settle");
    }
    private void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        checks.Add(name);
    }
    private static void WriteResult<T>(T value)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "modal-controls-result.json"), JsonSerializer.Serialize(value));
    }
    public ValueTask DisposeAsync() => presenter.DisposeAsync();
}
