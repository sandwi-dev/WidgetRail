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

/// <summary>Real native popup lifetime checks. Hardware input is deliberately absent.</summary>
internal sealed class ContextMenuControlValidationPage : Page, IAsyncDisposable
{
    private readonly WidgetViewPresenter presenter = new();
    private readonly TextBlock status = new() { Text = "Context menu validation waiting for layout" };
    private readonly List<string> checks = [];
    private readonly List<WidgetActionRequest> actions = [];
    private long sequence;
    private long runtime = 1;
    private bool disabled, ambiguous, modal, removed, rebound;
    private Exception? failure;
    private Task? run;
    public ContextMenuControlValidationPage()
    {
        AutomationProperties.SetAutomationId(status, "ContextMenu.Status");
        Content = new StackPanel { Spacing = 12, Children = { status, presenter } };
        presenter.Failed = error => failure = error;
        presenter.DispatchActionAsync = request => { actions.Add(request); return Task.CompletedTask; };
        Loaded += (_, _) => run ??= RunAsync();
    }
    private async Task RunAsync()
    {
        try
        {
            Apply();
            await OpenAsync(ControllerButton.X, "poster", "poster");
            Check(FocusedId.EndsWith("Context.play", StringComparison.Ordinal), "focused surface X menu wins over scoped X hint");
            presenter.MoveFocus(FocusNavigationDirection.Down);
            Check(FocusedId.EndsWith("Context.remove", StringComparison.Ordinal), "navigation skips disabled and busy actions");
            presenter.MoveFocus(FocusNavigationDirection.Down); presenter.MoveFocus(FocusNavigationDirection.Right);
            Check(FocusedId.EndsWith("Context.remove", StringComparison.Ordinal), "popup boundaries consume all navigation");
            Apply();
            Check(presenter.HasTransientControl, "unrelated publication retains popup");
            Check(FocusedId.EndsWith("Context.remove", StringComparison.Ordinal), "unrelated publication preserves chosen menu item");
            await presenter.HandleControllerButtonAsync(ControllerButton.Y);
            await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
            Check(actions.Count == 0, "popup consumes parent shortcuts and release");
            await presenter.HandleControllerButtonAsync(ControllerButton.A);
            await WaitAsync(() => actions.Count == 1);
            Check(actions[0].Action.ActionId == "remove" && actions[0].Action.SourceElementId == "poster" &&
                actions[0].Action.InputScopeId == "page" && actions[0].Authority.SnapshotSequence == sequence,
                "selection dispatches exact action owner and current displayed authority");
            Check(!presenter.HasTransientControl, "selection revokes popup before dispatch");
            await WaitAsync(() => FocusedId == "Widget.poster");
            Check(true, "selection returns focus to exact opener");
            await OpenAsync(ControllerButton.Menu, "poster", "hint");
            Check(FocusedId == "Widget.hint.Context.more", "Menu remains scoped more-options while X opens game options");
            await presenter.HandleControllerButtonAsync(ControllerButton.B);
            await WaitAsync(() => FocusedId == "Widget.poster");
            Check(actions.Count == 1 && !presenter.HasTransientControl, "back returns focus without invoking parent back");
            await OpenAsync(ControllerButton.Menu, "default", "default");
            Check(true, "action surface retains default Menu trigger");
            presenter.DismissTransientControl();
            await OpenAsync(ControllerButton.X, "after", "xhint");
            Check(true, "nonfocusable scope hint works away from an action surface");
            presenter.DismissTransientControl();
            await Task.Delay(150);
            ambiguous = true; Apply();
            await WaitAsync(() => Find(presenter, "Widget.ambiguous") is { ActualWidth: > 0, ActualHeight: > 0 });
            await FocusAsync("after");
            Check(!await presenter.HandleControllerButtonAsync(ControllerButton.X) && !presenter.HasTransientControl,
                "ambiguous scope hints never open a popup");
            ambiguous = false; Apply();
            await OpenAsync(ControllerButton.X, "poster", "poster");
            rebound = true; Apply();
            Check(!presenter.HasTransientControl, "action rebinding revokes popup");
            await OpenAsync(ControllerButton.X, "poster", "poster");
            disabled = true; Apply(); Check(!presenter.HasTransientControl, "disabled owner revokes popup");
            disabled = false; Apply();
            await OpenAsync(ControllerButton.X, "poster", "poster");
            modal = true; Apply(); Check(!presenter.HasTransientControl, "modal scope supersedes parent popup");
            await OpenAsync(ControllerButton.X, "modal.button", "modal.button");
            await presenter.HandleControllerButtonAsync(ControllerButton.B);
            await WaitAsync(() => FocusedId == "Widget.modal.button");
            Check(actions.Count == 1, "nested menu back preserves modal and returns its focus");
            modal = false; Apply();
            await OpenAsync(ControllerButton.X, "poster", "poster");
            ++runtime; Apply(); Check(!presenter.HasTransientControl, "runtime replacement revokes popup");
            await OpenAsync(ControllerButton.X, "poster", "poster");
            removed = true; Apply(); Check(!presenter.HasTransientControl, "removed owner revokes popup");
            removed = false; Apply();
            await OpenAsync(ControllerButton.X, "poster", "poster");
            presenter.DismissTransientControl();
            await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
            Check(actions.Count == 1, "hidden-host dismissal and release do not invoke a menu action");
            await OpenAsync(ControllerButton.X, "poster", "poster");
            var nativeItem = (FrameworkElement)FocusManager.GetFocusedElement(XamlRoot);
            var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(nativeItem);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            await WaitAsync(() => actions.Count == 2);
            Check(actions[1].Action.ActionId == "play.changed", "native menu invoke dispatches once before popup closes");
            await OpenAsync(ControllerButton.X, "poster", "poster");
            await presenter.DisposeAsync();
            Check(!presenter.HasTransientControl, "disposal revokes popup");
            if (failure is not null) throw failure;
            status.Text = $"Passed {checks.Count} context menu checks";
            WriteResult(new { result = "passed", checks });
        }
        catch (Exception error)
        { status.Text = "Context menu validation failed: " + error.Message; WriteResult(new { result = "failed", checks, error = error.ToString(), actions }); }
    }
    private async Task OpenAsync(ControllerButton button, string focused, string owner)
    {
        await Task.Delay(175);
        await FocusAsync(focused);
        Check(await presenter.HandleControllerButtonAsync(button), $"{button} handled for {owner} at {sequence}");
        await WaitAsync(() => presenter.HasTransientControl && FocusedId.StartsWith($"Widget.{owner}.Context.", StringComparison.Ordinal));
    }
    private async Task FocusAsync(string id)
    {
        await WaitAsync(() => Find(presenter, "Widget." + id) is Control { IsLoaded: true });
        ((Control)Find(presenter, "Widget." + id)!).Focus(FocusState.Keyboard);
        await WaitAsync(() => FocusedId == "Widget." + id);
    }
    private string FocusedId => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject element ? AutomationProperties.GetAutomationId(element) : "";
    private static async Task WaitAsync(Func<bool> condition)
    { for (var attempt = 0; attempt < 120; ++attempt) { if (condition()) return; await Task.Delay(25); } throw new TimeoutException("Context menu did not settle"); }
    private void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
    private static FrameworkElement? Find(DependencyObject root, string id)
    {
        if (root is FrameworkElement element && AutomationProperties.GetAutomationId(element) == id) return element;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i) if (Find(VisualTreeHelper.GetChild(root, i), id) is { } found) return found;
        return null;
    }
    private void Apply()
    {
        ViewNode Poster(string id, ControllerButton? button) => new() { Id = id, Kind = ViewNodeKind.ActionSurface,
            ActionId = "open", AccessibilityLabel = id, ActionSurfaceOrientation = ActionSurfaceOrientation.Horizontal, ContextMenuButton = button, IsDisabled = disabled,
            ContextActions = [ new(rebound ? "play.changed" : "play", "Play"), new("disabled", "Disabled", IsDisabled: true),
                new("busy", "Busy", IsBusy: true), new("remove", "Remove", WidgetContextActionStyle.Danger)],
            Children = [new() { Id = id + ".text", Kind = ViewNodeKind.Text, Text = id }] };
        ViewNode Hint(string id, ControllerButton button) => new() { Id = id, Kind = ViewNodeKind.Row, ContextMenuButton = button,
            ContextActions = [new("more", "More options")], Children = [new() { Id = id + ".text", Kind = ViewNodeKind.Text, Text = id }] };
        var nodes = new List<ViewNode> { Hint("hint", ControllerButton.Menu), Hint("xhint", ControllerButton.X),
            new() { Id = "after", Kind = ViewNodeKind.Button, Text = "After", ActionId = "after" }, Poster("default", null) };
        if (!removed) nodes.Add(Poster("poster", ControllerButton.X));
        if (ambiguous) nodes.Add(Hint("ambiguous", ControllerButton.X));
        nodes.Add(new() { Id = "modal", Kind = ViewNodeKind.Stack, InputScopeId = "dialog", Children = [Poster("modal.button", ControllerButton.X)] });
        var snapshot = new ViewSnapshot { WidgetInstanceId = "context.instance", Sequence = ++sequence,
            ActiveInputScopeId = modal ? "dialog" : "page", InitialFocusId = modal ? "modal.button" : "after",
            Root = new() { Id = "page", Kind = ViewNodeKind.Stack, Children = nodes } };
        var descriptor = new BridgeWidgetDescriptor { Id = "context", Name = "Context validation", InstanceId = snapshot.WidgetInstanceId,
            RuntimeGeneration = "runtime-" + runtime, PresentationGeneration = "presentation", Icon = WidgetGlyph.Connection, PackageContentDigest = "" };
        var errors = ViewSnapshotValidator.Validate(snapshot);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors.Select(error => error.Path + ": " + error.Message)));
        presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, runtime,
            snapshot.WidgetInstanceId, sequence, snapshot.ActiveInputScopeId), descriptor,
            SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)), new Dictionary<string, BridgeNodeRenderStyles>()));
    }
    private static void WriteResult<T>(T value)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
        Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "context-menu-result.json"), JsonSerializer.Serialize(value));
    }
    public ValueTask DisposeAsync() => presenter.DisposeAsync();
}
