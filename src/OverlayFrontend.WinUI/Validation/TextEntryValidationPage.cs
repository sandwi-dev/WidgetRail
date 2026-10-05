using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class TextEntryValidationPage : Page, IAsyncDisposable
{
    private readonly WidgetViewPresenter presenter = new();
    private readonly NativePopupTheme popupTheme = new();
    private readonly TextBlock status = new() { Text = "Text entry validation waiting" };
    private readonly List<string> checks = [];
    private readonly List<WidgetActionRequest> actions = [];
    private long sequence;
    private long owner = 1;
    private string value = "ab";
    private int maximumLength = 4;
    private string action = "commit";
    private bool sensitive;
    private bool disabled;
    private bool otherScope;
    private bool throwOnCommit;
    private string? failure;
    private Task? run;

    public TextEntryValidationPage()
    {
        popupTheme.Attach(presenter);
        popupTheme.Update(null, AppearanceSettings.Default);
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
            { maximumLength = 256; value = "Keyboard preview — move the caret with LB/RB"; Apply(); status.Text = "Text entry preview — activate the field"; }
            else run ??= RunAsync();
        };
    }
    private async Task RunAsync()
    {
        try
        {
            Apply(); await Task.Delay(150);
            var revokedOpener = (Button)Find(presenter, "Widget.entry")!;
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(revokedOpener);
            var valueProvider = (Microsoft.UI.Xaml.Automation.Provider.IValueProvider)peer.GetPattern(PatternInterface.Value);
            Check(valueProvider.Value == "ab" && valueProvider.IsReadOnly && peer.GetName() == "Text entry",
                "ordinary TextEntry exposes current value separately from its label");
            var rejectedSet = false;
            try { valueProvider.SetValue("z"); } catch (InvalidOperationException) { rejectedSet = true; }
            Check(rejectedSet && actions.Count == 0 && valueProvider.Value == "ab", "automation cannot bypass the explicit edit and commit session");
            revokedOpener.Focus(FocusState.Keyboard);
            var beforeRevocation = FocusedId;
            presenter.SetPresentationInputEnabled(false);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new ButtonAutomationPeer(revokedOpener)
                .GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(150);
            Check(!presenter.HasTransientControl && actions.Count == 0 && FocusedId == beforeRevocation,
                "native invocation cannot open TextEntry after presentation input is revoked");
            presenter.SetPresentationInputEnabled(true);
            await Open();
            presenter.SetPresentationInputEnabled(false);
            await Closed();
            Check(actions.Count == 0, "input revocation dismisses existing text entry without committing");
            presenter.SetPresentationInputEnabled(true);
            await Open();
            Check(FocusedId == "Widget.TextEntry.Key.10", "controller starts on first letter without click");
            presenter.MoveFocus(FocusNavigationDirection.Right);
            Check(FocusedId == "Widget.TextEntry.Key.11", "controller navigation uses native keyboard geometry");
            presenter.MoveFocus(FocusNavigationDirection.Left);
            presenter.MoveFocus(FocusNavigationDirection.Left);
            Check(FocusedId == "Widget.TextEntry.Key.19", "left edge wraps to the final key in the same row");
            presenter.MoveFocus(FocusNavigationDirection.Right);
            Check(FocusedId == "Widget.TextEntry.Key.10", "right edge wraps back to the first key");
            presenter.MoveFocus(FocusNavigationDirection.Up);
            presenter.MoveFocus(FocusNavigationDirection.Up);
            Check(FocusedId == "Widget.TextEntry.Backspace", "top edge wraps to aligned bottom action");
            presenter.MoveFocus(FocusNavigationDirection.Left);
            Check(FocusedId == "Widget.TextEntry.Commit", "action row wraps at its own width");
            presenter.MoveFocus(FocusNavigationDirection.Down);
            Check(FocusedId == "Widget.TextEntry.Key.8", "bottom edge wraps to aligned top character");
            ((Control)FindDialog("Widget.TextEntry.Key.0")!).Focus(FocusState.Keyboard);
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
            Check(valueProvider.Value == "new", "existing automation peer reflects an authored value update");
            await Open(); disabled = true; Apply(); await Closed(); Check(actions.Count == 1, "disabled opener cancels edits");
            disabled = false; Apply(); await Open(); otherScope = true; Apply(); await Closed(); Check(actions.Count == 1, "scope switch cancels edits");
            otherScope = false; Apply(); await Open(); ++owner; Apply(); await Closed(); Check(actions.Count == 1, "runtime replacement cancels edits");
            value = "a😀b"; Apply(); await Open();
            await presenter.HandleControllerButtonAsync(ControllerButton.LeftBumper);
            await presenter.HandleControllerButtonAsync(ControllerButton.LeftBumper);
            await presenter.HandleControllerButtonAsync(ControllerButton.RightBumper);
            await presenter.HandleControllerButtonAsync(ControllerButton.X);
            Check(((TextBox)Editor()).Text == "ab", "native ordinary editor moves across and deletes a complete supplementary character");
            ((TextBox)Editor()).Select(0, 1);
            await presenter.HandleControllerButtonAsync(ControllerButton.A);
            Check(((TextBox)Editor()).Text == "qb", "controller insertion replaces the native selected range");
            await presenter.HandleControllerButtonAsync(ControllerButton.B); await Closed();
            value = "ae\u0301b"; Apply(); await Open();
            await presenter.HandleControllerButtonAsync(ControllerButton.LeftBumper);
            await presenter.HandleControllerButtonAsync(ControllerButton.X);
            Check(((TextBox)Editor()).Text == "ab", "native ordinary editor deletes the combining sequence as one text element");
            await presenter.HandleControllerButtonAsync(ControllerButton.B); await Closed();
            sensitive = true; value = ""; Apply(); await Open();
            var sensitivePeer = FrameworkElementAutomationPeer.CreatePeerForElement((FrameworkElement)Find(presenter, "Widget.entry")!);
            Check(sensitivePeer.IsPassword() && sensitivePeer.GetPattern(PatternInterface.Value) is null,
                "sensitive opener exposes no value provider and reports protected semantics");
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
            await Open(); password = (PasswordBox)Editor(); password.Password = "😀b";
            await presenter.HandleControllerButtonAsync(ControllerButton.LeftBumper);
            await presenter.HandleControllerButtonAsync(ControllerButton.LeftBumper);
            await presenter.HandleControllerButtonAsync(ControllerButton.A);
            Check(password.Password == "q😀b", "sensitive controller insertion preserves supplementary characters and the length bound");
            await presenter.HandleControllerButtonAsync(ControllerButton.X);
            await presenter.HandleControllerButtonAsync(ControllerButton.RightBumper);
            await presenter.HandleControllerButtonAsync(ControllerButton.X);
            Check(password.Password == "b", "sensitive caret reversal and deletion preserve whole text elements");
            presenter.DismissTransientControl(); await Closed();
            Check(password.Password.Length == 0 && actions.Count == 0, "Unicode sensitive edits are cleared without dispatch on cancel");
            throwOnCommit = true; await Open(); await presenter.HandleControllerButtonAsync(ControllerButton.A);
            await presenter.HandleControllerButtonAsync(ControllerButton.RightTrigger); await Closed();
            Check(failure == "Text entry commit failed.", "exception diagnostics cannot echo committed secret");
            throwOnCommit = false; failure = null;
            await VerifyPopupScalingAsync();
            await VerifyKeyboardActivationAsync();
            await VerifyPasteAsync();
            await VerifyControllerEditingAsync();
            await Open(); await presenter.DisposeAsync(); await Closed(); Check(!presenter.HasTransientControl, "disposal retires keyboard");
            status.Text = $"Passed {checks.Count} TextEntry checks"; Write(new { result = "passed", checks });
        }
        catch (Exception error) { status.Text = "TextEntry validation failed: " + error.Message; Write(new { result = "failed", checks, error = error.ToString() }); }
    }
    private async Task VerifyKeyboardActivationAsync()
    {
        sensitive = false; value = "ab"; Apply(); await Open();
        var dialog = (WidgetTextEntryDialog)FindDialog("Widget.TextEntry.Dialog")!;
        var editor = (TextBox)Editor();
        var defaultFamily = dialog.FontFamily.Source;
        popupTheme.Update(new Dictionary<string, BridgeNodeRenderStyles>
        {
            ["body"] = new() { Base = new Dictionary<string, BridgeComputedStyleValue>
                { ["font-family"] = new() { Kind = WrssValueKind.FontFamily, Text = "Consolas" } },
                Focused = new Dictionary<string, BridgeComputedStyleValue>(), Pressed = new Dictionary<string, BridgeComputedStyleValue>() },
        }, AppearanceSettings.Default);
        Check(dialog.FontFamily.Source == "Consolas", "open text-entry dialog receives authored popup font");
        popupTheme.Update(null, AppearanceSettings.Default);
        Check(ReferenceEquals(FindDialog("Widget.TextEntry.Dialog"), dialog) && dialog.FontFamily.Source == defaultFamily &&
            ReferenceEquals(dialog.ReadLocalValue(Control.FontFamilyProperty), DependencyProperty.UnsetValue) && editor.Text == "ab",
            "removing popup font restores native dialog typography without replacing the edit session");

        var count = actions.Count;
        async Task InvokeKey(string id)
        {
            var key = (Button)FindDialog("Widget.TextEntry." + id)!;
            Check(key.Focus(FocusState.Keyboard), "native keyboard focus reaches " + id);
            Check(!dialog.HandleKeyboardKey(Windows.System.VirtualKey.Enter) &&
                !dialog.HandleKeyboardKey(Windows.System.VirtualKey.Space) && actions.Count == count,
                "dialog preserves native Enter and Space activation for " + id);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new ButtonAutomationPeer(key)
                .GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(100);
        }
        await InvokeKey("Clear");
        Check(editor.Text.Length == 0 && presenter.HasTransientControl, "native Clear activation edits without committing");
        await InvokeKey("Key.10");
        Check(editor.Text == "q" && presenter.HasTransientControl, "native character activation inserts its label without committing");
        await InvokeKey("Space");
        Check(editor.Text == "q " && presenter.HasTransientControl, "native Space key activation inserts one space");
        await InvokeKey("Cancel"); await Closed();
        Check(actions.Count == count && editor.Text.Length == 0, "native Cancel activation clears edits without committing");

        await Open();
        dialog = (WidgetTextEntryDialog)FindDialog("Widget.TextEntry.Dialog")!;
        editor = (TextBox)Editor();
        Check(editor.Focus(FocusState.Keyboard), "native editor accepts keyboard focus");
        Check(dialog.HandleKeyboardKey(Windows.System.VirtualKey.Enter), "Enter in the native editor remains a commit shortcut");
        await Closed();
        Check(actions.Count == count + 1 && actions[^1].Action.CommittedText == "ab",
            "editor Enter commits the final value exactly once");
        actions.Clear();
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
            Root = new() { Id = "page", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[
                new() { Id = "entry", Kind = ViewNodeKind.TextEntry, AccessibilityLabel = "Text entry", TextEntryValue = value, TextEntryPlaceholder = "Enter text", TextEntryMaximumLength = maximumLength,
                    TextEntryInputKind = sensitive ? TextEntryInputKind.Sensitive : null, ActionId = action, IsDisabled = disabled },
                new() { Id = "other", Kind = ViewNodeKind.Stack, InputScopeId = "other", Children = (ViewNode[])[
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
