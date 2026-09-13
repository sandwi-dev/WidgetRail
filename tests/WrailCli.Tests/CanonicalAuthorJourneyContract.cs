internal static class CanonicalAuthorJourneyContract
{
    private const string MarkerPrefix = "<!-- canonical-author-journey:";

    internal static void VerifyQuickstart(string repositoryRoot, string generatedSource)
    {
        var path = Path.Combine(repositoryRoot, "docs", "reference", "cli-workflows.md");
        var markdown = File.ReadAllText(path);
        var blocks = ExtractBlocks(markdown);
        var expected = new[]
        {
            "create-build-test",
            "widget-source",
            "validate",
            "render",
            "replay",
            "pack-install",
            "version-lifecycle",
            "remove",
        };
        Equal(expected, blocks.Keys.Order(StringComparer.Ordinal).ToArray());
        Equal(Normalize(generatedSource), Normalize(blocks["widget-source"].Content));

        Require(blocks["widget-source"].Language == "csharp",
            "The canonical generated source fence must be csharp.");
        foreach (var block in blocks.Where(item => item.Key != "widget-source"))
            Require(block.Value.Language == "powershell",
                $"Canonical block '{block.Key}' must be powershell.");

        ContainsAll(blocks["create-build-test"].Content,
            "wrail new widget VolumeControl",
            "--output .\\scratch\\VolumeControl",
            "--id dev.example.volume-control",
            "--publisher dev.example",
            "dotnet build .\\scratch\\VolumeControl\\VolumeControl.csproj -c Release",
            "dotnet run --project .\\scratch\\VolumeControl\\tests\\VolumeControl.Tests.csproj",
            "ready.snapshot.json");
        ContainsAll(blocks["validate"].Content,
            "wrail validate .\\scratch\\VolumeControl");
        ContainsAll(blocks["render"].Content,
            "wrail render",
            "ready.snapshot.json",
            "ready.canonical.json");
        ContainsAll(blocks["replay"].Content,
            "wrail replay",
            "ready.snapshot.json",
            "replays\\smoke.json");
        ContainsAll(blocks["pack-install"].Content,
            "wrail pack .\\scratch\\VolumeControl",
            "--configuration Release",
            "dev.example.volume-control-0.1.0.wrwidget",
            "wrail install");
        ContainsAll(blocks["version-lifecycle"].Content,
            "wrail disable dev.example.volume-control",
            "wrail version list dev.example.volume-control",
            "wrail version select dev.example.volume-control 0.2.0",
            "wrail enable dev.example.volume-control",
            "wrail version rollback dev.example.volume-control");
        ContainsAll(blocks["remove"].Content,
            "wrail disable dev.example.volume-control",
            "wrail uninstall dev.example.volume-control");

        foreach (var block in blocks.Values)
        {
            Require(!block.Content.Contains(repositoryRoot, StringComparison.OrdinalIgnoreCase),
                "Canonical author journey leaked an absolute checkout path.");
            Require(!block.Content.Contains("<confirmation-token", StringComparison.Ordinal) &&
                    !block.Content.Contains("example/widgets@", StringComparison.Ordinal),
                "Canonical offline author journey included an advanced or credential-adjacent placeholder.");
        }
    }

    private static IReadOnlyDictionary<string, CodeBlock> ExtractBlocks(string markdown)
    {
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        var result = new Dictionary<string, CodeBlock>(StringComparer.Ordinal);
        var index = 0;
        while ((index = normalized.IndexOf(MarkerPrefix, index, StringComparison.Ordinal)) >= 0)
        {
            var idStart = index + MarkerPrefix.Length;
            var idEnd = normalized.IndexOf(" -->", idStart, StringComparison.Ordinal);
            Require(idEnd > idStart, "Canonical author-journey marker is malformed.");
            var id = normalized[idStart..idEnd];
            Require(id.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'),
                $"Canonical author-journey marker '{id}' is invalid.");
            var fenceStart = normalized.IndexOf("```", idEnd, StringComparison.Ordinal);
            Require(fenceStart >= 0, $"Canonical block '{id}' has no code fence.");
            var languageEnd = normalized.IndexOf('\n', fenceStart + 3);
            Require(languageEnd > fenceStart, $"Canonical block '{id}' has no fence language.");
            var language = normalized[(fenceStart + 3)..languageEnd].Trim();
            var fenceEnd = normalized.IndexOf("\n```", languageEnd, StringComparison.Ordinal);
            Require(fenceEnd > languageEnd, $"Canonical block '{id}' has no closing fence.");
            var content = normalized[(languageEnd + 1)..fenceEnd];
            Require(result.TryAdd(id, new(language, content)),
                $"Canonical author-journey marker '{id}' is duplicated.");
            index = fenceEnd + 4;
        }
        return result;
    }

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static void ContainsAll(string actual, params string[] expected)
    {
        foreach (var value in expected)
            Require(actual.Contains(value, StringComparison.Ordinal),
                $"Canonical command block omitted '{value}'.");
    }

    private static void Equal(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        if (!expected.Order(StringComparer.Ordinal).SequenceEqual(
                actual.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"Canonical block set changed. Expected [{string.Join(", ", expected)}], " +
                $"got [{string.Join(", ", actual)}].");
    }

    private static void Equal(string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "The quickstart widget source diverged from the generated compiled source.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record CodeBlock(string Language, string Content);
}
