namespace WidgetRail.WrailCli;

using WidgetRail.WidgetCatalog;

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

        if (args.Length != 0 && args[0] == "__scenario-worker")
            return await ScenarioPreviewWorkerCommand.RunAsync(args[1..], cancellationToken)
                .ConfigureAwait(false);

        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            await output.WriteLineAsync(HelpText);
            return 0;
        }

        try
        {
            return args[0] switch
            {
                "doctor" => await DoctorCommand.RunAsync(args[1..], output, cancellationToken),
                "inspect" => await InspectCommand.RunAsync(args[1..], output, cancellationToken),
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
                _ => throw new CliUsageException($"Unknown command '{args[0]}'. Run 'wrail help'."),
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
        wrail - controller widget development tools

        Usage:
          wrail doctor [project-directory|widget.csproj] [--host <OverlayHost.exe>] [--json]
          wrail inspect <file.wrwidget> [--json]
          wrail new widget <Name> [--output <directory>] [--id <reverse.dns.id>] [--publisher <reverse.dns.id>] [--template <basic|data|media|embedded-media|multipage>]
          wrail validate <widget-directory|manifest.json|style.wrss>
          wrail dev <widget-directory|widget.csproj|file.wrwidget> [--host <OverlayHost.exe>] [--configuration <name>] [--build-timeout-seconds <10-600>] [--debounce-ms <50-2000>] [--log <new-file>]
          wrail preview <widget-directory|widgetrail.scenarios.json> [--scenario <name>] [--pinned-layout <id|@all>] [--output <snapshot.json>] [--instance <id>]
          wrail render <snapshot.json> [--output <canonical-snapshot.json>]
          wrail replay <snapshot.json> <input-replay.json>
          wrail pack <widget-directory|widget.csproj> [--output <file.wrwidget>] [--configuration <name>] [--build-timeout-seconds <10-600>]
          wrail install <file.wrwidget|https-url|github:owner/repository@tag/asset.wrwidget> [--sha256 <64-hex>] [--catalog <root>] [--accept-full-trust]
          wrail uninstall <widget-id> [--catalog <root>]
          wrail repair list [--catalog <root>]
          wrail repair remove <widget-id> <version> [--catalog <root>]
          wrail authority-recovery list
          wrail authority-recovery retry <confirmation-token>
          wrail list [--catalog <root>]
          wrail enable <widget-id> [--catalog <root>] [--accept-full-trust]
          wrail disable <widget-id> [--catalog <root>]
          wrail version list <widget-id> [--catalog <root>]
          wrail version select <widget-id> <version> [--catalog <root>]
          wrail version rollback <widget-id> [--to <version>] [--catalog <root>]
          wrail config set <widget-id> <key> <value> --publisher <publisher-id> [--settings-root <root>]
          wrail config get <widget-id> <key> --publisher <publisher-id> [--settings-root <root>]
          wrail config list <widget-id> --publisher <publisher-id> [--settings-root <root>]
          wrail config remove <widget-id> <key> --publisher <publisher-id> [--settings-root <root>]
          wrail config clear <widget-id> --publisher <publisher-id> [--settings-root <root>]
          wrail theme new <Name> [--output <directory>] [--id <id>] [--publisher <id>] [--version <version>]
          wrail theme validate <theme-directory|file.wrtheme>
          wrail theme pack <theme-directory> [--output <file.wrtheme>]
          wrail theme inspect <file.wrtheme>
          wrail theme preview <theme-directory|file.wrtheme>
          wrail theme install <file.wrtheme|https-url|github:owner/repository@tag/asset.wrtheme> [--sha256 <64-hex>] [--settings-root <root>]
          wrail theme list [--settings-root <root>]
          wrail theme remove <exact-id> <exact-version> [--settings-root <root>]
        render is data-only and never loads widget assemblies. Use wrail dev for
        isolated AppContainer execution of author code.

        repair manages quarantined installed-catalog generations. authority-recovery
        retries a separate host AppContainer DACL transaction by its fresh exact token;
        it has no force-clear, journal-path, content-path, SID, or ACL override.

        Exit codes: 0 success, 1 validation/runtime failure, 2 command usage error, 130 cancelled.
        """;
}

public sealed class CliUsageException(string message) : Exception(message);
public sealed class CliOperationException(string message, Exception? innerException = null) : Exception(message, innerException);
