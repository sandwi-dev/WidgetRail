using System.Text.RegularExpressions;

namespace GameBarAlternative.GbarCli;

internal static partial class NewCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        var parsed = new CommandArguments(args, "--output", "--id", "--publisher");
        if (parsed.Positionals.Count != 2 || parsed.Positionals[0] != "widget")
            throw new CliUsageException(
                "Usage: gbar new widget <Name> [--output <directory>] [--id <id>] " +
                "[--publisher <id>]");

        var name = parsed.Positionals[1];
        if (!TypeNameRegex().IsMatch(name))
            throw new CliUsageException("Widget name must be a valid PascalCase C# type name.");

        var publisher = parsed.Option("--publisher") ?? "dev.example";
        var id = parsed.Option("--id") ?? $"{publisher}.{ToKebabCase(name)}";
        if (!PublisherRegex().IsMatch(publisher))
            throw new CliUsageException("Publisher must be a lowercase reverse-DNS identifier that is also a valid C# namespace.");
        if (!WidgetIdRegex().IsMatch(id))
            throw new CliUsageException("Widget ID must be a lowercase reverse-DNS identifier.");
        var target = Path.GetFullPath(parsed.Option("--output") ?? Path.Combine(Environment.CurrentDirectory, name));
        if (File.Exists(target) || Directory.Exists(target))
            throw new CliUsageException(
                $"Output path already exists: {target}. Choose a new path; gbar never overwrites or deletes an existing destination.");

        var templateRoot = TemplateLocator.Find();
        var sdkPackage = LocalWidgetSdkPackage.Create();
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{{WidgetName}}"] = name,
            ["{{WidgetId}}"] = id,
            ["{{Publisher}}"] = publisher,
            ["{{SdkPackageId}}"] = sdkPackage.PackageId,
            ["{{SdkVersion}}"] = sdkPackage.Version,
        };

        var created = await ControllerWidgetScaffolder.GenerateAsync(
            templateRoot,
            target,
            replacements,
            sdkPackage,
            cancellationToken).ConfigureAwait(false);

        await output.WriteLineAsync($"Created {name} in {target}");
        await output.WriteLineAsync($"  ID: {id}");
        await output.WriteLineAsync(
            $"  SDK: GameBarAlternative.WidgetSdk {sdkPackage.Version} (local offline feed)");
        await output.WriteLineAsync($"  Files: {created}");
        await output.WriteLineAsync($"Next: dotnet build \"{Path.Combine(target, name + ".csproj")}\"");
        return 0;
    }

    private static string ToKebabCase(string value) =>
        string.Concat(value.Select((ch, index) => char.IsUpper(ch) && index > 0 ? $"-{char.ToLowerInvariant(ch)}" : char.ToLowerInvariant(ch).ToString()));

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex TypeNameRegex();

    [GeneratedRegex("^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex PublisherRegex();

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*(\\.[a-z0-9][a-z0-9_-]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex WidgetIdRegex();
}

internal static class TemplateLocator
{
    public static string Find()
    {
        var configured = Environment.GetEnvironmentVariable("GBAR_TEMPLATE_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var direct = Path.Combine(directory.FullName, "templates", "ControllerWidget");
                if (File.Exists(Path.Combine(direct, "template.json"))) return direct;
                directory = directory.Parent;
            }
        }
        throw new CliUsageException("ControllerWidget template was not found. Set GBAR_TEMPLATE_ROOT to its directory.");
    }
}
