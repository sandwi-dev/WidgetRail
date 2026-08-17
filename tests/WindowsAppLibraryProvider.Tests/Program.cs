using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WindowsAppLibraryProvider;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Catalog is lazy cached and refreshable", LazyAndRefreshable),
    ("Duplicate registrations prefer current-user entries", DeduplicatesByInternalIdentity),
    ("Distinct applications with the same display name remain distinct", PreservesNameCollisions),
    ("AppsFolder merges deterministically without display-name deduplication", AppsFolderMergesDeterministically),
    ("AppsFolder identifiers remain trusted and opaque", AppsFolderPayloadIsOpaque),
    ("AppsFolder rejects malformed launch identifiers", AppsFolderRejectsMalformedAumids),
    ("Steam manifests merge as games without exposing launcher identifiers", SteamCatalogIsOpaque),
    ("Steam launch exactly revalidates its manifest", SteamLaunchRevalidatesManifest),
    ("Steam manifest source reads registered libraries safely", SteamSourceReadsLibraries),
    ("Steam artwork is lazy bounded and opaque",
        SteamArtworkScenarios.LocalArtworkIsLazyBoundedAndOpaque),
    ("Modern Steam icons use exact deterministic provider-native precedence",
        SteamArtworkScenarios.ModernProviderIconsAreDeterministic),
    ("Modern Steam icon discovery is bounded nonrecursive and reparse safe",
        SteamArtworkScenarios.ModernDiscoveryRejectsUnsafeShapes),
    ("Modern Steam icon replacement rotates exact locator generations",
        SteamArtworkScenarios.ModernReplacementRotatesGeneration),
    ("Steam artwork replacement rotates and stale demand fails closed",
        SteamArtworkScenarios.ReplacementRotatesAndStaleDemandFailsClosed),
    ("Steam artwork replacement retains an unchanged neighbor",
        SteamArtworkScenarios.ReplacementRetainsUnaffectedNeighbor),
    ("Steam artwork files stay pinned through bounded decode",
        SteamArtworkScenarios.ReplacementDuringDecodeIsRejected),
    ("Steam artwork failures fall back and its cache stays bounded",
        SteamArtworkScenarios.FailuresFallBackAndCacheStaysBounded),
    ("Steam artwork work does not serialize catalog traffic and drains",
        SteamArtworkScenarios.BlockedArtworkDoesNotBlockCatalogAndDrains),
    ("Cooperative Steam artwork drains before source disposal",
        SteamArtworkScenarios.CooperativeArtworkDrainsBeforeSourceDisposal),
    ("Cancellation-ignoring Steam artwork makes terminal cleanup fail safely",
        SteamArtworkScenarios.CancellationIgnoringArtworkCannotRaceSourceDisposal),
    ("Steam artwork locator generations stay bounded through catalog churn",
        SteamArtworkScenarios.LocatorCatalogChurnStaysBounded),
    ("Canceled staged Steam catalogs leave current locators active",
        SteamArtworkScenarios.CanceledStagedCatalogLeavesCurrentLocatorsActive),
    ("Losing staged Steam catalogs cannot replace latest locators",
        SteamArtworkScenarios.LosingStagedCatalogCannotReplaceLatestLocators),
    ("Steam launch URI accepts only a numeric application ID", SteamLaunchIsConstrained),
    ("Names and catalog size are bounded and sanitized", SanitizesAndBounds),
    ("Opaque IDs are stable only while the registration remains current", OpaqueIdLifecycle),
    ("Public payload contains no trusted launch descriptors", PayloadIsOpaque),
    ("Trusted broker projection separates launch and stable provider identities", BrokerProjectionSeparatesIdentities),
    ("Application icons are rasterized on demand and cached", IconsAreOnDemandAndCached),
    ("AppsFolder icons are on demand and degrade safely", AppsFolderIconsAreOnDemand),
    ("Artwork demand revalidates the exact current registration", ArtworkDemandRevalidatesRegistration),
    ("Missing application icons degrade to an empty optional result", MissingIconFallsBack),
    ("Application icon PNG encoder is bounded and structurally valid", IconPngIsBounded),
    ("Launch revalidates the exact current shortcut before invoking Shell", LaunchRevalidatesExactShortcut),
    ("Launch adapters report only evidence they can prove", LaunchEvidenceIsAdapterBound),
    ("Packaged launch exactly revalidates AUMID before activation", PackagedLaunchRevalidatesExactAumid),
    ("Shell launch settings contain no arguments elevation or window delegation", ShellLaunchIsConstrained),
    ("Shell failures expose only sanitized broker errors", ShellFailureIsSanitized),
    ("Only current opaque IDs resolve inside the trusted provider", ResolvesOnlyCurrentIds),
    ("Cancellation reaches the source without publishing partial state", CancellationIsAtomic),
    ("AppsFolder cancellation cannot publish a partial merged catalog", AppsFolderCancellationIsAtomic),
    ("AppsFolder collection failures preserve last-good packaged state", AppsFolderFailurePreservesLastGood),
    ("Steam collection failures preserve last-good game state", SteamFailurePreservesLastGood),
    ("Windows package registration admits only documented game evidence",
        WindowsPackageGameScenarios.AdmitsOnlyExactGameEvidence),
    ("Windows package games deduplicate AppsFolder and preserve variants",
        WindowsPackageGameScenarios.DeduplicatesAppsFolderAndPreservesVariants),
    ("Windows package add remove update and failure are generation exact",
        WindowsPackageGameScenarios.CatalogChurnRetainsOnlyDisplayOnFailure),
    ("Windows package launch revalidates exact current registration",
        WindowsPackageGameScenarios.LaunchRevalidatesCurrentRegistration),
    ("Windows package enumeration is bounded and cancelable",
        WindowsPackageGameScenarios.EnumerationIsBoundedAndCancelable),
    ("Epic discovery is explicitly disabled without manifest reads",
        EpicInstalledGameScenarios.DisabledIsExplicitAndDoesNotRead),
    ("Epic manifests remain opaque bounded and constrained",
        EpicInstalledGameScenarios.ValidManifestsAreOpaqueBoundedAndLaunchable),
    ("Epic corrupt duplicate unsafe and partial records fail closed",
        EpicInstalledGameScenarios.CorruptDuplicateUnsafeAndPartialFailClosed),
    ("Epic generations and launch authority are exact",
        EpicInstalledGameScenarios.GenerationAndExactLaunchAreCurrent),
    ("GOG disabled empty and cancellation states are explicit",
        GogInstalledGameScenarios.DisabledEmptyAndCancellationAreExplicit),
    ("GOG installed registrations are opaque distinct and non-launchable",
        GogInstalledGameScenarios.ValidRegistrationsAreOpaqueDistinctAndNonLaunchable),
    ("GOG malformed duplicate and mixed registrations fail closed",
        GogInstalledGameScenarios.MalformedDuplicateAndMixedRegistrationsFailClosed),
    ("GOG refreshed evidence remains non-authorizing",
        GogInstalledGameScenarios.RefreshAndLaunchAreDenied),
    ("Installed sources share one normalized exact-authority contract",
        GameLibrarySourceScenarios.NormalizedAdaptersOwnExactAuthority),
    ("Source failure preserves another source and its own last-good records",
        GameLibrarySourceScenarios.FailureIsolationPreservesLastGood),
    ("Late source generations cannot replace a newer snapshot",
        GameLibrarySourceScenarios.LateGenerationCannotReplaceCurrent),
    ("Source disposal cancels and drains admitted work",
        GameLibrarySourceScenarios.DisposeCancelsAndDrains),
    ("Running apps map exactly to current normalized registrations",
        RunningAppScenarios.MapsOnlyExactCurrentRegistrations),
    ("Running app observation collapses windows and stays bounded",
        RunningAppScenarios.CollapsesDuplicatesAndBoundsResults),
    ("Running window visits and process inspection stop at the native bound",
        RunningAppScenarios.NativeWindowVisitsAreBoundedBeforeEligibility),
    ("Running app observation cancels and drains before provider disposal",
        RunningAppScenarios.CancellationIgnoringObservationCannotPublish),
    ("Cooperative running observation drains before source disposal",
        RunningAppScenarios.CooperativeObservationDrainsBeforeSourceDisposal),
    ("Composite shutdown disposes provider-owned normalized sources",
        ProviderLifetimeScenarios.CompositeDisposesOwnedSources),
    ("Concurrent provider disposal cleans every source exactly once",
        ProviderLifetimeScenarios.ConcurrentDisposalCleansEverySource),
    ("Provider disposal cancels admitted source work before cleanup",
        ProviderLifetimeScenarios.ActiveWorkCancelsBeforeTerminalCleanup),
    ("Cancellation-ignoring source completion cannot publish after disposal",
        ProviderLifetimeScenarios.CancellationIgnoringCompletionCannotPublish),
    ("Uncooperative source work reaches the bounded terminal outcome",
        ProviderLifetimeScenarios.UncooperativeWorkTimesOutSafely),
    ("Cursor queries traverse ten thousand records with bounded pages",
        AppLibraryCursorScenarios.TraversesTenThousandWithoutFullPages),
    ("Cursor queries reject tamper wrong query direction and stale refresh",
        AppLibraryCursorScenarios.CursorsRejectTamperQueryAndRefreshChurn),
    ("Cursor queries bind normalized search source identity and closed sort",
        AppLibraryCursorScenarios.QueryCriteriaAreRevisionBound),
    ("Controlled adapters preserve bounded Running and Ended evidence",
        AppLibraryCursorScenarios.ControlledLaunchEvidenceIsPreserved),
    ("Shell sources execute on the bounded STA lane", SourcesUseStaLane),
    ("Real AppsFolder scan is read-only bounded and sanitized", NativeAppsFolderSmoke),
    ("Real Start Menu scan is read-only bounded and sanitized", NativeReadOnlySmoke),
    ("Shell STA watchdog permanently poisons a stalled lane", ShellStaWatchdogPoisons),
};

