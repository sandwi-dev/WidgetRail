namespace WidgetRail.WidgetBridge;

internal sealed partial class BridgeClientRegistry
{
    private readonly Dictionary<string, (int Count, TaskCompletionSource Done)> widgetRestarts = new(StringComparer.Ordinal);

    internal async Task WaitForWidgetRetirementAsync(string widgetId)
    {
        Task restarting;
        lock (_gate) restarting = widgetRestarts.TryGetValue(widgetId, out var active) ? active.Done.Task : Task.CompletedTask;
        await restarting.WaitAsync(RestartDeadline).ConfigureAwait(false);
        ClientRegistration[] retiring;
        lock (_gate) retiring = _activeRetirements.Where(item => item.Configured.Id == widgetId).ToArray();
        await Task.WhenAll(retiring.Select(item => item.ResourceRetired)).WaitAsync(RestartDeadline).ConfigureAwait(false);
        var failures = retiring.Select(item => item.RetirementFailure).OfType<Exception>().ToArray();
        if (failures.Length != 0) throw new AggregateException("The widget stopped but resource cleanup failed.", failures);
    }
}
