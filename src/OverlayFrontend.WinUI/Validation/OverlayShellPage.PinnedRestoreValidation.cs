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
                var saved = await new PinnedPreferencesStore(options.SettingsRoot).LoadAsync(lifetime.Token);
                var widget = saved.WidgetId;
                var placement = widget is null ? null : saved.Placements.GetValueOrDefault(widget);
                var descriptor = catalogItems.FirstOrDefault(item => item.Id == widget);
                var frame = widget is null ? null : owner!.Session.GetState(widget)?.LastGood;
                var offered = descriptor?.PinningSupported == true && frame is not null && placement is not null &&
                    (placement.LayoutId == WidgetPinnedProjection.FullWidgetLayoutId
                        ? descriptor.FullWidgetPinningSupported && frame.Snapshot.EmbeddedMediaSession is null
                        : frame.Snapshot.PinnedLayouts.Any(layout => layout.Id == placement.LayoutId));
                if (!offered)
                {
                    Check(pinned is null,
                        "missing widget, removed layout, unavailable current media, or unsupported full-media pin is skipped without stale presentation");
                }
                else
                {
                    var savedPlacement = placement ?? throw new InvalidOperationException("Saved pin placement was unavailable.");
                    var current = pinned ?? throw new InvalidOperationException("An available saved pin was not restored.");
                    var binding = current.Presenter!.CurrentBinding ?? throw new InvalidOperationException("Restored pin has no genuine binding.");
                    var target = owner!.Session.GetTarget(current.WidgetId);
                    authority = binding.Frame.Authority;
                    Check(current.IsCurrent && ReferenceEquals(current.Selection, binding.Selection) && binding.IsCurrent &&
                        authority.WidgetInstanceId == target.Descriptor.InstanceId && authority.RuntimeGeneration == target.Descriptor.RuntimeGeneration &&
                        authority.PresentationGeneration == target.Descriptor.PresentationGeneration,
                        "restored pin uses the newly established session's genuine current selection and runtime authority");
                    Check(!current.Window.Interactive && !PinnedInputActive && current.Window.IsVisible &&
                        current.Window.FocusIndicator.Visibility == Visibility.Collapsed && !current.Presenter!.IsHitTestVisible,
                        "saved pin starts visible but passive with input and focus presentation withheld");
                    Check(current.Window.OpacityPercent == savedPlacement.OpacityPercent && current.LayoutId == savedPlacement.LayoutId,
                        "restoration retains the chosen layout and opacity without replaying controller state");
                    Check(binding.View.EmbeddedMediaSession?.PendingCommand?.Kind != EmbeddedMediaPlaybackCommandKind.Play,
                        "restored declaration has no pending embedded Play command");
                }
                var after = File.Exists(profilePath) ? File.ReadAllBytes(profilePath) : null;
                Check(before is null ? after is null : after is not null && before.AsSpan().SequenceEqual(after),
                    "startup leaves the user's saved pin preference file byte-for-byte unchanged");
                Write(new { passed = true, checks, restored = pinned is not null, widget, layout = placement?.LayoutId, authority });
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