var failures = 0;
foreach (var (name, run) in tests)
{
    try
    {
        await run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception}");
    }
}
if (failures != 0) Environment.Exit(1);
Console.WriteLine($"WindowsAppLibraryProvider.Tests passed ({tests.Length} tests)");

static async Task<IReadOnlyList<AppLibraryBackendItemSummary>> QueryFirst(
    IAppLibraryPlatformBrokerBackend backend,
    bool refresh = false) =>
    (await backend.QueryAppLibraryAsync(
        new AppLibraryBackendCursorRequest(
            new AppLibraryBackendQuery(), null, null, 64, refresh),
        CancellationToken.None)).Items;

static async Task LazyAndRefreshable()
{
    var source = new FakeSource(Reg("one", "One", StartMenuScope.CurrentUser, @"C:\Menu\One.lnk"));
    var provider = new WindowsAppLibraryProvider(source);
    Assert.Equal(0, source.Calls);
    Assert.Equal(1, (await provider.GetAppsAsync()).Count);
    Assert.Equal(1, source.Calls);
    Assert.Equal(1, (await provider.GetAppsAsync()).Count);
    Assert.Equal(1, source.Calls);
    source.Items = [Reg("two", "Two", StartMenuScope.CurrentUser, @"C:\Menu\Two.lnk")];
    var refreshed = await provider.RefreshAsync();
    Assert.Equal("Two", refreshed.Single().DisplayName);
    Assert.Equal(2, source.Calls);
}

static async Task DeduplicatesByInternalIdentity()
{
    var source = new FakeSource(
        Reg("same", "Common Name", StartMenuScope.AllUsers, @"C:\Common\Same.lnk"),
        Reg("same", "My Name", StartMenuScope.CurrentUser, @"C:\User\Same.lnk"));
    var launcher = new FakeShellLauncher();
    var provider = new WindowsAppLibraryProvider(source, launcher);
    var item = (await provider.GetAppsAsync()).Single();
    Assert.Equal("My Name", item.DisplayName);
    await provider.LaunchAppLibraryItemAsync(item.AppId, CancellationToken.None);
    Assert.Equal(@"C:\User\Same.lnk", launcher.Paths.Single());
}

static async Task LaunchEvidenceIsAdapterBound()
{
    var source = new FakeSource(
        Reg("evidence", "Evidence", StartMenuScope.CurrentUser,
            @"C:\Menu\Evidence.lnk"));
    var launcher = new FakeShellLauncher();
    await using var provider = new WindowsAppLibraryProvider(source, launcher);
    var app = (await provider.GetAppsAsync()).Single();

    var result = await provider.LaunchAppLibraryItemObservedAsync(
        app.AppId, CancellationToken.None);

    Assert.Equal(AppLibraryLaunchObservationState.LauncherStarted, result.State);
    Assert.True(!result.SupportsRunning);
    Assert.True(!result.SupportsEnded);
    Assert.Equal(@"C:\Menu\Evidence.lnk", launcher.Paths.Single());
}

static async Task PreservesNameCollisions()
{
    var provider = new WindowsAppLibraryProvider(new FakeSource(
        Reg("one", "Editor", StartMenuScope.CurrentUser, @"C:\One.lnk"),
        Reg("two", "Editor", StartMenuScope.CurrentUser, @"C:\Two.lnk")));
    var items = await provider.GetAppsAsync();
    Assert.Equal(2, items.Count);
    Assert.Equal(2, items.Select(item => item.AppId).Distinct(StringComparer.Ordinal).Count());
}

