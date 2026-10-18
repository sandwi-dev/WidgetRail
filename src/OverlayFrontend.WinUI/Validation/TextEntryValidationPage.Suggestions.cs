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
        ((TextBox)Editor()).Text = "fi";
        await WaitForSuggestions();
        await Task.Delay(60);
        Check(FocusedId == focus && suggestionRows.RowDefinitions.Count == 1 && suggestionRows.ActualHeight > 0,
            "two suggestions occupy one row without moving keyboard focus");
        Check(WidgetViewPresenter.CaptureKeyboardGuide(dialog).Any(h=>h.Button==ControllerButton.LeftStick),
            "first-suggestion shortcut is advertised when results arrive");
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
            .Select(i => new TextSuggestion("result" + i, "Result " + i, "Result " + i)).ToArray())));
        var adaptiveRows = (Grid)FindDialog("Widget.TextEntry.Suggestions")!;
        ((TextBox)Editor()).Text = "many"; await WaitForSuggestions(); await Task.Delay(60);
        var manyHeight = adaptiveRows.ActualHeight;
        Check(adaptiveRows.RowDefinitions.Count == 2 && manyHeight > 60, "six suggestions expand to two populated rows");
        ((TextBox)Editor()).Text = "one";
        for (var attempt = 0; attempt < 120 &&
            (adaptiveRows.Children.OfType<Button>().Count(button => button.Visibility == Visibility.Visible) != 1 ||
             adaptiveRows.ActualHeight >= manyHeight); attempt++) await Task.Delay(25);
        Check(adaptiveRows.RowDefinitions.Count == 1 && adaptiveRows.ActualHeight < manyHeight,
            "shrinking results remove unused rows without keeping an empty second row");
        ((TextBox)Editor()).Text = ""; await Task.Delay(60);
        Check(adaptiveRows.Visibility == Visibility.Collapsed, "clearing the field collapses all suggestion space");
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
        presenter.DismissTransientControl(); await Closed(); sensitive = false; Apply();
    }

    private async Task WaitForSuggestions()
    {
        for (var i = 0; i < 100; i++)
        {
            if (FindDialog("Widget.TextEntry.Suggestion.0") is { Visibility: Visibility.Visible }) return;
            await Task.Delay(25);
        }
        throw new TimeoutException("Suggestions did not arrive");
    }
}
