using System.Globalization;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetSdk;
namespace WidgetRail.WidgetRuntime;

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
