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

/// <summary>Autonomous real-XAML Select lifecycle and controller-route checks; no controller owner.</summary>
internal sealed class SelectControlValidationPage : Page, IAsyncDisposable
{
    private readonly WidgetViewPresenter presenter = new();
    private readonly TextBlock status = new() { Text = "Select validation waiting for layout" };
    private readonly List<string> checks = [];
    private readonly List<WidgetActionRequest> actions = [];
    private WidgetPresentationFrame? displayed;
    private long sequence;
    private long owner = 1;
    private bool alternateScope;
    private bool disabled;
    private bool removed;
    private string thirdAction = "choose.third";
    private Task? run;
    private Exception? failure;

    public SelectControlValidationPage()
    {
        AutomationProperties.SetAutomationId(status, "Select.Status");
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
            await OpenAsync();
            Check(FocusedId == "Widget.picker.Option.first", "selected option receives initial focus");
            presenter.MoveFocus(FocusNavigationDirection.Down);
            Check(FocusedId == "Widget.picker.Option.third", "disabled and busy options are skipped");
            presenter.MoveFocus(FocusNavigationDirection.Down);
            presenter.MoveFocus(FocusNavigationDirection.Right);
            Check(FocusedId == "Widget.picker.Option.third", "popup boundary consumes navigation");
            Apply();
            Check(presenter.HasTransientControl, "unchanged snapshot preserves open menu");
            Check(await presenter.HandleControllerButtonAsync(ControllerButton.Y) && actions.Count == 0,
                "popup captures parent shortcut before worker dispatch");
            await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
            Check(actions.Count == 0, "release does not commit popup");
            await presenter.HandleControllerButtonAsync(ControllerButton.A);
            await WaitAsync(() => actions.Count == 1);
            Check(actions[0].Action.ActionId == "choose.third" && actions[0].Action.SourceElementId == "picker" &&
                actions[0].Action.InputScopeId == "page" && actions[0].Authority.SnapshotSequence == sequence,
                "commit dispatches exact current option with opener authority once");
            Check(!presenter.HasTransientControl, "commit revokes popup before action dispatch");

            await OpenAsync();
            presenter.ActivateFocused();
            await WaitAsync(() => actions.Count == 2);
            Check(actions[1].Action.ActionId == "choose.first", "selected option remains invokable");
            await OpenAsync();
            Check(await presenter.HandleControllerButtonAsync(ControllerButton.B), "back dismisses popup before parent input");
            await Task.Delay(150);
            Check(actions.Count == 2 && !presenter.DismissTransientControl(), "dismissal never commits or consumes a second back");

            await OpenAsync();
            thirdAction = "choose.rebound"; Apply();
            Check(!presenter.HasTransientControl && actions.Count == 2, "changed option authority closes popup without action");
            await OpenAsync();
            disabled = true; Apply();
            Check(!presenter.HasTransientControl, "disabled opener revokes popup");
            disabled = false; Apply();
            await OpenAsync();
            alternateScope = true; Apply();
            Check(!presenter.HasTransientControl, "scope change revokes popup");
            alternateScope = false; Apply();
            await OpenAsync();
            ++owner; Apply();
            Check(!presenter.HasTransientControl, "runtime replacement revokes popup");
            await OpenAsync();
            removed = true; Apply();
            Check(!presenter.HasTransientControl, "removed opener revokes popup");
            removed = false; Apply();
            await OpenAsync();
            var origin = displayed;
            var dispatched = new TaskCompletionSource<WidgetActionRequest>();
            var acknowledged = new TaskCompletionSource();
            presenter.DispatchActionAsync = async request =>
            {
                dispatched.SetResult(request);
                await acknowledged.Task;
            };
            presenter.ActivateFocused();
            var captured = await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Apply(); // Simulate a publication arriving before lifecycle acknowledgment.
            Check(ReferenceEquals(captured.Displayed, origin) && !ReferenceEquals(captured.Displayed, displayed),
                "popup commit retains exact displayed frame across pending acknowledgment");
            Check(captured.Action.ActionId == "choose.first" && captured.Action.SourceElementId == "picker",
                "pending popup action keeps captured target and payload");
            acknowledged.SetResult();
            await OpenAsync();
            await presenter.DisposeAsync();
            await Task.Delay(150);
            Check(!presenter.HasTransientControl, "presenter disposal dismisses popup");
            if (failure is not null) throw failure;
            status.Text = $"Passed {checks.Count} Select checks";
            WriteResult(new { result = "passed", checks });
        }
        catch (Exception error)
        {
            status.Text = "Select validation failed: " + error.Message;
            WriteResult(new { result = "failed", checks, error = error.ToString() });
        }
    }

    private async Task OpenAsync()
    {
        await Task.Delay(150); // let WinUI finish the preceding popup's close
        var opener = Find(presenter, "Widget.picker") as Button ?? throw new InvalidOperationException("Missing Select opener");
        opener.Focus(FocusState.Keyboard);
        presenter.ActivateFocused();
        await WaitAsync(() => presenter.HasTransientControl && FocusedId.StartsWith("Widget.picker.Option.", StringComparison.Ordinal));
    }

    private string FocusedId => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject element
        ? AutomationProperties.GetAutomationId(element) : string.Empty;

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; ++attempt)
        { if (condition()) return; await Task.Delay(25); }
        throw new TimeoutException("Select control did not settle");
    }

    private void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        checks.Add(name);
    }

    private static FrameworkElement? Find(DependencyObject element, string id)
    {
        if (element is FrameworkElement candidate && AutomationProperties.GetAutomationId(candidate) == id) return candidate;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); ++index)
            if (Find(VisualTreeHelper.GetChild(element, index), id) is { } found) return found;
        return null;
    }

    private void Apply()
    {
        var options = new WidgetSelectOption[] {
            new("first", "First", "choose.first", IsSelected: true),
            new("disabled", "Unavailable", "choose.disabled", IsDisabled: true),
            new("busy", "Busy", "choose.busy", IsBusy: true),
            new("third", "Third", thirdAction),
        };
        var snapshot = new ViewSnapshot { WidgetInstanceId = "select.instance", Sequence = ++sequence,
            ActiveInputScopeId = alternateScope ? "other" : "page", InitialFocusId = alternateScope ? "other.button" : removed ? "after" : "picker",
            Root = new() { Id = "page", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[
                removed ? new() { Id = "replacement", Kind = ViewNodeKind.Text, Text = "Removed" } :
                    new() { Id = "picker", Kind = ViewNodeKind.Select, Text = "Choice: First", AccessibilityLabel = "Choice",
                        AccessibilityValue = "First", SelectOptions = options, IsDisabled = disabled },
                new() { Id = "after", Kind = ViewNodeKind.Button, Text = "After", ActionId = "after" },
                new() { Id = "other", Kind = ViewNodeKind.Stack, InputScopeId = "other", Children = (ViewNode[])[
                    new() { Id = "other.button", Kind = ViewNodeKind.Button, Text = "Other scope", ActionId = "other" }] },
            ] } };
        var descriptor = new BridgeWidgetDescriptor { Id = "select", Name = "Select validation", InstanceId = snapshot.WidgetInstanceId,
            RuntimeGeneration = $"runtime-{owner}", PresentationGeneration = "presentation", Icon = WidgetGlyph.Connection, PackageContentDigest = "" };
        var errors = ViewSnapshotValidator.Validate(snapshot);
        if (errors.Count != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(error => error.Path + ": " + error.Message)));
        displayed = new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, owner,
            snapshot.WidgetInstanceId, sequence, snapshot.ActiveInputScopeId), descriptor,
            SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)), new Dictionary<string, BridgeNodeRenderStyles>());
        presenter.Apply(displayed);
    }

    private static void WriteResult<T>(T result)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "select-controls-result.json"), JsonSerializer.Serialize(result));
    }

    public ValueTask DisposeAsync() => presenter.DisposeAsync();
}