static async Task AppsFolderMergesDeterministically()
{
    var start = new FakeSource(
        Reg("shortcut-one", "Same Name", StartMenuScope.CurrentUser, @"C:\One.lnk"));
    var apps = new FakeAppsFolderSource(
        AppReg("Contoso.One_abcd!App", "Same Name"),
        AppReg("Contoso.Two_abcd!App", "Alpha"));
    var provider = CreateMerged(start, apps);
    var snapshot = await provider.GetAppsAsync();
    Assert.Equal(3, snapshot.Count);
    Assert.Equal(2, snapshot.Count(item => item.DisplayName == "Same Name"));
    Assert.Equal("Alpha", snapshot[0].DisplayName);
    Assert.True(snapshot.All(item => item.Kind == WindowsAppLibraryKind.Application));

    var many = Enumerable.Range(0, WindowsAppLibraryProvider.MaximumApps + 40)
        .Select(index => AppReg($"Contoso.App{index:D4}_abcd!App", $"App {index:D4}"))
        .ToArray();
    var bounded = await CreateMerged(new FakeSource(),
        new FakeAppsFolderSource(many)).GetAppsAsync();
    Assert.Equal(WindowsAppLibraryProvider.MaximumApps, bounded.Count);
    Assert.Equal(bounded.Count, bounded.Select(item => item.AppId)
        .Distinct(StringComparer.Ordinal).Count());
}

static async Task AppsFolderPayloadIsOpaque()
{
    const string aumid = "Private.Package_123!SecretApplication";
    var provider = CreateMerged(new FakeSource(),
        new FakeAppsFolderSource(AppReg(aumid, "Visible App")));
    var publicJson = JsonSerializer.Serialize(await provider.GetAppsAsync());
    Assert.False(publicJson.Contains(aumid, StringComparison.OrdinalIgnoreCase));
    Assert.False(publicJson.Contains("Private.Package", StringComparison.OrdinalIgnoreCase));
    Assert.True(publicJson.Contains("Visible App", StringComparison.Ordinal));

    IAppLibraryPlatformBrokerBackend backend = provider;
    var brokerJson = JsonSerializer.Serialize(
        await QueryFirst(backend));
    Assert.False(brokerJson.Contains(aumid, StringComparison.OrdinalIgnoreCase));
    Assert.False(brokerJson.Contains("SecretApplication", StringComparison.OrdinalIgnoreCase));
}

static async Task AppsFolderRejectsMalformedAumids()
{
    var valid = AppReg("Contoso.Valid_abcd!App", "Valid");
    var generated = AppReg(
        "Microsoft.AutoGenerated.{04770C2D-34E6-BE94-EFAD-FA817237D6E9}",
        "Generated");
    var malformed = new[]
    {
        " leading",
        "trailing ",
        "contains space",
        "shell:AppsFolder",
        @"folder\\item",
        "folder/item",
        "quoted\"item",
        "unicode-\u00e9",
        "fullwidth-\uff21",
        "control\0item",
        new string('a', 130),
    };
    var registrations = malformed.Select((aumid, index) =>
            new AppsFolderRegistration(
                $"malformed-{index}", $"Malformed {index}", aumid, $"key-{index}"))
        .Prepend(generated)
        .Prepend(valid)
        .ToArray();

    var snapshot = await CreateMerged(new FakeSource(),
        new FakeAppsFolderSource(registrations)).GetAppsAsync();
    Assert.Equal(2, snapshot.Count);
    Assert.True(snapshot.Any(item => item.DisplayName == "Valid"));
    Assert.True(snapshot.Any(item => item.DisplayName == "Generated"));
    Assert.True(malformed.All(aumid =>
        WindowsAppsFolderApplicationSource.NormalizeAumid(aumid) is null));
}

static async Task SteamCatalogIsOpaque()
{
    var manifest = Path.Combine(Path.GetTempPath(), "appmanifest_440.acf");
    var steam = new FakeSteamSource(
        new SteamRegistration("steam-440", "Team Fortress 2", "440", manifest, "acf-one"));
    var provider = CreateMerged(new FakeSource(), new FakeAppsFolderSource(), steam: steam);

    var item = (await provider.GetAppsAsync()).Single();
    Assert.Equal("Team Fortress 2", item.DisplayName);
    Assert.Equal(WindowsAppLibraryKind.Game, item.Kind);
    Assert.False(item.AppId.Contains("440", StringComparison.Ordinal));

    var broker = ((IAppLibraryPlatformBrokerBackend)provider);
    var projected = (await QueryFirst(broker)).Single();
    var json = JsonSerializer.Serialize(projected);
    Assert.False(json.Contains("440", StringComparison.Ordinal));
    Assert.False(json.Contains("appmanifest", StringComparison.OrdinalIgnoreCase));
    Assert.Equal(AppLibraryKind.Game, projected.Kind);
    Assert.True(projected.ArtworkRevision is { Length: 64 });
    Assert.False(projected.ArtworkRevision.Contains("acf-one", StringComparison.Ordinal));

    steam.Items = [steam.Items.Single() with { RevalidationKey = "acf-two" }];
    var refreshed = (await QueryFirst(
        (IAppLibraryPlatformBrokerBackend)provider, refresh: true)).Single();
    Assert.Equal(projected.ProviderAppId, refreshed.ProviderAppId);
    Assert.Equal(projected.StableProviderIdentity, refreshed.StableProviderIdentity);
    Assert.True(projected.ArtworkRevision != refreshed.ArtworkRevision);
}

static async Task SteamLaunchRevalidatesManifest()
{
    var manifest = Path.Combine(Path.GetTempPath(), "appmanifest_570.acf");
    var registration = new SteamRegistration(
        "steam-570", "Dota 2", "570", manifest, "acf-current");
    var steam = new FakeSteamSource(registration);
    var launcher = new FakeSteamLauncher();
    var provider = CreateMerged(
        new FakeSource(), new FakeAppsFolderSource(), steam: steam, steamLauncher: launcher);
    var appId = (await provider.GetAppsAsync()).Single().AppId;

    await provider.LaunchAppLibraryItemAsync(appId, CancellationToken.None);
    Assert.Equal("570", launcher.AppIds.Single());
    Assert.Equal(1, steam.ExactReadCalls);

    steam.Items = [registration with { RevalidationKey = "acf-changed" }];
    var exception = await Assert.ThrowsAsync<BrokerException>(() =>
        provider.LaunchAppLibraryItemAsync(appId, CancellationToken.None));
    Assert.Equal("app_not_found", exception.Code);
    Assert.Equal(1, launcher.AppIds.Count);
}

