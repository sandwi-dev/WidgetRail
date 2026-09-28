using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed class TextEntryValidationPage : Page, IAsyncDisposable
{
    private readonly WidgetViewPresenter presenter = new();
    private readonly TextBlock status = new() { Text = "Text entry validation waiting" };
    private readonly List<string> checks = [];
    private readonly List<WidgetActionRequest> actions = [];
    private long sequence;
    private long owner = 1;
    private string value = "ab";
    private string action = "commit";
    private bool sensitive;
    private bool disabled;
    private bool otherScope;
    private bool throwOnCommit;
    private string? failure;
    private Task? run;

    public TextEntryValidationPage()
    {
        AutomationProperties.SetAutomationId(status, "TextEntry.Status");
        Content = new StackPanel { Spacing = 12, Children = { status, presenter } };
        presenter.Failed = error => failure = error.Message;
        presenter.DispatchActionAsync = request =>
        {
            if (throwOnCommit) throw new InvalidOperationException("secret echo " + request.Action.CommittedText);
            actions.Add(request); return Task.CompletedTask;
        };
        Loaded += (_, _) =>
        {
            if (Environment.GetCommandLineArgs().Contains("--text-entry-preview"))
            { Apply(); status.Text = "Text entry preview — activate the field"; }
            else run ??= RunAsync();
        };
    }
    private async Task RunAsync()
    {
        try
        {
            Apply(); await Open();
            Check(FocusedId == "Widget.TextEntry.Key.10", "controller starts on first letter without click");
            presenter.MoveFocus(FocusNavigationDirection.Right);
            Check(FocusedId == "Widget.TextEntry.Key.11", "controller navigation uses native keyboard geometry");
            presenter.MoveFocus(FocusNavigationDirection.Left);
            for (var i = 0; i < 8; ++i) presenter.MoveFocus(FocusNavigationDirection.Up);
            Check(FocusedId == "Widget.TextEntry.Key.0", "controller boundary stays inside virtual keyboard");
            presenter.MoveFocus(FocusNavigationDirection.Down);
            presenter.SetControllerFamily(WidgetRail.OverlayPlatformClient.ControllerFamily.PlayStation);
            Check(AutomationProperties.GetName(FindDialog("Widget.TextEntry.Commit")!) == "Done, R2 trigger", "open keyboard follows controller prompt family");
            presenter.SetControllerFamily(WidgetRail.OverlayPlatformClient.ControllerFamily.Xbox);
            Check(Editor() is TextBox { Text: "ab", MaxLength: 4 }, "native edit receives bounded authored value");
            await presenter.HandleControllerButtonAsync(ControllerButton.LeftBumper);
            await presenter.HandleControllerButtonAsync(ControllerButton.A);
            Check(((TextBox)Editor()).Text == "aqb", "controller inserts at moved caret");
            await presenter.HandleControllerButtonAsync(ControllerButton.X);
            Check(((TextBox)Editor()).Text == "ab", "controller backspace retains surrounding text");
            await presenter.HandleControllerButtonAsync(ControllerButton.Y);
            await presenter.HandleControllerButtonAsync(ControllerButton.LeftTrigger);
            await presenter.HandleControllerButtonAsync(ControllerButton.A);
            Check(((TextBox)Editor()).Text == "Q", "clear and shift operate locally");
            for (var i = 0; i < 8; ++i) await presenter.HandleControllerButtonAsync(ControllerButton.A);
            Check(((TextBox)Editor()).Text.Length == 4 && actions.Count == 0, "maximum length enforced without intermediate worker actions");
            Apply(); Check(presenter.HasTransientControl && ((TextBox)Editor()).Text.Length == 4, "harmless snapshot preserves edits");
            await presenter.HandleControllerButtonAsync(ControllerButton.RightTrigger, ControllerEventPhase.Released);
            Check(actions.Count == 0, "release does not commit");
            await presenter.HandleControllerButtonAsync(ControllerButton.RightTrigger);
            Check(actions.Count == 1 && actions[0].Action.CommittedText == "QQQQ" && actions[0].Authority.SnapshotSequence == sequence,
                "one commit carries final value and latest authority");
            await Closed(); Check(FocusedId == "Widget.entry", "closing restores opener focus");
            await Open(); await presenter.HandleControllerButtonAsync(ControllerButton.B); await Closed();
            Check(actions.Count == 1, "cancel dispatches nothing");
            await Open(); action = "replacement"; Apply(); await Closed();
            Check(actions.Count == 1, "changed action revokes editing authority");
            await Open(); value = "new"; Apply(); await Closed(); Check(actions.Count == 1, "changed authored value cancels pending edits");
            await Open(); disabled = true; Apply(); await Closed(); Check(actions.Count == 1, "disabled opener cancels edits");
            disabled = false; Apply(); await Open(); otherScope = true; Apply(); await Closed(); Check(actions.Count == 1, "scope switch cancels edits");
            otherScope = false; Apply(); await Open(); ++owner; Apply(); await Closed(); Check(actions.Count == 1, "runtime replacement cancels edits");
            sensitive = true; value = ""; Apply(); await Open();
            var password = (PasswordBox)Editor();
            Check(new PasswordBoxAutomationPeer(password).IsPassword(), "sensitive editor has protected UI Automation semantics");
            Check(password.PasswordRevealMode == PasswordRevealMode.Hidden && password.Password.Length == 0, "sensitive editor starts empty and cannot reveal");
            await presenter.HandleControllerButtonAsync(ControllerButton.A);
            Check(password.Password == "q" && actions.Count == 1, "sensitive edits remain local");
            await presenter.HandleControllerButtonAsync(ControllerButton.RightTrigger); await Closed();
            Check(actions.Count == 2 && actions[1].Action.CommittedText == "q" && password.Password.Length == 0,
                "sensitive commit transfers once and clears native edit");
            actions.Clear(); // Never write committed values to diagnostic artifacts.
            await Open(); password = (PasswordBox)Editor(); await presenter.HandleControllerButtonAsync(ControllerButton.A);
            presenter.DismissTransientControl(); await Closed();
            Check(password.Password.Length == 0 && actions.Count == 0, "hide/cancel clears secret without commit");
            throwOnCommit = true; await Open(); await presenter.HandleControllerButtonAsync(ControllerButton.A);
            await presenter.HandleControllerButtonAsync(ControllerButton.RightTrigger); await Closed();
            Check(failure == "Text entry commit failed.", "exception diagnostics cannot echo committed secret");
            throwOnCommit = false; failure = null;
            await Open(); await presenter.DisposeAsync(); await Closed(); Check(!presenter.HasTransientControl, "disposal retires keyboard");
            status.Text = $"Passed {checks.Count} TextEntry checks"; Write(new { result = "passed", checks });
        }
        catch (Exception error) { status.Text = "TextEntry validation failed: " + error.Message; Write(new { result = "failed", checks, error = error.ToString() }); }
    }
    private async Task Open()
    {
        await Task.Delay(200);
        ((Control)Find(presenter, "Widget.entry")!).Focus(FocusState.Keyboard);
        presenter.ActivateFocused();
        for (var i = 0; i < 100; ++i) { if (EditorOrNull() is not null && FocusedId.StartsWith("Widget.TextEntry.Key.", StringComparison.Ordinal)) return; await Task.Delay(25); }
        throw new TimeoutException("Keyboard did not open");
    }
    private async Task Closed()
    {
        for (var i = 0; i < 100; ++i) { if (!presenter.HasTransientControl && EditorOrNull() is null) { await Task.Delay(100); return; } await Task.Delay(25); }
        throw new TimeoutException("Keyboard did not close");
    }
    private FrameworkElement? FindDialog(string id) => Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot)
        .Select(popup => Find(popup.Child, id)).FirstOrDefault(element => element is not null);
    private FrameworkElement Editor() => EditorOrNull() ?? throw new InvalidOperationException("Missing native editor");
    private FrameworkElement? EditorOrNull() => Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot)
        .Select(popup => Find(popup.Child, "Widget.TextEntry.Editor")).FirstOrDefault(element => element is not null);
    private string FocusedId => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused ? AutomationProperties.GetAutomationId(focused) : "";
    private void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
    private static FrameworkElement? Find(DependencyObject root, string id)
    {
        if (root is FrameworkElement element && AutomationProperties.GetAutomationId(element) == id) return element;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i) if (Find(VisualTreeHelper.GetChild(root, i), id) is { } found) return found;
        return null;
    }
    private void Apply()
    {
        var snapshot = new ViewSnapshot { WidgetInstanceId = "text.instance", Sequence = ++sequence,
            ActiveInputScopeId = otherScope ? "other" : "page", InitialFocusId = otherScope ? "other.button" : "entry",
            Root = new() { Id = "page", Kind = ViewNodeKind.Stack, Children = [
                new() { Id = "entry", Kind = ViewNodeKind.TextEntry, AccessibilityLabel = "Text entry", TextEntryValue = value, TextEntryPlaceholder = "Enter text", TextEntryMaximumLength = 4,
                    TextEntryInputKind = sensitive ? TextEntryInputKind.Sensitive : null, ActionId = action, IsDisabled = disabled },
                new() { Id = "other", Kind = ViewNodeKind.Stack, InputScopeId = "other", Children = [
                    new() { Id = "other.button", Kind = ViewNodeKind.Button, Text = "Other", ActionId = "other" }] }
            ] } };
        var descriptor = new BridgeWidgetDescriptor { Id = "text", Name = "Text entry validation", InstanceId = snapshot.WidgetInstanceId,
            RuntimeGeneration = "runtime-" + owner, PresentationGeneration = "presentation", Icon = WidgetGlyph.Connection, PackageContentDigest = "" };
        var errors = ViewSnapshotValidator.Validate(snapshot);
        if (errors.Count != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(error => error.Path + ": " + error.Message)));
        presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, owner,
            snapshot.WidgetInstanceId, sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, new Dictionary<string, BridgeNodeRenderStyles>()));
    }
    private static void Write<T>(T result)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
        Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "text-entry-result.json"), JsonSerializer.Serialize(result));
    }
    public ValueTask DisposeAsync() => presenter.DisposeAsync();
}
