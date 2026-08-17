using WidgetRail.FirstPartyWidgets.GameLauncher;
using WidgetRail.WidgetSdk;

namespace GameLauncherCommunityApplication.Tests;

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

        var imported = await GameLauncherSourceConfiguration.LoadAsync(paths);
        Assert.IsTrue(imported.EpicInstalledGamesEnabled);
        Assert.IsFalse(imported.GogInstalledGamesEnabled);
        Assert.IsTrue(File.Exists(paths.SourceConfigurationFile));

        await File.WriteAllTextAsync(legacy,
            "{\"appLibrary\":{\"epicInstalledGamesEnabled\":false,\"gogInstalledGamesEnabled\":true}}");
        var retained = await GameLauncherSourceConfiguration.LoadAsync(paths);
        Assert.IsTrue(retained.EpicInstalledGamesEnabled);
        Assert.IsFalse(retained.GogInstalledGamesEnabled);
    }

    [TestMethod]
    public async Task OrganizationStateIsRevisionedAndRestartSafe()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "organization.json");
        var first = new GameLauncherStateFileStore(path);
        var expected = GameLauncherPrivateState.Empty with
        {
            ProvenSources = ["Steam"],
            ExperienceId = "compact-grid",
        };

        var mutation = await first.WriteAsync(expected, 0, CancellationToken.None);
        Assert.AreEqual(1L, mutation.Revision);

        var restarted = new GameLauncherStateFileStore(path);
        var actual = await restarted.ReadAsync(CancellationToken.None);
        Assert.IsTrue(actual.Exists);
        Assert.AreEqual(1L, actual.Revision);
        Assert.AreEqual("Steam", actual.Value!.ProvenSources.Single());
        Assert.AreEqual("compact-grid", actual.Value.ExperienceId);

        var conflict = await Assert.ThrowsExactlyAsync<WidgetCapabilityException>(
            async () => await restarted.WriteAsync(
                GameLauncherPrivateState.Empty, 0, CancellationToken.None));
        Assert.AreEqual("state_conflict", conflict.ErrorCode);
    }

    [TestMethod]
    public void SavedIdsArePackageOwnedStableAndIdentityDistinct()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "saved-id.key");
        var first = new GameLauncherSavedIdIssuer(path);
        var one = first.Issue("steam:1");
        var two = first.Issue("steam:2");
        var restarted = new GameLauncherSavedIdIssuer(path);

        Assert.AreEqual(one, restarted.Issue("steam:1"));
        Assert.AreNotEqual(one, two);
        Assert.AreEqual(32L, new FileInfo(path).Length);
        Assert.IsFalse(File.ReadAllBytes(path).AsSpan().IndexOf("steam"u8) >= 0);
    }

    private static GameLauncherApplicationPaths Paths(string root, string legacy) => new(
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
                System.IO.Path.GetTempPath(), "gba-game-launcher-app-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
