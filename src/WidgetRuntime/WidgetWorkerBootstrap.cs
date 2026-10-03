using System.Globalization;
using System.Collections.Frozen;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetRuntime;

/// <summary>
/// Closed process-exit vocabulary for the production generic package loader.
/// The host opts into interpreting these values only for WidgetWorkerHost;
/// arbitrary custom workers retain ordinary opaque process exit codes.
/// </summary>
public static class WidgetWorkerStartupDiagnostics
{
    private static readonly IReadOnlyDictionary<string, int> ExitCodesByDiagnostic =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["missing_entrypoint"] = 64,
            ["invalid_assembly"] = 65,
            ["missing_type"] = 66,
            ["invalid_type"] = 67,
            ["invalid_constructor"] = 68,
            ["constructor_failed"] = 69,
            ["path_escape"] = 70,
            ["reparse_point"] = 71,
        };

    /// <summary>Trusted host map used only for the production generic loader.</summary>
    public static IReadOnlyDictionary<int, string> LoaderExitCodes { get; } =
        ExitCodesByDiagnostic.ToFrozenDictionary(pair => pair.Value, pair => pair.Key);

    public static int ExitCodeFor(string diagnosticCode) =>
        ExitCodesByDiagnostic.TryGetValue(diagnosticCode, out var exitCode)
            ? exitCode
            : 2;
}

/// <summary>
/// Safe entry point for a native .NET widget worker. It owns host argument
/// parsing, authenticated capability-channel setup, service attachment,
/// cancellation, transport disposal, and process-safe error reporting.
/// </summary>
public static class WidgetWorkerBootstrap
{
    /// <summary>
    /// Runs a worker whose widget uses the protected <c>HostServices</c>
    /// property. This is the normal entrypoint for a custom worker executable.
    /// </summary>
    public static Task<int> RunAsync(
        string[] args,
        Func<Widget> widgetFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(widgetFactory);
        return RunAsync(args, _ => widgetFactory(), cancellationToken);
    }

    /// <summary>
    /// Runs one widget worker until its host asks it to stop. The factory is
    /// invoked only after any host-provided capability channel is authenticated.
    /// Its services are attached to the returned widget before lifecycle creation.
    /// </summary>
    /// <remarks>
    /// Widget implementations normally use their protected <c>HostServices</c>
    /// property and can ignore the factory parameter. It is supplied for widgets
    /// that prefer explicit constructor injection in their own code.
    /// </remarks>
    public static async Task<int> RunAsync(
        string[] args,
        Func<WidgetHostServices, Widget> widgetFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(widgetFactory);

        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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
            var launch = WidgetWorkerLaunchArguments.Parse(args);
            startupPhase = "capability";
            await using var capabilityConnection = await WorkerCapabilityConnection
                .ConnectAsync(launch.Broker, shutdown.Token).ConfigureAwait(false);
            startupPhase = "factory";
            var services = new WidgetHostServices(capabilityConnection.Client);
            var widget = widgetFactory(services)
                ?? throw new WidgetWorkerBootstrapException(
                    "invalid_widget", "The widget factory returned no widget.");
            startupPhase = "session";
            await new WidgetWorkerServer(
                    widget,
                    launch.WidgetInstanceId,
                    launch.WidgetPipeName,
                    launch.MaximumMessageBytes,
                    capabilityConnection.Client,
                    launch.SessionNonce,
                    diagnostics)
                .RunAsync(shutdown.Token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            return 0;
        }
        catch (WidgetWorkerBootstrapException exception)
        {
            diagnostics?.RecordRuntimeFailure($"startup-{startupPhase}", exception.Code);
            Console.Error.WriteLine($"Widget worker failed ({exception.Code}): {exception.SafeMessage}");
            return exception.ExitCode;
        }
        catch (ArgumentException)
        {
            diagnostics?.RecordRuntimeFailure($"startup-{startupPhase}", "invalid_arguments");
            Console.Error.WriteLine("Widget worker failed (invalid_arguments): Host launch arguments are invalid.");
            return 1;
        }
        catch (Exception exception)
        {
            diagnostics?.RecordStartupFailure(startupPhase, exception);
            // Never print exception messages, paths, transport credentials, or
            // stack traces across this process boundary.
            Console.Error.WriteLine("Widget worker failed: The worker could not be started.");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }
}

/// <summary>
/// A bounded, explicitly safe startup diagnostic for a custom worker factory.
/// Use only static messages that contain no paths, credentials, or user data.
/// </summary>
public sealed class WidgetWorkerBootstrapException : Exception
{
    public WidgetWorkerBootstrapException(
        string code,
        string safeMessage,
        int exitCode = 2,
        Exception? innerException = null)
        : base("Widget worker startup failed.", innerException)
    {
        Code = ValidateToken(code, nameof(code), 64);
        SafeMessage = ValidateMessage(safeMessage);
        ExitCode = exitCode is >= 1 and <= 255
            ? exitCode
            : throw new ArgumentOutOfRangeException(nameof(exitCode));
    }

    public string Code { get; }
    public string SafeMessage { get; }
    public int ExitCode { get; }

    private static string ValidateToken(string value, string parameterName, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumLength ||
            !value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
            throw new ArgumentException("The diagnostic code is invalid.", parameterName);
        return value;
    }

    private static string ValidateMessage(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 256 || value.Any(char.IsControl))
            throw new ArgumentException("The safe diagnostic message is invalid.", nameof(value));
        return value;
    }
}
