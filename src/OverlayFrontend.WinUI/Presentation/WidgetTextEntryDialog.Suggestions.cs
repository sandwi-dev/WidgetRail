using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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

    internal void EnableSuggestions(ViewNode node, Func<TextSuggestionQuery, CancellationToken, Task<TextSuggestionResult>> query)
    {
        if (suggestions is not null || IsSensitive || IsLive || text is null || node.TextEntrySuggestions is not { } source || !TextSuggestionContract.IsValid(source)) return;
        suggestionStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxLines = 2,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(suggestionStatus, "Widget.TextEntry.SuggestionStatus");
        AutomationProperties.SetLiveSetting(suggestionStatus, AutomationLiveSetting.Polite);
        suggestionGrid = new Grid { ColumnSpacing = 4, RowSpacing = 4 };
        for (var column = 0; column < 3; column++) suggestionGrid.ColumnDefinitions.Add(new());
        for (var row = 0; row < (source.MaximumResults + 2) / 3; row++) suggestionGrid.RowDefinitions.Add(new() { Height = new(40) });
        Grid.SetColumnSpan(suggestionStatus, 3);
        Grid.SetRowSpan(suggestionStatus, suggestionGrid.RowDefinitions.Count);
        suggestionGrid.Children.Add(suggestionStatus);
        for (var index = 0; index < source.MaximumResults; index++)
        {
            var position = index;
            var button = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0, MinHeight = 36,
                Padding = new(6, 2, 6, 2), Visibility = Visibility.Collapsed, UseSystemFocusVisuals = true };
            AutomationProperties.SetAutomationId(button, "Widget.TextEntry.Suggestion." + index);
            button.Click += (_, _) => AcceptSuggestion(position);
            Grid.SetRow(button, index / 3); Grid.SetColumn(button, index % 3);
            suggestionGrid.Children.Add(button); suggestionButtons.Add(button);
        }
        body.Children.Insert(body.Children.IndexOf(keys), suggestionGrid);
        suggestions = new(node.Id, source, maximumLength, query, state =>
        {
            void Apply() { if (!completed && suggestions?.IsCurrent(state) == true) ApplySuggestions(state); }
            if (DispatcherQueue.HasThreadAccess) Apply(); else DispatcherQueue.TryEnqueue(Apply);
        });
        text.TextChanged += SuggestionsTextChanged;
        ApplySuggestionMetrics(text.FontSize, text.FontWeight, text.FontFamily);
        suggestions.Update(text.Text);
    }

    private void SuggestionsTextChanged(object sender, TextChangedEventArgs args)
    {
        if (!completed && !acceptingSuggestion) suggestions?.Update(text!.Text);
    }

    private void ApplySuggestions(TextSuggestionView state)
    {
        // Clearing old choices after an edit must not leave focus on a collapsed button.
        if (state.Items.Count == 0) RestoreKeyFromSuggestion();
        suggestionView = state;
        suggestionStatus!.Text = state.Loading ? "Searching…" : state.Unavailable ? "Suggestions unavailable" :
            state.Queried && state.Items.Count == 0 ? "No matches" : "";
        suggestionStatus.Visibility = suggestionStatus.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        for (var index = 0; index < suggestionButtons.Count; index++)
        {
            var button = suggestionButtons[index];
            if (index < state.Items.Count)
            {
                var item = state.Items[index];
                button.Content = new TextBlock { Text = item.Label, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
                AutomationProperties.SetName(button, item.Description is { Length: > 0 } description ? item.Label + ", " + description : item.Label);
                ToolTipService.SetToolTip(button, item.Description is { Length: > 0 } detail ? item.Label + "\n" + detail : item.Label);
                button.Visibility = Visibility.Visible;
            }
            else button.Visibility = Visibility.Collapsed;
        }
        // The reserved rows keep the keyboard keys in place during asynchronous results.
    }

    private void AcceptSuggestion(int index)
    {
        if (completed || text is null || suggestionView is not { } state || index >= state.Items.Count) return;
        if (suggestions?.Accept(state.Revision, state.Items[index].Id) is not { } value) return;
        acceptingSuggestion = true;
        try { text.Text = value; text.Select(value.Length, 0); }
        finally { acceptingSuggestion = false; }
        keyboardInputStarted = true;
        (rememberedKey ?? characters[10]).Focus(FocusState.Keyboard);
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
        if (ReferenceEquals(focused, text) && key == Windows.System.VirtualKey.Down &&
            suggestionButtons.FirstOrDefault(button => button.Visibility == Visibility.Visible) is { } first)
        { first.Focus(FocusState.Keyboard); return true; }
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
        var visible = suggestionButtons.Where(button => button.Visibility == Visibility.Visible).ToArray();
        if (visible.Length == 0 || XamlRoot is null) return false;
        var focused = FocusManager.GetFocusedElement(XamlRoot) as Button;
        var index = focused is null ? -1 : Array.IndexOf(visible, focused);
        if (index >= 0)
        {
            Button target;
            if (direction is FocusNavigationDirection.Left or FocusNavigationDirection.Right)
                target = visible[(index + visible.Length + (direction == FocusNavigationDirection.Left ? -1 : 1)) % visible.Length];
            else if (direction == FocusNavigationDirection.Down && index + 3 < visible.Length) target = visible[index + 3];
            else if (direction == FocusNavigationDirection.Up && index >= 3) target = visible[index - 3];
            else target = characters[index % 3 * 3];
            target.Focus(FocusState.Keyboard); return true;
        }
        if (direction == FocusNavigationDirection.Up && focused is not null && commands.ContainsKey(focused) && Grid.GetRow(focused) == 0)
        {
            visible[Math.Min(visible.Length - 1, Grid.GetColumn(focused) / 7)].Focus(FocusState.Keyboard); return true;
        }
        return false;
    }

    private void ApplySuggestionMetrics(double size, Windows.UI.Text.FontWeight weight, FontFamily? family)
    {
        if (suggestionGrid is null) return;
        suggestionGrid.ColumnSpacing = suggestionGrid.RowSpacing = 4 * interfaceScale;
        foreach (var row in suggestionGrid.RowDefinitions) row.Height = new(40 * interfaceScale);
        suggestionStatus!.FontSize = size * .85;
        suggestionStatus.FontWeight = weight;
        if (family is not null) suggestionStatus.FontFamily = family; else suggestionStatus.ClearValue(TextBlock.FontFamilyProperty);
        foreach (var button in suggestionButtons)
        {
            button.FontSize = size; button.FontWeight = weight;
            button.MinHeight = 36 * interfaceScale; button.Padding = new(6 * interfaceScale, 2 * interfaceScale, 6 * interfaceScale, 2 * interfaceScale);
            if (family is not null) button.FontFamily = family; else button.ClearValue(Control.FontFamilyProperty);
        }
    }

    private void RetireSuggestions()
    {
        if (text is not null) text.TextChanged -= SuggestionsTextChanged;
        suggestions?.Dispose(); suggestions = null; suggestionView = null;
    }

    private void RestoreKeyFromSuggestion()
    {
        if (XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is Button button && suggestionButtons.Contains(button))
            (rememberedKey ?? characters[10]).Focus(FocusState.Keyboard);
    }
}
