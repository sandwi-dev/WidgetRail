using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetProtocol;
using Catalog = WidgetRail.WidgetCatalog.WidgetCatalog;

internal static class InitialBundleScenarios
{
    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-initial-bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var app = Path.Combine(root, "app");
            var store = Path.Combine(root, "catalog");
            Directory.CreateDirectory(Path.Combine(app, "initial-widgets"));
            var warnings = new List<string>();
            await BridgeInitialWidgetSeeder.SeedAsync(app, store, warnings.Add, default);
            Check(warnings.Count == 0, "Absent optional bundle blocked Bare startup");
            const string id = "widgetrail.firstparty.audio-mixer";
            var archive = WritePackage(app, id, false);
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive)));
            await Index(app, id, hash);
            await BridgeInitialWidgetSeeder.SeedAsync(app, store, warnings.Add, default);
            var catalog = new Catalog(store);
            Check(warnings.Count == 0 && (await catalog.DiscoverAsync()).Widgets.Single().Enabled, "Valid initial package was not admitted");
            var trustedIndex = Path.Combine(app, "widgets.json");
            await File.WriteAllTextAsync(trustedIndex, "{\"catalogVersion\":1,\"widgets\":[]}");
            var admitted = await BridgeCatalog.LoadWithInstalledAsync(trustedIndex, store, Environment.ProcessPath!);
            var optional = admitted.Catalog.GetConfigured(id);
            Check(optional.PublisherId.StartsWith("unsigned.", StringComparison.Ordinal) &&
                !optional.GrantsBundledPermissionDefaults && !WidgetBridgeServer.IsTrustedSettings(optional) &&
                !WidgetBridgeServer.IsTrustedMarketplace(optional), "Claimed first-party identity inherited trusted core privileges");
            await catalog.SetEnabledAsync(id, false);
            await catalog.UninstallAsync(id);
            File.Delete(archive);
            await BridgeInitialWidgetSeeder.SeedAsync(app, store, warnings.Add, default);
            Check((await catalog.DiscoverAsync()).Widgets.Count == 0 && warnings.Count == 0,
                "Removed package was reinstalled or its archive unnecessarily accessed");

            const string other = "dev.test.optional";
            archive = WritePackage(app, other, false);
            hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive)));
            await Index(app, other, new string('a',64));
            await BridgeInitialWidgetSeeder.SeedAsync(app, store, warnings.Add, default);
            Check(warnings.Count == 1 && (await catalog.DiscoverAsync()).Widgets.Count == 0, "Wrong digest installed bytes");
            await File.WriteAllTextAsync(Path.Combine(app,"initial-widgets.json"), JsonSerializer.Serialize(new
            { schemaVersion=1, widgets=new[]{new{id=other,package="../escape.wrwidget",sha256=hash}} }));
            await BridgeInitialWidgetSeeder.SeedAsync(app, store, warnings.Add, default);
            Check(warnings.Count == 2, "Traversal index was not rejected");
            await Index(app, other, hash);
            await BridgeInitialWidgetSeeder.SeedAsync(app, store, warnings.Add, default);
            Check((await catalog.DiscoverAsync()).Widgets.Count == 1, "A failed validation wrote a permanent seed receipt");

            const string full = "dev.test.full";
            archive = WritePackage(app, full, true);
            await Index(app, full, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))));
            await BridgeInitialWidgetSeeder.SeedAsync(app, store, warnings.Add, default);
            Check(warnings.Count == 3 && !(await catalog.DiscoverAsync()).Widgets.Any(widget=>widget.Id==full),
                "Bundle bypassed full-access consent");

            foreach (var retired in new[] { "widgetrail.firstparty.task-switcher", "widgetrail.firstparty.recent-apps" })
            {
                archive = WritePackage(app, retired, false);
                await catalog.InstallAsync(archive);
                await Index(app, retired, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))));
                File.Delete(archive);
                await BridgeInitialWidgetSeeder.SeedAsync(app, store, warnings.Add, default);
                Check(warnings.Count == 3 && !(await catalog.DiscoverAsync()).Widgets.Any(widget => widget.Id == retired),
                    "Retired host utility survived startup or its obsolete seed archive was accessed");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static Task Index(string app,string id,string hash) => File.WriteAllTextAsync(Path.Combine(app,"initial-widgets.json"),
        JsonSerializer.Serialize(new {schemaVersion=1, widgets=new[]{new{id,package=$"initial-widgets/{id}.wrwidget",sha256=hash}}}));
    private static string WritePackage(string app,string id,bool full)
    {
        var path=Path.Combine(app,"initial-widgets",id+".wrwidget");
        using var archive=ZipFile.Open(path,ZipArchiveMode.Create);
        var manifest=new WidgetManifest {Id=id,Publisher=id.StartsWith("widgetrail.",StringComparison.Ordinal)?"widgetrail.firstparty":"dev.test",
            Name="Optional",Version="1.0.0",HostApi=new("1.0",1),
            Entrypoint=full?new("full-trust-application-v1",Executable:"payload/widget.exe"):new("dotnet-worker","payload/widget.dll","Example.Widget")};
        using(var output=archive.CreateEntry("manifest.json").Open()) output.Write(ManifestJson.Serialize(manifest));
        using(var output=archive.CreateEntry(full?"payload/widget.exe":"payload/widget.dll").Open()) output.Write(new byte[]{1,2,3});
        return path;
    }
    private static void Check(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
}
