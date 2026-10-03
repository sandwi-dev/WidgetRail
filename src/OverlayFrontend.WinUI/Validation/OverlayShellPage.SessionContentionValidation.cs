using System.Reflection;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Recreates the crash shape without a real provider, controller input, or
    // system changes: native collection layout -> contended session read.
    private async Task ValidateLayoutSessionContentionAsync()
    {
        var session = owner!.Session;
        var gate = session.GetType().GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        var enter = gate.GetType().GetMethod("Enter", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var list = new ListView { Width = 200, Height = 150, Opacity = 0, IsHitTestVisible = false, IsTabStop = false };
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(5);
        bool inRead = false, nested = false, ran = false;
        timer.Tick += (_, _) => { if (inRead) nested = true; };
        Task? holder = null;
        list.ContainerContentChanging += (sender, args) =>
        {
            if (ran || args.InRecycleQueue) return;
            ran = true;
            using var held = new ManualResetEventSlim();
            using var reading = new ManualResetEventSlim();
            holder = Task.Run(() =>
            {
                using var scope = (IDisposable)enter.Invoke(gate, null)!;
                held.Set();
                if (!reading.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException("Native session read never began.");
                Thread.Sleep(250);
            });
            try
            {
                if (!SpinWait.SpinUntil(() => held.IsSet, 3000)) throw new TimeoutException("Session gate was not acquired.");
                inRead = true;
                reading.Set();
                _ = session.GetState("browser");
                inRead = false;
                if (nested) throw new InvalidOperationException("Session read dispatched nested messages during XAML layout.");
                done.TrySetResult();
            }
            catch (Exception error) { inRead = false; reading.Set(); done.TrySetException(error); }
        };
        try
        {
            timer.Start();
            StartupContentStage.Children.Add(list);
            list.ItemsSource = new[] { "Layout contention fixture" };
            await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (holder is not null) await holder;
        }
        finally { timer.Stop(); StartupContentStage.Children.Remove(list); }
    }
}
