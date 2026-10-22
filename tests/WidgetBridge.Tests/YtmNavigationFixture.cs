using WidgetRail.Samples.YtMusicWidget.Standalone;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetStyling;
using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetProtocol;
using System.Security.Cryptography;

internal static class YtmNavigationFixture
{
    internal static async Task ServeAsync(string pipe, string mode)
    {
        if (mode is not ("reordered" or "removed" or "ambiguous" or "performance")) throw new ArgumentException("Unknown navigation case.", nameof(mode));
        var packageRoot = Path.Combine(AppContext.BaseDirectory, "fixture/ytm");
        var manifest = ManifestJson.Deserialize(await File.ReadAllBytesAsync(Path.Combine(packageRoot, "manifest.json")));
        var files = manifest.IconAssets!.Values.Select(asset => asset.Path).Distinct(StringComparer.Ordinal)
            .ToDictionary(path => path, path =>
            {
                var bytes = File.ReadAllBytes(Path.Combine(packageRoot, path));
                return new VerifiedPackageFile(path, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            }, StringComparer.Ordinal);
        var icons = PackageIconAssetResolver.LoadMetadata(packageRoot, manifest, files);
        var styles = WrssPackageLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "fixture/ytm-default.wrss"));
        var catalog = new ThemeCatalog(new PlatformSettingsPaths(Path.Combine(Path.GetTempPath(), "wrail-ytm-navigation-themes")));
        var theme = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package, styles,
            catalog.Load(ThemeIdentity.BuiltInNeonCircuit, ThemeIdentity.BuiltInNeonCircuitVersion).Package).Theme!;
        var configured = new ConfiguredWidget
        {
            Id = "indexed-owned", Name = "YouTube Music navigation fixture", InstanceId = "ytm-navigation-" + mode,
            PackageId = "dev.ytm-navigation", PublisherId = "dev", WorkerExecutable = Environment.ProcessPath!,
            WorkerFingerprint = new('a', 64), CatalogFingerprint = new('a', 64), CompiledTheme = theme,
            PackageRoot = packageRoot, VerifiedPackageFiles = files, PackageIconAssets = icons,
            DeclaredPackageIconAssetIds = manifest.IconAssets.Keys.ToHashSet(StringComparer.Ordinal),
        };
        await using var server = new WidgetBridgeServer(pipe, new([configured]));
        await server.RunAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
    }

    internal sealed class Service(string mode) : IMusicService
    {
        private int artistReads;
        public MusicState State { get; } = MusicState.Empty with { Connected = true };
        public event Action? Changed { add { } remove { } }
        public Task InitializeAsync(CancellationToken token) => Task.CompletedTask;
        public async Task<MusicPage> BrowseAsync(string kind, string value, CancellationToken token)
        {
            await Task.Delay(120, token); // Make loading/restoration cross real worker publications.
            if (kind == "home") return new("Artists", Enumerable.Range(0, mode == "performance" ? 10000 : 100).Select(index => new MusicItem("artist" + index, "artist", "Artist " + index)).ToArray());
            if (kind == "artist")
            {
                var items = Enumerable.Range(0, 100).Select(index => new MusicItem("album" + index, "album", "Album " + index)).ToArray();
                if (++artistReads > 1)
                {
                    Array.Reverse(items);
                    if (mode == "removed") items[24] = new("replacement", "album", "Replacement album");
                    if (mode == "ambiguous") items[0] = new("album75", "album", "Duplicate album identity");
                }
                return new("Artist albums " + artistReads, items);
            }
            if (kind == "album") return new("Album songs", [new("song", "song", "Synthetic song")]);
            throw new InvalidOperationException("Unexpected browse: " + kind);
        }
        public Task<IReadOnlyList<string>> SearchSuggestionsAsync(string query, CancellationToken token) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task ReadSongLikeAsync(MusicItem item, CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<string> SignInAsync(CancellationToken token) => throw new NotSupportedException();
        public Task CancelSignInAsync(CancellationToken token) => throw new NotSupportedException();
        public Task DisconnectAsync(CancellationToken token) => throw new NotSupportedException();
        public Task PlayAsync(IReadOnlyList<MusicItem> tracks, int index, CancellationToken token) => throw new NotSupportedException();
        public Task SelectQueueItemAsync(IReadOnlyList<MusicItem> expectedQueue, int index, CancellationToken token) => throw new NotSupportedException();
        public Task RemoveQueueItemAsync(IReadOnlyList<MusicItem> expectedQueue, int index, CancellationToken token) => throw new NotSupportedException();
        public Task SetSongLikeAsync(MusicItem item, bool liked, CancellationToken token) => throw new NotSupportedException();
        public Task RadioAsync(MusicItem song, CancellationToken token) => throw new NotSupportedException();
        public Task<PlayNextResult> PlayNextAsync(MusicItem item, CancellationToken token) => throw new NotSupportedException();
        public Task CommandAsync(string command, double? value, CancellationToken token) => throw new NotSupportedException();
    }
}
