using System.Text.Json;
using Microsoft.UI.Xaml;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Read-only startup validation. Run in a fresh process per isolated profile:
    // available layout, temporarily absent layout, missing/disabled widget, or no
    // saved pin. It never invokes a widget/provider control or changes settings.
    internal void EnablePinnedRestoreValidation(string resultPath)
    {
        var profilePath = Path.Combine(options.SettingsRoot, "winui-pinned-state.json");
        var before = File.Exists(profilePath) ? File.ReadAllBytes(profilePath) : null;
        var started = false;
        Loaded += async (_, _) =>
        {
            if (started) return;
            started = true;
            var checks = new List<string>();
            WidgetPresentationAuthority? authority = null;
            try
            {
                if (startup is not null) await startup;
                Check(validationFailure is null && owner is not null && activeWidget is not null,
                    "fresh startup reaches the ordinary shell without a saved-pin failure");
                Check(pinned is null && pendingPinWidget is null,
                    "startup never restores an active pin, even when legacy preferences name an available widget");
                var after = File.Exists(profilePath) ? File.ReadAllBytes(profilePath) : null;
                Check(before is null ? after is null : after is not null && before.AsSpan().SequenceEqual(after),
                    "startup leaves the user's saved pin preference file byte-for-byte unchanged");
                Write(new { passed = true, checks, restored = pinned is not null, authority });
            }
            catch (Exception error) { Write(new { passed = false, checks, error = error.ToString(), authority }); }

            void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
            void Write<T>(T value)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath))!);
                File.WriteAllText(resultPath, JsonSerializer.Serialize(value));
            }
        };
    }
}
