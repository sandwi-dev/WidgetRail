using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Host-owned edit session. Only a final commit leaves this control.</summary>
internal sealed partial class WidgetTextEntryDialog : ContentDialog
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
    private readonly Input.TextEntryRepeat repeat = new();
    private Button? rememberedKey;
    private Action<bool>? finish;
    internal IDisposable? ThemeLease { get; set; }
    internal bool IsSensitive => password is not null;
    internal bool PasswordVisible { get; private set; }
    internal Action? GuideChanged { get; set; }

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
        AddKey("Left", "CaretLeft", 4, 0, 2, () => MoveCaret(-1), ControllerPrompt.LeftBumper);
        AddKey("Right", "CaretRight", 4, 2, 2, () => MoveCaret(1), ControllerPrompt.RightBumper);
        AddKey("Shift", "Shift", 4, 4, 2, ToggleShift, ControllerPrompt.LeftTrigger);
        AddKey("Symbols", "Symbols", 4, 6, 2, () => { repeat.Reset(); symbols = !symbols; RefreshCharacters(); });
        spaceKey = AddKey("Space", "Space", 4, 8, 2, () => Insert(" "));
        AddKey("Backspace", "Backspace", 5, 0, IsSensitive ? 2 : 3, Backspace, ControllerPrompt.X);
        AddKey("Clear", "Clear", 5, IsSensitive ? 2 : 3, IsSensitive ? 2 : 3, Clear, ControllerPrompt.Y);
        if (IsSensitive) AddKey("Show", "Reveal", 5, 4, 2, TogglePassword, ControllerPrompt.RightStickPress);
        AddKey("Cancel", "Cancel", 5, 6, 2, () => Complete(false), ControllerPrompt.B);
        AddKey("Done", "Commit", 5, 8, 2, () => Complete(true), ControllerPrompt.RightTrigger);
        WidgetControllerPrompts.Changed += RefreshPrompts;
        RefreshPrompts();
        RefreshCharacters();
        body.Children.Add(keys);
        InitializeCaret();
        Content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        ApplyPopupMetrics(1, 16, new() { Weight = 400 }, null);
        GettingFocus += (_, args) =>
        {
            if (needsInitialFocus && !completed && args.TrySetNewFocusedElement(characters[10])) needsInitialFocus = false;
        };
        GotFocus += (_, _) =>
        {
            if (FocusManager.GetFocusedElement(XamlRoot) is Button key && commands.ContainsKey(key))
            { if (!ReferenceEquals(rememberedKey, key)) repeat.CancelCharacter(); rememberedKey = key; }
            RefreshCaret();
        };
        Closing += (_, _) => { if (!completed) Complete(false); };
        AddHandler(PreviewKeyDownEvent, new KeyEventHandler((_, args) =>
        {
            if (Input.GamepadKeyBoundary.Owns(this, args)) return;
            var control = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control) &
                Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
            var shift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift) &
                Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
            if (HandleKeyboardKey(args.Key, control, shift)) args.Handled = true;
        }), true);
        CharacterReceived += (_, args) =>
        {
            // Space belongs to native Button activation while a key has focus.
            if (!completed && args.Character > 32 && FocusManager.GetFocusedElement(XamlRoot) is Button key && commands.ContainsKey(key))
            { Insert(char.ConvertFromUtf32((int)args.Character)); args.Handled = true; }
        };
    }

    internal bool HandleKeyboardKey(Windows.System.VirtualKey key, bool control = false, bool shift = false)
    {
        if (completed) return false;
        repeat.Reset();
        if (key == Windows.System.VirtualKey.Escape) { Complete(false); return true; }
        var editorFocused = ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), (Control?)text ?? password);
        // The controller keyboard owns initial focus. A physical paste shortcut
        // should still reach the native editor, without reading or logging the
        // clipboard ourselves. Focused editors retain native paste/selection behavior.
        if (!editorFocused && (control && key == Windows.System.VirtualKey.V || shift && key == Windows.System.VirtualKey.Insert))
        {
            ((Control?)text ?? password!).Focus(FocusState.Keyboard);
            if (text is not null) text.PasteFromClipboard();
            else password!.PasteFromClipboard();
            return true;
        }
        // Focused virtual keys retain native Enter/Space activation, including
        // Cancel. The commit shortcut applies only within the native editor.
        if (key == Windows.System.VirtualKey.Enter && editorFocused) { Complete(true); return true; }
        if (key == Windows.System.VirtualKey.Back && !editorFocused) { Backspace(); return true; }
        if (!editorFocused && !control && !shift && key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right)
        { MoveCaret(key == Windows.System.VirtualKey.Left ? -1 : 1); return true; }
        return false;
    }

    internal void BindRoot(XamlRoot root)
    {
        XamlRoot = root;
        root.Changed += RootChanged;
        ConstrainWidth();
        ApplyCaretMetrics();
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
        ApplyCaretMetrics();
    }
    internal string TakeValue() => text?.Text ?? password?.Password ?? string.Empty;
    internal void Erase()
    {
        repeat.Reset();
        RetireCaret();
        ThemeLease?.Dispose(); ThemeLease = null;
        completed = true;
        if (XamlRoot is { } root) root.Changed -= RootChanged;
        finish = null;
        GuideChanged = null;
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
        { (rememberedKey ?? characters[10]).Focus(FocusState.Keyboard); return; }
        var row = Grid.GetRow(key);
        Button? target = null;
        if (direction is FocusNavigationDirection.Left or FocusNavigationDirection.Right)
        {
            var peers = commands.Keys.Where(item => Grid.GetRow(item) == row).OrderBy(Grid.GetColumn).ToArray();
            var index = Array.IndexOf(peers, key);
            target = peers[(index + peers.Length + (direction == FocusNavigationDirection.Left ? -1 : 1)) % peers.Length];
        }
        else if (direction is FocusNavigationDirection.Up or FocusNavigationDirection.Down)
        {
            var nextRow = (row + keys.RowDefinitions.Count + (direction == FocusNavigationDirection.Up ? -1 : 1)) % keys.RowDefinitions.Count;
            var center = Grid.GetColumn(key) + Grid.GetColumnSpan(key) / 2d;
            target = commands.Keys.Where(item => Grid.GetRow(item) == nextRow)
                .OrderBy(item => Math.Abs(Grid.GetColumn(item) + Grid.GetColumnSpan(item) / 2d - center)).FirstOrDefault();
        }
        target?.Focus(FocusState.Keyboard);
    }
    internal void Activate()
    {
        if (!completed && FocusManager.GetFocusedElement(XamlRoot) is Button key && commands.TryGetValue(key, out var action)) action();
    }
    internal void Handle(ControllerButton button, ControllerEventPhase phase)
    {
        if (completed || phase == ControllerEventPhase.Released) return;
        if (phase == ControllerEventPhase.Repeated && button is not (ControllerButton.A or ControllerButton.X or ControllerButton.LeftBumper or ControllerButton.RightBumper or ControllerButton.DPadUp or ControllerButton.DPadDown or ControllerButton.DPadLeft or ControllerButton.DPadRight)) return;
        if (button is ControllerButton.X or ControllerButton.LeftBumper or ControllerButton.RightBumper) FocusControllerKey();
        switch (button)
        {
            case ControllerButton.A:
                if (phase != ControllerEventPhase.Repeated || CharacterRepeatIdentity is not null) Activate();
                break;
            case ControllerButton.B: Complete(false); break;
            case ControllerButton.X: Backspace(); break;
            case ControllerButton.Y: Clear(); break;
            case ControllerButton.LeftTrigger: ToggleShift(); break;
            case ControllerButton.RightTrigger: Complete(true); break;
            case ControllerButton.LeftBumper: MoveCaret(-1); break;
            case ControllerButton.RightBumper: MoveCaret(1); break;
            case ControllerButton.RightStick: TogglePassword(); break;
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
        RefreshRevealLabel();
    }
    private void TogglePassword()
    {
        if (completed || password is null) return;
        repeat.Reset();
        PasswordVisible = !PasswordVisible;
        password.PasswordRevealMode = PasswordVisible ? PasswordRevealMode.Visible : PasswordRevealMode.Hidden;
        RefreshRevealLabel(); RefreshCaret(); GuideChanged?.Invoke();
    }
    private void RefreshRevealLabel()
    {
        foreach (var (key, _, prompt, _) in prompts)
            if (prompt == ControllerPrompt.RightStickPress)
            {
                var label = PasswordVisible ? "Hide" : "Show";
                ((TextBlock)((StackPanel)key.Content).Children[1]).Text = label;
                AutomationProperties.SetName(key, label + " password, " + WidgetGlyphs.AccessibleName(prompt, WidgetControllerPrompts.PlayStation));
                AutomationProperties.SetItemStatus(key, PasswordVisible ? "Visible" : "Hidden");
            }
    }
    private string Character(int index) => symbols ? Symbols[index % Symbols.Length].ToString() :
        uppercase ? char.ToUpperInvariant(Letters[index]).ToString() : Letters[index].ToString();
    private void RefreshCharacters()
    {
        for (var index = 0; index < characters.Count; ++index) characters[index].Content = Character(index);
        foreach (var (key, _, prompt, label) in prompts)
            if (prompt == ControllerPrompt.LeftTrigger)
            {
                ((TextBlock)((StackPanel)key.Content).Children[1]).Text = uppercase && !symbols ? "ABC" : "Shift";
                AutomationProperties.SetItemStatus(key, uppercase && !symbols ? "Uppercase" : "Lowercase");
            }
        var symbolKey = commands.Keys.FirstOrDefault(key => AutomationProperties.GetAutomationId(key) == "Widget.TextEntry.Symbols");
        if (symbolKey is not null) { symbolKey.Content = symbols ? "ABC" : "Symbols"; AutomationProperties.SetItemStatus(symbolKey, symbols ? "Symbols" : "Letters"); }
    }
    private void ToggleShift() { repeat.Reset(); uppercase = !uppercase; symbols = false; RefreshCharacters(); }
    private void Complete(bool commit)
    {
        if (completed) return;
        completed = true;
        repeat.Reset();
        finish?.Invoke(commit);
    }
    private void Clear()
    {
        repeat.Reset();
        if (text is not null) text.Text = string.Empty;
        if (password is not null) password.Password = string.Empty;
    }
    private void MoveCaret(int delta)
    {
        if (text is not null) text.Select(TextEntryEditing.Move(text.Text, text.SelectionStart, text.SelectionLength, delta), 0);
        else if (password is not null) secretCaret = TextEntryEditing.Move(password.Password, secretCaret, 0, delta);
        RefreshCaret();
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
        RefreshCaret();
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
        RefreshCaret();
    }

    private readonly Button spaceKey;
    private object? CharacterRepeatIdentity => FocusManager.GetFocusedElement(XamlRoot) is Button key &&
        (characters.Contains(key) || ReferenceEquals(key, spaceKey)) ? (key, key.Content) : null;
    internal void ResetRepeat() => repeat.Reset();
    internal void SampleHeldButtons(ushort down, ushort pressed, long now)
    {
        if (completed) { repeat.Reset(); return; }
        if (repeat.Sample(down, pressed, CharacterRepeatIdentity, now) is { } button)
            Handle(button, ControllerEventPhase.Repeated);
    }
    private void FocusControllerKey()
    {
        if (FocusManager.GetFocusedElement(XamlRoot) is not Button key || !commands.ContainsKey(key))
            (rememberedKey ?? characters[10]).Focus(FocusState.Keyboard);
    }
}
