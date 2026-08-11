namespace GameBarAlternative.GbarCli;

using GameBarAlternative.WidgetCatalog;

public static class CliApplication
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
        => await RunAsync(args, output, error, remoteHttpHandler: null, CancellationToken.None);

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? remoteHttpHandler,
        CancellationToken cancellationToken = default)
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
                "new" => await NewCommand.RunAsync(args[1..], output, cancellationToken),
                "validate" => await ValidateCommand.RunAsync(args[1..], output, error),
                "dev" => await DevCommand.RunAsync(args[1..], output, error, cancellationToken),
                "preview" => await ScenarioPreviewCommand.RunAsync(args[1..], output, cancellationToken),
                "render" => await RenderCommand.RunAsync(args[1..], output),
                "replay" => await ReplayCommand.RunAsync(args[1..], output),
                "pack" => await PackCommand.RunAsync(
                    args[1..], output, error, cancellationToken),
                "install" => await InstallCommand.RunAsync(args[1..], output, remoteHttpHandler, cancellationToken),
                "uninstall" => await UninstallCommand.RunAsync(args[1..], output, cancellationToken),
                "repair" => await RepairCommand.RunAsync(args[1..], output, cancellationToken),
                "authority-recovery" => await AuthorityRecoveryCommand.RunAsync(
                    args[1..], output, cancellationToken),
                "list" => await ListCommand.RunAsync(args[1..], output),
                "enable" => await EnabledCommand.RunAsync(args[1..], output, enabled: true),
                "disable" => await EnabledCommand.RunAsync(args[1..], output, enabled: false),
                "version" => await VersionCommand.RunAsync(args[1..], output, cancellationToken),
                "config" => await ConfigCommand.RunAsync(args[1..], output, cancellationToken),
                "theme" => await ThemeCommand.RunAsync(args[1..], output, remoteHttpHandler, cancellationToken),
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
        catch (ThemePackageException exception)
        {
            await error.WriteLineAsync($"error {exception.Code}: {exception.Message}");
            return 1;
        }
        catch (PlatformSettings.PlatformSettingsException exception)
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await error.WriteLineAsync("error: operation cancelled.");
            return 130;
        }
    }

    public const string HelpText = """
        gbar - controller widget development tools

        Usage:
          gbar new widget <Name> [--output <directory>] [--id <reverse.dns.id>] [--publisher <reverse.dns.id>]
          gbar validate <widget-directory|manifest.json|style.gbss>
          gbar dev <widget-directory|widget.csproj|file.gbarwidget> [--host <OverlayHost.exe>] [--configuration <name>] [--build-timeout-seconds <10-600>] [--debounce-ms <50-2000>]
          gbar preview <widget-directory|gbar.scenarios.json> [--scenario <name>] [--output <snapshot.json>] [--instance <id>]
          gbar render <snapshot.json> [--output <canonical-snapshot.json>]
          gbar replay <snapshot.json> <input-replay.json>
          gbar pack <widget-directory|widget.csproj> [--output <file.gbarwidget>] [--configuration <name>] [--build-timeout-seconds <10-600>]
          gbar install <file.gbarwidget|https-url|github:owner/repository@tag/asset.gbarwidget> [--sha256 <64-hex>] [--catalog <root>]
          gbar uninstall <widget-id> [--catalog <root>]
          gbar repair list [--catalog <root>]
          gbar repair remove <widget-id> <version> [--catalog <root>]
          gbar authority-recovery list
          gbar authority-recovery retry <confirmation-token>
          gbar list [--catalog <root>]
          gbar enable <widget-id> [--catalog <root>]
          gbar disable <widget-id> [--catalog <root>]
          gbar version list <widget-id> [--catalog <root>]
          gbar version select <widget-id> <version> [--catalog <root>]
          gbar version rollback <widget-id> [--to <version>] [--catalog <root>]
          gbar config set <widget-id> <key> <value> --publisher <publisher-id> [--settings-root <root>]
          gbar config get <widget-id> <key> --publisher <publisher-id> [--settings-root <root>]
          gbar config list <widget-id> --publisher <publisher-id> [--settings-root <root>]
          gbar config remove <widget-id> <key> --publisher <publisher-id> [--settings-root <root>]
          gbar config clear <widget-id> --publisher <publisher-id> [--settings-root <root>]
          gbar theme new <Name> [--output <directory>] [--id <id>] [--publisher <id>] [--version <version>]
          gbar theme validate <theme-directory|file.gbartheme>
          gbar theme pack <theme-directory> [--output <file.gbartheme>]
          gbar theme inspect <file.gbartheme>
          gbar theme preview <theme-directory|file.gbartheme>
          gbar theme install <file.gbartheme|https-url|github:owner/repository@tag/asset.gbartheme> [--sha256 <64-hex>] [--settings-root <root>]
          gbar theme list [--settings-root <root>]

        render is data-only and never loads widget assemblies. Use gbar dev for
        isolated AppContainer execution of author code.

        repair manages quarantined installed-catalog generations. authority-recovery
        retries a separate host AppContainer DACL transaction by its fresh exact token;
        it has no force-clear, journal-path, content-path, SID, or ACL override.

        Exit codes: 0 success, 1 validation/runtime failure, 2 command usage error, 130 cancelled.
        """;
}

public sealed class CliUsageException(string message) : Exception(message);
public sealed class CliOperationException(string message, Exception? innerException = null) : Exception(message, innerException);
