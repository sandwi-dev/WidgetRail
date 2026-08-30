using WidgetRail.WidgetBridge;

internal static class BridgeRevisionNotificationScenarios
{
    internal static async Task LatestRevisionsAreBoundedAndOrdered()
    {
        var lane = new BridgeRevisionNotificationLane();
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var latestDrained = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new List<string>();

        lane.Enqueue(
            BridgeRevisionNotificationKind.Appearance,
            1,
            async _ =>
            {
                firstStarted.TrySetResult();
                await releaseFirst.Task.ConfigureAwait(false);
                published.Add("appearance:1");
            });
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        lane.Enqueue(
            BridgeRevisionNotificationKind.Appearance,
            2,
            _ => RecordAsync("appearance:2"));
        lane.Enqueue(
            BridgeRevisionNotificationKind.Catalog,
            8,
            _ => RecordAsync("catalog:8"));
        lane.Enqueue(
            BridgeRevisionNotificationKind.Appearance,
            5,
            _ => RecordAsync("appearance:5"));
        lane.Enqueue(
            BridgeRevisionNotificationKind.Catalog,
            13,
            _ => RecordAsync("catalog:13"));
        lane.Enqueue(
            BridgeRevisionNotificationKind.Appearance,
            4,
            _ => RecordAsync("appearance:4-stale"));

        releaseFirst.TrySetResult();
        await latestDrained.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await lane.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(2));

        BoundaryAssert.Equal(3, published.Count);
        BoundaryAssert.Equal("appearance:1", published[0]);
        BoundaryAssert.Equal("appearance:5", published[1]);
        BoundaryAssert.Equal("catalog:13", published[2]);

        lane.Enqueue(
            BridgeRevisionNotificationKind.Catalog,
            21,
            _ => RecordAsync("catalog:21-after-close"));
        BoundaryAssert.Equal(3, published.Count);
        return;

        Task RecordAsync(string value)
        {
            published.Add(value);
            if (value == "catalog:13") latestDrained.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
