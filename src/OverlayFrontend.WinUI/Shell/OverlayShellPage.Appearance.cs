using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private long paletteVersion;
    internal event Action? SurfaceAppearanceChanged;
    internal string? SurfaceAppearanceWidgetId { get; private set; }
    internal IReadOnlyDictionary<string, BridgeNodeRenderStyles>? ShellPalette { get; private set; }
    internal Microsoft.UI.Xaml.Controls.Border SurfaceBackground => WidgetSurface;

    internal async Task RefreshShellPaletteAsync()
    {
        if (retired || owner is null) return;
        var version = ++paletteVersion;
        try
        {
            var palette = await owner.Session.ReadShellStylesAsync(lifetime.Token);
            if (retired || version != paletteVersion) return;
            ShellPalette = palette;
            if (pinned is { } current) ApplyPinnedAppearance(current);
            SurfaceAppearanceChanged?.Invoke();
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error)
        { if (!retired) System.Diagnostics.Trace.TraceWarning("Shell palette refresh retained its last paint: {0}", error.GetType().Name); }
    }

    private void NotifySurfaceAppearanceChanged()
    {
        // Update only after an accepted view is committed, not when selection is requested.
        SurfaceAppearanceWidgetId = activeWidget;
        SurfaceAppearanceChanged?.Invoke();
    }
}
