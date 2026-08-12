using System.Text.Json;
using System.Text.Json.Nodes;
using GameBarAlternative.GbarCli;

internal static class ScaffoldTransactionScenarios
{
    public static async Task Run()
    {
        await BinaryInventoryPublishesExactlyAsync();
        await ManifestRulesFailBeforePublicationAsync();
        await ContentBoundsFailBeforePublicationAsync();
        await InputAndValidationFailuresRollbackAsync();
        await ExistingAndFaultedDestinationsArePreservedAsync();
        await CancellationRollsBackAsync();
        await ReparseInputIsRejectedAsync();
    }

    private static async Task BinaryInventoryPublishesExactlyAsync()
    {
        using var fixture = new ScaffoldFixture("binary");
        var bytes = new byte[] { 0, 255, 1, 2, 13, 10, 127 };
        fixture.AddFile("assets/controller.bin", bytes, "assets/controller.bin", "binary");

        var result = await fixture.RunAsync();

        Equal(0, result.Code);
        SequenceEqual(
            bytes,
            await File.ReadAllBytesAsync(
                Path.Combine(fixture.Target, "assets", "controller.bin")));
        fixture.AssertNoStagingResidue();
    }

    private static async Task ManifestRulesFailBeforePublicationAsync()
    {
        await AssertRejectedAsync(
            "unsupported-version",
            fixture => fixture.Manifest["templateVersion"] = 3,
            "version 3 is unsupported");
        await AssertRejectedAsync(
            "unknown-property",
            fixture => fixture.Manifest["hook"] = "run-me",
            "unknown property 'hook'");
        await AssertRejectedAsync(
            "undeclared-file",
            fixture => File.WriteAllText(
                Path.Combine(fixture.TemplateRoot, "surprise.txt"), "not declared"),
            "surprise.txt' is not declared");
        await AssertRejectedAsync(
            "missing-file",
            fixture => File.Delete(
                Path.Combine(fixture.TemplateRoot, "README.md.template")),
            "declares missing file 'README.md.template'");
        await AssertRejectedAsync(
            "traversal-destination",
            fixture => fixture.FirstFile["destination"] = "../escaped.cs",
            "not a canonical relative path");
        await AssertRejectedAsync(
            "duplicate-destination",
            fixture => fixture.Files[1]!["destination"] =
                fixture.Files[0]!["destination"]!.GetValue<string>(),
            "produced more than once");
        await AssertRejectedAsync(
            "unknown-kind",
            fixture => fixture.FirstFile["kind"] = "script",
            "unsupported kind 'script'");
    }

    private static async Task ContentBoundsFailBeforePublicationAsync()
    {
        await AssertRejectedAsync(
            "manifest-size",
            fixture => fixture.Manifest["description"] =
                new string('x', ControllerWidgetScaffolder.MaximumManifestBytes),
            "must be between 1 and");

        await AssertRejectedAsync(
            "file-count",
            fixture =>
            {
                fixture.Files.Clear();
                for (var index = 0; index <= ControllerWidgetScaffolder.MaximumFiles; index++)
                {
                    fixture.Files.Add(new JsonObject
                    {
                        ["source"] = $"files/{index}.bin",
                        ["destination"] = $"files/{index}.bin",
                        ["kind"] = "binary",
                    });
                }
            },
            "between 1 and 64 files");

        await AssertRejectedAsync(
            "file-size",
            fixture => fixture.AddFile(
                "assets/large.bin",
                new byte[ControllerWidgetScaffolder.MaximumFileBytes + 1],
                "assets/large.bin",
                "binary"),
            "per-file bound");

        await AssertRejectedAsync(
            "aggregate-size",
            fixture =>
            {
                for (var index = 0; index < 5; index++)
                {
                    fixture.AddFile(
                        $"assets/aggregate-{index}.bin",
                        new byte[900 * 1024],
                        $"assets/aggregate-{index}.bin",
                        "binary");
                }
            },
            "aggregate bound");
    }

    private static async Task InputAndValidationFailuresRollbackAsync()
    {
        using (var fixture = new ScaffoldFixture("invalid-text"))
        {
            await File.WriteAllBytesAsync(
                Path.Combine(fixture.TemplateRoot, "README.md.template"),
                [0xff, 0xfe]);
            var result = await fixture.RunAsync();
            Equal(2, result.Code);
            Contains("not valid UTF-8", result.Error);
            fixture.AssertUnpublished();
        }

        using (var fixture = new ScaffoldFixture("unreadable"))
        {
            var lockedPath = Path.Combine(fixture.TemplateRoot, "README.md.template");
            await using var locked = new FileStream(
                lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);
            var result = await fixture.RunAsync();
            Equal(2, result.Code);
            Contains("could not be read", result.Error);
            fixture.AssertUnpublished();
        }

        using (var fixture = new ScaffoldFixture("late-validation"))
        {
            await File.WriteAllTextAsync(
                Path.Combine(fixture.TemplateRoot, "manifest.json.template"),
                "{}");
            var result = await fixture.RunAsync();
            Equal(2, result.Code);
            Contains("generated an invalid scaffold", result.Error);
            fixture.AssertUnpublished();
        }
    }

