using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;
using Windows.Foundation;

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
        var key = (FrameworkElement)FindDialog("Widget.TextEntry.Key.10")!;
        var keyPosition = key.TransformToVisual(dialog).TransformPoint(new Point());
        var focus = FocusedId;
        ((TextBox)Editor()).Text = "fi";
        await WaitForSuggestions();
        Check(FocusedId == focus && key.TransformToVisual(dialog).TransformPoint(new Point()) == keyPosition,
            "suggestions arrive without moving keyboard focus or key positions");
        ((Control)FindDialog("Widget.TextEntry.Key.0")!).Focus(FocusState.Keyboard);
        presenter.MoveFocus(Microsoft.UI.Xaml.Input.FocusNavigationDirection.Up);
        Check(FocusedId == "Widget.TextEntry.Suggestion.0", "controller can enter suggestions from keyboard");
        await presenter.HandleControllerButtonAsync(ControllerButton.A);
        Check(((TextBox)Editor()).Text == "Fireball" && actions.Count == count && presenter.HasTransientControl,
            "suggestion replaces entire entry without committing");
        await Task.Delay(300);
        Check(calls == 1, "accepting a suggestion does not issue a redundant lookup");
        await presenter.HandleControllerButtonAsync(ControllerButton.RightTrigger); await Closed();
        Check(actions.Count == count + 1 && actions.Last().Action.CommittedText == "Fireball", "Done commits accepted suggestion once");

        await Open(); dialog = (WidgetTextEntryDialog)FindDialog("Widget.TextEntry.Dialog")!;
        dialog.EnableSuggestions(node, (_, _) => Task.FromResult(TextSuggestionResult.Empty));
        ((TextBox)Editor()).Text = "no match";
        await Task.Delay(600);
        Check(((TextBlock)FindDialog("Widget.TextEntry.SuggestionStatus")!).Text == "No matches" && ((TextBox)Editor()).Text == "no match",
            "no results leaves typed text intact");
        await presenter.HandleControllerButtonAsync(ControllerButton.B); await Closed();
        Check(actions.Count == count + 1, "Cancel does not submit a search");

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
