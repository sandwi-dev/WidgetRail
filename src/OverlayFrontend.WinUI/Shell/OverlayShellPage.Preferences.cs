namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private readonly ShellPreferencesStore preferencesStore;
    private ShellPreferences preferences = ShellPreferences.Empty;
    private bool preferencesLoaded;

    private async Task SavePreferencesAsync()
    {
        if (!preferencesLoaded) return;
        try { await preferencesStore.SaveAsync(preferences); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { System.Diagnostics.Trace.WriteLine("WinUI shell preferences could not be saved: " + error.GetType().Name); }
    }
}
