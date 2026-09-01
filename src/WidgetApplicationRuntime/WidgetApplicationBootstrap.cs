using System.Globalization;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetRuntime;

/// <summary>
/// Narrow worker-side bootstrap for an explicitly approved full-trust Community
/// application. It exposes only the generic overlay protocol and never creates
/// or connects to the product's sandbox capability broker.
/// </summary>
public static class WidgetApplicationBootstrap
{
    public static async Task<int> RunAsync(
        string[] args,
        Func<Widget> widgetFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(widgetFactory);
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        var diagnostics = WidgetWorkerDiagnosticLog.TryCreate(args);
        var startupPhase = "arguments";
        try
        {
            var launch = ApplicationLaunchArguments.Parse(args);
            startupPhase = "factory";
            var widget = widgetFactory() ?? throw new InvalidOperationException(
                "The widget factory returned no widget.");
            startupPhase = "session";
            await new WidgetWorkerServer(
                    widget,
                    launch.WidgetInstanceId,
                    launch.WidgetPipeName,
                    launch.MaximumMessageBytes,
                    sessionNonce: launch.SessionNonce,
                    diagnostics: diagnostics)
                .RunAsync(shutdown.Token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            return 0;
        }
        catch (ArgumentException)
        {
            diagnostics?.RecordRuntimeFailure($"startup-{startupPhase}", "invalid_arguments");
            Console.Error.WriteLine(
                "Community application failed (invalid_arguments): Host launch arguments are invalid.");
            return 1;
        }
        catch (Exception exception)
        {
            diagnostics?.RecordStartupFailure(startupPhase, exception);
            Console.Error.WriteLine(
                "Community application failed: The overlay session could not be started.");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private sealed record ApplicationLaunchArguments(
        string WidgetPipeName,
        string WidgetInstanceId,
        string SessionNonce,
        int MaximumMessageBytes)
    {
        internal static ApplicationLaunchArguments Parse(string[] args)
        {
            var pipe = Required(args, "--widget-pipe", 200);
            var instance = Required(args, "--widget-instance", 128);
            var nonce = Required(args, "--widget-session-nonce", 64);
            if (!int.TryParse(
                    Required(args, "--max-message-bytes", 16),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var maximumBytes) ||
                maximumBytes is < 256 or >
                    WidgetRuntimeProtocol.AbsoluteMaximumMessageBytes)
                throw new ArgumentException("The maximum message size is invalid.");
            if (instance.Length == 0 || !instance.All(character =>
                    char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
                throw new ArgumentException("The widget instance is invalid.");
            if (nonce.Length != 64 || !nonce.All(char.IsAsciiHexDigit))
                throw new ArgumentException("The session nonce is invalid.");
            const string localPrefix = "LOCAL\\";
            var pipeSuffix = pipe.StartsWith(localPrefix, StringComparison.Ordinal)
                ? pipe[localPrefix.Length..]
                : pipe;
            if (pipeSuffix.Length == 0 || pipeSuffix.Contains('\\') ||
                pipe.Any(char.IsControl))
                throw new ArgumentException("The widget pipe is invalid.");
            return new(pipe, instance, nonce, maximumBytes);
        }

        private static string Required(
            IReadOnlyList<string> args,
            string name,
            int maximumLength)
        {
            var index = -1;
            for (var candidate = 0; candidate < args.Count; candidate++)
                if (string.Equals(args[candidate], name, StringComparison.Ordinal))
                {
                    if (index >= 0) throw new ArgumentException("A launch argument is duplicated.");
                    index = candidate;
                }
            if (index < 0 || index + 1 >= args.Count ||
                args[index + 1].Length == 0 || args[index + 1].Length > maximumLength)
                throw new ArgumentException("A required launch argument is missing.");
            return args[index + 1];
        }
    }
}
