using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WebBrowserPresentationTests
{
    internal static Task Contract()
    {
        var document = new WebBrowserDocument("browser.page", 1, "https://example.com/guide", "Game guide") { InteractionMode = BrowserInteractionMode.ActivateToInteract };
        var first = new WidgetView(UI.Stack("root", UI.WebBrowser(document, BrowserInteractionMode.ActivateToInteract, "browser")), "browser").CreateSnapshot("browser.widget", 1);
        Check(first.ProtocolVersion == ProtocolConstants.WebBrowserVersion);
        Check(first.Root.Children[0].IsFocusable);
        Check(document.InteractionMode == BrowserInteractionMode.ActivateToInteract);
        var provider = new ProviderDocumentReference(new string('a', 32), ProviderDocumentReference.RestrictedHtml);
        var attribution = new WidgetView(UI.Stack("root", UI.ProviderContent(provider, BrowserInteractionMode.ActivateToInteract, "attribution")), "attribution").CreateSnapshot("browser.widget", 1);
        Check(attribution.ProtocolVersion == ProtocolConstants.ProviderDocumentVersion);
        Check(WinUiPresentationContract.Validate(attribution).Count == 0);
        Check(ViewSnapshotValidator.Validate(attribution with { ProtocolVersion = ProtocolConstants.CapturedMediaVersion }).Any(error => error.Code == "feature_requires_version"));
        Check(SnapshotJson.Deserialize(SnapshotJson.Serialize(attribution)).Root.Children[0].WebBrowser?.ProviderDocument == provider);
        Check((document with { Url = provider.Url }).IsWellFormed()); // Ordinary navigation conveys no provider-content authority.
        Check((document with { Url = provider.Url }).ProviderDocument is null);
        Check(!(attribution.Root.Children[0].WebBrowser! with { Url = "https://example.com/forged" }).IsWellFormed());
        Check(!(provider with { Provider = "arbitrary-html" }).IsWellFormed());

        var hostBack = first with { Root = first.Root with { Children = [first.Root.Children[0] with { WebBrowser = document with { InteractionMode = BrowserInteractionMode.InteractOnFocus } }] } };
        Check(SnapshotJson.Deserialize(SnapshotJson.Serialize(hostBack)).Root.Children[0].WebBrowser?.InteractionMode == BrowserInteractionMode.InteractOnFocus);
        Check(SnapshotJson.Deserialize(SnapshotJson.Serialize(first)).Root.Children[0].WebBrowser == document);
        Check(ViewSnapshotValidator.Validate(first with { ProtocolVersion = 65 }).Any(error => error.Code == "feature_requires_version"));
        foreach (var url in new[] { "file:///C:/x", "javascript:alert(1)", "data:text/html,test", "https://user:pw@example.com", " https://example.com", "https://example.com/\n" })
            Check(!(document with { Url = url }).IsWellFormed());
        Check(!(document with { InteractionMode = 0 }).IsWellFormed());
        Check(!(document with { InteractionMode = (BrowserInteractionMode)99 }).IsWellFormed());
        Check(!(document with { NavigationId = 0 }).IsWellFormed());
        var missingMode = first with { Root = first.Root with { Children = [first.Root.Children[0] with { WebBrowser = document with { InteractionMode = 0 } }] } };
        Check(ViewSnapshotValidator.Validate(missingMode).Any(error => error.Code == "invalid_web_browser"));
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
        var entry = UI.TextEntry("", "Address", "open", "address", ProtocolConstants.MaximumExtendedTextEntryLength);
        var extended = new WidgetView(UI.Stack("root", entry)).CreateSnapshot("browser.widget", 1);
        Check(extended.ProtocolVersion == ProtocolConstants.ExtendedTextEntryVersion);
        Check(ViewSnapshotValidator.Validate(extended).Count == 0);
        Check(ViewSnapshotValidator.Validate(extended with { ProtocolVersion = 65 }).Any(error => error.Code == "feature_requires_version"));
        var ordinary = UI.TextEntry("", "Search", "search", "search").ToProtocolNode();
        Check(ordinary.TextEntryMaximumLength == 96);
        var action = new WidgetActionEvent("open", "address", InputScopeId: "root") { CommittedText = new string('a', 2048) };
        var pinned = new PinnedActionInput(1, PinnedSurfaceContract.FullWidgetLayoutId, 1, action);
        Check(PinnedActionContract.Resolve(extended, pinned).Role == "text");
        var rejected = false;
        try { PinnedActionContract.Resolve(extended, pinned with { Action = action with { CommittedText = new string('a', 2049) } }); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected);
        rejected = false;
        try { PinnedActionContract.Resolve(extended with { Root = extended.Root with { Children = [entry.ToProtocolNode() with { TextEntryMaximumLength = 96 }] } }, pinned); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected);
        return Task.CompletedTask;
    }
    private static void Check(bool value, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(value))] string? expression = null)
    { if (!value) throw new Exception(expression); }
}
