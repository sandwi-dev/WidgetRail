using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System.Runtime.CompilerServices;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal static class WidgetNativeResource
{
    private static readonly ConditionalWeakTable<ResourceDictionary, Dictionary<string, SolidColorBrush>> Owned = new();
    // Native visual-state storyboards retain brush references. Mutate owned
    // brushes so every template part follows a live WRSS theme change.
    internal static void Set(ResourceDictionary resources, string key, object? value)
    {
        var brushes = Owned.GetOrCreateValue(resources);
        if (value is null) { resources.Remove(key); brushes.Remove(key); }
        else if (value is SolidColorBrush color)
        {
            // Resource lookup can find a shared, immutable theme brush outside
            // this dictionary. Only mutate brushes created by this owner.
            if (brushes.TryGetValue(key, out var owned))
                owned.Color = color.Color;
            else { owned = new SolidColorBrush(color.Color); brushes[key] = owned; resources[key] = owned; }
        }
        else if (!resources.TryGetValue(key, out var current) || !Equals(current, value)) resources[key] = value;
    }
}
