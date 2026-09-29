using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WidgetRail.OverlayFrontend.WinUI;

internal static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        ProcessLifecycle? lifetime = null;
        try
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            var launch = activation.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launchArgs ? launchArgs.Arguments : "";
            var arguments = FrontendArguments.Parse(launch, args);
            var fixture = false;
            IsValidationLaunch(arguments, ref fixture);
            OverlayShellOptions? options = null;
            Exception? configurationError = null;
            if (!fixture)
            {
                var defaults = WidgetRail.PlatformSettings.PlatformSettingsPaths.CreateDefault().RootDirectory;
                try { options = OverlayLaunchConfiguration.Resolve(arguments, AppContext.BaseDirectory, defaults); }
                catch (Exception error) when (OverlayLaunchConfiguration.IsConfigurationError(error))
                { configurationError = error; }
                if (options is not null)
                {
                    var election = OverlayProcessLease.Elect(ProcessProfilePolicy.ForSettingsRoot(options.SettingsRoot, defaults),
                        showExisting: !arguments.Contains("--hidden"));
                    if (election.Lease is null) return 0;
                    lifetime = new(election.Lease);
                }
            }
            Application.Start(parameter =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                // The elected profile and the shell must consume the same value,
                // even if a diagnostic configuration file changes during startup.
                _ = new App(arguments, options, configurationError);
            });
            return 0;
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("WinUI process startup failed: {0}", error);
            return 1; // Fail closed: no window, input session or Bridge is created after a failed election.
        }
        finally { lifetime?.Dispose(); }
    }
    static partial void IsValidationLaunch(IReadOnlyList<string> arguments, ref bool fixture);
}
