namespace WidgetRail.WrailCli;

using WidgetRail.WidgetCatalog;
using CatalogService = WidgetRail.WidgetCatalog.WidgetCatalog;

internal static class VersionCommand
{
    private const string HelpText = """
        Usage:
          wrail version list <widget-id> [--catalog <root>]
          wrail version select <widget-id> <version> [--catalog <root>]
          wrail version rollback <widget-id> [--to <version>] [--catalog <root>]

        Version selection requires a disabled widget. Selecting a version does
        not enable it; review the package, then run wrail enable explicitly.
        """;

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            await output.WriteLineAsync(HelpText);
            return 0;
        }

        return args[0] switch
        {
            "list" => await ListAsync(args[1..], output, cancellationToken),
            "select" => await SelectAsync(args[1..], output, cancellationToken),
            "rollback" => await RollbackAsync(args[1..], output, cancellationToken),
            _ => throw new CliUsageException($"Unknown version command '{args[0]}'. Run 'wrail version help'."),
        };
    }

    private static async Task<int> ListAsync(
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var parsed = new CommandArguments(args, "--catalog");
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException("Usage: wrail version list <widget-id> [--catalog <root>]");

        var widget = await GetWidgetAsync(parsed.Positionals[0], parsed.Option("--catalog"), cancellationToken);
        await output.WriteLineAsync(
            $"{widget.Id} is {(widget.Enabled ? "enabled" : "disabled")}; active version {widget.ActiveVersion.Version}.");
        foreach (var installed in widget.Versions)
        {
            var marker = installed.Version == widget.ActiveVersion.Version ? "active" : "     ";
            await output.WriteLineAsync($"{marker}  {installed.Version}  {installed.InstallPath}");
        }
        return 0;
    }

    private static async Task<int> SelectAsync(
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var parsed = new CommandArguments(args, "--catalog");
        if (parsed.Positionals.Count != 2)
            throw new CliUsageException(
                "Usage: wrail version select <widget-id> <version> [--catalog <root>]");

        var requested = ParseCanonicalVersion(parsed.Positionals[1]);
        var catalog = new CatalogService(CatalogPath.Resolve(parsed.Option("--catalog")));
        try
        {
            await catalog.SetActiveVersionAsync(parsed.Positionals[0], requested, cancellationToken);
        }
        catch (KeyNotFoundException exception)
        {
            throw new CliOperationException(exception.Message, exception);
        }
        await output.WriteLineAsync(
            $"Selected {parsed.Positionals[0]} {requested} (disabled). Review it, then run wrail enable when ready.");
        return 0;
    }

    private static async Task<int> RollbackAsync(
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var parsed = new CommandArguments(args, "--catalog", "--to");
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException(
                "Usage: wrail version rollback <widget-id> [--to <version>] [--catalog <root>]");

        var widgetId = parsed.Positionals[0];
        var catalogRoot = CatalogPath.Resolve(parsed.Option("--catalog"));
        var requested = parsed.Option("--to");
        var target = requested is null ? null : ParseCanonicalVersion(requested);
        WidgetVersionChange change;
        try
        {
            change = await new CatalogService(catalogRoot)
                .RollbackAsync(widgetId, target, cancellationToken);
        }
        catch (KeyNotFoundException exception)
        {
            throw new CliOperationException(exception.Message, exception);
        }
        catch (WidgetPackageException exception) when (exception.Code == "invalid_rollback")
        {
            throw new CliUsageException(exception.Message);
        }
        catch (WidgetPackageException exception) when (exception.Code == "no_rollback_version")
        {
            throw new CliOperationException(exception.Message, exception);
        }
        await output.WriteLineAsync(
            $"Rolled back {widgetId} from {change.PreviousVersion} to {change.SelectedVersion} (disabled). " +
            "Review it, then run wrail enable when ready.");
        return 0;
    }

    private static async Task<CatalogWidget> GetWidgetAsync(
        string widgetId,
        string? catalogPath,
        CancellationToken cancellationToken,
        bool pathIsResolved = false)
    {
        var root = pathIsResolved ? catalogPath! : CatalogPath.Resolve(catalogPath);
        var snapshot = await new CatalogService(root).DiscoverAsync(cancellationToken);
        return snapshot.Widgets.SingleOrDefault(widget => widget.Id == widgetId)
            ?? throw new CliOperationException($"Widget '{widgetId}' is not installed.");
    }

    private static Version ParseCanonicalVersion(string value)
    {
        if (!Version.TryParse(value, out var version) ||
            !string.Equals(version.ToString(), value, StringComparison.Ordinal))
            throw new CliUsageException("Widget version must use canonical dotted numeric notation.");
        return version;
    }
}
