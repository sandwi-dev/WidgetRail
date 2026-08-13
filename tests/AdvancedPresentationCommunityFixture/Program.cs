using System.IO.Compression;
using System.Reflection;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.Tests.AdvancedPresentationCommunityFixture;

internal static class Program
{
    private sealed record Package(
        string Id,
        string Publisher,
        string Name,
        string Assembly,
        string Type);

    private static readonly Package[] Packages =
    [
        new(
            "net.unrelated.bravo.deck", "net.unrelated", "Bravo Deck",
            "BravoDeck.dll",
            "GameBarAlternative.Tests.AdvancedPresentationCommunityFixture.BravoDeckWidget"),
    ];

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 4 || args[0] != "--install" ||
            args[2] != "--candidate-package")
            throw new ArgumentException(
                "Usage: AdvancedPresentationCommunityFixture --install <catalog-root> " +
                "--candidate-package <gbarwidget>");
        var catalogRoot = Path.GetFullPath(args[1]);
        var candidatePackage = Path.GetFullPath(args[3]);
        if (!File.Exists(candidatePackage))
            throw new FileNotFoundException(
                "The exported Game Launcher candidate package was absent.",
                candidatePackage);
        Directory.CreateDirectory(catalogRoot);
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(), $"gba-dlv213-community-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            var catalog = new GameBarAlternative.WidgetCatalog.WidgetCatalog(catalogRoot);
            var candidate = await catalog.InstallAsync(candidatePackage)
                .ConfigureAwait(false);
            if (candidate.Manifest.Id !=
                "org.gbar.community.reference.game-launcher")
                throw new InvalidOperationException(
                    "The supplied candidate was not the supported Game Launcher export.");
            await catalog.SetEnabledAsync(candidate.Manifest.Id, true)
                .ConfigureAwait(false);
            foreach (var package in Packages)
            {
                var archivePath = Path.Combine(temporaryRoot, $"{package.Id}.gbarwidget");
                using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
                {
                    WriteText(archive, "manifest.json", $$"""
                        {
                          "manifestVersion": 1,
                          "id": "{{package.Id}}",
                          "publisher": "{{package.Publisher}}",
                          "name": "{{package.Name}}",
                          "version": "1.0.0",
                          "hostApi": { "minimum": "1.0", "maximumMajor": 1 },
                          "entrypoint": {
                            "runtime": "dotnet-worker",
                            "assembly": "payload/{{package.Assembly}}",
                            "type": "{{package.Type}}"
                          },
                          "advancedPresentation": {
                            "schemaVersion": 1,
                            "kind": "launcherExperience"
                          },
                          "permissions": [],
                          "optionalPermissions": [],
                          "residencyPolicy": {
                            "schemaVersion": 1,
                            "mode": "keep-alive"
                          },
                          "resourceRequest": { "memoryMb": 32, "updateHz": 2 },
                          "architectures": [ "x64" ]
                        }
                        """);
                    archive.CreateEntryFromFile(
                        Assembly.GetExecutingAssembly().Location,
                        $"payload/{package.Assembly}", CompressionLevel.Optimal);
                    AddRuntimeAssembly(archive, typeof(Widget).Assembly.Location);
                    AddRuntimeAssembly(archive, typeof(WidgetSurfaceHints).Assembly.Location);
                    AddRuntimeAssembly(
                        archive,
                        typeof(GameBarAlternative.WidgetCatalog.WidgetCatalog).Assembly.Location);
                }
                await catalog.InstallAsync(archivePath).ConfigureAwait(false);
                await catalog.SetEnabledAsync(package.Id, true).ConfigureAwait(false);
            }
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
            path, $"payload/{Path.GetFileName(path)}", CompressionLevel.Optimal);
}

public abstract class AdvancedPresentationFixtureWidget(
    string prefix,
    WidgetAdvancedPresentationPreset preset,
    string stylePrefix) : Widget
{
    private bool _activated;

    public override WidgetView Render()
    {
        WidgetElement Slot(string suffix, WidgetAdvancedPresentationSlot slot,
            params WidgetElement[] children) =>
            UI.Stack($"{prefix}.{suffix}", children)
                .Classes($"{stylePrefix}-{suffix}")
                .InAdvancedPresentationSlot(slot);

        return new WidgetView(UI.Stack($"{prefix}.root",
            UI.Stack($"{prefix}.outer",
                Slot("details", WidgetAdvancedPresentationSlot.DetailsPanel,
                    UI.Text(_activated ? "Activated" : "Ready", $"{prefix}.title")),
                UI.Row($"{prefix}.middle",
                    Slot("collection", WidgetAdvancedPresentationSlot.PrimaryCollection,
                        UI.Button("Open item", "fixture.activate", $"{prefix}.item"))))
                .Classes($"{stylePrefix}-outer"),
            Slot("navigation", WidgetAdvancedPresentationSlot.CollectionNavigation,
                UI.Button("All", "fixture.collection", $"{prefix}.all")),
            Slot("source", WidgetAdvancedPresentationSlot.SourceStatus,
                UI.Text("Installed Community source", $"{prefix}.source.copy")),
            Slot("operation", WidgetAdvancedPresentationSlot.OperationStatus,
                UI.Text("No active operation", $"{prefix}.operation.copy")),
            Slot("hints", WidgetAdvancedPresentationSlot.ControllerHints,
                UI.Text("A Select  B Back", $"{prefix}.hints.copy"))))
        {
            InitialFocusId = $"{prefix}.item",
            AdvancedPresentation = new(
                WidgetAdvancedPresentationKind.LauncherExperience, preset),
        };
    }

    public override ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (action.ActionId == "fixture.activate")
        {
            _activated = true;
            Invalidate();
        }
        return ValueTask.CompletedTask;
    }
}

public sealed class BravoDeckWidget() : AdvancedPresentationFixtureWidget(
    "q7", WidgetAdvancedPresentationPreset.CompactGrid, "bravo-theme");
