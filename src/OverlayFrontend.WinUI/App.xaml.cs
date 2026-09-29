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
    private readonly IReadOnlyList<string> arguments;
    private readonly Shell.OverlayShellOptions? launchOptions;
    private readonly Exception? configurationError;
    internal static string? ValidationFixturePath { get; private set; }
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
    internal App(IReadOnlyList<string> arguments, Shell.OverlayShellOptions? launchOptions, Exception? configurationError)
    {
        this.arguments = arguments;
        this.launchOptions = launchOptions;
        this.configurationError = configurationError;
        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        if (arguments.Contains("--trace-layout")) DebugSettings.LayoutCycleTracingLevel = LayoutCycleTracingLevel.High;
        ValidationFixturePath = Shell.FrontendArguments.Value(arguments, "--playnite-layout-fixture");
        var main = new MainWindow(arguments, launchOptions, configurationError);
        Window = main;
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        ProcessLifecycle.Attach(main, main.ShowOverlay);
        main.StartPresentation();
        main.StartReplay();
    }
}
