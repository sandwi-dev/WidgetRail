using System.Globalization;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WidgetRuntime;

namespace GameBarAlternative.WidgetWorkerHost;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        BrokerPipeClient? brokerClient = null;
        try
        {
            var pipeName = RequiredValue(args, "--widget-pipe", 200);
            var instanceId = RequiredValue(args, "--widget-instance", 128);
            var maximumBytes = int.Parse(
                RequiredValue(args, "--max-message-bytes", 16),
                NumberStyles.None,
                CultureInfo.InvariantCulture);
            var packageRoot = RequiredValue(args, "--package-root", 4096);
            var assemblyPath = RequiredValue(args, "--widget-assembly", 4096);
            var typeName = RequiredValue(args, "--widget-type", 512);
            var brokerConnection = ParseBrokerConnection(args, instanceId);
            BrokerWidgetCapabilityClient? capabilityClient = null;
            if (brokerConnection is not null)
            {
                brokerClient = new BrokerPipeClient(
                    brokerConnection.PipeName,
                    new BrokerWidgetIdentity(
                        brokerConnection.PackageId,
                        brokerConnection.PublisherId,
                        brokerConnection.InstanceId),
                    brokerConnection.Nonce);
                await brokerClient.ConnectAsync().ConfigureAwait(false);
                capabilityClient = new BrokerWidgetCapabilityClient(brokerClient);
            }
            var widget = WidgetAssemblyLoader.Load(packageRoot, assemblyPath, typeName);
            using var shutdown = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdown.Cancel();
            };
            await new WidgetWorkerServer(
                widget, instanceId, pipeName, maximumBytes, capabilityClient).RunAsync(shutdown.Token)
                .ConfigureAwait(false);
            return 0;
        }
        catch (WidgetLoadException exception)
        {
            Console.Error.WriteLine($"Widget worker failed ({exception.Code}): {exception.Message}");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Widget worker failed: {exception.Message}");
            return 1;
        }
        finally
        {
            if (brokerClient is not null)
                await brokerClient.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal static WorkerBrokerConnection? ParseBrokerConnection(
        string[] args, string widgetInstanceId)
    {
        ArgumentNullException.ThrowIfNull(args);
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
        if (string.IsNullOrWhiteSpace(pipe) || pipe.Length > 200 || pipe.Contains('\\'))
            throw new ArgumentException("Broker pipe name is invalid.");
        return new WorkerBrokerConnection(pipe!, package!, publisher!, instance!, nonce);
    }

    private static string RequiredValue(string[] args, string name, int maximumLength)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 >= args.Length ||
            string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].Length > maximumLength)
            throw new ArgumentException($"Missing or invalid required argument {name}.");
        return args[index + 1];
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
            args[index + 1].Length > maximumLength || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Invalid argument {name}.");
        return args[index + 1];
    }
}

internal sealed record WorkerBrokerConnection(
    string PipeName,
    string PackageId,
    string PublisherId,
    string InstanceId,
    string Nonce);
