using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

// Controller prompts are a process-wide host preference, as in the native host.
// No widget snapshot changes when the last-used physical controller changes.
internal static class WidgetControllerPrompts
{
    private static int playStation;
    internal static bool PlayStation => Volatile.Read(ref playStation) != 0;
    internal static event Action? Changed;
    internal static void Set(WidgetRail.OverlayPlatformClient.ControllerFamily family)
    {
        if (family == WidgetRail.OverlayPlatformClient.ControllerFamily.Unknown) return;
        var next = family == WidgetRail.OverlayPlatformClient.ControllerFamily.PlayStation ? 1 : 0;
        if (Interlocked.Exchange(ref playStation, next) != next) Changed?.Invoke();
    }
}

internal sealed partial class WidgetViewPresenter
{
    private bool playStationPrompts = WidgetControllerPrompts.PlayStation;
    internal void SetControllerFamily(WidgetRail.OverlayPlatformClient.ControllerFamily family)
    {
        if (!DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Update controller prompts on their UI dispatcher.");
        if (!disposed) WidgetControllerPrompts.Set(family);
    }
    private void ControllerPromptsChanged()
    {
        if (disposed) return;
        if (!DispatcherQueue.HasThreadAccess) { DispatcherQueue.TryEnqueue(ControllerPromptsChanged); return; }
        var next = WidgetControllerPrompts.PlayStation;
        if (next == playStationPrompts) return;
        playStationPrompts = next;
        foreach (var binding in bindings.Values)
            if (binding.Element is FontIcon icon && binding.Identity.Kind == ViewNodeKind.ControllerGlyph)
                WidgetGlyphs.Apply(icon, declarations[binding.Identity.Id].Node, next);
    }
}
