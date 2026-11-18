using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class EditingPreparationTests
{
    internal static async Task CancellationCannotPublishLatePreparation()
    {
        using var f = new Fixture();
        using var store = new CaptureLibraryStore(f.Library, f.Exports, null, null,
            (path, previous, _) => Task.FromResult(previous with { Width = 50, Height = 60, ByteLength = new FileInfo(path).Length }));
        var original = await f.Store.SaveAsync(f.Source, default);
        for (var attempt = 0; attempt < 80; ++attempt)
        {
            using var cancelled = new CancellationTokenSource();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var created = 0; var disposed = 0;
            CaptureLibraryEditRequests.Configure((_, _, _, _, _) => throw new InvalidOperationException("Cancelled preparation reached encoder"),
                async (_, _) => { entered.TrySetResult(); await release.Task; }, // Deliberately ignores cancellation: the host must reject its late reply.
                (input, _) => { ++created; return Task.FromResult(new CaptureEditPreview(new Uri(input.Path).AbsoluteUri, new Cleanup(() => ++disposed))); });
            var pending = CaptureLibraryEditRequests.RunAsync(Fixture.Identity,
                new(Saved: new(original.Id, original.Revision, original.MetadataRevision)), store, WindowCaptureRegistry.Shared, new object(), cancelled.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Task.WhenAll(Task.Run(cancelled.Cancel), Task.Run(() => release.TrySetResult()));
            try { await pending.WaitAsync(TimeSpan.FromSeconds(3)); throw new InvalidOperationException("Cancelled source preparation returned normally"); }
            catch (OperationCanceledException) { }
            if (CaptureLibraryEditRequests.Pending is not null || created != disposed) throw new InvalidOperationException("Late preparation retained a visible editor or preview lifetime");
        }
        if ((await store.QueryAsync(new(new()), default)).TotalCount != 1) throw new InvalidOperationException("Cancelled preparation changed library content");
    }
    private sealed class Cleanup(Action disposed) : IDisposable
    { public void Dispose() => disposed(); }
}