static async Task SteamSourceReadsLibraries()
{
    if (!OperatingSystem.IsWindows()) return;
    var root = Path.Combine(Path.GetTempPath(), "gba-steam-source-" + Guid.NewGuid().ToString("N"));
    var extra = Path.Combine(root, "extra-library");
    try
    {
        Directory.CreateDirectory(Path.Combine(root, "steamapps"));
        Directory.CreateDirectory(Path.Combine(extra, "steamapps"));
        File.WriteAllText(Path.Combine(root, "steamapps", "libraryfolders.vdf"),
            $"\"libraryfolders\" {{ \"1\" {{ \"path\" \"{extra.Replace("\\", "\\\\")}\" }} }}");
        File.WriteAllText(Path.Combine(root, "steamapps", "appmanifest_730.acf"),
            "\"AppState\" { \"appid\" \"730\" \"name\" \"Counter-Strike 2\" }");
        var extraManifest = Path.Combine(extra, "steamapps", "appmanifest_1172470.acf");
        File.WriteAllText(extraManifest,
            "\"AppState\" { \"appid\" \"1172470\" \"name\" \"Apex Legends\" }");
        File.WriteAllText(Path.Combine(extra, "steamapps", "appmanifest_99.acf"),
            "\"AppState\" { \"appid\" \"100\" \"name\" \"Mismatch\" }");

        var source = new WindowsSteamApplicationSource([root]);
        var items = source.Enumerate(CancellationToken.None);
        Assert.Equal(2, items.Count);
        Assert.True(items.Any(item => item.SteamAppId == "730"));
        var apex = items.Single(item => item.SteamAppId == "1172470");
        Assert.Equal("Apex Legends", apex.DisplayName);
        Assert.Equal(apex, source.ReadExact("1172470", extraManifest, CancellationToken.None));

        File.AppendAllText(extraManifest, "\n\"StateFlags\" \"4\"");
        Assert.False(string.Equals(apex.RevalidationKey,
            source.ReadExact("1172470", extraManifest, CancellationToken.None)?.RevalidationKey,
            StringComparison.Ordinal));
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static Task SteamLaunchIsConstrained()
{
    var start = WindowsSteamLauncher.CreateStartInfo("1172470");
    Assert.Equal("steam://rungameid/1172470", start.FileName);
    Assert.Equal("open", start.Verb);
    Assert.True(start.UseShellExecute);
    Assert.Equal(0, start.ArgumentList.Count);
    Assert.Throws<ArgumentException>(() => WindowsSteamLauncher.CreateStartInfo("1 --unsafe"));
    Assert.Throws<ArgumentException>(() => WindowsSteamLauncher.CreateStartInfo("0"));
    return Task.CompletedTask;
}

static async Task SanitizesAndBounds()
{
    var items = Enumerable.Range(0, WindowsAppLibraryProvider.MaximumApps + 20)
        .Select(index => Reg(
            "id-" + index,
            index == 0 ? "  A\t\r\nName\0\u202e  " + new string('x', 200) : $"App {index:0000}",
            StartMenuScope.CurrentUser,
            $@"C:\Menu\App{index}.lnk"))
        .Append(Reg("empty", "\0\r\n", StartMenuScope.CurrentUser, @"C:\Empty.lnk"))
        .Append(Reg("bad-scope", "Bad scope", (StartMenuScope)99, @"C:\Bad.lnk"))
        .ToArray();
    var snapshot = await new WindowsAppLibraryProvider(new FakeSource(items)).GetAppsAsync();
    Assert.Equal(WindowsAppLibraryProvider.MaximumApps, snapshot.Count);
    Assert.True(snapshot.All(item => item.DisplayName.Length is > 0 and <=
        WindowsAppLibraryProvider.MaximumDisplayNameLength));
    Assert.True(snapshot.All(item => !item.DisplayName.Any(char.IsControl)));
    Assert.True(snapshot.Any(item => item.DisplayName.StartsWith("A Name", StringComparison.Ordinal)));
    Assert.False(snapshot.Any(item => item.DisplayName == "Bad scope"));
}

static async Task OpaqueIdLifecycle()
{
    var source = new FakeSource(Reg("one", "One", StartMenuScope.CurrentUser, @"C:\One.lnk"));
    var provider = new WindowsAppLibraryProvider(source);
    var original = (await provider.GetAppsAsync()).Single().AppId;
    Assert.Equal(original, (await provider.RefreshAsync()).Single().AppId);

    source.Items = [];
    Assert.Equal(0, (await provider.RefreshAsync()).Count);
    source.Items = [Reg("one", "One", StartMenuScope.CurrentUser, @"C:\One.lnk")];
    var replacement = (await provider.RefreshAsync()).Single().AppId;
    Assert.False(original == replacement);
    Assert.True(replacement.StartsWith("app-", StringComparison.Ordinal));
    Assert.Equal(36, replacement.Length);
}

static async Task PayloadIsOpaque()
{
    const string targetPath = @"C:\Users\private\Secret Game.lnk";
    const string identity = "sensitive-target-and---launch-arguments";
    var provider = new WindowsAppLibraryProvider(new FakeSource(
        Reg(identity, "Safe Game", StartMenuScope.CurrentUser, targetPath)));
    var json = JsonSerializer.Serialize(await provider.GetAppsAsync());
    Assert.False(json.Contains("private", StringComparison.OrdinalIgnoreCase));
    Assert.False(json.Contains("sensitive-target", StringComparison.Ordinal));
    Assert.False(json.Contains("launch-arguments", StringComparison.Ordinal));
    Assert.True(json.Contains("Safe Game", StringComparison.Ordinal));
}

static async Task BrokerProjectionSeparatesIdentities()
{
    const string privatePath = @"C:\Users\private\Hidden Game.lnk";
    var provider = new WindowsAppLibraryProvider(new FakeSource(
        Reg("trusted-private-identity", "Visible Game", StartMenuScope.CurrentUser, privatePath)));
    IAppLibraryPlatformBrokerBackend backend = provider;

    var projected = await QueryFirst(backend);
    var item = projected.Single();
    Assert.Equal("Visible Game", item.DisplayName);
    Assert.Equal(AppLibraryKind.Application, item.Kind);
    Assert.True(item.ProviderAppId.StartsWith("app-", StringComparison.Ordinal));
    Assert.Equal("trusted-private-identity", item.StableProviderIdentity);
    Assert.False(item.ProviderAppId.Contains("trusted-private-identity", StringComparison.Ordinal));
    Assert.False(item.ProviderAppId.Contains(".lnk", StringComparison.OrdinalIgnoreCase));
    var serialized = JsonSerializer.Serialize(projected);
    Assert.False(serialized.Contains("trusted-private-identity", StringComparison.Ordinal));
    Assert.False(serialized.Contains(item.ProviderAppId, StringComparison.Ordinal));

    var refreshed = await QueryFirst(backend, refresh: true);
    Assert.Equal(item.ProviderAppId, refreshed.Single().ProviderAppId);
    Assert.Equal(item.StableProviderIdentity, refreshed.Single().StableProviderIdentity);
}

static async Task IconsAreOnDemandAndCached()
{
    const string privatePath = @"C:\Users\private\Icon Source.lnk";
    var source = new FakeSource(
        Reg("icon-app", "Icon App", StartMenuScope.CurrentUser, privatePath));
    var iconSource = new FakeIconSource(CreateTestIcon());
    var provider = new WindowsAppLibraryProvider(source, new FakeShellLauncher(), iconSource);
    var appId = (await provider.GetAppsAsync()).Single().AppId;
    Assert.Equal(0, iconSource.Calls);

    var first = await provider.GetAppLibraryIconAsync(appId, CancellationToken.None);
    var second = await provider.GetAppLibraryIconAsync(appId, CancellationToken.None);
    Assert.True(!string.IsNullOrEmpty(first.PngBase64));
    Assert.Equal(first.PngBase64, second.PngBase64);
    Assert.Equal(1, iconSource.Calls);
    Assert.False(JsonSerializer.Serialize(first).Contains("private", StringComparison.OrdinalIgnoreCase));
    Assert.False(JsonSerializer.Serialize(first).Contains(".lnk", StringComparison.OrdinalIgnoreCase));
}

static async Task MissingIconFallsBack()
{
    var source = new FakeSource(
        Reg("missing-icon", "No Icon", StartMenuScope.CurrentUser, @"C:\NoIcon.lnk"));
    var iconSource = new FakeIconSource(null);
    var provider = new WindowsAppLibraryProvider(source, new FakeShellLauncher(), iconSource);
    var appId = (await provider.GetAppsAsync()).Single().AppId;
    Assert.Equal(null, (await provider.GetAppLibraryIconAsync(
        appId, CancellationToken.None)).PngBase64);
    Assert.Equal(null, (await provider.GetAppLibraryIconAsync(
        "app-00000000000000000000000000000000", CancellationToken.None)).PngBase64);
    Assert.Equal(1, iconSource.Calls);
}

static async Task AppsFolderIconsAreOnDemand()
{
    const string aumid = "Contoso.Icon_abcd!App";
    var iconSource = new FakeIconSource(CreateTestIcon());
    var provider = CreateMerged(new FakeSource(),
        new FakeAppsFolderSource(AppReg(aumid, "Packaged Icon")), iconSource: iconSource);
    var appId = (await provider.GetAppsAsync()).Single().AppId;
    Assert.Equal(0, iconSource.Calls);
    var first = await provider.GetAppLibraryIconAsync(appId, CancellationToken.None);
    var second = await provider.GetAppLibraryIconAsync(appId, CancellationToken.None);
    Assert.True(!string.IsNullOrWhiteSpace(first.PngBase64));
    Assert.Equal(first.PngBase64, second.PngBase64);
    Assert.Equal(1, iconSource.Calls);
    Assert.Equal(aumid, iconSource.AppsFolderAumids.Single());

    await provider.RefreshAsync();
    var refreshed = await provider.GetAppLibraryIconAsync(appId, CancellationToken.None);
    Assert.Equal(first.PngBase64, refreshed.PngBase64);
    Assert.Equal(2, iconSource.Calls);

    var fallbackIconSource = new FakeIconSource(null);
    var fallback = CreateMerged(new FakeSource(),
        new FakeAppsFolderSource(AppReg("Contoso.NoIcon_abcd!App", "No Icon")),
        iconSource: fallbackIconSource);
    var fallbackId = (await fallback.GetAppsAsync()).Single().AppId;
    Assert.Equal(null, (await fallback.GetAppLibraryIconAsync(
        fallbackId, CancellationToken.None)).PngBase64);
    Assert.Equal(null, (await fallback.GetAppLibraryIconAsync(
        fallbackId, CancellationToken.None)).PngBase64);
    Assert.Equal(2, fallbackIconSource.Calls);
}

static async Task ArtworkDemandRevalidatesRegistration()
{
    const string shortcut = @"C:\Menu\Artwork.lnk";
    var start = new FakeSource(
        Reg("original", "Original", StartMenuScope.CurrentUser, shortcut));
    var apps = new FakeAppsFolderSource(
        AppReg("Contoso.Original_abcd!App", "Original packaged app"));
    var iconSource = new FakeIconSource(CreateTestIcon());
    var provider = CreateMerged(start, apps, iconSource: iconSource);
    var initial = await provider.GetAppsAsync();
    var shortcutId = initial.Single(item => item.DisplayName == "Original").AppId;
    var packagedId = initial.Single(
        item => item.DisplayName == "Original packaged app").AppId;

    start.Items = [Reg("replacement", "Replacement", StartMenuScope.CurrentUser, shortcut)];
    apps.Items = [AppReg("Contoso.Original_abcd!App", "Changed packaged app") with
    {
        RevalidationKey = "replacement-registration",
    }];

    Assert.Equal(null, (await provider.GetAppLibraryIconAsync(
        shortcutId, CancellationToken.None)).PngBase64);
    Assert.Equal(null, (await provider.GetAppLibraryIconAsync(
        packagedId, CancellationToken.None)).PngBase64);
    Assert.Equal(0, iconSource.Calls);
}

static Task IconPngIsBounded()
{
    var png = CreateTestIcon();
    Assert.True(png.Length <= AppLibraryImageLimits.MaximumPngBytes);
    Assert.True(png.AsSpan(0, 8).SequenceEqual(
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
    return Task.CompletedTask;
}

static byte[] CreateTestIcon()
{
    var bgra = new byte[WindowsAppIconSource.IconPixels *
        WindowsAppIconSource.IconPixels * 4];
    for (var index = 0; index < bgra.Length; index += 4)
    {
        bgra[index] = (byte)(index % 251);
        bgra[index + 1] = (byte)((index / 3) % 251);
        bgra[index + 2] = (byte)((index / 7) % 251);
        bgra[index + 3] = 255;
    }
    return WindowsAppIconSource.EncodePng(
        bgra, WindowsAppIconSource.IconPixels, WindowsAppIconSource.IconPixels);
}

static async Task LaunchRevalidatesExactShortcut()
{
    const string shortcut = @"C:\Menu\Game.lnk";
    var source = new FakeSource(
        Reg("target-one", "Game", StartMenuScope.CurrentUser, shortcut));
    var launcher = new FakeShellLauncher();
    var provider = new WindowsAppLibraryProvider(source, launcher);
    var appId = (await provider.GetAppsAsync()).Single().AppId;

    await provider.LaunchAppLibraryItemAsync(appId, CancellationToken.None);
    Assert.Equal(2, source.Calls);
    Assert.Equal(1, source.EnumerateCalls);
    Assert.Equal(1, source.ExactReadCalls);
    Assert.Equal(shortcut, launcher.Paths.Single());

    source.Items = [Reg("replacement-target", "Game", StartMenuScope.CurrentUser, shortcut)];
    var replaced = await Assert.ThrowsAsync<BrokerException>(() =>
        provider.LaunchAppLibraryItemAsync(appId, CancellationToken.None));
    Assert.Equal("app_not_found", replaced.Code);
    Assert.Equal(1, launcher.Paths.Count);

    source.Items = [Reg(
        "target-one", "Game", StartMenuScope.CurrentUser, shortcut,
        revalidationKey: "replacement-link-content")];
    var replacedLink = await Assert.ThrowsAsync<BrokerException>(() =>
        provider.LaunchAppLibraryItemAsync(appId, CancellationToken.None));
    Assert.Equal("app_not_found", replacedLink.Code);
    Assert.Equal(1, launcher.Paths.Count);

    source.Items = [Reg("target-one", "Game", StartMenuScope.CurrentUser, @"C:\Moved\Game.lnk")];
    var moved = await Assert.ThrowsAsync<BrokerException>(() =>
        provider.LaunchAppLibraryItemAsync(appId, CancellationToken.None));
    Assert.Equal("app_not_found", moved.Code);
    Assert.Equal(1, launcher.Paths.Count);

    var unknown = await Assert.ThrowsAsync<BrokerException>(() =>
        provider.LaunchAppLibraryItemAsync(
            "app-00000000000000000000000000000000", CancellationToken.None));
    Assert.Equal("app_not_found", unknown.Code);
    Assert.Equal(5, source.Calls);
}

static Task ShellLaunchIsConstrained()
{
    var startInfo = WindowsShellLauncher.CreateStartInfo(@"C:\Menu\Exact Game.lnk");
    Assert.Equal(@"C:\Menu\Exact Game.lnk", startInfo.FileName);
    Assert.Equal("open", startInfo.Verb);
    Assert.True(startInfo.UseShellExecute);
    Assert.Equal(string.Empty, startInfo.Arguments);
    Assert.Equal(0, startInfo.ArgumentList.Count);
    Assert.Equal(string.Empty, startInfo.WorkingDirectory);
    Assert.False(startInfo.RedirectStandardInput);
    Assert.False(startInfo.RedirectStandardOutput);
    Assert.False(startInfo.RedirectStandardError);
    return Task.CompletedTask;
}

static async Task PackagedLaunchRevalidatesExactAumid()
{
    const string aumid = "Contoso.Game_abcd!Main";
    var apps = new FakeAppsFolderSource(AppReg(aumid, "Packaged Game"));
    var launcher = new FakePackagedAppLauncher();
    var provider = CreateMerged(new FakeSource(), apps, packagedLauncher: launcher);
    var appId = (await provider.GetAppsAsync()).Single().AppId;

    await provider.LaunchAppLibraryItemAsync(appId, CancellationToken.None);
    Assert.Equal(1, apps.ExactReadCalls);
    Assert.Equal(1, apps.EnumerateCalls);
    Assert.Equal(aumid, launcher.Aumids.Single());

    apps.Items = [AppReg("Contoso.Replacement_abcd!Main", "Packaged Game")];
    var missing = await Assert.ThrowsAsync<BrokerException>(() =>
        provider.LaunchAppLibraryItemAsync(appId, CancellationToken.None));
    Assert.Equal("app_not_found", missing.Code);
    Assert.Equal(1, launcher.Aumids.Count);

    apps.Items = [AppReg(aumid, "Packaged Game", "different-revalidation")];
    var changed = await Assert.ThrowsAsync<BrokerException>(() =>
        provider.LaunchAppLibraryItemAsync(appId, CancellationToken.None));
    Assert.Equal("app_not_found", changed.Code);
    Assert.Equal(1, launcher.Aumids.Count);
}

static async Task ShellFailureIsSanitized()
{
    const string privatePath = @"C:\Users\private\Failing Game.lnk";
    var provider = new WindowsAppLibraryProvider(
        new FakeSource(Reg("target", "Game", StartMenuScope.CurrentUser, privatePath)),
        new FakeShellLauncher(new System.ComponentModel.Win32Exception(5, privatePath)));
    var appId = (await provider.GetAppsAsync()).Single().AppId;

    var failure = await Assert.ThrowsAsync<BrokerException>(() =>
        provider.LaunchAppLibraryItemAsync(appId, CancellationToken.None));
    Assert.Equal("launch_failed", failure.Code);
    Assert.False(failure.Message.Contains("private", StringComparison.OrdinalIgnoreCase));
    Assert.False(failure.Message.Contains(".lnk", StringComparison.OrdinalIgnoreCase));
}

static async Task ResolvesOnlyCurrentIds()
{
    var source = new FakeSource(Reg("one", "One", StartMenuScope.CurrentUser, @"C:\One.lnk"));
    var launcher = new FakeShellLauncher();
    var provider = new WindowsAppLibraryProvider(source, launcher);
    var id = (await provider.GetAppsAsync()).Single().AppId;
    await provider.LaunchAppLibraryItemAsync(id, CancellationToken.None);
    Assert.Equal(@"C:\One.lnk", launcher.Paths.Single());
    var unknown = await Assert.ThrowsAsync<BrokerException>(() =>
        provider.LaunchAppLibraryItemAsync(
            "app-00000000000000000000000000000000", CancellationToken.None));
    Assert.Equal("app_not_found", unknown.Code);
    source.Items = [];
    await provider.RefreshAsync();
    var stale = await Assert.ThrowsAsync<BrokerException>(() =>
        provider.LaunchAppLibraryItemAsync(id, CancellationToken.None));
    Assert.Equal("app_not_found", stale.Code);
}

static async Task CancellationIsAtomic()
{
    var source = new BlockingSource();
    var provider = new WindowsAppLibraryProvider(source);
    using var cancellation = new CancellationTokenSource();
    var scan = provider.GetAppsAsync(cancellation.Token);
    await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    cancellation.Cancel();
    await Assert.ThrowsAsync<OperationCanceledException>(() => scan);
    var missing = await Assert.ThrowsAsync<BrokerException>(() =>
        provider.LaunchAppLibraryItemAsync("app-anything", CancellationToken.None));
    Assert.Equal("app_not_found", missing.Code);
}

static async Task AppsFolderCancellationIsAtomic()
{
    var start = new FakeSource(
        Reg("shortcut", "Shortcut", StartMenuScope.CurrentUser, @"C:\Menu\Shortcut.lnk"));
    var apps = new BlockingAppsFolderSource(
        AppReg("Contoso.Package_abcd!App", "Packaged"));
    var provider = CreateMerged(start, apps);
    using var cancellation = new CancellationTokenSource();
    var scan = provider.GetAppsAsync(cancellation.Token);
    await apps.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    cancellation.Cancel();
    await Assert.ThrowsAsync<OperationCanceledException>(() => scan);

    apps.Block = false;
    var complete = await provider.GetAppsAsync().WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Equal(2, complete.Count);
    Assert.True(complete.Any(item => item.DisplayName == "Shortcut"));
    Assert.True(complete.Any(item => item.DisplayName == "Packaged"));
    Assert.Equal(2, start.EnumerateCalls);
    Assert.Equal(2, apps.EnumerateCalls);
}

static async Task AppsFolderFailurePreservesLastGood()
{
    var start = new FakeSource(
        Reg("one", "One", StartMenuScope.CurrentUser, @"C:\Menu\One.lnk"));
    var apps = new FailingAppsFolderSource(
        AppReg("Contoso.Keep_abcd!App", "Keep Packaged"));
    var provider = CreateMerged(start, apps);
    Assert.Equal(2, (await provider.GetAppsAsync()).Count);

    start.Items = [Reg("two", "Two", StartMenuScope.CurrentUser, @"C:\Menu\Two.lnk")];
    apps.FailEnumeration = true;
    var preserved = await provider.RefreshAsync();
    Assert.Equal(2, preserved.Count);
    Assert.True(preserved.Any(item => item.DisplayName == "Two"));
    Assert.True(preserved.Any(item => item.DisplayName == "Keep Packaged"));

    var firstFailureApps = new FailingAppsFolderSource(
        AppReg("Contoso.Unavailable_abcd!App", "Unavailable"))
    {
        FailEnumeration = true,
    };
    var degraded = await CreateMerged(
        new FakeSource(Reg("start", "Start Only", StartMenuScope.CurrentUser,
            @"C:\Menu\Start.lnk")), firstFailureApps).GetAppsAsync();
    Assert.Equal(1, degraded.Count);
    Assert.Equal("Start Only", degraded.Single().DisplayName);
}

static async Task SteamFailurePreservesLastGood()
{
    var manifest = Path.Combine(Path.GetTempPath(), "appmanifest_620.acf");
    var steam = new FailingSteamSource(
        new SteamRegistration("steam-620", "Portal 2", "620", manifest, "acf-portal"));
    var provider = CreateMerged(new FakeSource(), new FakeAppsFolderSource(), steam: steam);

    var first = await provider.GetAppsAsync();
    Assert.Equal("Portal 2", first.Single().DisplayName);
    steam.FailEnumeration = true;
    var refreshed = await provider.RefreshAsync();
    Assert.Equal("Portal 2", refreshed.Single().DisplayName);
    Assert.Equal(WindowsAppLibraryKind.Game, refreshed.Single().Kind);
}

static async Task ShellStaWatchdogPoisons()
{
    var executor = new ShellStaExecutor(TimeSpan.FromMilliseconds(100));
    using var release = new ManualResetEventSlim();
    var started = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var stalled = executor.RunAsync(
        _ =>
        {
            started.TrySetResult();
            release.Wait();
            return true;
        }, CancellationToken.None);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => stalled.WaitAsync(TimeSpan.FromSeconds(2)));
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => executor.RunAsync(_ => true, CancellationToken.None));
    release.Set();
}

static async Task NativeReadOnlySmoke()
{
    if (!OperatingSystem.IsWindows()) return;
    var startMenuCount = (await ShellStaExecutor.Shared.RunAsync(
        token => new WindowsStartMenuApplicationSource().Enumerate(token),
        CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10))).Count;
    var appsFolderCount = (await ShellStaExecutor.Shared.RunAsync(
        token => new WindowsAppsFolderApplicationSource().Enumerate(token),
        CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10))).Count;
    var steamCount = (await ShellStaExecutor.Shared.RunAsync(
        token => new WindowsSteamApplicationSource().Enumerate(token),
        CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10))).Count;
    var provider = new WindowsAppLibraryProvider();
    var snapshot = await provider.GetAppsAsync().WaitAsync(TimeSpan.FromSeconds(10));
    Assert.True(snapshot.Count <= WindowsAppLibraryProvider.MaximumApps);
    Assert.Equal(snapshot.Count,
        snapshot.Select(item => item.AppId).Distinct(StringComparer.Ordinal).Count());
    Assert.True(snapshot.All(item =>
        item.AppId.StartsWith("app-", StringComparison.Ordinal) &&
        item.AppId.Length == 36 &&
        item.DisplayName.Length is > 0 and <= WindowsAppLibraryProvider.MaximumDisplayNameLength &&
        item.Kind is WindowsAppLibraryKind.Application or WindowsAppLibraryKind.Game));
    Console.WriteLine($"INFO Real merged app catalog contains {snapshot.Count} entries " +
        $"from {startMenuCount} Start Menu, {appsFolderCount} AppsFolder, and " +
        $"{steamCount} Steam candidates");
}

