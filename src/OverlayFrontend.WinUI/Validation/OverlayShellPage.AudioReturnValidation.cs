namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Real providers are read only. Exercise preview return, where the incoming
    // surface is prepared while the switcher still owns focus.
    internal void EnableAudioReturnValidation(string path)
    {
        Loaded += async (_, _) =>
        {
            var checks = new List<string>();
            try
            {
                if (startup is not null) await startup;
                await SelectAsync("audio-mixer", true);
                for (var pass = 0; pass < 8; ++pass)
                {
                    surface!.ScrollBy(0, 140);
                    await Task.Delay(200);
                    await SelectAsync("widgetrail.samples.spotify", true);
                    // Four owners exceed native retention, restoring Audio
                    // Mixer's logical scroll anchor into a newly created tree.
                    await SelectAsync("power", false);
                    await SelectAsync("network-controls", true);
                    PrepareRadialBackEntry(); SetInteractive(false); FocusTray();
                    if (!RadialOpen) throw new InvalidOperationException("Fixture requires radial switcher settings.");
                    PreviewRadial(catalogItems.Single(item => item.Id == "audio-mixer"));
                    var deadline = DateTime.UtcNow.AddSeconds(15);
                    while (switching && DateTime.UtcNow < deadline) await Task.Delay(25);
                    await Task.Delay(250);
                    if (switching || activeWidget != "audio-mixer" || validationFailure is not null || RecoveryVisible)
                        throw new InvalidOperationException("Audio Mixer preview did not settle.", validationFailure);
                    checks.Add($"Audio Mixer scrolled/evicted radial return {pass + 1} completed with provider updates");
                    await SelectAsync("audio-mixer", true);
                }
                Write(true, null);
            }
            catch (Exception error) { Write(false, error.ToString()); }
            void Write(bool passed, string? error)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { passed, checks, error }));
            }
        };
    }
}
