using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WindowsAppLibraryProvider;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

internal static class SteamArtworkScenarios
{
    internal static async Task LocalArtworkIsLazyBoundedAndOpaque()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var layout = SteamLayout.Create("730", ".png", CreatePng(12, 18, 7));
        var resolver = new WindowsSteamArtworkSource();
        await using var provider = CreateProvider(layout.Root, resolver);

        var page = await Query(provider);
        var item = page.Items.Single();
        Assert.Equal(0, resolver.CacheCount);
        Assert.Equal(0, resolver.FileProbeCalls);
        _ = await Query(provider, refresh: true);
        Assert.Equal(0, resolver.FileProbeCalls);
        Assert.True(item.ArtworkRevision.Length == 64);
        var serialized = System.Text.Json.JsonSerializer.Serialize(page);
        Assert.False(serialized.Contains("730", StringComparison.Ordinal));
        Assert.False(serialized.Contains("librarycache", StringComparison.OrdinalIgnoreCase));
        Assert.False(serialized.Contains("_icon", StringComparison.OrdinalIgnoreCase));

        var icon = await provider.GetAppLibraryIconAsync(
            item.ProviderAppId, CancellationToken.None);
        Assert.True(icon.PngBase64 is not null);
        var bytes = Convert.FromBase64String(icon.PngBase64!);
        Assert.True(bytes.Length <= AppLibraryImageLimits.MaximumPngBytes);
        Assert.True(bytes.AsSpan(0, 8).SequenceEqual(
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        Assert.Equal(1, resolver.CacheCount);
        Assert.True(resolver.FileProbeCalls > 0);

        using var manyLayout = SteamLayout.CreateEmpty();
        for (var index = 1; index <= 32; index++)
        {
            var appId = (30_000 + index).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            manyLayout.WriteManifest(appId, $"Steam Game {index}");
            _ = manyLayout.WriteArtwork(appId, ".png", CreatePng(2, 2, (byte)index));
        }
        var manyResolver = new WindowsSteamArtworkSource();
        await using (var manyProvider = CreateProvider(manyLayout.Root, manyResolver))
        {
            Assert.Equal(32, (await Query(manyProvider)).Items.Count);
            Assert.Equal(32, (await Query(manyProvider, refresh: true)).Items.Count);
            Assert.Equal(0, manyResolver.FileProbeCalls);
            var demanded = (await Query(manyProvider)).Items[17];
            Assert.True((await manyProvider.GetAppLibraryIconAsync(
                demanded.ProviderAppId, CancellationToken.None)).PngBase64 is not null);
            Assert.True(manyResolver.FileProbeCalls > 0);
        }

        using var unavailableLayout = SteamLayout.CreateEmpty();
        unavailableLayout.WriteManifest("39001", "Cache unavailable");
        Directory.Delete(Path.Combine(unavailableLayout.Root, "appcache"), recursive: true);
        var unavailableResolver = new WindowsSteamArtworkSource();
        await using (var unavailableProvider = CreateProvider(
                         unavailableLayout.Root, unavailableResolver))
        {
            var unavailableItem = (await Query(unavailableProvider)).Items.Single();
            Assert.Equal(0, unavailableResolver.FileProbeCalls);
            Assert.Equal<string?>(null, (await unavailableProvider.GetAppLibraryIconAsync(
                unavailableItem.ProviderAppId, CancellationToken.None)).PngBase64);
            Assert.True(unavailableResolver.FileProbeCalls > 0);
        }

        using var jpegLayout = SteamLayout.Create(
            "440", ".jpg", await CreateJpeg(13, 9));
        await using var jpegProvider = CreateProvider(
            jpegLayout.Root, new WindowsSteamArtworkSource());
        var jpegItem = (await Query(jpegProvider)).Items.Single();
        var jpeg = await jpegProvider.GetAppLibraryIconAsync(
            jpegItem.ProviderAppId, CancellationToken.None);
        Assert.True(jpeg.PngBase64 is not null);
        Assert.True(Convert.FromBase64String(jpeg.PngBase64!).AsSpan(0, 8)
            .SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));

        using var duplicateWithoutArtwork = SteamLayout.CreateEmpty();
        using var duplicateWithArtwork = SteamLayout.CreateEmpty();
        duplicateWithoutArtwork.WriteManifest("333", "Duplicate Steam Game");
        duplicateWithArtwork.WriteManifest("333", "Duplicate Steam Game");
        _ = duplicateWithArtwork.WriteArtwork(
            "333", ".png", CreatePng(8, 8, 31));
        var duplicateSource = new WindowsSteamApplicationSource(
            [duplicateWithoutArtwork.Root, duplicateWithArtwork.Root],
            new WindowsSteamArtworkSource());
        await using var duplicateProvider = new WindowsAppLibraryProvider(
            [new SteamGameLibrarySource(
                duplicateSource, new NoopSteamLauncher())],
            ImmediateSta.Instance);
        var duplicate = (await Query(duplicateProvider)).Items.Single();
        Assert.True((await duplicateProvider.GetAppLibraryIconAsync(
            duplicate.ProviderAppId, CancellationToken.None)).PngBase64 is not null);
    }

