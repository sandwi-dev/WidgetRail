using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.Diagnostics;
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
            if (ControllerRecoveryStartup.TryRun(args, [], out var recoveryExitCode))
                return recoveryExitCode;
            var arguments = args;
            var fixture = false;
            IsValidationLaunch(arguments, ref fixture);
            OverlayShellOptions? options = null;
            Exception? configurationError = null;
            if (!fixture)
            {
                var defaults = WidgetRail.PlatformSettings.PlatformSettingsPaths.CreateDefault().RootDirectory;
                try { options = OverlayLaunchConfiguration.Resolve(arguments, AppContext.BaseDirectory, defaults); }
                catch (Exception error) when (OverlayLaunchConfiguration.IsConfigurationError(error))
                {
                    // A malformed development generation must never fall through
                    // to the normal profile or create an unowned recovery window.
                    if (arguments.Any(value => value.StartsWith("--development-", StringComparison.Ordinal))) throw;
                    configurationError = error;
                }
                if (options is not null)
                {
                    if (options.Development is { } development) DevelopmentJobEnrollment.Join(development.JobName);
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
            if (App.RestartRequested)
            {
                // All windows, workers, media and native input are gone before
                // reactivation. Release profile election before the replacement
                // process starts. Inno and restart use the same direct executable.
                lifetime?.Dispose();
                lifetime = null;
                var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("Application executable is unavailable."))
                { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
                foreach (var argument in arguments.Where(argument => argument != "--hidden")) start.ArgumentList.Add(argument);
                using var replacement = Process.Start(start) ?? throw new InvalidOperationException("WidgetRail restart failed.");
            }
            return 0;
        }
        catch (Exception error)
        {
            Diagnostics.FrontendFailureLog.Current.Write("process-startup", error);
            System.Diagnostics.Trace.TraceError("WinUI process startup failed: {0}", error);
            return 1; // Fail closed: no window, input session or Bridge is created after a failed election.
        }
        finally { lifetime?.Dispose(); }
    }
    static partial void IsValidationLaunch(IReadOnlyList<string> arguments, ref bool fixture);
}
