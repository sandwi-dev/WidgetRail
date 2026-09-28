using System.Text.Json;
using System.Text.Json.Nodes;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using WidgetRail.WindowsDisplayProvider;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private AppearanceSettings savedAppearance = AppearanceSettings.Default;
    private string activeDisplayId = string.Empty;
    private long displayVersion;
    private WidgetSurfaceHints? authoredSurfaceHints;
    internal WidgetSurfaceHints? SurfaceHints => IsMediaFullscreen ? new()
    { WidthMode = WidgetSurfaceAxisMode.FillAvailable, HeightMode = WidgetSurfaceAxisMode.FillAvailable } : authoredSurfaceHints;
    internal event Action? SizingChanged;
    internal double ContentWidth => WidgetHost.ActualWidth;
    internal double ContentHeight => WidgetHost.ActualHeight;
    internal JsonObject? SizingDiagnostics { get; private set; }
    internal void RecordSizing(SurfaceExtent available, SurfaceExtent chrome, SurfaceExtent resolved)
    {
        SizingDiagnostics = new JsonObject { ["available"] = Extent(available), ["chrome"] = Extent(chrome),
            ["resolved"] = Extent(resolved), ["hints"] = JsonSerializer.SerializeToNode(SurfaceHints, ShellJsonContext.Default.WidgetSurfaceHints),
            ["scale"] = Appearance.InterfaceScale, ["display"] = activeDisplayId };
        static JsonObject Extent(SurfaceExtent value) => new() { ["Width"] = value.Width, ["Height"] = value.Height };
        UpdateDiagnostics();
    }

    internal async Task SetDisplayAsync(WindowDisplayContext context)
    {
        if (retired || owner is null) return;
        var version = ++displayVersion;
        try
        {
            var id = await owner.Session.ResolveDisplayAsync(context.Id, context.Name, context.DevicePaths, lifetime.Token);
            if (retired || version != displayVersion) return;
            activeDisplayId = id;
            ApplyEffectiveAppearance();
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error)
        {
            if (retired || version != displayVersion) return;
            activeDisplayId = string.Empty;
            ApplyEffectiveAppearance();
            ReportFailure(error);
        }
    }

    private void ApplyEffectiveAppearance()
    {
        var scale = DisplayScalePolicy.Resolve(savedAppearance, activeDisplayId);
        Appearance = savedAppearance with { InterfaceScale = scale.InterfaceScale, TextScale = scale.TextScale };
        surface?.ApplyAppearance(Appearance, systemUi.AnimationsEnabled);
        if (pinned is { } current) ApplyPinnedAppearance(current);
        AppearanceLoaded?.Invoke(Appearance);
    }

    private void UpdateSurfaceHints(WidgetSurfaceHints? hints)
    {
        var changed = authoredSurfaceHints != hints;
        authoredSurfaceHints = hints;
        NotifySurfaceAppearanceChanged();
        if (changed || hints?.WidthMode == WidgetSurfaceAxisMode.Content || hints?.HeightMode == WidgetSurfaceAxisMode.Content)
            SizingChanged?.Invoke();
    }

    internal SurfaceExtent? MeasureContent(SurfaceExtent constraint)
    {
        if (surface is null || !surface.IsLoaded) return null;
        surface.Measure(new(constraint.Width, constraint.Height));
        var extent = new SurfaceExtent(surface.DesiredSize.Width, surface.DesiredSize.Height);
        // This policy probe must not leave a retained child measured against a
        // temporary constraint if the final HWND size happens to stay unchanged.
        surface.InvalidateMeasure();
        return extent;
    }
}
