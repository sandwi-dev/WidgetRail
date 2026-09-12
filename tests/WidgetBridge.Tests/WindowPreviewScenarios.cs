using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

internal static class WindowPreviewScenarios
{
    internal static Task ThemedRenderRoles()
    {
        var parsed = WrssParser.Parse("windowPreview { opacity: 0.75; } .wrail-window-preview { corner-radius: 8px; }", "preview.wrss");
        var compiled = WrssThemeCompiler.Compile([parsed.Document]);
        Check(compiled.IsValid, "Preview theme must compile.");
        var snapshot = new WidgetView(UI.WindowPreview("window-test", "preview", "Window preview"))
            .CreateSnapshot("preview.snapshot", 1);
        var styles = BridgeRenderStyleResolver.Resolve(snapshot, compiled.Theme);
        Check(styles["preview"].Base["opacity"].Number == 0.75, "Preview role selectors must resolve.");
        Check(styles["preview"].Base["corner-radius"].Number == 8, "Preview theme classes must resolve.");
        // Every protocol element must survive the themed publication path, even
        // when the active theme has no selector specifically targeting it.
        foreach (var kind in Enum.GetValues<ViewNodeKind>())
            BridgeRenderStyleResolver.Resolve(snapshot with { Root = snapshot.Root with { Kind = kind } }, compiled.Theme);
        return Task.CompletedTask;
    }

    internal static async Task Authority()
    {
        Check(!BridgeJson.FromElement<BridgeHello>(BridgeJson.ToElement(new { clientName = "legacy" })).WindowPreviews,
            "Existing clients must not receive native metadata without negotiating it.");
        Check(BridgeJson.FromElement<BridgeHello>(BridgeJson.ToElement(new { clientName = "native", windowPreviews = true })).WindowPreviews,
            "Native clients can negotiate window preview metadata.");
        Check(BridgeRequestClassifier.Classify(new BridgeEnvelope
        {
            Type = BridgeMessageTypes.WindowPreviewPermissions, RequestId = 1, Payload = BridgeJson.ToElement(new { widgetId = "preview" }),
        }).Kind == BridgeRequestKind.WindowPreviewPermissions, "Preview grants have a dedicated host control-plane request.");
        var directory = Path.Combine(Path.GetTempPath(), "WidgetRail.WindowPreview." + Guid.NewGuid().ToString("N"));
        var store = new ConsentStore(directory);
        var owner = new object();
        var identity = new BrokerWidgetIdentity("preview.widget", "preview.publisher", "preview.instance");
        var configured = new ConfiguredWidget
        {
            Id = "preview", PackageId = identity.PackageId, PublisherId = identity.PublisherId,
            InstanceId = identity.InstanceId, Name = "Preview", WorkerExecutable = "unused.exe",
            DeclaredCapabilities = [PlatformCapabilities.TaskWindowsPreviewV1],
            WorkerFingerprint = new string('a', 64), CatalogFingerprint = new string('b', 64),
        };
        var snapshot = new WidgetView(UI.WindowPreview("window-test", "preview", "Window preview"))
            .CreateSnapshot("preview.snapshot", 1);
        var target = new NativeWindowPreviewTarget("1234", 42, "12345678", "TestWindow");
        try
        {
            WindowPreviewRegistry.Replace(owner, identity, new Dictionary<string, NativeWindowPreviewTarget>
                { ["window-test"] = target, ["not-in-snapshot"] = target });
            async Task<int> Count(ConfiguredWidget widget, ViewSnapshot view) =>
                (await WindowPreviewResolver.ResolveAsync(store, widget, view, default)).Count;
            Check(await Count(configured, snapshot) == 0, "Missing consent must yield a fallback.");
            await store.SetDecisionAsync(identity, PlatformCapabilities.TaskWindowsPreviewV1, ConsentDecision.Grant);
            Check((await WindowPreviewResolver.AllowedWindowIdsAsync(store, configured, default))
                .Contains("window-test"), "Host permission polling reads current grants without a widget snapshot.");
            var resolved = await WindowPreviewResolver.ResolveAsync(store, configured, snapshot, default);
            Check(resolved.Count == 1 && resolved["window-test"] == target, "Resolve only current snapshot sources.");
            Check(await Count(configured with { DeclaredCapabilities = [] }, snapshot) == 0, "Manifest declaration is required.");
            Check(await Count(configured with { InstanceId = "different.instance" }, snapshot) == 0, "Another instance cannot borrow the source.");
            var empty = new WidgetView(UI.Text("No preview", "text")).CreateSnapshot("preview.snapshot", 2);
            Check(await Count(configured, empty) == 0, "Removed previews cannot retain authority.");
            await store.SetDecisionAsync(identity, PlatformCapabilities.TaskWindowsPreviewV1, ConsentDecision.Deny);
            Check(await Count(configured, snapshot) == 0, "Revocation must remove native targets.");
            Check((await WindowPreviewResolver.AllowedWindowIdsAsync(store, configured, default)).Count == 0,
                "An unchanged published snapshot cannot keep capture alive after revocation.");
            await store.SetDecisionAsync(identity, PlatformCapabilities.TaskWindowsPreviewV1, ConsentDecision.Grant);
            WindowPreviewRegistry.Replace(owner, identity, new Dictionary<string, NativeWindowPreviewTarget>
                { ["window-test"] = target with { ProcessId = 0 } });
            Check(WindowPreviewRegistry.Resolve(identity, "window-test") is null, "Malformed provider evidence is not published.");
            WindowPreviewRegistry.Replace(owner, identity, new Dictionary<string, NativeWindowPreviewTarget>
                { ["window-test"] = target });
            WindowPreviewRegistry.Retire(owner);
            Check(WindowPreviewRegistry.Resolve(identity, "window-test") is null, "Broker retirement releases targets.");
            Check((await WindowPreviewResolver.AllowedWindowIdsAsync(store, configured, default)).Count == 0, "Native polling also observes broker retirement.");
            WindowPreviewRegistry.Replace(owner, identity, new Dictionary<string, NativeWindowPreviewTarget>
                { ["window-test"] = target });
            Check(WindowPreviewRegistry.Resolve(identity, "window-test") is null, "Late list completion cannot resurrect a retired broker.");
        }
        finally
        {
            WindowPreviewRegistry.Retire(owner);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
