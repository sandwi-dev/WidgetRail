using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Host-owned edit session. Only a final commit leaves this control.</summary>
internal sealed class WidgetTextEntryDialog : ContentDialog
{
    private const string Letters = "1234567890qwertyuiopasdfghjkl-zxcvbnm,./";
    private const string Symbols = "!@#$%^&*()-_=+[]{}\\|;:'\",.<>/?`~?:+=_ ";
    private readonly TextBox? text;
    private readonly PasswordBox? password;
    private readonly Grid keys = new() { ColumnSpacing = 4, RowSpacing = 4 };
    private readonly List<Button> characters = [];
    private readonly List<(Button Key, FontIcon Icon, ControllerPrompt Prompt, string Label)> prompts = [];
    private readonly Dictionary<Button, Action> commands = [];
    private readonly int maximumLength;
    private readonly StackPanel body = new() { Spacing = 12 };
    private bool uppercase;
    private bool symbols;
    private bool completed;
    private bool needsInitialFocus = true;
    private int secretCaret;
    private Action<bool>? finish;

    internal WidgetTextEntryDialog(ViewNode node, Action<bool> finish)
    {
        Input.GamepadKeyBoundary.ObserveDialog(this);
        this.finish = finish;
        maximumLength = node.TextEntryMaximumLength ?? ProtocolConstants.MaximumTextEntryLength;
        Title = node.AccessibilityLabel ?? node.TextEntryPlaceholder ?? "Enter text";
        AutomationProperties.SetAutomationId(this, "Widget.TextEntry.Dialog");
        DefaultButton = ContentDialogButton.None;
        IsPrimaryButtonEnabled = false;
        Resources["ContentDialogMaxWidth"] = 760d;
        if (node.TextEntryInputKind == TextEntryInputKind.Sensitive)
        {
            password = new PasswordBox { MaxLength = maximumLength, PasswordRevealMode = PasswordRevealMode.Hidden,
                Header = node.TextEntryPlaceholder ?? "Private text" };
            password.PasswordChanged += (_, _) => secretCaret = password.Password.Length;
            password.Paste += (_, args) => args.Handled = true;
            body.Children.Add(password);
        }
        else
        {
            text = new TextBox { Text = node.TextEntryValue ?? string.Empty, MaxLength = maximumLength,
                Header = node.TextEntryPlaceholder ?? "Text", IsSpellCheckEnabled = false, IsTextPredictionEnabled = false };
            text.Select(text.Text.Length, 0);
            body.Children.Add(text);
        }
        var editor = (Control?)text ?? password!;
        AutomationProperties.SetAutomationId(editor, "Widget.TextEntry.Editor");
        for (var column = 0; column < 10; ++column) keys.ColumnDefinitions.Add(new());
        for (var row = 0; row < 6; ++row) keys.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var index = 0; index < Letters.Length; ++index)
        {
            var position = index;
            var key = AddKey("", "Key." + index, index / 10, index % 10, 1, () => Insert(Character(position)));
            characters.Add(key);
        }
        AddKey("Shift", "Shift", 4, 0, 3, ToggleShift, ControllerPrompt.LeftTrigger);
        AddKey("Symbols", "Symbols", 4, 3, 3, () => { symbols = !symbols; RefreshCharacters(); });
        AddKey("Space", "Space", 4, 6, 4, () => Insert(" "));
        AddKey("Backspace", "Backspace", 5, 0, 3, Backspace, ControllerPrompt.X);
        AddKey("Clear", "Clear", 5, 3, 3, Clear, ControllerPrompt.Y);
        AddKey("Cancel", "Cancel", 5, 6, 2, () => Complete(false), ControllerPrompt.B);
        AddKey("Done", "Commit", 5, 8, 2, () => Complete(true), ControllerPrompt.RightTrigger);
        WidgetControllerPrompts.Changed += RefreshPrompts;
        RefreshPrompts();
        RefreshCharacters();
        body.Children.Add(keys);
        Content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        GettingFocus += (_, args) =>
        {
            if (needsInitialFocus && !completed && args.TrySetNewFocusedElement(characters[10])) needsInitialFocus = false;
        };
        Closing += (_, _) => { if (!completed) Complete(false); };
        AddHandler(PreviewKeyDownEvent, new KeyEventHandler((_, args) =>
        {
            if (Input.GamepadKeyBoundary.Owns(this, args)) return;
            if (args.Key == Windows.System.VirtualKey.Escape) { args.Handled = true; Complete(false); }
            else if (args.Key == Windows.System.VirtualKey.Enter)
            { args.Handled = true; Complete(true); }
            else if (args.Key == Windows.System.VirtualKey.Space && !ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), editor))
            { args.Handled = true; Insert(" "); }
            else if (args.Key == Windows.System.VirtualKey.Back && !ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), editor))
            { args.Handled = true; Backspace(); }
        }), true);
        CharacterReceived += (_, args) =>
        {
            if (!completed && args.Character >= 32 && FocusManager.GetFocusedElement(XamlRoot) is Button key && commands.ContainsKey(key))
            { Insert(char.ConvertFromUtf32((int)args.Character)); args.Handled = true; }
        };
    }

    internal void BindRoot(XamlRoot root)
    {
        XamlRoot = root;
        root.Changed += RootChanged;
        ConstrainWidth();
    }
    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ConstrainWidth();
    private void ConstrainWidth() => body.Width = Math.Max(200, Math.Min(640, XamlRoot.Size.Width - 120));
    internal string TakeValue() => text?.Text ?? password?.Password ?? string.Empty;
    internal void Erase()
    {
        completed = true;
        if (XamlRoot is { } root) root.Changed -= RootChanged;
        finish = null;
        WidgetControllerPrompts.Changed -= RefreshPrompts;
        if (text is not null) text.Text = string.Empty;
        if (password is not null) password.Password = string.Empty;
    }
    internal void RestoreFocus()
    {
        if (completed) return;
        if (FocusManager.GetFocusedElement(XamlRoot) is Button key && commands.ContainsKey(key)) key.Focus(FocusState.Keyboard);
        else characters[10].Focus(FocusState.Keyboard);
    }
    internal void MoveFocus(FocusNavigationDirection direction)
    {
        if (completed) return;
        if (FocusManager.GetFocusedElement(XamlRoot) is not Button key || !commands.ContainsKey(key))
        { characters[10].Focus(FocusState.Keyboard); return; }
        FocusManager.TryMoveFocus(direction, new FindNextElementOptions { SearchRoot = keys });
    }
    internal void Activate()
    {
        if (!completed && FocusManager.GetFocusedElement(XamlRoot) is Button key && commands.TryGetValue(key, out var action)) action();
    }
    internal void Handle(ControllerButton button, ControllerEventPhase phase)
    {
        if (completed || phase == ControllerEventPhase.Released) return;
        if (phase == ControllerEventPhase.Repeated && button is not (ControllerButton.X or ControllerButton.LeftBumper or ControllerButton.RightBumper or ControllerButton.DPadUp or ControllerButton.DPadDown or ControllerButton.DPadLeft or ControllerButton.DPadRight)) return;
        switch (button)
        {
            case ControllerButton.A: Activate(); break;
            case ControllerButton.B: Complete(false); break;
            case ControllerButton.X: Backspace(); break;
            case ControllerButton.Y: Clear(); break;
            case ControllerButton.LeftTrigger: ToggleShift(); break;
            case ControllerButton.RightTrigger: Complete(true); break;
            case ControllerButton.LeftBumper: MoveCaret(-1); break;
            case ControllerButton.RightBumper: MoveCaret(1); break;
            case ControllerButton.DPadUp: MoveFocus(FocusNavigationDirection.Up); break;
            case ControllerButton.DPadDown: MoveFocus(FocusNavigationDirection.Down); break;
            case ControllerButton.DPadLeft: MoveFocus(FocusNavigationDirection.Left); break;
            case ControllerButton.DPadRight: MoveFocus(FocusNavigationDirection.Right); break;
        }
    }
    private Button AddKey(string label, string id, int row, int column, int span, Action action, ControllerPrompt? prompt = null)
    {
        var key = new Button { Content = label, Padding = new Thickness(4), MinWidth = 0, MinHeight = 36,
            HorizontalAlignment = HorizontalAlignment.Stretch, XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled };
        AutomationProperties.SetAutomationId(key, "Widget.TextEntry." + id);
        key.Click += (_, _) => { if (!completed) action(); };
        commands.Add(key, action);
        if (prompt is { } semantic)
        {
            var icon = new FontIcon { FontSize = 18 };
            key.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
                Children = { icon, new TextBlock { Text = label } } };
            prompts.Add((key, icon, semantic, label));
        }
        Grid.SetRow(key, row); Grid.SetColumn(key, column); Grid.SetColumnSpan(key, span);
        keys.Children.Add(key);
        return key;
    }
    private void RefreshPrompts()
    {
        if (completed) return;
        if (!DispatcherQueue.HasThreadAccess) { DispatcherQueue.TryEnqueue(RefreshPrompts); return; }
        foreach (var (key, icon, prompt, label) in prompts)
        {
            WidgetGlyphs.Apply(icon, new ViewNode { Id = "keyboard.prompt", Kind = ViewNodeKind.ControllerGlyph, ControllerPrompt = prompt }, WidgetControllerPrompts.PlayStation);
            AutomationProperties.SetName(key, label + ", " + WidgetGlyphs.AccessibleName(prompt, WidgetControllerPrompts.PlayStation));
        }
    }
    private string Character(int index) => symbols ? Symbols[index % Symbols.Length].ToString() :
        uppercase ? char.ToUpperInvariant(Letters[index]).ToString() : Letters[index].ToString();
    private void RefreshCharacters()
    {
        for (var index = 0; index < characters.Count; ++index) characters[index].Content = Character(index);
    }
    private void ToggleShift() { uppercase = !uppercase; symbols = false; RefreshCharacters(); }
    private void Complete(bool commit)
    {
        if (completed) return;
        completed = true;
        finish?.Invoke(commit);
    }
    private void Clear()
    {
        if (text is not null) text.Text = string.Empty;
        if (password is not null) password.Password = string.Empty;
    }
    private void MoveCaret(int delta)
    {
        if (text is not null) text.Select(Math.Clamp(text.SelectionStart + delta, 0, text.Text.Length), 0);
        else if (password is not null) secretCaret = Math.Clamp(secretCaret + delta, 0, password.Password.Length);
    }
    private void Insert(string value)
    {
        if (text is not null)
        {
            var start = text.SelectionStart;
            if (text.Text.Length - text.SelectionLength + value.Length > maximumLength) return;
            text.Text = text.Text.Remove(start, text.SelectionLength).Insert(start, value);
            text.Select(start + value.Length, 0);
        }
        else if (password is not null && password.Password.Length + value.Length <= maximumLength)
        {
            var caret = secretCaret;
            password.Password = password.Password.Insert(caret, value);
            secretCaret = caret + value.Length;
        }
    }
    private void Backspace()
    {
        if (text is not null)
        {
            var start = text.SelectionStart;
            var count = text.SelectionLength;
            if (count == 0 && start > 0) { --start; count = 1; }
            if (count == 0) return;
            text.Text = text.Text.Remove(start, count); text.Select(start, 0);
        }
        else if (password is not null && secretCaret > 0)
        {
            var caret = secretCaret - 1;
            password.Password = password.Password.Remove(caret, 1); secretCaret = caret;
        }
    }
}
