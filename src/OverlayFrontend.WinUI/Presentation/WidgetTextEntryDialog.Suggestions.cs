using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetTextEntryDialog
{
    private TextSuggestionController? suggestions;
    private readonly List<Button> suggestionButtons = [];
    private TextSuggestionView? suggestionView;
    private TextBlock? suggestionStatus;
    private Grid? suggestionGrid;
    private bool acceptingSuggestion;
    private FontIcon? firstSuggestionPrompt;
    private FontIcon? suggestionFocusPrompt;
    private Grid? suggestionRegion;
    private ScrollViewer? suggestionViewport;
    private ProgressRing? suggestionProgress;
    private Storyboard? suggestionResize;
    private double suggestionTargetHeight = -1, suggestionAvailableHeight = double.PositiveInfinity;
    internal bool SuggestionsFocused => XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is Button button && suggestionButtons.Contains(button);
    internal bool CanAcceptFirstSuggestion => !completed && !IsSensitive && suggestionView is { Items.Count: > 0, Selectable: true } state &&
        (!wordCompletion || text is not null && completionContext == CaptureCompletionContext()) &&
        state.Text == (wordCompletion?completionContext?.Prefix:text?.Text) && suggestions?.IsCurrent(state) == true;

    internal void EnableSuggestions(ViewNode node, Func<TextSuggestionQuery, CancellationToken, Task<TextSuggestionResult>> query)
    {
        if (suggestions is not null || IsSensitive || IsLive || text is null || node.TextEntrySuggestions is not { } source || !TextSuggestionContract.IsValid(source)) return;
        suggestionStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxLines = 2,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(suggestionStatus, "Widget.TextEntry.SuggestionStatus");
        AutomationProperties.SetLiveSetting(suggestionStatus, AutomationLiveSetting.Polite);
        suggestionGrid = new Grid { ColumnSpacing = 4, RowSpacing = 4, Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(suggestionGrid, "Widget.TextEntry.Suggestions");
        suggestionGrid.Children.Add(suggestionStatus);
        for (var index = 0; index < source.MaximumResults; index++)
        {
            var position = index;
            var button = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0, MinHeight = 36,
                Padding = new(6, 2, 6, 2), Visibility = Visibility.Collapsed, UseSystemFocusVisuals = true };
            AutomationProperties.SetAutomationId(button, "Widget.TextEntry.Suggestion." + index);
            button.Click += (_, _) => AcceptSuggestion(position);
            Grid.SetColumn(button, index);
            suggestionGrid.Children.Add(button); suggestionButtons.Add(button);
        }
        suggestionViewport = new ScrollViewer { Content = suggestionGrid, IsTabStop = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        suggestionViewport.PointerWheelChanged += (_, args) =>
        {
            if (suggestionViewport.ScrollableWidth <= 0) return;
            var delta = args.GetCurrentPoint(suggestionViewport).Properties.MouseWheelDelta;
            suggestionViewport.ChangeView(Math.Clamp(suggestionViewport.HorizontalOffset - delta, 0, suggestionViewport.ScrollableWidth),
                null, null, disableAnimation: true);
            args.Handled = true;
        };
        suggestionProgress = new ProgressRing { Width = 16, Height = 16, IsActive = false,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
        AutomationProperties.SetName(suggestionProgress, "Updating suggestions");
        suggestionRegion = new Grid { Height = 0, MinHeight = 0 };
        suggestionRegion.ColumnDefinitions.Add(new() { Width = new(40) });
        suggestionRegion.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        suggestionRegion.ColumnDefinitions.Add(new() { Width = new(24) });
        suggestionFocusPrompt = new FontIcon { FontSize = 18, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 10, 8, 0) };
        AutomationProperties.SetAutomationId(suggestionFocusPrompt, "Widget.TextEntry.SuggestionsFocusHint");
        Grid.SetColumn(suggestionViewport, 1); Grid.SetColumn(suggestionProgress, 2);
        suggestionRegion.Children.Add(suggestionFocusPrompt); suggestionRegion.Children.Add(suggestionViewport); suggestionRegion.Children.Add(suggestionProgress);
        suggestionRegion.SizeChanged += (_, args) =>
        {
            suggestionRegion.Clip = new RectangleGeometry { Rect = new(0, 0, Math.Max(0, args.NewSize.Width), Math.Max(0, args.NewSize.Height)) };
            suggestionViewport.MaxHeight = Math.Max(0, args.NewSize.Height - body.Spacing);
        };
        // Separate rows let only the suggestion region resize. The original
        // editor/key body retains its geometry and focus behavior.
        keyboardLayout.Children.Insert(0, suggestionRegion);
        suggestions = new(node.Id, source, maximumLength, query, state =>
        {
            void Apply() { if (!completed && suggestions?.IsCurrent(state) == true) ApplySuggestions(state); }
            if (DispatcherQueue.HasThreadAccess) Apply(); else DispatcherQueue.TryEnqueue(Apply);
        });
        text.TextChanged += SuggestionsTextChanged;
        ApplySuggestionMetrics(text.FontSize, text.FontWeight, text.FontFamily);
        suggestions.Update(wordCompletion?completionContext?.Prefix??"":text.Text);
    }

    private void SuggestionsTextChanged(object sender, TextChangedEventArgs args)
    {
        if(wordCompletion)UpdateWordCompletion();
        else if (!completed && !acceptingSuggestion) suggestions?.Update(text!.Text);
    }

    private void ApplySuggestions(TextSuggestionView state)
    {
        if (!state.Selectable || state.Items.Count == 0) RestoreKeyFromSuggestion();
        suggestionView = state;
        suggestionStatus!.Text = wordCompletion ? "" : state.Loading ? "Searching…" : state.Unavailable ? "Suggestions unavailable" :
            state.Queried && state.Items.Count == 0 ? "No matches" : "";
        suggestionStatus.Visibility = state.Items.Count == 0 && suggestionStatus.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        suggestionProgress!.IsActive = state.Loading || state.Stale;
        suggestionProgress.Visibility = suggestionProgress.IsActive ? Visibility.Visible : Visibility.Collapsed;
        for (var index = 0; index < suggestionButtons.Count; index++)
        {
            var button = suggestionButtons[index];
            button.IsEnabled = state.Selectable;
            button.Opacity = state.Stale ? .55 : 1;
            if (index < state.Items.Count)
            {
                var item = state.Items[index];
                var label = new TextBlock { Text = item.Label, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
                if (index == 0)
                {
                    firstSuggestionPrompt = new FontIcon { FontSize = button.FontSize };
                    var content = new Grid { ColumnSpacing = 6 * interfaceScale };
                    content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                    content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                    Grid.SetColumn(label, 1); content.Children.Add(firstSuggestionPrompt); content.Children.Add(label);
                    button.Content = content;
                }
                else button.Content = label;
                AutomationProperties.SetName(button, item.Description is { Length: > 0 } description ? item.Label + ", " + description : item.Label);
                ToolTipService.SetToolTip(button, item.Description is { Length: > 0 } detail ? item.Label + "\n" + detail : item.Label);
                button.Visibility = Visibility.Visible;
            }
            else button.Visibility = Visibility.Collapsed;
        }
        UpdateSuggestionRows();
        RefreshSuggestionPrompt();
        GuideChanged?.Invoke();
    }

    private void UpdateSuggestionRows()
    {
        if (suggestionGrid is null) return;
        var count = suggestionView?.Items.Count ?? 0;
        var rows = count > 0 || suggestionStatus?.Visibility == Visibility.Visible ? 1 : 0;
        while (suggestionGrid.RowDefinitions.Count > rows) suggestionGrid.RowDefinitions.RemoveAt(suggestionGrid.RowDefinitions.Count - 1);
        while (suggestionGrid.RowDefinitions.Count < rows) suggestionGrid.RowDefinitions.Add(new());
        foreach (var row in suggestionGrid.RowDefinitions) row.Height = count > 0 ? new(40 * interfaceScale) : GridLength.Auto;
        var columns = Math.Max(1, count);
        while (suggestionGrid.ColumnDefinitions.Count > columns) suggestionGrid.ColumnDefinitions.RemoveAt(suggestionGrid.ColumnDefinitions.Count - 1);
        while (suggestionGrid.ColumnDefinitions.Count < columns) suggestionGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Grid.SetColumnSpan(suggestionStatus!, columns);
        suggestionGrid.Visibility = rows > 0 ? Visibility.Visible : Visibility.Collapsed;
        ResizeSuggestionRegion();
    }

    private void ResizeSuggestionRegion()
    {
        if (suggestionRegion is null || suggestionGrid is null) return;
        var count = suggestionView?.Items.Count ?? 0;
        var desired = count > 0 ? 40 * interfaceScale :
            suggestionStatus?.Visibility == Visibility.Visible ? 24 * interfaceScale : 0;
        if (desired > 0) desired += body.Spacing;
        var target = Math.Min(desired, suggestionAvailableHeight);
        if (Math.Abs(target - suggestionTargetHeight) < .5) return;
        suggestionTargetHeight = target;
        var from = suggestionRegion.ActualHeight;
        suggestionResize?.Stop(); suggestionResize = null;
        suggestionRegion.Height = target;
        var duration = NativeComputedStyleAdapter.DialogResizeDuration;
        if (!IsLoaded || duration == TimeSpan.Zero || Math.Abs(from - target) < .5) return;
        var animation = new DoubleAnimation { From = from, To = target, Duration = duration,
            EnableDependentAnimation = true, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(animation, suggestionRegion); Storyboard.SetTargetProperty(animation, "Height");
        var storyboard = new Storyboard(); storyboard.Children.Add(animation); suggestionResize = storyboard;
        storyboard.Completed += (_, _) => { if (ReferenceEquals(suggestionResize, storyboard)) { storyboard.Stop(); suggestionResize = null; suggestionRegion.Height = suggestionTargetHeight; } };
        storyboard.Begin();
    }

    private void ToggleSuggestionFocus()
    {
        if (SuggestionsFocused) { repeat.Reset(); (rememberedKey ?? DefaultKey).Focus(FocusState.Keyboard); return; }
        if (!CanAcceptFirstSuggestion) return;
        repeat.Reset(); keyboardInputStarted = true;
        FocusSuggestion(suggestionButtons[0]);
    }

    private void FocusSuggestion(Button button)
    {
        // ContentDialog can host its template outside the control's routed
        // event ancestry. Capture the return key at the actual handoff too.
        if (FocusManager.GetFocusedElement(XamlRoot) is Button previous && commands.ContainsKey(previous)) rememberedKey = previous;
        button.Focus(FocusState.Keyboard);
        button.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        GuideChanged?.Invoke();
    }

    private void RefreshSuggestionPrompt()
    {
        if (suggestionFocusPrompt is not null)
        {
            suggestionFocusPrompt.Visibility = CanAcceptFirstSuggestion ? Visibility.Visible : Visibility.Collapsed;
            WidgetGlyphs.Apply(suggestionFocusPrompt, new ViewNode { Id = "keyboard.suggestions.focus", Kind = ViewNodeKind.ControllerGlyph,
                ControllerPrompt = ControllerPrompt.RightStickPress }, WidgetControllerPrompts.PlayStation);
            var label = SuggestionsFocused ? "Back to keyboard" : "Suggestions";
            AutomationProperties.SetName(suggestionFocusPrompt, label);
            ToolTipService.SetToolTip(suggestionFocusPrompt, label);
        }
        if (firstSuggestionPrompt is not null)
        {
            firstSuggestionPrompt.Opacity = CanAcceptFirstSuggestion ? 1 : 0; // Retain width while old results are inactive.
            WidgetGlyphs.Apply(firstSuggestionPrompt, new ViewNode { Id = "keyboard.suggestion.prompt", Kind = ViewNodeKind.ControllerGlyph,
                ControllerPrompt = ControllerPrompt.LeftStickPress }, WidgetControllerPrompts.PlayStation);
        }
        if (suggestionView is { Items.Count: > 0 } state && suggestionButtons.Count > 0)
        {
            var item = state.Items[0];
            AutomationProperties.SetName(suggestionButtons[0], item.Label + (string.IsNullOrEmpty(item.Description) ? "" : ", " + item.Description) +
                (CanAcceptFirstSuggestion ? ", " + WidgetGlyphs.AccessibleName(ControllerPrompt.LeftStickPress, WidgetControllerPrompts.PlayStation) : ""));
        }
    }

    private void AcceptFirstSuggestion()
    {
        if (CanAcceptFirstSuggestion) { repeat.Reset(); AcceptSuggestion(0); }
    }

    private void AcceptSuggestion(int index)
    {
        if (!CanAcceptFirstSuggestion || text is null || suggestionView is not { } state || index < 0 || index >= state.Items.Count) return;
        var value = state.Items[index].Value;
        var edit = wordCompletion ? completionContext?.Accept(value,maximumLength) : new TextEntryEditing.Edit(value,value.Length);
        if(edit is null)return;
        acceptingSuggestion = true;
        try
        {
            if (suggestions?.Accept(state.Revision, state.Items[index].Id) is null) return;
            text.Text = edit.Value.Value; text.Select(edit.Value.Caret, 0);
        }
        finally { acceptingSuggestion = false; }
        if(wordCompletion)completionContext=CaptureCompletionContext();
        keyboardInputStarted = true;
        (rememberedKey ?? DefaultKey).Focus(FocusState.Keyboard);
        RefreshCaret();
    }

    private bool ActivateSuggestion()
    {
        if (XamlRoot is null || FocusManager.GetFocusedElement(XamlRoot) is not Button button) return false;
        var index = suggestionButtons.IndexOf(button);
        if (index < 0) return false;
        AcceptSuggestion(index); return true;
    }

    private bool HandleSuggestionKey(Windows.System.VirtualKey key)
    {
        if (XamlRoot is null) return false;
        var focused = FocusManager.GetFocusedElement(XamlRoot);
        if (ReferenceEquals(focused, text) && key == Windows.System.VirtualKey.Up && CanAcceptFirstSuggestion)
        { FocusSuggestion(suggestionButtons[0]); return true; }
        if (focused is not Button button || !suggestionButtons.Contains(button)) return false;
        var direction = key switch
        {
            Windows.System.VirtualKey.Left => FocusNavigationDirection.Left,
            Windows.System.VirtualKey.Right => FocusNavigationDirection.Right,
            Windows.System.VirtualKey.Up => FocusNavigationDirection.Up,
            Windows.System.VirtualKey.Down => FocusNavigationDirection.Down,
            _ => FocusNavigationDirection.None,
        };
        return direction != FocusNavigationDirection.None && NavigateSuggestions(direction);
    }

    private bool NavigateSuggestions(FocusNavigationDirection direction)
    {
        if (!CanAcceptFirstSuggestion) return false;
        var visible = suggestionButtons.Where(button => button.Visibility == Visibility.Visible && button.IsEnabled).ToArray();
        if (visible.Length == 0 || XamlRoot is null) return false;
        var focused = FocusManager.GetFocusedElement(XamlRoot) as Button;
        var index = focused is null ? -1 : Array.IndexOf(visible, focused);
        if (index >= 0)
        {
            Button target;
            if (direction is FocusNavigationDirection.Left or FocusNavigationDirection.Right)
                target = visible[(index + visible.Length + (direction == FocusNavigationDirection.Left ? -1 : 1)) % visible.Length];
            else target = rememberedKey ?? DefaultKey;
            if (suggestionButtons.Contains(target)) FocusSuggestion(target); else target.Focus(FocusState.Keyboard);
            return true;
        }
        if (direction == FocusNavigationDirection.Up && focused is not null && commands.ContainsKey(focused) && Grid.GetRow(focused) == 0)
        {
            FocusSuggestion(visible[0]); return true;
        }
        return false;
    }

    private void ApplySuggestionMetrics(double size, Windows.UI.Text.FontWeight weight, FontFamily? family)
    {
        if (suggestionGrid is null) return;
        suggestionGrid.ColumnSpacing = suggestionGrid.RowSpacing = 4 * interfaceScale;
        suggestionRegion!.ColumnDefinitions[0].Width = new(40 * interfaceScale);
        suggestionRegion.ColumnDefinitions[2].Width = new(24 * interfaceScale);
        suggestionFocusPrompt!.FontSize = 18 * interfaceScale;
        suggestionFocusPrompt.Margin = new(0, 10 * interfaceScale, 8 * interfaceScale, 0);
        UpdateSuggestionRows();
        suggestionStatus!.FontSize = size * .85;
        suggestionStatus.FontWeight = weight;
        if (family is not null) suggestionStatus.FontFamily = family; else suggestionStatus.ClearValue(TextBlock.FontFamilyProperty);
        foreach (var button in suggestionButtons)
        {
            button.FontSize = size; button.FontWeight = weight;
            button.MinHeight = 36 * interfaceScale; button.Padding = new(6 * interfaceScale, 2 * interfaceScale, 6 * interfaceScale, 2 * interfaceScale);
            button.MaxWidth = Math.Min(260 * interfaceScale, Math.Max(60, body.Width - 64 * interfaceScale));
            if (family is not null) button.FontFamily = family; else button.ClearValue(Control.FontFamilyProperty);
        }
        if (firstSuggestionPrompt is not null) firstSuggestionPrompt.FontSize = size;
    }

    private void RetireSuggestions()
    {
        if (text is not null) text.TextChanged -= SuggestionsTextChanged;
        suggestions?.Dispose(); suggestions = null; suggestionView = null;
        suggestionResize?.Stop(); suggestionResize = null;
    }

    private void RestoreKeyFromSuggestion()
    {
        if (XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is Button button && suggestionButtons.Contains(button))
            (rememberedKey ?? DefaultKey).Focus(FocusState.Keyboard);
    }
}
