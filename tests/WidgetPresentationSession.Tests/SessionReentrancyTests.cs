using System.Reflection;

namespace WidgetRail.WidgetPresentationSession.Tests;

public sealed partial class SessionTransportTests
{
    [TestMethod]
    public async Task StaSessionReadDoesNotPumpMessagesWhileContended()
    {
        await using var server = new ScriptedBridgeServer();
        var serverTask = server.RunAuthenticatedAsync(ExpectStopAsync);
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName, Options()))
        {
            // Hold the real session gate from a worker while a renderer-style
            // synchronous read runs on STA. A pumping wait can re-enter XAML.
            var gate = typeof(WidgetPresentationSession).GetField("_gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session)!;
            using var held = new ManualResetEventSlim();
            using var reading = new ManualResetEventSlim();
            var holder = Task.Run(() =>
            {
                void Hold() { held.Set(); Assert.IsTrue(reading.Wait(TestDeadline)); Thread.Sleep(250); }
                var enter = gate.GetType().GetMethod("Enter", BindingFlags.NonPublic | BindingFlags.Instance);
                if (enter is not null) { using var scope = (IDisposable)enter.Invoke(gate, null)!; Hold(); }
                else { lock (gate) Hold(); } // Negative control: the old Monitor gate.
            });
            var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    Assert.IsTrue(held.Wait(TestDeadline));
                    var context = new ObservePumpingContext();
                    SynchronizationContext.SetSynchronizationContext(context);
                    reading.Set();
                    var state = session.GetState("not-published");
                    Assert.AreSame(context, SynchronizationContext.Current, "Session reads must restore the caller's dispatcher context.");
                    var enter = gate.GetType().GetMethod("Enter", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    using ((IDisposable)enter.Invoke(gate, null)!)
                    {
                        Assert.IsNull(session.GetState("nested-read"));
                        Assert.AreSame(context, SynchronizationContext.Current);
                    }
                    SynchronizationContext.SetSynchronizationContext(null);
                    Assert.IsNull(state);
                    result.SetResult(context.Trace);
                }
                catch (Exception error) { result.SetException(error); }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            var waits = await result.Task.WaitAsync(TestDeadline);
            await holder.WaitAsync(TestDeadline);
            Assert.IsNull(waits, "Session lock contention must not enter a message-pumping STA wait. Gate=" + gate.GetType() + "\n" + waits);
        }
        await serverTask.WaitAsync(TestDeadline);
    }

    private sealed class ObservePumpingContext : SynchronizationContext
    {
        internal string? Trace;
        internal ObservePumpingContext() => SetWaitNotificationRequired();
        public override int Wait(IntPtr[] waitHandles, bool waitAll, int millisecondsTimeout)
        {
            Trace = Environment.StackTrace;
            return WaitHelper(waitHandles, waitAll, millisecondsTimeout);
        }
    }
}
