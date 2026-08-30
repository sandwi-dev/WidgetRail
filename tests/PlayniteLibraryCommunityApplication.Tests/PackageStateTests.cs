using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetSdk;

namespace PlayniteLibraryCommunityApplication.Tests;

[TestClass]
public sealed class PackageStateTests
{
    [TestMethod]
    public async Task OrganizationStateIsRevisionedRestartSafeAndConflictBounded()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "organization.json");
        var gameId = "00000000-0000-0000-0000-000000000001";
        var first = new PlayniteLibraryStateFileStore(path);
        var expected = PlayniteLibraryPrivateState.Empty with
        {
            Items = [new(gameId, "Game", "Playnite")],
            ProvenSources = ["Playnite"],
        };

        var mutation = await first.WriteAsync(expected, 0, CancellationToken.None);
        Assert.AreEqual(1L, mutation.Revision);

        var restarted = new PlayniteLibraryStateFileStore(path);
        var actual = await restarted.ReadAsync(CancellationToken.None);
        Assert.IsTrue(actual.Exists);
        Assert.AreEqual(1L, actual.Revision);
        Assert.AreEqual(gameId, actual.Value!.Items.Single().SavedId);
        Assert.AreEqual("Playnite", actual.Value.ProvenSources.Single());

        var conflict = await Assert.ThrowsExactlyAsync<WidgetCapabilityException>(
            async () => await restarted.WriteAsync(
                PlayniteLibraryPrivateState.Empty, 0, CancellationToken.None));
        Assert.AreEqual("state_conflict", conflict.ErrorCode);
    }

    [TestMethod]
    public void ApplicationPathsOwnOnlyPackageOrganizationState()
    {
        var properties = typeof(PlayniteLibraryApplicationPaths).GetProperties()
            .Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(new[] { "Root", "StateFile" }, properties);
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "samples",
            "PlayniteLibraryWidget", "Application", "PlayniteLibraryApplicationPaths.cs"));
        Assert.IsFalse(source.Contains("PlatformSettings", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("sources.json", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("saved-id.key", StringComparison.Ordinal));
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null &&
               !(File.Exists(Path.Combine(current.FullName, "README.md")) &&
                 File.Exists(Path.Combine(current.FullName, "docs", "README.md")) &&
                 File.Exists(Path.Combine(current.FullName, "global.json"))))
            current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException(
            "Repository root is unavailable.");
    }

    private sealed class TestDirectory : IDisposable
    {
        internal TestDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "wrail-playnite-library-app-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
