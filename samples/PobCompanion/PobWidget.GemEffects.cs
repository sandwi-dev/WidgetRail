using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private sealed record GemEffectsContext(PobResult Build, string? Document, JsonElement Group);
    private GemEffectsContext? gemEffectsContext;
    private WidgetFocusPreviewSource<GemEffectsContext>? gemEffectsPreviews;

    private WidgetElement WithGemEffectsPreviews(WidgetElement content, JsonElement group)
    {
        if (gemEffectsContext is null || !ReferenceEquals(gemEffectsContext.Build, result) ||
            gemEffectsContext.Document != documentId || I(gemEffectsContext.Group, "id") != I(group, "id"))
        {
            gemEffectsContext = new(result!, documentId, group);
            if (gemEffectsPreviews is null)
                gemEffectsPreviews = CreateFocusPreviewSource("pob.gem-effects.source", gemEffectsContext,
                    new WidgetFocusPreviewOptions<GemEffectsContext> { Resolve = ResolveGemEffectsAsync });
            else gemEffectsPreviews.Publish(gemEffectsContext);
        }
        return UI.FocusPresentationSurface(content, Text("", "gem-effects.empty"), "pob.gem-effects") with
        {
            Preview = new(Width: 480, Height: 520) { SizeToContentHeight = true },
            PreviewSource = gemEffectsPreviews!.Descriptor,
        };
    }

    private async ValueTask<WidgetElement> ResolveGemEffectsAsync(GemEffectsContext context, string key, CancellationToken token)
    {
        if (!int.TryParse(key, out var id)) throw new ArgumentException("Unknown gem preview key.");
        var gem = A(context.Group, "gems").Single(g => I(g, "id") == id);
        try
        {
            var details = await engine.ReadGemDetailsAsync(I(context.Group, "id"), id, context.Build.Xml, token, WidgetLifetimeToken);
            token.ThrowIfCancellationRequested();
            return UI.Stack("pob.gem-effects.content", GemEffectsRows(details.State, context.Group, gem, preview: true))
                .Classes("pob-preview-content");
        }
        catch (IOException)
        {
            token.ThrowIfCancellationRequested();
            return Text("Gem details are unavailable. Select the gem to retry.", "gem-effects.unavailable", "pob-muted");
        }
    }

    private static WidgetElement[] GemEffectsRows(JsonElement details, JsonElement group, JsonElement gem, bool preview)
    {
        var name = S(gem, "name");
        var rows = new List<WidgetElement>();
        if (preview) rows.Add(Text(name, "gem-effects.title", "pob-preview-title pob-blue"));
        if (preview) rows.Add(Text("Level " + S(gem, "level") + " · Quality " + S(gem, "quality") + "%" +
            (B(gem, "enabled") ? "" : " · Disabled"), "gem-effects.level", "pob-preview-meta"));
        if (B(gem, "levelReadOnly"))
            rows.Add(Text("Granted by " + S(group, "sourceName", "build effects") +
                (S(group, "slot").Length > 0 ? " · " + S(group, "slot") : "") + ". Level follows its source.",
                "gem-effects.source", "pob-preview-meta"));
        var lines = A(details, "lines").Where(line => S(line, "text").Trim() != name).ToArray();
        var shown = preview ? lines.Where(line => S(line, "kind") is "description" or "effect" or "heading").Take(8).ToArray() : lines;
        if (preview && shown.Length == 0) shown = lines.Take(6).ToArray();
        foreach (var (line, index) in shown.Select((line, index) => (line, index)))
        {
            var runs = ReadFormattedRuns(line, "text", "runs");
            AddRuns(rows, preview ? SummaryRuns(runs, S(line, "kind") == "description" ? 480 : 220, maximumRuns: 25) : runs,
                "gem-effects.line." + index, preview ? "pob-preview-description" : "pob-text");
        }
        if (lines.Length == 0) rows.Add(Text("PoB provides no description for this gem.", "gem-effects.none", "pob-muted"));
        if (preview) rows.Add(Text("Select gem for full details", "gem-effects.more", "pob-preview-meta"));
        return rows.ToArray();
    }

    private bool HandleGemEffectsAction(WidgetActionEvent action)
    {
        if (!action.ActionId.StartsWith("gem-details.", StringComparison.Ordinal)) return false;
        if (detailKind != "skill" || result is null || !int.TryParse(action.ActionId[12..], out var id)) return true;
        var captured = result;
        var group = A(State, "skills").FirstOrDefault(g => I(g, "id") == detailId);
        var gem = A(group, "gems").FirstOrDefault(g => I(g, "id") == id);
        if (gem.ValueKind != JsonValueKind.Object) return true;
        Work(async token =>
        {
            var details = await engine.ReadGemDetailsAsync(I(group, "id"), id, captured.Xml, token, WidgetLifetimeToken);
            token.ThrowIfCancellationRequested();
            var rows = GemEffectsRows(details.State, group, gem, preview: false);
            lock (gate)
                if (ReferenceEquals(result, captured)) featureReading = (S(gem, "name"), rows);
        }, "Reading gem details…");
        return true;
    }
}
