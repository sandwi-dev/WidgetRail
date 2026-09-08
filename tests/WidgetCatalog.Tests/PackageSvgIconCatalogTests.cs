using System.Text;
using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetProtocol;

internal static class PackageSvgIconCatalogTests
{
    internal static Task Run()
    {
        const string source =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\">" +
            "<style>.mark{fill:currentColor;stroke:#fff;stroke-width:1}</style>" +
            "<path class=\"mark\" d=\"M2 12 L12 2 L22 12 L12 22 Z\"/></svg>";
        var normalized = SvgIconNormalizer.Normalize(Encoding.UTF8.GetBytes(source));
        var repeated = SvgIconNormalizer.Normalize(Encoding.UTF8.GetBytes(source));
        Require(normalized.Bytes.SequenceEqual(repeated.Bytes) &&
                normalized.NormalizedSha256 == repeated.NormalizedSha256,
            "Package SVG normalization must be deterministic.");
        var text = Encoding.UTF8.GetString(normalized.Bytes);
        Require(!text.Contains("<style", StringComparison.Ordinal) &&
                !text.Contains("class=", StringComparison.Ordinal) &&
                text.Contains("fill=\"currentColor\"", StringComparison.Ordinal),
            "Supported class declarations were not flattened deterministically.");

        const string editorExport =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<svg id=\"Layer_1\" xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 236.05 225.25\">" +
            "<defs><style>.cls-1{fill:#12ab34;stroke-width:0px;}</style></defs>" +
            "<path class=\"cls-1\" d=\"m122.37,3.31C61.99.91,11.1,47.91,8.71,108.29Z\"/></svg>";
        var editorNormalized = SvgIconNormalizer.Normalize(
            Encoding.UTF8.GetBytes(editorExport));
        var editorText = Encoding.UTF8.GetString(editorNormalized.Bytes);
        Require(!editorText.Contains("id=", StringComparison.Ordinal) &&
                !editorText.Contains("class=", StringComparison.Ordinal) &&
                !editorText.Contains("<style", StringComparison.Ordinal) &&
                editorText.Contains("stroke-width=\"0px\"", StringComparison.Ordinal),
            "Inert editor-export IDs/styles were not normalized safely.");
        var compatibilityFiles = Environment.GetEnvironmentVariable(
            "WRAIL_PACKAGE_SVG_COMPATIBILITY_FILES");
        if (!string.IsNullOrWhiteSpace(compatibilityFiles))
        {
            foreach (var file in compatibilityFiles.Split(
                         Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var compatibility = SvgIconNormalizer.Normalize(File.ReadAllBytes(file));
                Require(compatibility.Bytes.Length > 0,
                    "An explicit package SVG compatibility input normalized empty.");
            }
        }

        foreach (var invalid in new[]
        {
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"><script/></svg>",
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"><foreignObject/></svg>",
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"><path d=\"M0 0\" fill=\"url(https://example.test/a)\"/></svg>",
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"><image href=\"https://example.test/a.png\"/></svg>",
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"><path d=\"M0 0\" filter=\"blur(1px)\"/></svg>",
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"><path d=\"M0 0\" transform=\"translate(Infinity)\"/></svg>",
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"garbage\"><path d=\"M0 0\"/></svg>",
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 -1 1\"><path d=\"M0 0\"/></svg>",
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"><path d=\"M0 0\" transform=\"matrix()\"/></svg>",
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"><path d=\"M0 0 X1 1\"/></svg>",
        })
            RequireThrows(() => SvgIconNormalizer.Normalize(Encoding.UTF8.GetBytes(invalid)));

        var pathBomb =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"><path d=\"" +
            string.Concat(Enumerable.Repeat("M0 0 ", SvgIconNormalizer.MaximumPathTokens)) +
            "\"/></svg>";
        RequireThrows(() => SvgIconNormalizer.Normalize(Encoding.UTF8.GetBytes(pathBomb)));
        ChangedAfterAdmissionIsRejectedBeforeAllocation(editorExport);
        MixedRuntimeAvailabilityPreservesValidSiblings(editorExport);
        AggregatePreflightRejectsBeforeReading();
        return Task.CompletedTask;
    }

    private static void AggregatePreflightRejectsBeforeReading()
    {
        const int assetCount = 9;
        const int declaredBytes = 60 * 1024;
        var assets = Enumerable.Range(0, assetCount).ToDictionary(
            index => $"test.asset-{index}",
            index => new WidgetPackageIconAsset($"assets/missing-{index}.svg"),
            StringComparer.Ordinal);
        var verified = Enumerable.Range(0, assetCount).ToDictionary(
            index => $"assets/missing-{index}.svg",
            index => new VerifiedPackageFile(
                $"assets/missing-{index}.svg", declaredBytes,
                new string('a', 64)),
            StringComparer.Ordinal);
        var manifest = new WidgetManifest
        {
            Id = "dev.test.package-icon-aggregate",
            Publisher = "dev.test",
            Name = "Aggregate package icons",
            Version = "1.0.0",
            HostApi = new("1.0", 1),
            Entrypoint = new(
                WidgetEntrypointRuntimes.DotNetWorker,
                Assembly: "payload/Widget.dll", Type: "Test.Widget"),
            IconAssets = assets,
        };
        var runtime = PackageIconAssetResolver.LoadAvailableMetadata(
            Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}"),
            manifest, verified);
        Require(runtime.Available.Count == 0 &&
                runtime.Unavailable.Count == assetCount &&
                runtime.Unavailable.Values.All(
                    code => code == "icon_assets_too_large"),
            "Aggregate overflow exposed a partial runtime icon inventory or touched absent payloads.");
        try
        {
            _ = PackageIconAssetResolver.LoadMetadata(
                Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}"),
                manifest, verified);
        }
        catch (WidgetPackageException exception)
        {
            Require(exception.Code == "icon_assets_too_large",
                "Strict aggregate preflight did not reject before file access.");
            return;
        }
        throw new InvalidOperationException(
            "Strict package icon aggregate preflight unexpectedly succeeded.");
    }

    private static void MixedRuntimeAvailabilityPreservesValidSiblings(
        string validSource)
    {
        var root = Path.Combine(
            Path.GetTempPath(), $"wrail-package-icon-mixed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        try
        {
            var validBytes = Encoding.UTF8.GetBytes(validSource);
            var invalidBytes = Encoding.UTF8.GetBytes(
                "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"><script/></svg>");
            File.WriteAllBytes(Path.Combine(root, "assets", "valid.svg"), validBytes);
            File.WriteAllBytes(Path.Combine(root, "assets", "invalid.svg"), invalidBytes);
            var verified = new Dictionary<string, VerifiedPackageFile>(StringComparer.Ordinal)
            {
                ["assets/valid.svg"] = Verified("assets/valid.svg", validBytes),
                ["assets/invalid.svg"] = Verified("assets/invalid.svg", invalidBytes),
            };
            var manifest = new WidgetManifest
            {
                Id = "dev.test.package-icon-mixed",
                Publisher = "dev.test",
                Name = "Mixed package icons",
                Version = "1.0.0",
                HostApi = new("1.0", 1),
                Entrypoint = new(
                    WidgetEntrypointRuntimes.DotNetWorker,
                    Assembly: "payload/Widget.dll", Type: "Test.Widget"),
                IconAssets = new Dictionary<string, WidgetPackageIconAsset>(StringComparer.Ordinal)
                {
                    ["test.valid"] = new("assets/valid.svg"),
                    ["test.unavailable"] = new("assets/invalid.svg"),
                },
            };
            var runtime = PackageIconAssetResolver.LoadAvailableMetadata(
                root, manifest, verified);
            Require(runtime.Available.Keys.SequenceEqual(["test.valid"]) &&
                    runtime.Unavailable.TryGetValue(
                        "test.unavailable", out var failure) &&
                    failure == "svg_element",
                "Runtime icon admission did not isolate one malformed asset from its valid sibling.");
            RequireThrows(() => PackageIconAssetResolver.LoadMetadata(
                root, manifest, verified));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static VerifiedPackageFile Verified(string path, byte[] bytes) => new(
            path,
            bytes.Length,
            Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant());
    }

    private static void ChangedAfterAdmissionIsRejectedBeforeAllocation(string source)
    {
        var root = Path.Combine(
            Path.GetTempPath(), $"wrail-package-icon-bounded-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        try
        {
            var relative = "assets/mark.svg";
            var path = Path.Combine(root, "assets", "mark.svg");
            var bytes = Encoding.UTF8.GetBytes(source);
            File.WriteAllBytes(path, bytes);
            var hash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            var verified = new Dictionary<string, VerifiedPackageFile>(StringComparer.Ordinal)
            {
                [relative] = new(relative, bytes.Length, hash),
            };
            var manifest = new WidgetManifest
            {
                Id = "dev.test.package-icon-bounded",
                Publisher = "dev.test",
                Name = "Package icon bounded",
                Version = "1.0.0",
                HostApi = new("1.0", 1),
                Entrypoint = new(
                    WidgetEntrypointRuntimes.DotNetWorker,
                    Assembly: "payload/Widget.dll", Type: "Test.Widget"),
                IconAssets = new Dictionary<string, WidgetPackageIconAsset>(StringComparer.Ordinal)
                {
                    ["test.mark"] = new(relative),
                },
            };
            var metadata = PackageIconAssetResolver.LoadMetadata(
                root, manifest, verified)["test.mark"];
            File.WriteAllBytes(path,
                new byte[ProtocolConstants.MaximumPackageIconBytes + 1]);
            RequireThrows(() => PackageIconAssetResolver.Resolve(
                root, metadata, verified));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireThrows(Action action)
    {
        try { action(); }
        catch (WidgetPackageException) { return; }
        throw new InvalidOperationException("Expected bounded package SVG rejection.");
    }
}
