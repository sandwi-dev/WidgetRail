using WidgetRail.WrailCli;

using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += cancelHandler;
try
{
    return await CliApplication.RunAsync(
        args, Console.Out, Console.Error, remoteHttpHandler: null, cancellation.Token);
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