static async Task SourcesUseStaLane()
{
    if (!OperatingSystem.IsWindows()) return;
    var start = new ApartmentRecordingStartMenuSource();
    var apps = new ApartmentRecordingAppsFolderSource();
    var provider = CreateMerged(start, apps);
    await provider.GetAppsAsync();
    Assert.Equal(ApartmentState.STA, start.Apartment);
    Assert.Equal(ApartmentState.STA, apps.Apartment);
}

static async Task NativeAppsFolderSmoke()
{
    if (!OperatingSystem.IsWindows()) return;
    var items = await ShellStaExecutor.Shared.RunAsync(
        token => new WindowsAppsFolderApplicationSource().Enumerate(token),
        CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
    Assert.True(items.Count <= WindowsAppsFolderApplicationSource.MaximumCandidates);
    Assert.True(items.All(item =>
        item.DisplayName.Length <= 1024 &&
        WindowsAppsFolderApplicationSource.NormalizeAumid(item.Aumid) == item.Aumid &&
        item.IdentityKey == WindowsAppsFolderApplicationSource.IdentityFor(item.Aumid)));
    Assert.Equal(items.Count, items.Select(item => item.Aumid)
        .Distinct(StringComparer.OrdinalIgnoreCase).Count());
    var repeated = await ShellStaExecutor.Shared.RunAsync(
        token => new WindowsAppsFolderApplicationSource().Enumerate(token),
        CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
    Assert.Equal(items.Count, repeated.Count);
    if (items.Count != 0)
    {
        var exact = await ShellStaExecutor.Shared.RunAsync(
            token => new WindowsAppsFolderApplicationSource().ReadExact(
                items[0].Aumid, token),
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(exact is not null);
        Assert.Equal(items[0].Aumid, exact!.Aumid);
    }
    Console.WriteLine($"INFO Real AppsFolder contains {items.Count} bounded registrations");
}

static StartMenuRegistration Reg(
    string identity,
    string name,
    StartMenuScope scope,
    string path,
    string? revalidationKey = null) =>
    new(identity, name, scope, path, revalidationKey ?? $"link-{identity}-{path}");

static AppsFolderRegistration AppReg(
    string aumid,
    string name,
    string? revalidationKey = null)
{
    var normalized = WindowsAppsFolderApplicationSource.NormalizeAumid(aumid)!;
    var identity = WindowsAppsFolderApplicationSource.IdentityFor(normalized);
    return new AppsFolderRegistration(
        identity, name, normalized, revalidationKey ?? identity);
}

static WindowsAppLibraryProvider CreateMerged(
    IStartMenuApplicationSource startMenu,
    IAppsFolderApplicationSource appsFolder,
    IWindowsShellLauncher? shellLauncher = null,
    IWindowsPackagedAppLauncher? packagedLauncher = null,
    IWindowsAppIconSource? iconSource = null,
    ISteamApplicationSource? steam = null,
    IWindowsSteamLauncher? steamLauncher = null) =>
    new(startMenu, appsFolder, steam ?? new FakeSteamSource(),
        shellLauncher ?? new FakeShellLauncher(),
        packagedLauncher ?? new FakePackagedAppLauncher(),
        steamLauncher ?? new FakeSteamLauncher(),
        iconSource ?? new FakeIconSource(null),
        ShellStaExecutor.Shared);

file sealed class FakeSource(params StartMenuRegistration[] items) : IStartMenuApplicationSource
{
    public IReadOnlyList<StartMenuRegistration> Items { get; set; } = items;
    public int EnumerateCalls { get; private set; }
    public int ExactReadCalls { get; private set; }
    public int Calls => EnumerateCalls + ExactReadCalls;
    public IReadOnlyList<StartMenuRegistration> Enumerate(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnumerateCalls++;
        return Items.ToArray();
    }

    public StartMenuRegistration? ReadExact(
        string shortcutPath,
        StartMenuScope scope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExactReadCalls++;
        var matches = Items.Where(item => item.Scope == scope &&
                string.Equals(item.ShortcutPath, shortcutPath,
                    StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}

file sealed class FakeAppsFolderSource(params AppsFolderRegistration[] items) :
    IAppsFolderApplicationSource
{
    public IReadOnlyList<AppsFolderRegistration> Items { get; set; } = items;
    public int EnumerateCalls { get; private set; }
    public int ExactReadCalls { get; private set; }

    public IReadOnlyList<AppsFolderRegistration> Enumerate(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnumerateCalls++;
        return Items.ToArray();
    }

    public AppsFolderRegistration? ReadExact(
        string aumid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExactReadCalls++;
        var matches = Items.Where(item => string.Equals(
                item.Aumid, aumid, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}

file sealed class FakeSteamSource(params SteamRegistration[] items) :
    ISteamApplicationSource
{
    public IReadOnlyList<SteamRegistration> Items { get; set; } = items;
    public int ExactReadCalls { get; private set; }

    public IReadOnlyList<SteamRegistration> Enumerate(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Items.ToArray();
    }

    public SteamRegistration? ReadExact(
        string steamAppId,
        string manifestPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExactReadCalls++;
        var matches = Items.Where(item =>
                item.SteamAppId == steamAppId &&
                string.Equals(item.ManifestPath, manifestPath, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}

file sealed class FailingAppsFolderSource(params AppsFolderRegistration[] items) :
    IAppsFolderApplicationSource
{
    public bool FailEnumeration { get; set; }

    public IReadOnlyList<AppsFolderRegistration> Enumerate(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailEnumeration)
            throw new AppsFolderEnumerationException("Simulated AppsFolder failure.");
        return items.ToArray();
    }

    public AppsFolderRegistration? ReadExact(
        string aumid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return items.SingleOrDefault(item => string.Equals(
            item.Aumid, aumid, StringComparison.OrdinalIgnoreCase));
    }
}

file sealed class FailingSteamSource(params SteamRegistration[] items) :
    ISteamApplicationSource
{
    public bool FailEnumeration { get; set; }

    public IReadOnlyList<SteamRegistration> Enumerate(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailEnumeration) throw new IOException("Simulated Steam library failure.");
        return items.ToArray();
    }

    public SteamRegistration? ReadExact(
        string steamAppId,
        string manifestPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return items.SingleOrDefault(item => item.SteamAppId == steamAppId &&
            string.Equals(item.ManifestPath, manifestPath, StringComparison.OrdinalIgnoreCase));
    }
}

file sealed class ApartmentRecordingStartMenuSource : IStartMenuApplicationSource
{
    public ApartmentState Apartment { get; private set; } = ApartmentState.Unknown;
    public IReadOnlyList<StartMenuRegistration> Enumerate(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Apartment = Thread.CurrentThread.GetApartmentState();
        return [];
    }

    public StartMenuRegistration? ReadExact(
        string shortcutPath,
        StartMenuScope scope,
        CancellationToken cancellationToken) => null;
}

file sealed class ApartmentRecordingAppsFolderSource : IAppsFolderApplicationSource
{
    public ApartmentState Apartment { get; private set; } = ApartmentState.Unknown;
    public IReadOnlyList<AppsFolderRegistration> Enumerate(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Apartment = Thread.CurrentThread.GetApartmentState();
        return [];
    }

    public AppsFolderRegistration? ReadExact(
        string aumid,
        CancellationToken cancellationToken) => null;
}

file sealed class BlockingSource : IStartMenuApplicationSource
{
    public TaskCompletionSource Started { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public IReadOnlyList<StartMenuRegistration> Enumerate(CancellationToken cancellationToken)
    {
        Started.TrySetResult();
        cancellationToken.WaitHandle.WaitOne();
        cancellationToken.ThrowIfCancellationRequested();
        return [];
    }


    public StartMenuRegistration? ReadExact(
        string shortcutPath,
        StartMenuScope scope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }
}

file sealed class BlockingAppsFolderSource(params AppsFolderRegistration[] items) :
    IAppsFolderApplicationSource
{
    public TaskCompletionSource Started { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Block { get; set; } = true;
    public int EnumerateCalls { get; private set; }

    public IReadOnlyList<AppsFolderRegistration> Enumerate(
        CancellationToken cancellationToken)
    {
        EnumerateCalls++;
        Started.TrySetResult();
        if (Block)
        {
            cancellationToken.WaitHandle.WaitOne();
            cancellationToken.ThrowIfCancellationRequested();
        }
        return items.ToArray();
    }

    public AppsFolderRegistration? ReadExact(
        string aumid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return items.SingleOrDefault(item => string.Equals(
            item.Aumid, aumid, StringComparison.OrdinalIgnoreCase));
    }
}

file sealed class FakeShellLauncher(Exception? failure = null) : IWindowsShellLauncher
{
    public List<string> Paths { get; } = [];

    public void Launch(string exactShortcutPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Paths.Add(exactShortcutPath);
        if (failure is not null) throw failure;
    }
}

file sealed class FakePackagedAppLauncher(Exception? failure = null) :
    IWindowsPackagedAppLauncher
{
    public List<string> Aumids { get; } = [];

    public void Launch(string exactAumid, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Aumids.Add(exactAumid);
        if (failure is not null) throw failure;
    }
}

file sealed class FakeSteamLauncher(Exception? failure = null) : IWindowsSteamLauncher
{
    public List<string> AppIds { get; } = [];

    public void Launch(string exactSteamAppId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppIds.Add(exactSteamAppId);
        if (failure is not null) throw failure;
    }
}

file sealed class FakeIconSource(byte[]? png) : IWindowsAppIconSource
{
    public int Calls { get; private set; }
    public List<string> AppsFolderAumids { get; } = [];

    public string? TryRasterizePngBase64(
        string shortcutPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        return png is null ? null : Convert.ToBase64String(png);
    }

    public string? TryRasterizeAppsFolderPngBase64(
        string aumid, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        AppsFolderAumids.Add(aumid);
        return png is null ? null : Convert.ToBase64String(png);
    }
}

internal static class Assert
{
    public static void True(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Expected true.");
    }

    public static void False(bool condition) => True(!condition);

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    public static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try
        {
            await action();
        }
        catch (T exception)
        {
            return exception;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    public static T Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            return exception;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
