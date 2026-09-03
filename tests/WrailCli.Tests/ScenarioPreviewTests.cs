using System.Text.Json;
using WidgetRail.WrailCli;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class ScenarioPreviewTests
{
    internal static async Task Run()
    {
        await ExplainsTheIsolatedContractAsync();
        await ListsWithoutResolvingTheAssemblyAsync();
        await MissingAssemblyFailsWithoutWritingAsync();
        await RejectsMalformedAndUnboundedDeclarationsAsync();
        await DescendantScopedPinnedLayoutUsesTheRealCliPathAsync();
        PinnedLayoutsUseValidatedSemanticPreview();
        MediaViewportUsesDeterministicNativePlaceholder();
    }

    private static async Task ExplainsTheIsolatedContractAsync()
    {
        var help = await RunCliAsync("preview", "help");
        Equal(0, help.Code, $"preview help failed: {help.Error}");
        Contains("widgetrail.scenarios.json", help.Output);
        Contains("without resolving or", help.Output);
        Contains("loading the provider assembly", help.Output);
        Contains("capability-free AppContainer/Job worker", help.Output);
        Contains("never loads or executes scenario code in-process", help.Output);
    }

    private static async Task ListsWithoutResolvingTheAssemblyAsync()
    {
        using var temp = new ScenarioDirectory();
        await temp.WriteManifestAsync(
            Scenario("playing", "Playing", "Authenticated playback without OAuth"),
            Scenario("empty", "Empty", "No active playback"));

        var listed = await RunCliAsync("preview", temp.Path);
        Equal(0, listed.Code, $"scenario listing failed: {listed.Error}");
        Contains("playing - Authenticated playback without OAuth", listed.Output);
        Contains("empty - No active playback", listed.Output);
        Contains("Listing does not load the provider assembly", listed.Output);
        False(File.Exists(Path.Combine(temp.Path, ScenarioDirectory.MissingAssembly)),
            "Listing unexpectedly required or created the provider assembly.");

        var unknown = await RunCliAsync(
            "preview", temp.Path, "--scenario", "missing");
        Equal(2, unknown.Code);
        Contains("Available: playing, empty", unknown.Error);
    }

    private static async Task MissingAssemblyFailsWithoutWritingAsync()
    {
        using var temp = new ScenarioDirectory();
        await temp.WriteManifestAsync(Scenario("playing", "Playing"));
        var destination = Path.Combine(temp.Path, "playing.snapshot.json");

        var result = await RunCliAsync(
            "preview", temp.Path, "--scenario", "playing",
            "--output", destination, "--instance", "preview.playing");

        Equal(1, result.Code);
        Contains("declared scenario assembly does not exist", result.Error);
        DoesNotContain(ScenarioDirectory.MissingAssembly, result.Error);
        False(File.Exists(destination),
            "Fail-closed execution unexpectedly wrote a snapshot.");

        var manifestPath = Path.Combine(temp.Path, "widgetrail.scenarios.json");
        var before = await File.ReadAllTextAsync(manifestPath);
        var overwrite = await RunCliAsync(
            "preview", temp.Path, "--scenario", "playing", "--output", manifestPath);
        Equal(1, overwrite.Code);
        Contains("declared scenario assembly does not exist", overwrite.Error);
        Equal(before, await File.ReadAllTextAsync(manifestPath));
    }

    private static async Task RejectsMalformedAndUnboundedDeclarationsAsync()
    {
        using var temp = new ScenarioDirectory();
        await temp.WriteRawManifestAsync("""
            {
              "version": 1,
              "assembly": "missing-provider.dll",
              "providerType": "Tests.PreviewScenarios",
              "scenarios": [{ "name": "playing", "factory": "Playing" }],
              "unexpected": true
            }
            """);
        var unknownField = await RunCliAsync("preview", temp.Path);
        Equal(1, unknownField.Code);
        Contains("Scenario manifest is invalid", unknownField.Error);

        await temp.WriteManifestAsync(
            Scenario("playing", "Playing"), Scenario("playing", "Empty"));
        var duplicate = await RunCliAsync("preview", temp.Path);
        Equal(1, duplicate.Code);
        Contains("declared more than once", duplicate.Error);

        await temp.WriteRawManifestAsync("""
            {
              "version": 1,
              "assembly": "../outside.dll",
              "providerType": "Tests.PreviewScenarios",
              "scenarios": [{ "name": "playing", "factory": "Playing" }]
            }
            """);
        var traversal = await RunCliAsync("preview", temp.Path);
        Equal(1, traversal.Code);
        Contains("bounded relative .dll path", traversal.Error);

        await temp.WriteManifestAsync(Enumerable.Range(0, 33)
            .Select(index => Scenario($"scenario-{index}", $"Scenario{index}"))
            .ToArray());
        var tooMany = await RunCliAsync("preview", temp.Path);
        Equal(1, tooMany.Code);
        Contains("between 1 and 32 scenarios", tooMany.Error);

        await temp.WriteRawManifestAsync(new string(' ', 64 * 1024 + 1));
        var oversized = await RunCliAsync("preview", temp.Path);
        Equal(1, oversized.Code);
        Contains("between 1 and 65536 bytes", oversized.Error);
    }

    private static void PinnedLayoutsUseValidatedSemanticPreview()
    {
        var compactRoot = ProjectionRoot(
            "compact", "Compact playback", "compact.play", "compact.scope");
        var queueRoot = ProjectionRoot(
            "queue", "Now playing and queue", "queue.play", "queue.scope");
        var snapshot = new ViewSnapshot
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            Sequence = 42,
            WidgetInstanceId = "preview.layouts",
            ActiveInputScopeId = "full.root",
            Root = new ViewNode { Id = "full.root", Kind = ViewNodeKind.Stack },
            PinnedLayouts =
            [
                Layout("compact", "Compact now playing", 360, 240,
                    compactRoot, "compact.scope", "compact.play"),
                Layout("up-next", "Now playing + up next", 640, 360,
                    queueRoot, "queue.scope", "queue.play"),
                Layout("compact-copy", "Compact duplicate", 420, 280,
                    compactRoot, "compact.scope", "compact.play"),
            ],
        };

        var exact = SnapshotPreview.FormatPinnedLayouts(snapshot, "up-next");
        Contains("Layout up-next | Now playing + up next", exact);
        Contains("Preferred extent: 640 x 360 DIP", exact);
        Contains("Minimum extent: 240 x 180 DIP", exact);
        Contains("Active input scope: queue.scope", exact);
        Contains("Initial focus: queue.play", exact);
        Contains("▶ Button #queue.play", exact);
        Contains("action=queue.toggle", exact);
        Contains("a11y=\"Now playing and queue\"", exact);
        DoesNotContain("Layout compact |", exact);

        var all = SnapshotPreview.FormatPinnedLayouts(snapshot, "@all");
        Ordered(all, "Layout compact |", "Layout up-next |", "Layout compact-copy |");
        Contains(
            "Warning: pinned layouts 'compact' and 'compact-copy' have semantically identical roots.",
            all);

        var unknown = Throws<CliUsageException>(() =>
            SnapshotPreview.FormatPinnedLayouts(snapshot, "missing"));
        Contains("Pinned layout 'missing' was not declared", unknown.Message);
        Contains("Available: compact, up-next, compact-copy", unknown.Message);

        var invalid = snapshot with
        {
            PinnedLayouts =
            [
                Layout("invalid", "Invalid", 360, 240,
                    compactRoot, "compact.scope", "missing.focus"),
            ],
        };
        var validation = Throws<CliOperationException>(() =>
            SnapshotPreview.FormatPinnedLayouts(invalid, "invalid"));
        Contains("Snapshot is invalid", validation.Message);
        Contains("initialFocusId", validation.Message);
    }

    private static void MediaViewportUsesDeterministicNativePlaceholder()
    {
        var media = new EmbeddedMediaSession
        {
            Id = "aurora.media",
            AccessibleName = "Aurora local media",
            EntryAsset = "media/index.html",
            Surface = new WidgetSurfaceHints
            {
                PreferredWidth = 640,
                PreferredHeight = 360,
                MinimumWidth = 240,
                MinimumHeight = 180,
            },
            AspectRatio = 16.0 / 9.0,
            Resources =
            [
                new() { Path = "media/index.html", ContentType = "text/html" },
            ],
        };
        var snapshot = new WidgetView(
            UI.Stack(
                "root",
                UI.Text("Aurora", "title"),
                UI.MediaViewport(media, "viewport")),
            ActiveInputScopeId: "root")
        {
            EmbeddedMediaSession = media,
        }.CreateSnapshot("aurora.preview", 1);

        var preview = SnapshotPreview.Format(snapshot);
        Contains("MediaViewport #viewport", preview);
        Contains("media=aurora.media placeholder=native", preview);
        Contains("a11y=\"Aurora local media\"", preview);
        DoesNotContain("WebView", preview);
        DoesNotContain("media/index.html", preview);
    }

    private static async Task DescendantScopedPinnedLayoutUsesTheRealCliPathAsync()
    {
        using var temp = new ScenarioDirectory();
        var assemblyName = "pinned-preview-fixture.dll";
        File.Copy(
            typeof(PinnedPreviewScenarioProvider).Assembly.Location,
            Path.Combine(temp.Path, assemblyName));
        await temp.WriteManifestAsync(
            assemblyName,
            typeof(PinnedPreviewScenarioProvider).FullName!,
            Scenario("descendant", nameof(PinnedPreviewScenarioProvider.DescendantScoped)));

        var ordinary = await RunCliAsync(
            "preview", temp.Path, "--scenario", "descendant");
        Equal(0, ordinary.Code, $"ordinary scenario preview failed: {ordinary.Error}");
        using (var document = JsonDocument.Parse(ordinary.Output))
            Equal("descendant", document.RootElement.GetProperty("scenario").GetString());

        var destination = Path.Combine(temp.Path, "descendant.scenario.json");
        var pinned = await RunCliAsync(
            "preview", temp.Path, "--scenario", "descendant",
            "--pinned-layout", "descendant", "--output", destination);
        Equal(0, pinned.Code, $"pinned scenario preview failed: {pinned.Error}");
        Contains("Layout descendant | Descendant scope", pinned.Output);
        Contains("Active input scope: descendant.scope", pinned.Output);
        Contains("▶ Button #descendant.play", pinned.Output);
        Contains(
            "Warning: pinned layouts 'descendant' and 'descendant-copy' have semantically identical roots.",
            pinned.Output);
        using var written = JsonDocument.Parse(await File.ReadAllBytesAsync(destination));
        Equal("descendant", written.RootElement.GetProperty("scenario").GetString());
        Equal("descendant.scope", written.RootElement.GetProperty("snapshot")
            .GetProperty("pinnedLayouts")[0]
            .GetProperty("activeInputScopeId").GetString());
    }

    private static PinnedPresentationLayout Layout(
        string id,
        string name,
        double width,
        double height,
        ViewNode root,
        string scope,
        string focus) => new()
    {
        Id = id,
        Name = name,
        Surface = new WidgetSurfaceHints
        {
            Mode = WidgetSurfaceMode.Compact,
            WidthMode = WidgetSurfaceAxisMode.Preferred,
            HeightMode = WidgetSurfaceAxisMode.Content,
            PreferredWidth = width,
            PreferredHeight = height,
            MinimumWidth = 240,
            MinimumHeight = 180,
        },
        Root = root,
        ActiveInputScopeId = scope,
        InitialFocusId = focus,
    };

    private static ViewNode ProjectionRoot(
        string prefix,
        string label,
        string focus,
        string scope) => new()
    {
        Id = $"{prefix}.root",
        Kind = ViewNodeKind.Stack,
        InputScopeId = scope,
        Children =
        [
            new ViewNode
            {
                Id = focus,
                Kind = ViewNodeKind.Button,
                Text = "Play",
                ActionId = $"{prefix}.toggle",
                AccessibilityLabel = label,
            },
        ],
    };

    private static object Scenario(
        string name,
        string factory,
        string? description = null) => new { name, factory, description };

    private static async Task<CliResult> RunCliAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await CliApplication.RunAsync(args, output, error);
        return new(code, output.ToString(), error.ToString());
    }

    private sealed record CliResult(int Code, string Output, string Error);

    private sealed class ScenarioDirectory : IDisposable
    {
        internal const string MissingAssembly = "missing-provider.dll";

        internal ScenarioDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "wrail-scenario-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        internal Task WriteManifestAsync(params object[] scenarios) =>
            WriteManifestAsync(
                MissingAssembly, "Tests.PreviewScenarios", scenarios);

        internal Task WriteManifestAsync(
            string assembly,
            string providerType,
            params object[] scenarios) =>
            WriteRawManifestAsync(JsonSerializer.Serialize(new
            {
                version = 1,
                assembly,
                providerType,
                scenarios,
            }));

        internal Task WriteRawManifestAsync(string json) => File.WriteAllTextAsync(
            System.IO.Path.Combine(Path, "widgetrail.scenarios.json"), json);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }

    private static void False(bool value, string message)
    {
        if (value) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(
                $"{message} Expected '{expected}', got '{actual}'.");
    }

    private static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Expected output to contain '{expected}'. Actual: {actual}");
    }

    private static void DoesNotContain(string expected, string actual)
    {
        if (actual.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Expected output not to contain '{expected}'.");
    }

    private static void Ordered(string actual, params string[] expected)
    {
        var previous = -1;
        foreach (var value in expected)
        {
            var index = actual.IndexOf(value, previous + 1, StringComparison.Ordinal);
            if (index < 0)
                throw new InvalidOperationException(
                    $"Expected output to contain '{value}' after offset {previous}. Actual: {actual}");
            previous = index;
        }
    }

    private static TException Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }
        throw new InvalidOperationException(
            $"Expected {typeof(TException).Name} to be thrown.");
    }
}

