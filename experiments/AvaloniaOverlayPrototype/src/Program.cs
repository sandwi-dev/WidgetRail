using Avalonia;
using GameBarAlternative.AvaloniaPrototype.Lifecycle;

namespace GameBarAlternative.AvaloniaPrototype;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        PrototypeArguments.Initialize(args);
        if (!PrototypeInstanceGuard.TryAcquire(out var instance))
        {
            Console.Error.WriteLine("AvaloniaOverlayPrototype is already running; the second instance will exit.");
            Environment.ExitCode = 2;
            return;
        }
        using (instance)
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
