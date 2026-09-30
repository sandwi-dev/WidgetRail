using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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
    private readonly TextBlock heading = new() { TextWrapping = TextWrapping.Wrap, MaxLines = 2 };
    private double interfaceScale = 1;
    private bool uppercase;
    private bool symbols;
    private bool completed;
    private bool needsInitialFocus = true;
    private int secretCaret;
    private Action<bool>? finish;
    internal IDisposable? ThemeLease { get; set; }

    internal WidgetTextEntryDialog(ViewNode node, Action<bool> finish)
    {
        Input.GamepadKeyBoundary.ObserveDialog(this);
        this.finish = finish;
        maximumLength = node.TextEntryMaximumLength ?? ProtocolConstants.MaximumTextEntryLength;
        heading.Text = node.AccessibilityLabel ?? node.TextEntryPlaceholder ?? "Enter text";
        Title = heading;
        AutomationProperties.SetName(this, heading.Text);
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
        ApplyPopupMetrics(1, 16, new() { Weight = 400 }, null);
        GettingFocus += (_, args) =>
        {
            if (needsInitialFocus && !completed && args.TrySetNewFocusedElement(characters[10])) needsInitialFocus = false;
        };
        Closing += (_, _) => { if (!completed) Complete(false); };
        AddHandler(PreviewKeyDownEvent, new KeyEventHandler((_, args) =>
        {
            if (Input.GamepadKeyBoundary.Owns(this, args)) return;
            if (HandleKeyboardKey(args.Key)) args.Handled = true;
        }), true);
        CharacterReceived += (_, args) =>
        {
            // Space belongs to native Button activation while a key has focus.
            if (!completed && args.Character > 32 && FocusManager.GetFocusedElement(XamlRoot) is Button key && commands.ContainsKey(key))
            { Insert(char.ConvertFromUtf32((int)args.Character)); args.Handled = true; }
        };
    }

    internal bool HandleKeyboardKey(Windows.System.VirtualKey key)
    {
        if (completed) return false;
        if (key == Windows.System.VirtualKey.Escape) { Complete(false); return true; }
        var editorFocused = ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), (Control?)text ?? password);
        // Focused virtual keys retain native Enter/Space activation, including
        // Cancel. The commit shortcut applies only within the native editor.
        if (key == Windows.System.VirtualKey.Enter && editorFocused) { Complete(true); return true; }
        if (key == Windows.System.VirtualKey.Back && !editorFocused) { Backspace(); return true; }
        return false;
    }

    internal void BindRoot(XamlRoot root)
    {
        XamlRoot = root;
        root.Changed += RootChanged;
        ConstrainWidth();
    }
    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ConstrainWidth();
    private void ConstrainWidth()
    {
        if (XamlRoot is not { } root) return;
        body.Width = Math.Max(1, Math.Min(640 * interfaceScale, root.Size.Width - 120 * interfaceScale));
    }
    internal void ApplyPopupMetrics(double zoom, double size, Windows.UI.Text.FontWeight weight, FontFamily? family)
    {
        // ContentDialog lives in a popup outside the overlay's scale transform.
        // Scale owned native metrics once, retaining controls, edits and focus.
        interfaceScale = zoom;
        Resources["ContentDialogMaxWidth"] = 760 * zoom;
        Resources["ContentDialogMinWidth"] = 320 * zoom;
        Resources["ContentDialogPadding"] = new Thickness(24 * zoom);
        Resources["ContentDialogTitleMargin"] = new Thickness(0, 0, 0, 12 * zoom);
        keys.ColumnSpacing = keys.RowSpacing = 4 * zoom;
        body.Spacing = 12 * zoom;
        heading.FontSize = size * 1.25;
        heading.FontWeight = new() { Weight = Math.Max((ushort)600, weight.Weight) };
        if (family is null) heading.ClearValue(TextBlock.FontFamilyProperty);
        else heading.FontFamily = family;
        foreach (var control in commands.Keys.Cast<Control>().Append((Control?)text ?? password!))
        {
            control.FontSize = size;
            control.FontWeight = weight;
            control.MinHeight = 36 * zoom;
            control.Padding = new Thickness(4 * zoom);
            if (family is null) control.ClearValue(Control.FontFamilyProperty);
            else control.FontFamily = family;
        }
        foreach (var (button, icon, _, _) in prompts)
        {
            icon.FontSize = 18 * zoom;
            var row = (StackPanel)button.Content;
            row.Spacing = 6 * zoom;
            var label = (TextBlock)row.Children[1];
            label.FontSize = size;
            label.FontWeight = weight;
            if (family is null) label.ClearValue(TextBlock.FontFamilyProperty);
            else label.FontFamily = family;
        }
        ConstrainWidth();
    }
    internal string TakeValue() => text?.Text ?? password?.Password ?? string.Empty;
    internal void Erase()
    {
        ThemeLease?.Dispose(); ThemeLease = null;
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
        if (text is not null) text.Select(TextEntryEditing.Move(text.Text, text.SelectionStart, text.SelectionLength, delta), 0);
        else if (password is not null) secretCaret = TextEntryEditing.Move(password.Password, secretCaret, 0, delta);
    }
    private void Insert(string value)
    {
        if (text is not null)
        {
            if (TextEntryEditing.Insert(text.Text, text.SelectionStart, text.SelectionLength, value, maximumLength) is not { } edit) return;
            text.Text = edit.Value;
            text.Select(edit.Caret, 0);
        }
        else if (password is not null)
        {
            if (TextEntryEditing.Insert(password.Password, secretCaret, 0, value, maximumLength) is not { } edit) return;
            password.Password = edit.Value;
            secretCaret = edit.Caret;
        }
    }
    private void Backspace()
    {
        if (text is not null)
        {
            var edit = TextEntryEditing.Backspace(text.Text, text.SelectionStart, text.SelectionLength);
            text.Text = edit.Value; text.Select(edit.Caret, 0);
        }
        else if (password is not null && secretCaret > 0)
        {
            var edit = TextEntryEditing.Backspace(password.Password, secretCaret, 0);
            password.Password = edit.Value; secretCaret = edit.Caret;
        }
    }
}
