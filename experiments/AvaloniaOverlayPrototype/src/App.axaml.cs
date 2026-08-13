using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GameBarAlternative.AvaloniaPrototype.Diagnostics;

namespace GameBarAlternative.AvaloniaPrototype;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var window = new MainWindow();
            desktop.MainWindow = window;

            var arguments = PrototypeArguments.Current;
            if (arguments.EvidencePath is not null)
            {
                _ = EvidenceScenario.RunAsync(window, desktop, arguments);
            }
            else
            {
                window.Show();
                _ = window.NavigateAsync(PrototypeRoute.Settings);

                if (arguments.ExitAfterSeconds > 0)
                {
                    _ = ExitAfterAsync(desktop, arguments.ExitAfterSeconds);
                }
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task ExitAfterAsync(IClassicDesktopStyleApplicationLifetime desktop, int seconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        desktop.Shutdown();
    }
}
