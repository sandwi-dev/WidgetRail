using System.Globalization;
using System.Collections.Frozen;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.WidgetRuntime;

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
        try
        {
            var launch = WidgetWorkerLaunchArguments.Parse(args);
            await using var capabilityConnection = await WorkerCapabilityConnection
                .ConnectAsync(launch.Broker, shutdown.Token).ConfigureAwait(false);
            var services = new WidgetHostServices(capabilityConnection.Client);
            var widget = widgetFactory(services)
                ?? throw new WidgetWorkerBootstrapException(
                    "invalid_widget", "The widget factory returned no widget.");
            await new WidgetWorkerServer(
                    widget,
                    launch.WidgetInstanceId,
                    launch.WidgetPipeName,
                    launch.MaximumMessageBytes,
                    capabilityConnection.Client,
                    launch.SessionNonce)
                .RunAsync(shutdown.Token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            return 0;
        }
        catch (WidgetWorkerBootstrapException exception)
        {
            Console.Error.WriteLine($"Widget worker failed ({exception.Code}): {exception.SafeMessage}");
            return exception.ExitCode;
        }
        catch (ArgumentException)
        {
            Console.Error.WriteLine("Widget worker failed (invalid_arguments): Host launch arguments are invalid.");
            return 1;
        }
        catch (Exception)
        {
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

internal sealed record WidgetWorkerLaunchArguments(
    string WidgetPipeName,
    string WidgetInstanceId,
    string SessionNonce,
    int MaximumMessageBytes,
    WorkerBrokerConnection? Broker)
{
    internal static WidgetWorkerLaunchArguments Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var pipe = RequiredValue(args, "--widget-pipe", 200);
        var instance = RequiredValue(args, "--widget-instance", 128);
        var sessionNonce = RequiredValue(args, "--widget-session-nonce", 64);
        if (!int.TryParse(
                RequiredValue(args, "--max-message-bytes", 16),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var maximumBytes) ||
            maximumBytes is < 256 or > WidgetRuntimeProtocol.AbsoluteMaximumMessageBytes)
            throw new ArgumentException("The maximum message size is invalid.");
        ValidatePipeName(pipe, "Widget");
        if (sessionNonce.Length != 64 || !sessionNonce.All(char.IsAsciiHexDigit))
            throw new ArgumentException("The session nonce is invalid.");
        return new(pipe, ValidateInstanceId(instance), sessionNonce, maximumBytes,
            ParseBroker(args, instance));
    }

    private static WorkerBrokerConnection? ParseBroker(string[] args, string widgetInstanceId)
    {
        var pipe = OptionalValue(args, "--broker-pipe", 200);
        var package = OptionalValue(args, "--broker-package", 256);
        var publisher = OptionalValue(args, "--broker-publisher", 256);
        var instance = OptionalValue(args, "--broker-instance", 128);
        var nonce = OptionalValue(args, "--broker-nonce", 128);
        var supplied = new[] { pipe, package, publisher, instance, nonce }
            .Count(value => value is not null);
        if (supplied == 0) return null;
        if (supplied != 5)
            throw new ArgumentException("Broker connection arguments must be supplied together.");
        if (!string.Equals(instance, widgetInstanceId, StringComparison.Ordinal))
            throw new ArgumentException("Broker identity does not match the widget instance.");
        if (nonce!.Length is < 32 or > 128 ||
            !nonce.All(character => char.IsAsciiLetterOrDigit(character)))
            throw new ArgumentException("Broker connection nonce is invalid.");

        var identity = new BrokerWidgetIdentity(package!, publisher!, instance!);
        identity.Validate();
        ValidatePipeName(pipe!, "Broker");
        return new WorkerBrokerConnection(pipe!, identity, nonce);
    }

    private static string RequiredValue(string[] args, string name, int maximumLength)
    {
        var value = OptionalValue(args, name, maximumLength);
        return value ?? throw new ArgumentException($"Missing required argument {name}.");
    }

    private static string? OptionalValue(string[] args, string name, int maximumLength)
    {
        var matches = Enumerable.Range(0, args.Length)
            .Where(index => string.Equals(args[index], name, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length != 1)
            throw new ArgumentException($"Duplicate argument {name}.");
        var index = matches[0];
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) ||
            args[index + 1].Length > maximumLength ||
            args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Invalid argument {name}.");
        return args[index + 1];
    }

    private static string ValidateInstanceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
            !value.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
            throw new ArgumentException("The widget instance ID is invalid.");
        return value;
    }

    private static void ValidatePipeName(string value, string label)
    {
        const string localPrefix = "LOCAL\\";
        var suffix = value.StartsWith(localPrefix, StringComparison.Ordinal)
            ? value[localPrefix.Length..]
            : value;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 ||
            string.IsNullOrWhiteSpace(suffix) || suffix.Contains('\\') || value.Any(char.IsControl))
            throw new ArgumentException($"{label} pipe name is invalid.");
    }

}

internal sealed record WorkerBrokerConnection(
    string PipeName,
    BrokerWidgetIdentity Identity,
    string Nonce);

internal sealed class WorkerCapabilityConnection : IAsyncDisposable
{
    private readonly BrokerPipeClient? _transport;

    private WorkerCapabilityConnection(
        IWidgetCapabilityClient client,
        BrokerPipeClient? transport)
    {
        Client = client;
        _transport = transport;
    }

    internal IWidgetCapabilityClient Client { get; }

    internal static async ValueTask<WorkerCapabilityConnection> ConnectAsync(
        WorkerBrokerConnection? connection,
        CancellationToken cancellationToken)
    {
        if (connection is null)
            return new WorkerCapabilityConnection(
                UnavailableWidgetCapabilityClient.Instance, null);

        var transport = new BrokerPipeClient(
            connection.PipeName,
            connection.Identity,
            connection.Nonce);
        try
        {
            await transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return new WorkerCapabilityConnection(
                new BrokerWidgetCapabilityClient(transport), transport);
        }
        catch
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_transport is not null)
            await _transport.DisposeAsync().ConfigureAwait(false);
    }
}