    internal static async Task ReplacementRotatesAndStaleDemandFailsClosed()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var layout = SteamLayout.Create("570", ".png", CreatePng(16, 16, 11));
        var resolver = new WindowsSteamArtworkSource();
        var applicationSource = new WindowsSteamApplicationSource([layout.Root], resolver);
        var gameSource = new SteamGameLibrarySource(
            applicationSource, new NoopSteamLauncher());
        Assert.False(((IGameLibrarySource)gameSource).RequiresStaArtwork);
        await using var provider = new WindowsAppLibraryProvider(
            [gameSource], ImmediateSta.Instance);
        var initial = (await Query(provider)).Items.Single();
        var initialSourceItem = gameSource.Snapshot.Items.Single();
        var first = await provider.GetAppLibraryIconAsync(
            initial.ProviderAppId, CancellationToken.None);
        Assert.True(first.PngBase64 is not null);

        var priorWriteTime = File.GetLastWriteTimeUtc(layout.ArtworkPath);
        var replacement = CreatePng(16, 16, 29);
        var priorLength = new FileInfo(layout.ArtworkPath).Length;
        Assert.True(replacement.LongLength <= priorLength);
        Array.Resize(ref replacement, checked((int)priorLength));
        var replacementPath = layout.ArtworkPath + ".replacement";
        File.WriteAllBytes(replacementPath, replacement);
        File.Move(replacementPath, layout.ArtworkPath, overwrite: true);
        File.SetLastWriteTimeUtc(layout.ArtworkPath, priorWriteTime);
        var exact = gameSource.ResolveExact(initialSourceItem, CancellationToken.None)!;
        Assert.Equal(initialSourceItem.ArtworkRevision, exact.ArtworkRevision);
        var stale = await provider.GetAppLibraryIconAsync(
            initial.ProviderAppId, CancellationToken.None);
        Assert.Equal<string?>(null, stale.PngBase64);

        var refreshed = (await Query(provider, refresh: true)).Items.Single();
        Assert.Equal(initial.ProviderAppId, refreshed.ProviderAppId);
        Assert.False(initial.ArtworkRevision == refreshed.ArtworkRevision);
        var second = await provider.GetAppLibraryIconAsync(
            refreshed.ProviderAppId, CancellationToken.None);
        Assert.True(second.PngBase64 is not null);
        Assert.False(first.PngBase64 == second.PngBase64);

