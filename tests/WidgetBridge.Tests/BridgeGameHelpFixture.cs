using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;
using WidgetRail.Samples.GameHelp;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

internal static class BridgeGameHelpFixture
{
    internal static async Task ServeAsync(string pipe, string settingsRoot)
    {
        var paths = new PlatformSettingsPaths(Path.GetFullPath(settingsRoot));
        var store = new PlatformSettingsStore(paths);
        await using var appearance = new PlatformAppearanceService(paths, new ThemeManager(store, new ThemeCatalog(paths)));
        await appearance.StartAsync();
        string[] capabilities = [PlatformCapabilities.WindowCaptureV1, PlatformCapabilities.ProviderDocumentsV1];
        var style = WrssPackageLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "gamehelp-default.wrss"));
        var compiled = WrssThemeCompiler.Compile(style);
        if (!compiled.IsValid) throw new InvalidOperationException("Game Help fixture stylesheet is invalid.");
        var widget = new ConfiguredWidget
        {
            Id = "game-help", PackageId = "example.game-help", PublisherId = "example.publisher", Name = "Game Help fixture", InstanceId = "game-help.instance",
            WorkerExecutable = Environment.ProcessPath!, WorkerFingerprint = new string('a', 64), CatalogFingerprint = new string('a', 64),
            PinningSupported = true, FullWidgetPinningSupported = true, DeclaredCapabilities = capabilities,
            ExecutionTrust = WidgetExecutionTrust.FullTrustCurrentUser,
            CompiledTheme = compiled.Theme, StylePackage = style,
        };
        var identity = new BrokerWidgetIdentity(widget.PackageId, widget.PublisherId, widget.InstanceId);
        var consent = new ConsentStore(Path.Combine(settingsRoot, "consent"));
        foreach (var capability in capabilities) await consent.SetDecisionAsync(identity, capability, ConsentDecision.Grant);
        var simulator = new SimulatedPlatformBrokerBackend();

        await using var backend = new CompositePlatformBrokerBackend(simulator, simulator, appLibrary: simulator);
        await using var server = new WidgetBridgeServer(pipe, new([widget]), appearance: appearance, consentStore: consent, platformBackend: backend);
        await server.RunAsync(TimeSpan.FromSeconds(30), default);
    }
    internal sealed class FakeGemini(Func<WidgetHostServices> services) : IGameHelpService
    {
        public Task<bool> HasKeyAsync(CancellationToken token) => Task.FromResult(true);
        public Task SaveKeyAsync(string key, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteKeyAsync(CancellationToken token) => Task.CompletedTask;
        public async Task<GameHelpAnswer> AskAsync(GameHelpRequest request, CancellationToken cancellationToken)
        {
            var bytes = request.Attachment is null ? null : await services().Capture.ReadContentAsync(request.Attachment, cancellationToken);
            if (bytes is not { Length: > 12 }) throw new InvalidOperationException("Capture bytes did not reach the application provider.");
            var answer = new GameHelpAnswer("Synthetic answer: take the green path.", ["Red and green fixture"], "Synthetic test content", [new("What is on the green path?", "Tell me what is on the green path.")], [],
                request.WebGrounding ? [new("Fixture source", "https://example.com/guide")] : [],
                SearchSuggestionsHtml: request.WebGrounding ? ["<style>body{margin:0}a{position:fixed;inset:0;display:grid;place-items:center;background:#eee;color:#123}</style><script>window.providerScriptRan=true</script><img src='http://127.0.0.1:9/must-not-load'><a href='https://www.google.com/search?q=fixture'>Fixture Google Search suggestion</a>"] : null,
                DetailedSolution: "Synthetic detailed solution: use the office door.");
            var reference = answer.SearchSuggestionsHtml is { } html ? await services().Documents.CreateAsync(html, cancellationToken) : null;
            return answer with { Attribution = reference, SearchSuggestionsHtml = null };
        }
    }
}
