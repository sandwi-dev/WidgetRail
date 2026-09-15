using WidgetRail.PlatformBroker;
using WidgetRail.WindowsAppLibraryProvider;

internal static class TaskWindowScenarios
{
    internal static async Task Run()
    {
        var observer = new Observer();
        var first = new WindowsRunningAppObservation("app", "process-creation-1", "Editor")
            { Window = new(101, 101, 0, 11, "EditorWindow", Title: "Document A") };
        var second = first with { Window = first.Window! with { Handle = 102, Root = 102, Title = "Document B" } };
        observer.Items = [first, second];
        var control = new Control();
        await using var provider = new WindowsAppLibraryProvider(
            [new RunningAppScenarios.Source(0)], RunningAppScenarios.ImmediateSta.Instance, observer)
            { TaskWindowControl = control };
        var list = await provider.GetTaskWindowsAsync(CancellationToken.None);
        Assert.Equal(2, list.Count);
        Assert.False(list[0].WindowId == list[1].WindowId);
        Assert.Equal("Document A", list[0].Title);
        Assert.Equal("Document B", list[1].Title);
        var refreshed = await provider.GetTaskWindowsAsync(CancellationToken.None);
        Assert.Equal(list[0].WindowId, refreshed[0].WindowId);
        await provider.SwitchTaskWindowAsync(list[1].WindowId, CancellationToken.None);
        Assert.Equal((nint)102, control.Switched);
        await provider.CloseTaskWindowAsync(list[0].WindowId, CancellationToken.None);
        Assert.Equal((nint)101, control.Closed);
        control.Closed = 0;
        observer.Items = [first with { InstanceEvidence = "reused-process" }, second];
        var stale = await Assert.ThrowsAsync<BrokerException>(() =>
            provider.CloseTaskWindowAsync(list[0].WindowId, CancellationToken.None));
        Assert.Equal("window_unavailable", stale.Code);
        Assert.Equal((nint)0, control.Closed);
        control.Switched = 0;
        await Assert.ThrowsAsync<BrokerException>(() =>
            provider.SwitchTaskWindowAsync(list[0].WindowId, CancellationToken.None));
        Assert.Equal((nint)0, control.Switched);
        observer.Items = [first with { Window = first.Window! with { ClassName = "ReusedWindow" } }, second];
        await Assert.ThrowsAsync<BrokerException>(() =>
            provider.SwitchTaskWindowAsync(list[0].WindowId, CancellationToken.None));
        Assert.Equal((nint)0, control.Switched);
        observer.Items = [second];
        await provider.GetTaskWindowsAsync(CancellationToken.None);
        await Assert.ThrowsAsync<BrokerException>(() =>
            provider.SwitchTaskWindowAsync(list[0].WindowId, CancellationToken.None));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.SwitchTaskWindowAsync(list[1].WindowId, canceled.Token));
        Assert.Equal((nint)0, control.Switched);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.CloseTaskWindowAsync(list[1].WindowId, canceled.Token));
    }

    private sealed class Observer : IWindowsRunningAppObserver
    {
        internal IReadOnlyList<WindowsRunningAppObservation> Items = [];
        public IReadOnlyList<WindowsRunningAppObservation> Observe(CancellationToken cancellationToken) => Items;
    }

    private sealed class Control : IWindowsTaskWindowControl
    {
        internal nint Switched;
        internal nint Closed;
        public void PrepareSwitch(WindowsRunningAppObservation expected, CancellationToken cancellationToken) => Switched = expected.Window!.Handle;
        public void Close(WindowsRunningAppObservation expected, CancellationToken cancellationToken) => Closed = expected.Window!.Handle;
    }
}
