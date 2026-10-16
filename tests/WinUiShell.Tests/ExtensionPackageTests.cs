using System.IO.Compression;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Browser.Extensions;

namespace WidgetRail.WinUiShell.Tests;

[TestClass]
public sealed class ExtensionPackageTests
{
    private string root = null!;
    [TestInitialize] public void Initialize() => Directory.CreateDirectory(root = Path.Combine(Path.GetTempPath(), "WidgetRail-extension-tests-" + Guid.NewGuid().ToString("N")));
    [TestCleanup] public void Cleanup() => Directory.Delete(root, true);
    private const string Manifest = """{"name":"Example","version":"1.0","manifest_version":3,"permissions":["storage","https://example.com/*"],"options_ui":{"page":"options.html"}}""";

    [TestMethod]
    public void ImportsUnpackedFolderAndReportsScope()
    {
        var source = Path.Combine(root, "source"); Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "manifest.json"), Manifest);
        File.WriteAllText(Path.Combine(source, "options.html"), "Options");
        var destination = Path.Combine(root, "copy");
        var parsed = ExtensionPackage.Copy(source, destination, default);
        CollectionAssert.AreEqual(new[] { "storage" }, parsed.Permissions);
        CollectionAssert.AreEqual(new[] { "https://example.com/*" }, parsed.Sites);
        Assert.AreEqual("options.html", parsed.OptionsPage);
        File.Delete(Path.Combine(source, "options.html"));
        Assert.AreEqual("Options", File.ReadAllText(Path.Combine(destination, "options.html")));
    }
    [TestMethod]
    public void ImportsInstallableZip()
    {
        var file = Zip(("manifest.json", Manifest), ("options.html", "Options"));
        Assert.AreEqual("Example", ExtensionPackage.Copy(file, Path.Combine(root, "copy"), default).Name);
    }
    [TestMethod]
    [DataRow("../escape.js")]
    [DataRow("/absolute.js")]
    [DataRow("C:/absolute.js")]
    [DataRow("folder/../../escape.js")]
    [DataRow("file.js:payload")]
    [DataRow("CON.txt")]
    [DataRow("trailing./file.js")]
    [DataRow("folder\\escape.js")]
    public void RejectsUnsafeArchivePaths(string entry)
    {
        var file = Zip(("manifest.json", Manifest), (entry, "bad"));
        Assert.ThrowsExactly<InvalidDataException>(() => ExtensionPackage.Copy(file, Path.Combine(root, "copy"), default));
    }
    [TestMethod]
    public void RejectsArchiveLinks()
    {
        var file = Zip(("manifest.json", Manifest));
        using (var archive = ZipFile.Open(file, ZipArchiveMode.Update))
        { var link = archive.CreateEntry("linked.js"); link.ExternalAttributes = 0xA1FF << 16; }
        Assert.ThrowsExactly<InvalidDataException>(() => ExtensionPackage.Copy(file, Path.Combine(root, "copy"), default));
    }
    [TestMethod]
    public void RejectsSourceArchiveAndRemoteOptionsPage()
    {
        var file = Zip(("source/manifest.json", Manifest));
        Assert.ThrowsExactly<InvalidDataException>(() => ExtensionPackage.Copy(file, Path.Combine(root, "copy"), default));
        var folder = Path.Combine(root, "bad"); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "manifest.json"), Manifest.Replace("options.html", "https://example.com/options"));
        Assert.ThrowsExactly<InvalidDataException>(() => ExtensionPackage.ReadManifest(folder));
    }
    [TestMethod]
    public async Task CatalogSurvivesRestartAndKeepsProfilesSeparate()
    {
        var store = new ExtensionStore(root);
        var profile = new ExtensionProfile("persistent-first", "first", "First", false);
        await store.RegisterAsync(profile, default);
        var manifest = new ExtensionManifest("Example", "1", 3, null, null, [], []);
        var install = new ExtensionEntry(Guid.NewGuid().ToString("N"), "extension", manifest, "v1", true, new() { [profile.Name] = ExtensionOverride.Off }, new());
        await store.SaveAsync(install, default);
        var reopened = new ExtensionStore(root);
        var catalog = await reopened.ReadAsync(default);
        Assert.AreEqual("extension", catalog.Library.Single().Id);
        Assert.IsFalse(catalog.Library.Single().EnabledFor(profile.Name));
        Assert.IsTrue(catalog.Library.Single().EnabledFor("persistent-future"));
        await reopened.ForgetAsync(install.Slot, default);
        Assert.AreEqual(0, (await store.ReadAsync(default)).Library.Length);
    }
    private string Zip(params (string Path, string Content)[] files)
    {
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in files) { using var writer = new StreamWriter(archive.CreateEntry(file.Path).Open()); writer.Write(file.Content); }
        return path;
    }

    [TestMethod]
    public void ExplicitOverridesSurviveDefaultChangesAndUnknownProfilesInherit()
    {
        var entry = new ExtensionEntry(Guid.NewGuid().ToString("N"), "id", new("Example", "1", 3, null, null, [], []), "v1", false,
            new() { ["on"] = ExtensionOverride.On, ["off"] = ExtensionOverride.Off }, new());
        foreach (var enabled in new[] { false, true })
        {
            var policy = entry with { EnabledByDefault = enabled };
            Assert.IsTrue(policy.EnabledFor("on")); Assert.IsFalse(policy.EnabledFor("off"));
            Assert.AreEqual(enabled, policy.EnabledFor("future"));
        }
        entry.Overrides.Remove("on");
        Assert.IsFalse(entry.EnabledFor("on"));
    }
}
