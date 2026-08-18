using System.IO.Compression;
using System.Reflection;
using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Tests.TrayRefreshCommunityFixture;

internal static class Program
{
    private const string PackageId = "dev.widgetrail.tests.tray-refresh-community";
    private const string PackageVersion = "1.0.0";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 ||
            !string.Equals(args[0], "--install", StringComparison.Ordinal))
            throw new ArgumentException(
                "Usage: TrayRefreshCommunityFixture --install <catalog-root>");

        var catalogRoot = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(catalogRoot);
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(), $"wrail-dlv209-community-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            var packagePath = Path.Combine(temporaryRoot, "community.wrwidget");
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                WriteText(archive, "manifest.json", $$"""
                    {
                      "manifestVersion": 1,
                      "id": "{{PackageId}}",
                      "publisher": "dev.widgetrail.tests",
                      "name": "Community Refresh Fixture",
                      "version": "{{PackageVersion}}",
                      "hostApi": { "minimum": "1.0", "maximumMajor": 1 },
                      "entrypoint": {
                        "runtime": "dotnet-worker",
                        "assembly": "payload/TrayRefreshCommunityFixture.dll",
                        "type": "WidgetRail.Tests.TrayRefreshCommunityFixture.CommunityRefreshWidget"
                      },
                      "permissions": [],
                      "optionalPermissions": [],
                      "residencyPolicy": {
                        "schemaVersion": 1,
                        "mode": "keep-alive"
                      },
                      "resourceRequest": { "memoryMb": 32, "updateHz": 1 },
                      "architectures": [ "x64" ]
                    }
                    """);
                var assemblyPath = Assembly.GetExecutingAssembly().Location;
                archive.CreateEntryFromFile(
                    assemblyPath,
                    "payload/TrayRefreshCommunityFixture.dll",
                    CompressionLevel.Optimal);
                AddRuntimeAssembly(archive, typeof(Widget).Assembly.Location);
                AddRuntimeAssembly(
                    archive,
                    typeof(WidgetSurfaceHints).Assembly.Location);
                AddRuntimeAssembly(
                    archive,
                    typeof(WidgetRail.WidgetCatalog.WidgetCatalog).Assembly.Location);
            }

            var catalog = new WidgetRail.WidgetCatalog.WidgetCatalog(catalogRoot);
            await catalog.InstallAsync(packagePath).ConfigureAwait(false);
            await catalog.SetEnabledAsync(PackageId, true).ConfigureAwait(false);
            return 0;
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static void WriteText(ZipArchive archive, string name, string value)
    {
        using var writer = new StreamWriter(
            archive.CreateEntry(name, CompressionLevel.Optimal).Open());
        writer.Write(value);
    }

    private static void AddRuntimeAssembly(ZipArchive archive, string path) =>
        archive.CreateEntryFromFile(
            path,
            $"payload/{Path.GetFileName(path)}",
            CompressionLevel.Optimal);
}

public sealed class CommunityRefreshWidget : Widget
{
    public override WidgetView Render() => new(
        UI.Stack(
            "community-root",
            UI.Text("Installed Community widget", "community-title"),
            UI.Button("Ready", "fixture.ready", "community-ready")),
        InitialFocusId: "community-ready",
        Surface: new WidgetSurfaceHints
        {
            Mode = WidgetSurfaceMode.Compact,
            PreferredWidth = 560,
            PreferredHeight = 420,
            MinimumWidth = 320,
            MinimumHeight = 300,
        });
}
