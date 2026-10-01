using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WebBrowserPresentationTests
{
    internal static Task Contract()
    {
        var document = new WebBrowserDocument("browser.page", 1, "https://example.com/guide", "Game guide");
        var first = new WidgetView(UI.Stack("root", UI.WebBrowser(document, "browser")), "browser").CreateSnapshot("browser.widget", 1);
        Check(first.ProtocolVersion == ProtocolConstants.WebBrowserVersion);
        Check(first.Root.Children[0].IsFocusable);
        Check(SnapshotJson.Deserialize(SnapshotJson.Serialize(first)).Root.Children[0].WebBrowser == document);
        Check(ViewSnapshotValidator.Validate(first with { ProtocolVersion = 65 }).Any(error => error.Code == "feature_requires_version"));
        foreach (var url in new[] { "file:///C:/x", "javascript:alert(1)", "data:text/html,test", "https://user:pw@example.com", " https://example.com", "https://example.com/\n" })
            Check(!(document with { Url = url }).IsWellFormed());
        Check(!(document with { NavigationId = 0 }).IsWellFormed());
        Check(!(document with { NavigationId = WebBrowserDocument.MaximumNavigationId + 1 }).IsWellFormed());
        Check(!(document with { Id = "bad id" }).IsWellFormed());
        var node = first.Root.Children[0];
        Check(ViewSnapshotValidator.Validate(first with { Root = first.Root with { Children = [node with { ActionId = "fake" }] } })
            .Any(error => error.Code == "invalid_web_browser"));
        Check(ViewSnapshotValidator.Validate(first with { Root = first.Root with { WebBrowser = document } })
            .Any(error => error.Code == "web_browser_not_allowed"));
        const string generation = "cccccccccccccccccccccccccccccccc";
        var second = first with { Sequence = 2, Root = first.Root with { Children = [node with
            { WebBrowser = document with { NavigationId = 2, Url = "https://example.com/next" } }] } };
        var update = WidgetPresentationDiff.Create(first, second, generation, 1, PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate).Update;
        Check(update is not null);
        Check(SnapshotJson.Serialize(PresentationUpdateMaterializer.Apply(first, update!, generation)).SequenceEqual(SnapshotJson.Serialize(second)));
        Check(PresentationPropertyMetadata.Impact(PresentationProperty.WebBrowser).HasFlag(PresentationPropertyImpact.Authority));
        return Task.CompletedTask;
    }
    private static void Check(bool value, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(value))] string? expression = null)
    { if (!value) throw new Exception(expression); }
}
