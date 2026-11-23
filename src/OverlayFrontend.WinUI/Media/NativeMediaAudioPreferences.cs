using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

// Host/profile ownership, independent of a player's source or widget-authored
// playback memory. Portals follow their existing logical presentation ancestry.
internal sealed class NativeMediaAudioPreferences(
    Func<string, MediaAudioPreference?> read,
    Action<string, MediaAudioPreference> remember)
{
    private static readonly ConditionalWeakTable<DependencyObject, NativeMediaAudioPreferences> Owners = new();
    internal MediaAudioPreference Read(string widgetId, double defaultVolume) => read(widgetId) ?? new(true, defaultVolume);
    internal void Remember(string widgetId, bool muted, double volume) => remember(widgetId, new(muted, volume));
    internal void Attach(DependencyObject root)
    {
        Owners.Remove(root);
        Owners.Add(root, this);
    }
    internal static NativeMediaAudioPreferences? Find(DependencyObject origin)
    {
        for (DependencyObject? item = origin; item is not null; item = PresentationLayers.LogicalParent(item))
            if (Owners.TryGetValue(item, out var owner)) return owner;
        return null;
    }
}