public static class PinnedPreviewScenarioProvider
{
    public static WidgetScenarioDefinition DescendantScoped() => new(
        new DescendantScopedPinnedPreviewWidget(),
        new WidgetTestHostServicesBuilder().Build());

    private sealed class DescendantScopedPinnedPreviewWidget : Widget
    {
        public override WidgetView Render()
        {
            var root = UI.Stack(
                "descendant.root",
                UI.Stack(
                        "descendant.scope.root",
                        UI.Button("Play", "descendant.toggle", "descendant.play") with
                        {
                            AccessibilityLabel = "Descendant playback",
                        })
                    .InputScope("descendant.scope"));
            var surface = new WidgetSurfaceHints
            {
                Mode = WidgetSurfaceMode.Compact,
                PreferredWidth = 360,
                PreferredHeight = 240,
                MinimumWidth = 240,
                MinimumHeight = 180,
            };
            return new WidgetView(UI.Text("Full widget", "full.root"))
            {
                PinnedLayouts =
                [
                    WidgetView.PinnedLayout(
                        "descendant", "Descendant scope", surface, root,
                        "descendant.play", "descendant.scope"),
                    WidgetView.PinnedLayout(
                        "descendant-copy", "Descendant scope copy", surface, root,
                        "descendant.play", "descendant.scope"),
                ],
            };
        }
    }
}
