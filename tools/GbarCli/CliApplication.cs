namespace GameBarAlternative.GbarCli;

using GameBarAlternative.WidgetCatalog;

public static class CliApplication
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            await output.WriteLineAsync(HelpText);
            return 0;
        }

        try
        {
            return args[0] switch
            {
                "new" => await NewCommand.RunAsync(args[1..], output),
                "validate" => await ValidateCommand.RunAsync(args[1..], output, error),
                "render" => await RenderCommand.RunAsync(args[1..], output),
                "replay" => await ReplayCommand.RunAsync(args[1..], output),
                "pack" => await PackCommand.RunAsync(args[1..], output),
                "install" => await InstallCommand.RunAsync(args[1..], output),
                "list" => await ListCommand.RunAsync(args[1..], output),
                "enable" => await EnabledCommand.RunAsync(args[1..], output, enabled: true),
                "disable" => await EnabledCommand.RunAsync(args[1..], output, enabled: false),
                _ => throw new CliUsageException($"Unknown command '{args[0]}'. Run 'gbar help'."),
            };
        }
        catch (CliUsageException exception)
        {
            await error.WriteLineAsync($"error: {exception.Message}");
            return 2;
        }
        catch (WidgetPackageException exception)
        {
            await error.WriteLineAsync($"error {exception.Code}: {exception.Message}");
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await error.WriteLineAsync($"error: {exception.Message}");
            return 1;
        }
        catch (CliOperationException exception)
        {
            await error.WriteLineAsync($"error: {exception.Message}");
            return 1;
        }
    }

    public const string HelpText = """
        gbar - controller widget development tools

        Usage:
          gbar new widget <Name> [--output <directory>] [--id <reverse.dns.id>] [--publisher <reverse.dns.id>]
          gbar validate <widget-directory|manifest.json|style.gbss>
          gbar render <snapshot.json>
          gbar render <widget.dll> --type <Namespace.Widget> [--output <snapshot.json>] [--instance <id>]
          gbar replay <snapshot.json> <input-replay.json>
          gbar pack <widget-directory> [--output <file.gbarwidget>]
          gbar install <file.gbarwidget> [--catalog <root>]
          gbar list [--catalog <root>]
          gbar enable <widget-id> [--catalog <root>]
          gbar disable <widget-id> [--catalog <root>]

        Exit codes: 0 success, 1 validation/runtime failure, 2 command usage error.
        """;
}

public sealed class CliUsageException(string message) : Exception(message);
public sealed class CliOperationException(string message, Exception? innerException = null) : Exception(message, innerException);
