namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private readonly ShellPreferencesStore preferencesStore;
    private ShellPreferences preferences = ShellPreferences.Empty;
    private bool preferencesLoaded;
    private Media.NativeMediaAudioPreferences? mediaAudioPreferences;
    private Task? pendingPreferenceWrite;
    private long preferenceWriteRevision;

    private void InitializeMediaAudioPreferences()
    {
        mediaAudioPreferences = new(id => preferences.MediaAudio.GetValueOrDefault(id), (id, value) =>
        {
            if (retired || !preferencesLoaded) return;
            preferences = preferences.RememberAudio(id, value);
            _ = SavePreferencesAsync();
        });
        mediaAudioPreferences.Attach(ProductionRoot);
    }

    private Task SavePreferencesAsync()
    {
        if (!preferencesLoaded) return Task.CompletedTask;
        ++preferenceWriteRevision;
        return pendingPreferenceWrite ??= SaveLatestPreferencesAsync();
    }

    private async Task SaveLatestPreferencesAsync()
    {
        // Slider input can arrive faster than disk writes. Keep one writer and
        // the latest immutable state, not one waiting task per value change.
        await Task.Yield();
        try
        {
            long written;
            do
            {
                written = preferenceWriteRevision;
                await preferencesStore.SaveAsync(preferences);
            } while (written != preferenceWriteRevision);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { System.Diagnostics.Trace.WriteLine("WinUI shell preferences could not be saved: " + error.GetType().Name); }
        finally { pendingPreferenceWrite = null; }
    }
}
