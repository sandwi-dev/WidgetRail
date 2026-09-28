using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WidgetRail.OverlayFrontend.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// The main application window. Use <c>App.Window</c> from any class that needs
    /// the window reference (for dialogs, pickers, interop, etc.).
    /// </summary>
    public static Window Window { get; private set; } = null!;

    /// <summary>
    /// The UI thread dispatcher. Use <c>App.DispatcherQueue</c> to marshal calls
    /// to the UI thread. Fully qualified to avoid CS0104 ambiguity with
    /// <see cref="Windows.System.DispatcherQueue"/>.
    /// </summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    /// <summary>
    /// The native window handle (HWND). Use for file pickers,
    /// <c>DataTransferManager</c>, and any WinRT interop that requires
    /// <c>InitializeWithWindow</c>.
    /// </summary>
    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var arguments = args.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Concat(Environment.GetCommandLineArgs().Skip(1)).ToHashSet(StringComparer.Ordinal);
        var widgetConfiguration = Environment.GetCommandLineArgs().Skip(1)
            .FirstOrDefault(value => value.StartsWith("--widget-config=", StringComparison.Ordinal))?["--widget-config=".Length..];
        var main = new MainWindow(arguments.Contains("--validate-external-surface"), arguments.Contains("--validate-controller"),
            arguments.Contains("--replay-controller"), arguments.Contains("--validate-collection"), widgetConfiguration,
            arguments.Contains("--validate-gridview"), arguments.Contains("--validate-controls"), arguments.Contains("--validate-indexed"), arguments.Contains("--validate-focus-policy"),
            arguments.FirstOrDefault(value => value.StartsWith("--indexed-validation-pipe=", StringComparison.Ordinal))?["--indexed-validation-pipe=".Length..],
            arguments.Contains("--validate-grouped"), arguments.Contains("--validate-grouped-flat"), arguments.Contains("--validate-grouped-adapted"), arguments.Contains("--validate-surfaces"), arguments.Contains("--validate-select"));
        Window = main;
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Window.Activate();
        main.StartInput();
        main.StartReplay();
    }
}
