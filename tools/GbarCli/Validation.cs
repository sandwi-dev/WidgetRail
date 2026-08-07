using System.Text.Json;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetStyling;

namespace GameBarAlternative.GbarCli;

public sealed record ToolDiagnostic(
    string File,
    int Line,
    int Column,
    string Code,
    string Message,
    GbssDiagnosticSeverity Severity = GbssDiagnosticSeverity.Error)
{
    public override string ToString() => $"{File}({Line},{Column}): {Severity.ToString().ToLowerInvariant()} {Code}: {Message}";
}

internal static class ValidateCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        var parsed = new CommandArguments(args);
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException("Usage: gbar validate <widget-directory|manifest.json|style.gbss>");

        var target = Path.GetFullPath(parsed.Positionals[0]);
        if (!File.Exists(target) && !Directory.Exists(target))
            throw new CliUsageException($"Path does not exist: {target}");

        var files = Directory.Exists(target)
            ? Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(path).Equals("manifest.json", StringComparison.OrdinalIgnoreCase) ||
                               Path.GetExtension(path).Equals(".gbss", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [target];

        if (files.Length == 0)
            throw new CliUsageException("No manifest.json or .gbss files were found.");

        var diagnostics = new List<ToolDiagnostic>();
        foreach (var file in files)
        {
            if (Path.GetExtension(file).Equals(".gbss", StringComparison.OrdinalIgnoreCase))
                diagnostics.AddRange(GbssValidator.Validate(file, await File.ReadAllTextAsync(file)));
            else if (Path.GetFileName(file).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                diagnostics.AddRange(await ValidateManifestAsync(file));
            else
                throw new CliUsageException("Validation supports manifest.json and .gbss files.");
        }

        foreach (var warning in diagnostics.Where(item => item.Severity == GbssDiagnosticSeverity.Warning))
            await error.WriteLineAsync(warning.ToString());

        var errors = diagnostics.Where(item => item.Severity == GbssDiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0)
        {
            foreach (var diagnostic in errors) await error.WriteLineAsync(diagnostic.ToString());
            await error.WriteLineAsync($"Validation failed with {errors.Length} error(s).");
            return 1;
        }

        await output.WriteLineAsync($"Valid: {files.Length} file(s) checked.");
        return 0;
    }

    private static async Task<IReadOnlyList<ToolDiagnostic>> ValidateManifestAsync(string path)
    {
        try
        {
            var manifest = ManifestJson.Deserialize(await File.ReadAllBytesAsync(path));
            return WidgetManifestValidator.Validate(manifest)
                .Select(issue => new ToolDiagnostic(path, 1, 1, issue.Code, $"{issue.Path}: {issue.Message}"))
                .ToArray();
        }
        catch (JsonException exception)
        {
            return [new ToolDiagnostic(path, (int)(exception.LineNumber ?? 0) + 1, (int)(exception.BytePositionInLine ?? 0) + 1, "invalid_json", exception.Message)];
        }
    }
}

/// <summary>Compatibility adapter for callers of the original CLI-local validator.</summary>
public static class GbssValidator
{
    public static IReadOnlyList<ToolDiagnostic> Validate(string file, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentNullException.ThrowIfNull(source);
        var fullFile = Path.GetFullPath(file);
        var root = Path.GetDirectoryName(fullFile)!;
        var entry = Path.GetFileName(fullFile);
        var package = GbssPackageLoader.Load(entry, new EntrySourceProvider(root, entry, source));
        var compiled = GbssThemeCompiler.Compile(package);
        return compiled.Diagnostics
            .Select(item => new ToolDiagnostic(
                ResolveSource(root, item.Source), item.Line, item.Column, item.Code, item.Message, item.Severity))
            .ToArray();
    }

    private static string ResolveSource(string root, string source) =>
        source.StartsWith('<')
            ? source
            : Path.GetFullPath(Path.Combine(root, source.Replace('/', Path.DirectorySeparatorChar)));

    private sealed class EntrySourceProvider(string root, string entry, string source) : IGbssSourceProvider
    {
        private readonly GbssFileSourceProvider _files = new(root);

        public bool TryRead(string packageRelativePath, out string content)
        {
            if (string.Equals(packageRelativePath, entry, StringComparison.Ordinal))
            {
                content = source;
                return true;
            }
            return _files.TryRead(packageRelativePath, out content);
        }
    }
}