    private static async Task ExistingAndFaultedDestinationsArePreservedAsync()
    {
        using (var fixture = new ScaffoldFixture("existing"))
        {
            Directory.CreateDirectory(fixture.Target);
            var marker = Path.Combine(fixture.Target, "author.txt");
            await File.WriteAllTextAsync(marker, "keep me");
            var result = await fixture.RunAsync();
            Equal(2, result.Code);
            Contains("never overwrites or deletes", result.Error);
            Equal("keep me", await File.ReadAllTextAsync(marker));
            fixture.AssertNoStagingResidue();
        }

        using (var fixture = new ScaffoldFixture("destination-fault"))
        {
            var blockedParent = Path.Combine(fixture.Root, "parent-is-a-file");
            await File.WriteAllTextAsync(blockedParent, "keep parent");
            var result = await fixture.RunAsync(Path.Combine(blockedParent, "Widget"));
            Equal(2, result.Code);
            Contains("Could not prepare the output parent", result.Error);
            Equal("keep parent", await File.ReadAllTextAsync(blockedParent));
            fixture.AssertNoStagingResidue();
        }
    }

    private static async Task CancellationRollsBackAsync()
    {
        using var fixture = new ScaffoldFixture("cancelled");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await fixture.RunAsync(cancellationToken: cancellation.Token);
        Equal(130, result.Code);
        Contains("operation cancelled", result.Error);
        fixture.AssertUnpublished();
    }

    private static async Task ReparseInputIsRejectedAsync()
    {
        using var fixture = new ScaffoldFixture("reparse");
        var source = Path.Combine(fixture.TemplateRoot, "README.md.template");
        var outside = Path.Combine(fixture.Root, "outside.txt");
        await File.WriteAllTextAsync(outside, "outside");
        File.Delete(source);
        try
        {
            File.CreateSymbolicLink(source, outside);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        {
            return;
        }

        var result = await fixture.RunAsync();
        Equal(2, result.Code);
        Contains("reparse point", result.Error);
        fixture.AssertUnpublished();
    }

    private static async Task AssertRejectedAsync(
        string name,
        Action<ScaffoldFixture> mutate,
        string expectedError)
    {
        using var fixture = new ScaffoldFixture(name);
        mutate(fixture);
        fixture.SaveManifest();
        var result = await fixture.RunAsync();
        Equal(2, result.Code);
        Contains(expectedError, result.Error);
        fixture.AssertUnpublished();
    }

    private sealed class ScaffoldFixture : IDisposable
    {
        public ScaffoldFixture(string name)
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                $"gbar-scaffold-{name}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            TemplateRoot = Path.Combine(Root, "ControllerWidget");
            CopyDirectory(FindTemplateRoot(), TemplateRoot);
            Manifest = JsonNode.Parse(
                File.ReadAllText(Path.Combine(TemplateRoot, "template.json")))!
                .AsObject();
            Target = Path.Combine(Root, "GeneratedWidget");
        }

        public string Root { get; }
        public string TemplateRoot { get; }
        public string Target { get; }
        public JsonObject Manifest { get; }
        public JsonArray Files => Manifest["profiles"]!.AsArray()[0]!["files"]!.AsArray();
        public JsonObject FirstFile => Files[0]!.AsObject();

        public void AddFile(
            string source,
            byte[] content,
            string destination,
            string kind)
        {
            var path = Path.Combine(
                TemplateRoot,
                source.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
            Files.Add(new JsonObject
            {
                ["source"] = source,
                ["destination"] = destination,
                ["kind"] = kind,
            });
        }

        public void SaveManifest() => File.WriteAllText(
            Path.Combine(TemplateRoot, "template.json"),
            Manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        public async Task<CliResult> RunAsync(
            string? target = null,
            CancellationToken cancellationToken = default)
        {
            SaveManifest();
            var original = Environment.GetEnvironmentVariable("GBAR_TEMPLATE_ROOT");
            Environment.SetEnvironmentVariable("GBAR_TEMPLATE_ROOT", TemplateRoot);
            try
            {
                var output = new StringWriter();
                var error = new StringWriter();
                var code = await CliApplication.RunAsync(
                    ["new", "widget", "GeneratedWidget", "--output", target ?? Target,
                     "--id", "dev.test.generated", "--publisher", "dev.test"],
                    output,
                    error,
                    remoteHttpHandler: null,
                    cancellationToken);
                return new(code, output.ToString(), error.ToString());
            }
            finally
            {
                Environment.SetEnvironmentVariable("GBAR_TEMPLATE_ROOT", original);
            }
        }

        public void AssertUnpublished()
        {
            True(!Directory.Exists(Target), "A failed scaffold published its final target.");
            True(!File.Exists(Target), "A failed scaffold published a target file.");
            AssertNoStagingResidue();
        }

        public void AssertNoStagingResidue()
        {
            var residues = Directory.EnumerateFileSystemEntries(
                    Root, ".*.gbar-stage-*", SearchOption.TopDirectoryOnly)
                .ToArray();
            Equal(0, residues.Length);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }

        private static string FindTemplateRoot()
        {
            var current = new DirectoryInfo(Environment.CurrentDirectory);
            while (current is not null)
            {
                var candidate = Path.Combine(
                    current.FullName, "templates", "ControllerWidget");
                if (File.Exists(Path.Combine(candidate, "template.json")))
                    return candidate;
                current = current.Parent;
            }
            throw new InvalidOperationException("Could not locate ControllerWidget test input.");
        }

        private static void CopyDirectory(string source, string destination)
        {
            foreach (var directory in Directory.EnumerateDirectories(
                         source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(
                    destination, Path.GetRelativePath(source, directory)));
            }
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(
                         source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }
    }

    private sealed record CliResult(int Code, string Output, string Error);

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    private static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Expected output to contain '{expected}', got:{Environment.NewLine}{actual}");
    }

    private static void SequenceEqual(byte[] expected, byte[] actual)
    {
        if (!expected.AsSpan().SequenceEqual(actual))
            throw new InvalidOperationException("Generated binary asset did not preserve exact bytes.");
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
