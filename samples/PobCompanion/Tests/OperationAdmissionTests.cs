using System.Reflection;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetSdk;

internal static class OperationAdmissionTests
{
    internal static async Task RunAsync()
    {
        var directory = Directory.CreateTempSubdirectory("pob-admission-");
        var widget = new PobWidget(1, new PobEngine(directory.FullName, directory.FullName, "unused"), directory.FullName);
        var operations = (WidgetOperations)typeof(Widget).GetProperty("Operations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(widget)!;
        var work = typeof(PobWidget).GetMethod("Work", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var busy = typeof(PobWidget).GetField("busy", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Start(Func<CancellationToken, Task> action) => work.Invoke(widget, [action, "Testing admission"]);
        void CheckIdle(string message)
        {
            if ((bool)busy.GetValue(widget)!) throw new InvalidOperationException(message);
        }
        try
        {
            WidgetTestHost.Attach(widget, new WidgetTestHostServicesBuilder().Build());
            await WidgetTestHost.InitializeAsync(widget);
            for (var index = 0; index < WidgetOperations.MaximumTrackedKeys; index++)
                operations.RunSingleFlight("held." + index, context => new(release.Task.WaitAsync(context.CancellationToken)), WidgetOperationLifetime.Widget);
            var calls = 0;
            Start(_ => { calls++; return Task.CompletedTask; });
            CheckIdle("A capacity-rejected operation left PoB busy.");
            if (calls != 0) throw new InvalidOperationException("Rejected work executed.");
            release.SetResult();
            await operations.WhenAllIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));

            var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            operations.RunSingleFlight("pob.work", context => new(joined.Task.WaitAsync(context.CancellationToken)), WidgetOperationLifetime.Widget);
            Start(_ => { calls++; return Task.CompletedTask; });
            CheckIdle("Joining existing work created an orphaned busy flag.");
            if (calls != 0) throw new InvalidOperationException("Joined work executed twice.");
            joined.SetResult();
            await operations.WhenAllIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));

            Start(_ => { calls++; return Task.CompletedTask; });
            await operations.WhenAllIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
            CheckIdle("Completed work did not clear busy state.");
            if (calls != 1) throw new InvalidOperationException("Work did not recover after rejection.");
        }
        finally
        {
            release.TrySetResult();
            await WidgetTestHost.DestroyAsync(widget);
            directory.Delete(recursive: true);
        }
    }
}
