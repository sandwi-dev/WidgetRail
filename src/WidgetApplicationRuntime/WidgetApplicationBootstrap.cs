using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetRuntime;

/// <summary>
/// Narrow worker-side bootstrap for an explicitly approved full-trust Community
/// application. It connects to the same authenticated, permission-checked host services
/// as sandboxed widgets, without restricting its ordinary current-user APIs.
/// </summary>
public static class WidgetApplicationBootstrap
{
    public static Task<int> RunAsync(string[] args, Func<Widget> widgetFactory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(widgetFactory);
        return RunAsync(args, _ => widgetFactory(), cancellationToken);
    }

    public static async Task<int> RunAsync(
        string[] args,
        Func<WidgetHostServices, Widget> widgetFactory,
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
            var launch = WidgetWorkerLaunchArguments.Parse(args);
            startupPhase = "capability";
            await using var connection = await WorkerCapabilityConnection.ConnectAsync(launch.Broker, shutdown.Token).ConfigureAwait(false);
            startupPhase = "factory";
            var widget = widgetFactory(new WidgetHostServices(connection.Client)) ?? throw new InvalidOperationException(
                "The widget factory returned no widget.");
            startupPhase = "session";
            await new WidgetWorkerServer(
                    widget,
                    launch.WidgetInstanceId,
                    launch.WidgetPipeName,
                    launch.MaximumMessageBytes,
                    capabilityClient: connection.Client,
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

}
