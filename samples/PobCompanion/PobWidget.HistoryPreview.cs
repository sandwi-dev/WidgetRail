using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private sealed record HistoryPreviews(string Current, string? Undo, string? Redo, PobHistorySummary? UndoSummary, PobHistorySummary? RedoSummary);
    private HistoryPreviews? historyPreviews;

    private void PrepareHistoryPreviews(PobWorkState work, System.Text.Json.JsonElement context)
    {
        var back = work.Undo.FirstOrDefault(); var forward = work.Redo.FirstOrDefault();
        if (historyPreviews is { } old && old.Current == work.CommittedXml && old.Undo == back && old.Redo == forward) return;
        var prepared = new HistoryPreviews(work.CommittedXml, back, forward,
            back is null ? null : PobHistoryPreview.Describe(work.CommittedXml, back, context),
            forward is null ? null : PobHistoryPreview.Describe(work.CommittedXml, forward, context));
        lock (gate) historyPreviews = prepared;
    }

    private WidgetElement HistoryButton(bool backwards)
    {
        var action = backwards ? "undo" : "redo"; var title = backwards ? "Undo" : "Redo";
        var history = backwards ? undo : redo;
        var button = (UI.Button((backwards ? "↶ " : "↷ ") + title, action, "pob." + action) with { AccessibilityLabel = title })
            .Disabled(history.Count == 0 || pendingBefore is not null).Classes("pob-button", "pob-history-button");
        if (history.Count == 0 || pendingBefore is not null || historyPreviews is not { } prepared || prepared.Current != result?.Xml ||
            (backwards ? prepared.Undo : prepared.Redo) != history.Peek()) return button;
        var summary = backwards ? prepared.UndoSummary : prepared.RedoSummary;
        if (summary is null) return button;
        var prefix = "pob." + action + ".preview";
        var rows = new List<WidgetElement> { UI.Text(title + " · " + summary.Title, prefix + ".title").Classes("pob-preview-title") };
        for (var index = 0; index < summary.Changes.Length; index++)
        {
            var change = summary.Changes[index];
            rows.Add(UI.Stack(prefix + ".change." + index,
                UI.Text(change.Label, prefix + ".label." + index).Classes("pob-preview-meta"),
                UI.Text(change.Before + " → " + change.After, prefix + ".value." + index).Classes("pob-history-preview-value"))
                .Classes("pob-history-preview-change"));
        }
        if (summary.OtherChanges > 0) rows.Add(UI.Text("And " + summary.OtherChanges + " other changes", prefix + ".more").Classes("pob-preview-meta"));
        return button.PresentOnFocus(UI.Stack(prefix, rows.ToArray()).Classes("pob-preview-content"));
    }
}
