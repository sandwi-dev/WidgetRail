using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class TextEntryValidationPage
{
    private async Task VerifySuggestionsAsync()
    {
        var duplicate = new WidgetTextEntryDialog(new ViewNode { Id = "label", Kind = ViewNodeKind.TextEntry,
            AccessibilityLabel = "Search inventory", TextEntryPlaceholder = "Search inventory" }, _ => { });
        var duplicateEditor = (TextBox)duplicate.EditorForValidation;
        Check(duplicateEditor.Header is null && duplicateEditor.PlaceholderText == "" &&
            Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(duplicateEditor) == "Search inventory",
            "matching field title and hint render once while retaining the editor accessible name");
        duplicate.Erase();
        sensitive = false; value = ""; maximumLength = 100; Apply(); await Open();
        var count = actions.Count;
        var dialog = (WidgetTextEntryDialog)FindDialog("Widget.TextEntry.Dialog")!;
        var node = new ViewNode { Id = "entry", Kind = ViewNodeKind.TextEntry,
            TextEntrySuggestions = new("fixture") { DebounceMilliseconds = 300, MinimumRequestIntervalMilliseconds = 500, TimeoutMilliseconds = 3000 },
            TextEntryMaximumLength = 100 };
        var calls = 0;
        dialog.EnableSuggestions(node, (query, _) =>
        {
            if (query.TimeoutMilliseconds != 3000) throw new InvalidOperationException("Configured suggestion deadline was lost");
            calls++;
            return Task.FromResult(query.Text.StartsWith("fi", StringComparison.Ordinal)
                ? new TextSuggestionResult((TextSuggestion[])[new("fireball", "Fireball", "Fireball"), new("firetrap", "Fire Trap", "Fire Trap")])
                : TextSuggestionResult.Empty);
        });
        await Task.Delay(100);
        var suggestionRows = (Grid)FindDialog("Widget.TextEntry.Suggestions")!;
        Check(suggestionRows.Visibility == Visibility.Collapsed, "empty suggestions reserve no keyboard space");
        await CapturePopupAsync(dialog, "text-suggestions-empty");
        var focus = FocusedId;
        var anchored = dialog.SuggestionPositionForValidation;
        Check(((TextBox)Editor()).Header is null && ((TextBox)Editor()).PlaceholderText == "Enter text",
            "keyboard labels the editor once and uses a native placeholder for the distinct hint");
        ((TextBox)Editor()).Text = "fi";
        await WaitForSuggestions();
        Check(dialog.SuggestionsAnimatingForValidation, "suggestion growth uses a short resize animation");
        for (var sample = 0; sample < 8; sample++)
        {
            await Task.Delay(25);
            var position = dialog.SuggestionPositionForValidation;
            Check(Math.Abs(position.EditorY - anchored.EditorY) <= 2 && Math.Abs(position.KeyY - anchored.KeyY) <= 2,
                "suggestion growth keeps the editor and key positions anchored, sample " + sample);
        }
        Check(FocusedId == focus && suggestionRows.RowDefinitions.Count == 1 && suggestionRows.ActualHeight > 0,
            "two suggestions occupy one row without moving keyboard focus");
        Check(WidgetViewPresenter.CaptureKeyboardGuide(dialog).Any(h=>h.Button==ControllerButton.LeftStick),
            "first-suggestion shortcut is advertised when results arrive");
        ((Control)FindDialog("Widget.TextEntry.Paste")!).Focus(FocusState.Keyboard);
        await presenter.HandleControllerButtonAsync(ControllerButton.RightStick);
        Check(FocusedId == "Widget.TextEntry.Suggestion.0" && WidgetViewPresenter.CaptureKeyboardGuide(dialog).Any(h => h.Button == ControllerButton.RightStick && h.Label == "Back to keyboard"),
            "R3 enters suggestions from a bottom-row key and advertises return");
        await presenter.HandleControllerButtonAsync(ControllerButton.RightStick);
        Check(FocusedId == "Widget.TextEntry.Paste", "R3 returns to the exact remembered key");
        await CapturePopupAsync(dialog, "text-suggestions-ready");
        await presenter.HandleControllerButtonAsync(ControllerButton.LeftStick, ControllerEventPhase.Released);
        await presenter.HandleControllerButtonAsync(ControllerButton.LeftStick, ControllerEventPhase.Repeated);
        Check(((TextBox)Editor()).Text == "fi", "release and repeat cannot accept the first suggestion");
        await presenter.HandleControllerButtonAsync(ControllerButton.LeftStick);
        Check(((TextBox)Editor()).Text == "Fireball" && actions.Count == count && presenter.HasTransientControl && !dialog.CanAcceptFirstSuggestion,
            "left-stick press fills the first suggestion without committing or retaining an obsolete shortcut");
        Check(suggestionRows.Visibility == Visibility.Collapsed, "accepting a suggestion removes its unused rows");
        ((TextBox)Editor()).Text = "fir";
        await WaitForSuggestions();
        ((Control)FindDialog("Widget.TextEntry.Key.0")!).Focus(FocusState.Keyboard);
        presenter.MoveFocus(Microsoft.UI.Xaml.Input.FocusNavigationDirection.Up);
        Check(FocusedId == "Widget.TextEntry.Suggestion.0", "controller can enter suggestions from keyboard");
        await presenter.HandleControllerButtonAsync(ControllerButton.A);
        Check(((TextBox)Editor()).Text == "Fireball" && actions.Count == count && presenter.HasTransientControl,
            "suggestion replaces entire entry without committing");
        await Task.Delay(300);
        Check(calls == 2, "accepting a suggestion does not issue a redundant lookup");
        await presenter.HandleControllerButtonAsync(ControllerButton.RightTrigger); await Closed();
        Check(actions.Count == count + 1 && actions.Last().Action.CommittedText == "Fireball", "Done commits accepted suggestion once");

        await Open(); dialog = (WidgetTextEntryDialog)FindDialog("Widget.TextEntry.Dialog")!;
        dialog.EnableSuggestions(node, (_, _) => Task.FromResult(TextSuggestionResult.Empty));
        ((TextBox)Editor()).Text = "no match";
        await Task.Delay(600);
        Check(((TextBlock)FindDialog("Widget.TextEntry.SuggestionStatus")!).Text == "No matches" && ((TextBox)Editor()).Text == "no match",
            "no results leaves typed text intact");
        var emptyRows = (Grid)FindDialog("Widget.TextEntry.Suggestions")!;
        Check(emptyRows.RowDefinitions.Count == 1 && emptyRows.RowDefinitions[0].Height.IsAuto && emptyRows.ActualHeight < 40,
            "no-match status uses only its text height instead of reserved result rows");
        await CapturePopupAsync(dialog, "text-suggestions-no-matches");
        await presenter.HandleControllerButtonAsync(ControllerButton.LeftStick);
        Check(((TextBox)Editor()).Text == "no match" && !WidgetViewPresenter.CaptureKeyboardGuide(dialog).Any(h=>h.Button==ControllerButton.LeftStick),
            "first-suggestion shortcut is absent and inert without results");
        await presenter.HandleControllerButtonAsync(ControllerButton.B); await Closed();
        Check(actions.Count == count + 1, "Cancel does not submit a search");

        await Open(); dialog = (WidgetTextEntryDialog)FindDialog("Widget.TextEntry.Dialog")!;
        dialog.EnableSuggestions(node, (query, _) => Task.FromResult(new TextSuggestionResult(Enumerable.Range(0, query.Text == "many" ? 6 : 1)
            .Select(i => new TextSuggestion("result" + i, i == 0 ? "Short" : "Longer suggestion label " + i, "Result " + i)).ToArray())));
        ((TextBox)Editor()).Text = "many"; await WaitForSuggestions(); await Task.Delay(60);
        var adaptiveRows = (Grid)FindDialog("Widget.TextEntry.Suggestions")!;
        var manyHeight = adaptiveRows.ActualHeight;
        Check(adaptiveRows.RowDefinitions.Count == 1 && manyHeight <= 40.5 &&
            ((Button)FindDialog("Widget.TextEntry.Suggestion.0")!).ActualWidth < ((Button)FindDialog("Widget.TextEntry.Suggestion.1")!).ActualWidth,
            "suggestions use content widths in one row rather than equal-width cells");
        NativeComputedStyleAdapter.SetMotionPolicy(WidgetRail.PlatformSettings.AppearanceSettings.Default with
            { Motion = WidgetRail.PlatformSettings.MotionPreference.Full }, true);
        dialog.AnchorNearWorkAreaTopForValidation();
        await Task.Delay(250);
        Check(dialog.SuggestionViewportScrollsForValidation && dialog.AllKeysVisibleForValidation,
            "overflowing suggestions use their own horizontal viewport without clipping keyboard actions");
        var constrainedKeys = dialog.SuggestionPositionForValidation;
        await presenter.HandleControllerButtonAsync(ControllerButton.RightStick);
        for (var step = 0; step < 5; step++) presenter.MoveFocus(Microsoft.UI.Xaml.Input.FocusNavigationDirection.Right);
        await Task.Delay(80);
        Check(FocusedId == "Widget.TextEntry.Suggestion.5" && dialog.FocusedSuggestionVisibleForValidation &&
            Math.Abs(dialog.SuggestionPositionForValidation.KeyY - constrainedKeys.KeyY) <= 2,
            "controller navigation reveals a scrolled suggestion without moving the keyboard");
        Check(FindDialog("Widget.TextEntry.SuggestionsFocusHint") is { Visibility: Visibility.Visible, Opacity: 1 },
            "R3 hint stays visible outside the horizontally scrolled results");
        await CapturePopupAsync(dialog, "text-suggestions-scrolling");
        await presenter.HandleControllerButtonAsync(ControllerButton.RightStick);
        dialog.FieldAnchor = null;
        ((TextBox)Editor()).Text = "one";
        for (var attempt = 0; attempt < 120 &&
            (adaptiveRows.Children.OfType<Button>().Count(button => button.Visibility == Visibility.Visible && button.IsEnabled) != 1); attempt++) await Task.Delay(25);
        Check(adaptiveRows.RowDefinitions.Count == 1 && Math.Abs(adaptiveRows.ActualHeight - manyHeight) <= 1,
            "fewer results retain the same compact row height");
        ((TextBox)Editor()).Text = ""; await Task.Delay(60);
        Check(adaptiveRows.Visibility == Visibility.Collapsed, "clearing the field collapses all suggestion space");
        await presenter.HandleControllerButtonAsync(ControllerButton.B); await Closed();

        await Open(); dialog = (WidgetTextEntryDialog)FindDialog("Widget.TextEntry.Dialog")!;
        var replacement = new TaskCompletionSource<TextSuggestionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.EnableSuggestions(node, (query, _) => query.Text == "new" ? replacement.Task :
            Task.FromResult(new TextSuggestionResult((TextSuggestion[])[new("old", "Old result", "Old result")])));
        ((TextBox)Editor()).Text = "old"; await WaitForSuggestions(); await Task.Delay(180);
        ((Control)FindDialog("Widget.TextEntry.Paste")!).Focus(FocusState.Keyboard);
        await presenter.HandleControllerButtonAsync(ControllerButton.RightStick);
        ((TextBox)Editor()).Text = "new";
        var stale = (Button)FindDialog("Widget.TextEntry.Suggestion.0")!;
        Check(!dialog.CanAcceptFirstSuggestion, "changed native text immediately revokes selection before the deferred TextChanged event");
        for (var attempt = 0; attempt < 40 && stale.IsEnabled; attempt++) await Task.Delay(10);
        Check(stale.Visibility == Visibility.Visible && !stale.IsEnabled && !dialog.CanAcceptFirstSuggestion,
            "previous results remain visible but nonselectable throughout refresh");
        Check(FocusedId == "Widget.TextEntry.Paste", "editing while browsing returns to the remembered keyboard key");
        await presenter.HandleControllerButtonAsync(ControllerButton.LeftStick);
        await presenter.HandleControllerButtonAsync(ControllerButton.RightStick);
        dialog.AcceptSuggestionForValidation(0);
        Check(((TextBox)Editor()).Text == "new" && FocusedId == "Widget.TextEntry.Paste",
            "L3, R3 and a queued suggestion activation cannot accept stale results");
        await CapturePopupAsync(dialog, "text-suggestions-updating");
        replacement.SetResult(new((TextSuggestion[])[new("new", "New result", "New result")]));
        await WaitForSuggestions();
        Check(FocusedId == "Widget.TextEntry.Paste", "new results do not move focus back into suggestions");
        NativeComputedStyleAdapter.SetMotionPolicy(WidgetRail.PlatformSettings.AppearanceSettings.Default with
            { Motion = WidgetRail.PlatformSettings.MotionPreference.Reduced }, true);
        ((TextBox)Editor()).Text = "";
        Check(!dialog.SuggestionsAnimatingForValidation, "reduced motion collapses suggestions without animation");
        NativeComputedStyleAdapter.SetMotionPolicy(WidgetRail.PlatformSettings.AppearanceSettings.Default, true);
        await presenter.HandleControllerButtonAsync(ControllerButton.B); await Closed();

        await Open(); dialog = (WidgetTextEntryDialog)FindDialog("Widget.TextEntry.Dialog")!;
        var late = new TaskCompletionSource<TextSuggestionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.EnableSuggestions(node, (_, _) => { entered.TrySetResult(); return late.Task; });
        ((TextBox)Editor()).Text = "late";
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        presenter.DismissTransientControl(); await Closed();
        late.SetResult(new((TextSuggestion[])[new("late", "Late result", "Late result")])); await Task.Delay(100);
        Check(!presenter.HasTransientControl && actions.Count == count + 1, "late results cannot reopen or commit a closed keyboard");

        sensitive = true; Apply(); await Open(); dialog = (WidgetTextEntryDialog)FindDialog("Widget.TextEntry.Dialog")!;
        dialog.EnableSuggestions(node, (_, _) => throw new InvalidOperationException("Sensitive provider must not run"));
        Check(FindDialog("Widget.TextEntry.SuggestionStatus") is null, "password keyboard never exposes suggestion provider");
        await presenter.HandleControllerButtonAsync(ControllerButton.RightStick);
        Check(dialog.PasswordVisible, "R3 retains password reveal when suggestions are prohibited");
        await presenter.HandleControllerButtonAsync(ControllerButton.RightStick);
        Check(!dialog.PasswordVisible, "R3 hides the password again");
        presenter.DismissTransientControl(); await Closed(); sensitive = false; Apply();
    }

    private async Task WaitForSuggestions()
    {
        for (var i = 0; i < 100; i++)
        {
            if (FindDialog("Widget.TextEntry.Suggestion.0") is Button { Visibility: Visibility.Visible, IsEnabled: true }) return;
            await Task.Delay(25);
        }
        throw new TimeoutException("Suggestions did not arrive");
    }
}
