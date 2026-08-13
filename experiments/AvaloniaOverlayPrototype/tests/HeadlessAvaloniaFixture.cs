using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameBarAlternative.AvaloniaPrototype.Tests;

[TestClass]
public static class HeadlessAvaloniaFixture
{
    private static readonly ManualResetEventSlim Ready = new();
    private static readonly CancellationTokenSource Shutdown = new();
    private static Thread? dispatcherThread;

    [AssemblyInitialize]
    public static void Initialize(TestContext context)
    {
        _ = context;
        dispatcherThread = new Thread(() =>
        {
            AppBuilder.Configure<TestApplication>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();
            Ready.Set();
            Dispatcher.UIThread.MainLoop(Shutdown.Token);
        })
        {
            IsBackground = true,
            Name = "AVP-001 headless dispatcher",
        };
        dispatcherThread.SetApartmentState(ApartmentState.STA);
        dispatcherThread.Start();
        Assert.IsTrue(Ready.Wait(TimeSpan.FromSeconds(10)), "Avalonia headless dispatcher did not start.");
    }

    [AssemblyCleanup]
    public static void Cleanup()
    {
        Shutdown.Cancel();
        Dispatcher.UIThread.Post(() => { });
        dispatcherThread?.Join(TimeSpan.FromSeconds(5));
        Ready.Dispose();
        Shutdown.Dispose();
    }

    private sealed class TestApplication : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());
    }
}