        File.Delete(layout.ArtworkPath);
        Assert.Equal<string?>(null, (await provider.GetAppLibraryIconAsync(
            refreshed.ProviderAppId, CancellationToken.None)).PngBase64);
        var removed = (await Query(provider, refresh: true)).Items.Single();
        Assert.False(refreshed.ArtworkRevision == removed.ArtworkRevision);
        Assert.Equal<string?>(null, (await provider.GetAppLibraryIconAsync(
            removed.ProviderAppId, CancellationToken.None)).PngBase64);
    }

    internal static async Task ReplacementDuringDecodeIsRejected()
    {
        if (!OperatingSystem.IsWindows()) return;
        var png = CreatePng(20, 20, 17);
        using var layout = SteamLayout.Create("620", ".png", png);
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var resolver = new WindowsSteamArtworkSource((_, cancellationToken) =>
        {
            entered.TrySetResult();
            WaitHandle.WaitAny([release.WaitHandle, cancellationToken.WaitHandle]);
            cancellationToken.ThrowIfCancellationRequested();
            return Convert.ToBase64String(png);
        });
        await using var provider = CreateProvider(layout.Root, resolver);
        var item = (await Query(provider)).Items.Single();
        var demand = provider.GetAppLibraryIconAsync(
            item.ProviderAppId, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var replacementRejected = false;
        try
        {
            using var writer = new FileStream(
                layout.ArtworkPath, FileMode.Open, FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
        }
        catch (IOException)
        {
            replacementRejected = true;
        }
        finally
        {
            release.Set();
        }
        Assert.True(replacementRejected);
        Assert.True((await demand).PngBase64 is not null);
    }

    internal static async Task ReplacementRetainsUnaffectedNeighbor()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var layout = SteamLayout.CreateEmpty();
        layout.WriteManifest("701", "Changed game");
        layout.WriteManifest("702", "Neighbor game");
        var changedPath = layout.WriteArtwork("701", ".png", CreatePng(9, 9, 17));
        _ = layout.WriteArtwork("702", ".png", CreatePng(9, 9, 23));
        var resolver = new WindowsSteamArtworkSource();
        await using var provider = CreateProvider(layout.Root, resolver);

        var initial = await Query(provider);
        foreach (var item in initial.Items)
            Assert.True((await provider.GetAppLibraryIconAsync(
                item.ProviderAppId, CancellationToken.None)).PngBase64 is not null);
        var discovered = await Query(provider, refresh: true);
        var changed = discovered.Items.Single(item => item.DisplayName == "Changed game");
        var neighbor = discovered.Items.Single(item => item.DisplayName == "Neighbor game");
        var neighborPng = (await provider.GetAppLibraryIconAsync(
            neighbor.ProviderAppId, CancellationToken.None)).PngBase64;

        File.WriteAllBytes(changedPath, CreatePng(10, 8, 41));
        Assert.Equal<string?>(null, (await provider.GetAppLibraryIconAsync(
            changed.ProviderAppId, CancellationToken.None)).PngBase64);
        var rotated = await Query(provider, refresh: true);
        var changedAfter = rotated.Items.Single(item => item.DisplayName == "Changed game");
        var neighborAfter = rotated.Items.Single(item => item.DisplayName == "Neighbor game");
        Assert.False(changed.ArtworkRevision == changedAfter.ArtworkRevision);
        Assert.Equal(neighbor.ArtworkRevision, neighborAfter.ArtworkRevision);
        Assert.Equal(neighbor.ProviderAppId, neighborAfter.ProviderAppId);
        Assert.Equal(neighborPng, (await provider.GetAppLibraryIconAsync(
            neighborAfter.ProviderAppId, CancellationToken.None)).PngBase64);
        Assert.True((await provider.GetAppLibraryIconAsync(
            changedAfter.ProviderAppId, CancellationToken.None)).PngBase64 is not null);
    }

    internal static async Task FailuresFallBackAndCacheStaysBounded()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var corrupt = SteamLayout.Create(
            "999", ".png", [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3]);
        await using (var provider = CreateProvider(
                         corrupt.Root, new WindowsSteamArtworkSource()))
        {
            var item = (await Query(provider)).Items.Single();
            Assert.Equal<string?>(null, (await provider.GetAppLibraryIconAsync(
                item.ProviderAppId, CancellationToken.None)).PngBase64);
        }

        using var oversized = SteamLayout.Create(
            "1000", ".png", new byte[WindowsSteamArtworkSource.MaximumSourceBytes + 1]);
        await using (var provider = CreateProvider(
                         oversized.Root, new WindowsSteamArtworkSource()))
        {
            var item = (await Query(provider)).Items.Single();
            Assert.Equal<string?>(null, (await provider.GetAppLibraryIconAsync(
                item.ProviderAppId, CancellationToken.None)).PngBase64);
        }

        var decoded = Convert.ToBase64String(CreatePng(4, 4, 3));
        var resolver = new WindowsSteamArtworkSource((_, _) => decoded);
        using var cache = SteamLayout.CreateEmpty();
        for (var index = 1; index <= WindowsSteamArtworkSource.MaximumCacheEntries + 1; index++)
        {
            var appId = (20_000 + index).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var path = cache.WriteArtwork(appId, ".png", CreatePng(1, 1, (byte)index));
            var registration = resolver.Register([cache.Root], appId);
            Assert.True(registration is not null);
            Assert.True(resolver.Load(registration!, CancellationToken.None) is not null);
            Assert.True(File.Exists(path));
        }
        Assert.Equal(WindowsSteamArtworkSource.MaximumCacheEntries, resolver.CacheCount);
    }

    internal static async Task BlockedArtworkDoesNotBlockCatalogAndDrains()
    {
        var source = new BlockingSteamSource();
        await using var provider = new WindowsAppLibraryProvider(
            [new SteamGameLibrarySource(source, new NoopSteamLauncher())],
            ImmediateSta.Instance);
        var appId = (await provider.GetAppsAsync()).Single().AppId;
        var artwork = provider.GetAppLibraryIconAsync(appId, CancellationToken.None);
        await source.ArtworkStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var page = await Query(provider).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, page.Items.Count);
        var disposal = provider.DisposeAsync().AsTask();
        await Assert.ThrowsAsync<OperationCanceledException>(() => artwork);
        await disposal.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(source.ArtworkCancellationObserved);
        Assert.Equal(1, source.ArtworkCalls);
    }

    internal static async Task CooperativeArtworkDrainsBeforeSourceDisposal()
    {
        var source = new CancellationIgnoringArtworkSource
        {
            IgnoreCancellation = false,
        };
        var provider = new WindowsAppLibraryProvider(
            [source], ImmediateSta.Instance, TimeSpan.FromMilliseconds(500));
        var appId = (await Query(provider)).Items.Single().ProviderAppId;
        var artwork = provider.GetAppLibraryIconAsync(appId, CancellationToken.None);
        await source.ArtworkStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await provider.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<OperationCanceledException>(() => artwork);
        Assert.Equal(1, source.DisposeCalls);
        Assert.False(source.DisposedBeforeArtworkCompleted);
    }

    internal static async Task CancellationIgnoringArtworkCannotRaceSourceDisposal()
    {
        var source = new CancellationIgnoringArtworkSource();
        var provider = new WindowsAppLibraryProvider(
            [source], ImmediateSta.Instance, TimeSpan.FromMilliseconds(100));
        var appId = (await Query(provider)).Items.Single().ProviderAppId;
        var artwork = provider.GetAppLibraryIconAsync(appId, CancellationToken.None);
        await source.ArtworkStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var firstDisposal = provider.DisposeAsync().AsTask();
        var secondDisposal = provider.DisposeAsync().AsTask();
        var firstFailure = await Assert.ThrowsAsync<AggregateException>(() => firstDisposal);
        var secondFailure = await Assert.ThrowsAsync<AggregateException>(() => secondDisposal);
        Assert.True(firstFailure.Flatten().InnerExceptions.Any(exception =>
            exception.Message.Contains("artwork work", StringComparison.Ordinal)));
        Assert.True(secondFailure.Flatten().InnerExceptions.Any(exception =>
            exception.Message.Contains("artwork work", StringComparison.Ordinal)));
        Assert.Equal(0, source.DisposeCalls);
        Assert.True(source.CancellationObserved);
        Assert.True(provider.HasRetainedCatalogState);

        source.ReleaseArtwork.Set();
        await Assert.ThrowsAsync<OperationCanceledException>(() => artwork);
        Assert.Equal(0, source.DisposeCalls);
    }

    internal static async Task LocatorCatalogChurnStaysBounded()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var layout = SteamLayout.CreateEmpty();
        const string stableId = "50001";
        const string removedId = "50002";
        _ = layout.WriteArtwork(stableId, ".png", CreatePng(3, 3, 17));
        var removedPath = layout.WriteArtwork(
            removedId, ".png", CreatePng(3, 3, 23));
        var resolver = new WindowsSteamArtworkSource();
        var firstIds = Enumerable.Range(
                50_001, WindowsSteamArtworkSource.MaximumLocatorRegistrations)
            .Select(value => value.ToString(
                System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        var firstCandidate = resolver.StageCatalog([layout.Root], firstIds);
        firstCandidate.Commit();
        var first = firstCandidate.Registrations;
        var stable = first[stableId];
        var removed = first[removedId];
        Assert.True(resolver.Load(stable, CancellationToken.None) is not null);
        Assert.True(resolver.Load(removed, CancellationToken.None) is not null);

        var secondIds = new[] { stableId }.Concat(Enumerable.Range(
                60_001, WindowsSteamArtworkSource.MaximumLocatorRegistrations - 1)
            .Select(value => value.ToString(
                System.Globalization.CultureInfo.InvariantCulture)))
            .ToArray();
        var secondCandidate = resolver.StageCatalog([layout.Root], secondIds);
        secondCandidate.Commit();
        var second = secondCandidate.Registrations;
        Assert.Equal(WindowsSteamArtworkSource.MaximumLocatorRegistrations,
            resolver.LocatorCount);
        Assert.True(ReferenceEquals(stable.Locator, second[stableId].Locator));
        Assert.Equal(stable.Revision, second[stableId].Revision);
        Assert.Equal<string?>(null, resolver.Load(removed, CancellationToken.None));

        File.WriteAllBytes(removedPath, CreatePng(4, 3, 31));
        Assert.Equal<string?>(null, resolver.Load(removed, CancellationToken.None));

        var thirdCandidate = resolver.StageCatalog(
            [layout.Root], new[] { stableId, removedId });
        thirdCandidate.Commit();
        var third = thirdCandidate.Registrations;
        Assert.Equal(2, resolver.LocatorCount);
        Assert.True(ReferenceEquals(stable.Locator, third[stableId].Locator));
        Assert.False(ReferenceEquals(removed.Locator, third[removedId].Locator));
        Assert.Equal("undiscovered", third[removedId].Revision);
        Assert.Equal<string?>(null, resolver.Load(removed, CancellationToken.None));
        Assert.True(resolver.Load(third[removedId], CancellationToken.None) is not null);

        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var delayed = new WindowsSteamArtworkSource((bytes, cancellationToken) =>
        {
            entered.TrySetResult();
            WaitHandle.WaitAny([release.WaitHandle, cancellationToken.WaitHandle]);
            cancellationToken.ThrowIfCancellationRequested();
            return Convert.ToBase64String(bytes);
        });
        var admittedCandidate = delayed.StageCatalog(
            [layout.Root], new[] { stableId });
        admittedCandidate.Commit();
        var admitted = admittedCandidate.Registrations[stableId];
        var late = Task.Run(() => delayed.Load(admitted, CancellationToken.None));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        delayed.StageCatalog([layout.Root], Array.Empty<string>()).Commit();
        Assert.Equal(0, delayed.LocatorCount);
        release.Set();
        Assert.Equal<string?>(null, await late.WaitAsync(TimeSpan.FromSeconds(2)));
        var replacementCandidate = delayed.StageCatalog(
            [layout.Root], new[] { stableId });
        replacementCandidate.Commit();
        var replacement = replacementCandidate.Registrations[stableId];
        Assert.False(ReferenceEquals(admitted.Locator, replacement.Locator));
    }

    internal static async Task CanceledStagedCatalogLeavesCurrentLocatorsActive()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var layout = SteamLayout.Create("71001", ".png", CreatePng(3, 3, 41));
        var resolver = new WindowsSteamArtworkSource();
        var stagedSource = new StagedSteamSource(
            new WindowsSteamApplicationSource([layout.Root], resolver), blockCall: 2);
        using var source = new SteamGameLibrarySource(
            stagedSource, new NoopSteamLauncher());
        var initial = source.Refresh(CancellationToken.None);
        var initialItem = initial.Items.Single();
        var initialRegistration = resolver.Register([layout.Root], "71001")!;
        var initialLocator = initialRegistration.Locator;
        Assert.True(source.LoadArtwork(initialItem, CancellationToken.None) is not null);
        var probesBeforeStage = resolver.FileProbeCalls;
        layout.WriteManifest("71002", "Candidate game");

        using var cancellation = new CancellationTokenSource();
        var refresh = Task.Run(() => source.Refresh(cancellation.Token));
        await stagedSource.Staged.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, resolver.LocatorCount);
        Assert.Equal(probesBeforeStage, resolver.FileProbeCalls);
        cancellation.Cancel();
        stagedSource.Release.Set();
        await Assert.ThrowsAsync<OperationCanceledException>(() => refresh);

        var retainedRegistration = resolver.Register([layout.Root], "71001")!;
        var stagedLocator = stagedSource.Captured!.Registrations
            .Single(registration => registration.SteamAppId == "71002")
            .Artwork!.Locator;
        Assert.Equal(1, resolver.LocatorCount);
        Assert.Equal(1, source.Snapshot.Items.Count);
        Assert.True(ReferenceEquals(initialLocator, retainedRegistration.Locator));
        Assert.Equal(initialRegistration.Revision, retainedRegistration.Revision);
        Assert.False(ReferenceEquals(stagedLocator,
            resolver.Register([layout.Root], "71002")!.Locator));
        Assert.True(source.LoadArtwork(initialItem, CancellationToken.None) is not null);
        Assert.False(source.Snapshot.Items.Any(item =>
            item.DisplayName == "Candidate game"));
    }

    internal static async Task LosingStagedCatalogCannotReplaceLatestLocators()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var layout = SteamLayout.Create("72001", ".png", CreatePng(3, 3, 47));
        var resolver = new WindowsSteamArtworkSource();
        var stagedSource = new StagedSteamSource(
            new WindowsSteamApplicationSource([layout.Root], resolver), blockCall: 2);
        using var source = new SteamGameLibrarySource(
            stagedSource, new NoopSteamLauncher());
        var initial = source.Refresh(CancellationToken.None);
        var stableRegistration = resolver.Register([layout.Root], "72001")!;
        var stableLocator = stableRegistration.Locator;
        var probesBeforeStages = resolver.FileProbeCalls;
        layout.WriteManifest("72002", "Losing game");

        var losing = Task.Run(() => source.Refresh(CancellationToken.None));
        await stagedSource.Staged.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var losingLocator = stagedSource.Captured!.Registrations
            .Single(registration => registration.SteamAppId == "72002")
            .Artwork!.Locator;
        layout.DeleteManifest("72002");
        layout.WriteManifest("72003", "Winning game");
        var winning = source.Refresh(CancellationToken.None);
        Assert.True(winning.Items.Any(item => item.DisplayName == "Winning game"));
        Assert.False(winning.Items.Any(item => item.DisplayName == "Losing game"));
        Assert.Equal(2, resolver.LocatorCount);
        Assert.Equal(probesBeforeStages, resolver.FileProbeCalls);
        stagedSource.Release.Set();
        var staleResult = await losing.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(staleResult.Items.Any(item => item.DisplayName == "Winning game"));
        Assert.False(staleResult.Items.Any(item => item.DisplayName == "Losing game"));
        var retainedStable = resolver.Register([layout.Root], "72001")!;
        Assert.True(ReferenceEquals(stableLocator, retainedStable.Locator));
        Assert.Equal(stableRegistration.Revision, retainedStable.Revision);
        Assert.False(ReferenceEquals(losingLocator,
            resolver.Register([layout.Root], "72002")!.Locator));
        Assert.True(source.LoadArtwork(initial.Items.Single(), CancellationToken.None)
            is not null);
    }

    private static WindowsAppLibraryProvider CreateProvider(
        string root,
        WindowsSteamArtworkSource artwork) =>
        new([
            new SteamGameLibrarySource(
                new WindowsSteamApplicationSource([root], artwork),
                new NoopSteamLauncher()),
        ], ImmediateSta.Instance);

    private static Task<AppLibraryBackendCursorPage> Query(
        WindowsAppLibraryProvider provider,
        bool refresh = false) =>
        provider.QueryAppLibraryAsync(
            new AppLibraryBackendCursorRequest(
                new AppLibraryBackendQuery(), null, null, 64, refresh),
            CancellationToken.None);

    private static byte[] CreatePng(int width, int height, byte seed)
    {
        var bgra = new byte[checked(width * height * 4)];
        for (var index = 0; index < bgra.Length; index += 4)
        {
            bgra[index] = (byte)(seed + index);
            bgra[index + 1] = (byte)(seed * 3 + index);
            bgra[index + 2] = (byte)(seed * 7 + index);
            bgra[index + 3] = 255;
        }
        return WindowsAppIconSource.EncodePng(bgra, width, height);
    }

    private static async Task<byte[]> CreateJpeg(uint width, uint height)
    {
        var rgba = new byte[checked((int)(width * height * 4))];
        for (var index = 0; index < rgba.Length; index += 4)
        {
            rgba[index] = 42;
            rgba[index + 1] = 93;
            rgba[index + 2] = 177;
            rgba[index + 3] = 255;
        }
        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output);
        encoder.SetPixelData(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Straight,
            width, height, 96, 96, rgba);
        await encoder.FlushAsync();
        output.Seek(0);
        using var reader = new DataReader(output.GetInputStreamAt(0));
        var loaded = await reader.LoadAsync((uint)output.Size);
        var result = new byte[loaded];
        reader.ReadBytes(result);
        return result;
    }

    private sealed class SteamLayout : IDisposable
    {
        private SteamLayout(string root)
        {
            Root = root;
            Directory.CreateDirectory(Path.Combine(root, "steamapps"));
            Directory.CreateDirectory(Path.Combine(root, "appcache", "librarycache"));
        }

        internal string Root { get; }
        internal string ArtworkPath { get; private set; } = string.Empty;

        internal static SteamLayout Create(
            string appId, string extension, byte[] artwork)
        {
            var layout = CreateEmpty();
            layout.WriteManifest(appId, "Steam Game");
            layout.ArtworkPath = layout.WriteArtwork(appId, extension, artwork);
            return layout;
        }

        internal static SteamLayout CreateEmpty() => new(Path.Combine(
            Path.GetTempPath(), "gba-steam-artwork-" + Guid.NewGuid().ToString("N")));

        internal string WriteArtwork(string appId, string extension, byte[] bytes)
        {
            var path = Path.Combine(Root, "appcache", "librarycache",
                appId + "_icon" + extension);
            File.WriteAllBytes(path, bytes);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(appId.Length));
            return path;
        }

        internal void WriteManifest(string appId, string displayName) =>
            File.WriteAllText(Path.Combine(Root, "steamapps",
                    $"appmanifest_{appId}.acf"),
                $"\"AppState\" {{ \"appid\" \"{appId}\" " +
                $"\"name\" \"{displayName}\" }}");

        internal void DeleteManifest(string appId) => File.Delete(Path.Combine(
            Root, "steamapps", $"appmanifest_{appId}.acf"));

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class NoopSteamLauncher : IWindowsSteamLauncher
    {
        public void Launch(string exactSteamAppId, CancellationToken cancellationToken) =>
            cancellationToken.ThrowIfCancellationRequested();
    }

    private sealed class StagedSteamSource(
        WindowsSteamApplicationSource inner,
        int blockCall) : ISteamApplicationSource
    {
        private int _calls;
        internal SteamApplicationSourceCandidate? Captured { get; private set; }
        internal TaskCompletionSource Staged { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new();

        public IReadOnlyList<SteamRegistration> Enumerate(
            CancellationToken cancellationToken) => inner.Enumerate(cancellationToken);

        public SteamApplicationSourceCandidate Stage(
            CancellationToken cancellationToken)
        {
            var candidate = inner.Stage(cancellationToken);
            if (Interlocked.Increment(ref _calls) == blockCall)
            {
                Captured = candidate;
                Staged.TrySetResult();
                Release.Wait();
            }
            return candidate;
        }

        public SteamRegistration? ReadExact(
            string steamAppId,
            string manifestPath,
            CancellationToken cancellationToken) =>
            inner.ReadExact(steamAppId, manifestPath, cancellationToken);

        public string? LoadArtwork(
            SteamRegistration exactRegistration,
            CancellationToken cancellationToken) =>
            inner.LoadArtwork(exactRegistration, cancellationToken);
    }

    private sealed class ImmediateSta : IShellStaExecutor
    {
        internal static ImmediateSta Instance { get; } = new();
        public Task<T> RunAsync<T>(
            Func<CancellationToken, T> operation,
            CancellationToken cancellationToken) =>
            Task.FromResult(operation(cancellationToken));
    }

    private sealed class BlockingSteamSource : ISteamApplicationSource
    {
        private readonly SteamRegistration _registration = new(
            "steam-blocked", "Blocked artwork", "12345",
            Path.Combine(Path.GetTempPath(), "appmanifest_12345.acf"),
            "acf-blocked")
        {
            Artwork = new SteamArtworkRegistration(
                new SteamArtworkLocator([Path.GetTempPath()], "12345"),
                new string('A', 64)),
        };

        internal int ArtworkCalls { get; private set; }
        internal bool ArtworkCancellationObserved { get; private set; }
        internal TaskCompletionSource ArtworkStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<SteamRegistration> Enumerate(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return [_registration];
        }

        public SteamRegistration? ReadExact(
            string steamAppId, string manifestPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _registration;
        }

        public string? LoadArtwork(
            SteamRegistration exactRegistration, CancellationToken cancellationToken)
        {
            ArtworkCalls++;
            ArtworkStarted.TrySetResult();
            cancellationToken.WaitHandle.WaitOne();
            ArtworkCancellationObserved = cancellationToken.IsCancellationRequested;
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    private sealed class CancellationIgnoringArtworkSource : IGameLibrarySource
    {
        private readonly GameLibrarySourceItem _item = new(
            "source-ignoring-artwork",
            "Ignoring artwork",
            "stable-ignoring-artwork",
            "Ignoring artwork",
            WindowsAppLibraryKind.Game,
            true,
            true,
            GameLibrarySourceActions.Launch | GameLibrarySourceActions.Artwork,
            new string('B', 64),
            "item-ignoring-artwork");
        private int _disposeCalls;

        internal int DisposeCalls => Volatile.Read(ref _disposeCalls);
        internal bool IgnoreCancellation { get; init; } = true;
        internal bool CancellationObserved { get; private set; }
        internal bool ArtworkCompleted { get; private set; }
        internal bool DisposedBeforeArtworkCompleted { get; private set; }
        internal TaskCompletionSource ArtworkStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim ReleaseArtwork { get; } = new();

        public string SourceIdentity => "source-ignoring-artwork";
        public string Attribution => "Ignoring artwork";
        public bool RequiresStaArtwork => false;
        public GameLibrarySourceSnapshot Snapshot { get; private set; } = new(
            "source-ignoring-artwork", "Ignoring artwork", 0,
            GameLibrarySourceHealth.Unavailable, []);

        public GameLibrarySourceSnapshot Refresh(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Snapshot = new(SourceIdentity, Attribution, 1,
                GameLibrarySourceHealth.Healthy, [_item]);
        }

        public GameLibrarySourceItem? ResolveExact(
            GameLibrarySourceItem item, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _item;
        }

        public GameLibraryLaunchResult Launch(
            GameLibrarySourceItem exactItem, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Launch is not part of this fixture.");

        public string? LoadArtwork(
            GameLibrarySourceItem exactItem, CancellationToken cancellationToken)
        {
            ArtworkStarted.TrySetResult();
            cancellationToken.Register(() => CancellationObserved = true);
            try
            {
                if (IgnoreCancellation)
                    ReleaseArtwork.Wait();
                else
                    cancellationToken.WaitHandle.WaitOne();
                cancellationToken.ThrowIfCancellationRequested();
                return Convert.ToBase64String(CreatePng(2, 2, 9));
            }
            finally
            {
                ArtworkCompleted = true;
            }
        }

        public void Dispose()
        {
            DisposedBeforeArtworkCompleted = !ArtworkCompleted;
            Interlocked.Increment(ref _disposeCalls);
        }
    }
}
