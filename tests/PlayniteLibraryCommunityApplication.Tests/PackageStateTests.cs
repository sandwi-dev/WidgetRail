using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetSdk;

namespace PlayniteLibraryCommunityApplication.Tests;

[TestClass]
public sealed class PackageStateTests
{
    [TestMethod]
    public async Task SourceConfigurationImportsOnceThenBecomesPackageOwned()
    {
        using var directory = new TestDirectory();
        var product = Path.Combine(directory.Path, "product");
        var package = Path.Combine(directory.Path, "package");
        Directory.CreateDirectory(product);
        var legacy = Path.Combine(product, "platform-settings.json");
        await File.WriteAllTextAsync(legacy,
            "{\"appLibrary\":{\"epicInstalledGamesEnabled\":true,\"gogInstalledGamesEnabled\":false}}");
        var paths = Paths(package, legacy);

        var imported = await PlayniteLibrarySourceConfiguration.LoadAsync(paths);
        Assert.IsTrue(imported.EpicInstalledGamesEnabled);
        Assert.IsFalse(imported.GogInstalledGamesEnabled);
        Assert.IsTrue(File.Exists(paths.SourceConfigurationFile));

        await File.WriteAllTextAsync(legacy,
            "{\"appLibrary\":{\"epicInstalledGamesEnabled\":false,\"gogInstalledGamesEnabled\":true}}");
        var retained = await PlayniteLibrarySourceConfiguration.LoadAsync(paths);
        Assert.IsTrue(retained.EpicInstalledGamesEnabled);
        Assert.IsFalse(retained.GogInstalledGamesEnabled);
    }

    [TestMethod]
    public async Task OrganizationStateIsRevisionedAndRestartSafe()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "organization.json");
        var first = new PlayniteLibraryStateFileStore(path);
        var expected = PlayniteLibraryPrivateState.Empty with
        {
            ProvenSources = ["Steam"],
        };

        var mutation = await first.WriteAsync(expected, 0, CancellationToken.None);
        Assert.AreEqual(1L, mutation.Revision);

        var restarted = new PlayniteLibraryStateFileStore(path);
        var actual = await restarted.ReadAsync(CancellationToken.None);
        Assert.IsTrue(actual.Exists);
        Assert.AreEqual(1L, actual.Revision);
        Assert.AreEqual("Steam", actual.Value!.ProvenSources.Single());

        var conflict = await Assert.ThrowsExactlyAsync<WidgetCapabilityException>(
            async () => await restarted.WriteAsync(
                PlayniteLibraryPrivateState.Empty, 0, CancellationToken.None));
        Assert.AreEqual("state_conflict", conflict.ErrorCode);
    }

    [TestMethod]
    public void SavedIdsArePackageOwnedStableAndIdentityDistinct()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "saved-id.key");
        var first = new PlayniteLibrarySavedIdIssuer(path);
        var one = first.Issue("steam:1");
        var two = first.Issue("steam:2");
        var restarted = new PlayniteLibrarySavedIdIssuer(path);

        Assert.AreEqual(one, restarted.Issue("steam:1"));
        Assert.AreNotEqual(one, two);
        Assert.AreEqual(32L, new FileInfo(path).Length);
        Assert.IsFalse(File.ReadAllBytes(path).AsSpan().IndexOf("steam"u8) >= 0);
    }

    private static PlayniteLibraryApplicationPaths Paths(string root, string legacy) => new(
        root,
        Path.Combine(root, "organization.json"),
        Path.Combine(root, "saved-id.key"),
        Path.Combine(root, "sources.json"),
        legacy);

    private sealed class TestDirectory : IDisposable
    {
        internal TestDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "wrail-playnite-library-app-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
