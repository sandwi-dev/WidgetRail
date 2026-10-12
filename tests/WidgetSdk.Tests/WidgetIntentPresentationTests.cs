using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WidgetIntentPresentationTests
{
    internal static Task Declarations()
    {
        var button = UI.Button("Read guide", "read-guide", "guide")
            .OpenIntent(WidgetIntentContracts.Web, Json("""{"url":"https://example.com/guide"}"""));
        var snapshot = new WidgetView(UI.Stack("root", button), "guide").CreateSnapshot("intent.widget", 1);
        Check(snapshot.ProtocolVersion == ProtocolConstants.WidgetIntentVersion);
        Check(ViewSnapshotValidator.Validate(snapshot).Count == 0);
        var restored = SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot));
        Check(restored.Root.Children[0].Intent!.Matches(snapshot.Root.Children[0].Intent));
        Check(ViewSnapshotValidator.Validate(snapshot with { ProtocolVersion = 64 }).Any(e => e.Code == "feature_requires_version"));
        var ordinary = new WidgetView(UI.Stack("root", button with { Intent = null })).CreateSnapshot("intent.widget", 2);
        Check(ordinary.ProtocolVersion < ProtocolConstants.WidgetIntentVersion);
        Check(!System.Text.Encoding.UTF8.GetString(SnapshotJson.Serialize(ordinary)).Contains("\"intent\""));
        using (var source = JsonDocument.Parse("""{"url":"https://example.com"}"""))
            button = button.OpenIntent(WidgetIntentContracts.Web, source.RootElement);
        Check(button.Intent!.IsWellFormed());
        var explicitOpen = button.OpenIntent(WidgetIntentContracts.Web, button.Intent.Payload, WidgetIntentPresentation.OpenWidget);
        Check(!button.Intent.Matches(explicitOpen.Intent));
        Check(!(button.Intent with { Presentation = (WidgetIntentPresentation)999 }).IsWellFormed());
        return Task.CompletedTask;
    }

    internal static Task ComponentDeclarations()
    {
        foreach (var routing in Enum.GetValues<WidgetIntentRouting>())
        foreach (var presentation in Enum.GetValues<WidgetIntentPresentation>())
        {
            var intent = WidgetIntentRequest.Create(WidgetIntentContracts.Web,
                Json("""{"url":"https://example.com/guide"}"""), routing) with { Presentation = presentation };
            var action = new ComponentAction("Read guide", "read-guide") { Intent = intent };
            var icon = WidgetIcon.Glyph(WidgetGlyph.Connection);
            var components = new WidgetView(UI.Stack("components",
                UI.SettingsRow("Guide", action, "setting"),
                UI.SettingsRow(icon, "Guide", action, "icon-setting"),
                UI.Alert("Guide", "Read more", AlertTone.Info, "alert", action: action),
                UI.Alert(icon, "Guide", "Read more", AlertTone.Info, "icon-alert", action),
                UI.EmptyState("No results", "Read more", "empty", action),
                UI.EmptyState(icon, "No results", "Read more", "icon-empty", action),
                UI.ActionSheet("Links", "links", "links.scope", "back",
                    [new ActionSheetItem("links.open", "Read guide", "read-guide") { Intent = intent }]),
                UI.SettingsRow("Local action", new ComponentAction("Refresh", "refresh"), "local")))
                .CreateSnapshot("intent.components", 1);
            Check(ViewSnapshotValidator.Validate(components).Count == 0);
            var restored = SnapshotJson.Deserialize(SnapshotJson.Serialize(components));
            var declarations = Flatten(restored.Root).Where(node => node.Intent is not null).ToArray();
            Check(declarations.Length == 7 && declarations.All(node =>
                node.Kind == ViewNodeKind.Button && node.ActionId == "read-guide" && intent.Matches(node.Intent)));
            Check(Flatten(restored.Root).Single(node => node.Id == "local.action").Intent is null);
            Check(ViewSnapshotValidator.Validate(components with { ProtocolVersion = 64 })
                .Any(error => error.Code == "feature_requires_version"));
        }
        return Task.CompletedTask;
        static IEnumerable<ViewNode> Flatten(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Flatten));
    }

    internal static Task InvalidDeclarations()
    {
        var intent = WidgetIntentRequest.Create(WidgetIntentContracts.Web, Json("""{"url":"https://example.com"}"""));
        var snapshot = new WidgetView(UI.Stack("root", UI.Button("Open", "open", "open"))).CreateSnapshot("intent.widget", 1)
            with { ProtocolVersion = ProtocolConstants.WidgetIntentVersion };
        Check(ViewSnapshotValidator.Validate(snapshot with { Root = snapshot.Root with { Intent = intent } }).Any(e => e.Code == "intent_not_allowed"));
        var node = snapshot.Root.Children[0];
        foreach (var invalid in new[] { intent with { Payload = default }, intent with { SchemaDigest = "bogus" },
            intent with { Payload = Json("""{"url":"a","url":"b"}""") }, intent with { Version = 0 } })
            Check(ViewSnapshotValidator.Validate(snapshot with { Root = snapshot.Root with { Children = [node with { Intent = invalid }] } })
                .Any(e => e.Code == "invalid_intent"));
        Check(!intent.Matches(intent with { Payload = Json("""{"url":"https://other.example"}""") }));
        var before = new WidgetIntentRequest("example.list", 1, new string('a', 64), Json("""{"items":[1,2],"other":true}"""));
        Check(before.Matches(before with { Payload = Json("""{"other":true,"items":[1.0,2]}""") }));
        Check(!before.Matches(before with { Payload = Json("""{"items":[2,1],"other":true}""") }));
        return Task.CompletedTask;
    }

    internal static Task Updates()
    {
        const string generation = "abababababababababababababababab";
        var first = new WidgetView(UI.Stack("root", UI.Button("Read", "read", "guide")
            .OpenIntent(WidgetIntentContracts.Web, Json("""{"url":"https://example.com/first"}"""))))
            .CreateSnapshot("intent.widget", 1);
        var node = first.Root.Children[0];
        var next = first with { Sequence = 2, Root = first.Root with { Children = [node with
        { Intent = WidgetIntentRequest.Create(WidgetIntentContracts.Web, Json("""{"url":"https://example.com/next"}""")) }] } };
        var batch = WidgetPresentationDiff.Create(first, next, generation, 1, PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate).Update;
        Check(batch is not null);
        var materialized = PresentationUpdateMaterializer.Apply(first, batch!, generation);
        Check(SnapshotJson.Serialize(materialized).SequenceEqual(SnapshotJson.Serialize(next)));
        var cleared = next with { Sequence = 3, Root = next.Root with { Children = [node with { Intent = null }] } };
        batch = WidgetPresentationDiff.Create(next, cleared, generation, 2, PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate).Update;
        Check(batch is not null);
        Check(SnapshotJson.Serialize(PresentationUpdateMaterializer.Apply(next, batch!, generation)).SequenceEqual(SnapshotJson.Serialize(cleared)));
        Check(PresentationPropertyMetadata.Impact(PresentationProperty.Intent).HasFlag(PresentationPropertyImpact.Authority));
        return Task.CompletedTask;
    }

    private static JsonElement Json(string text) { using var document = JsonDocument.Parse(text); return document.RootElement.Clone(); }
    private static void Check(bool value, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(value))] string? expression = null)
    { if (!value) throw new Exception(expression); }
}
