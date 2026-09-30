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
    // The work-area HWND already contains a separate aspect-fit fullscreen layer.
    // Its extent must never overwrite the retained ordinary widget's layout.
    internal WidgetSurfaceHints? SurfaceHints => authoredSurfaceHints;
    internal event Action? SizingChanged;
    internal double ContentWidth => WidgetSurface.ActualWidth;
    internal double ContentHeight => WidgetSurface.ActualHeight;
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
        Appearance = AppearanceForDisplay(activeDisplayId);
        surface?.ApplyAppearance(Appearance, systemUi.AnimationsEnabled);
        if (preparingSurface is { } preparing && !ReferenceEquals(preparing.Presenter, surface))
            preparing.Presenter.ApplyAppearance(Appearance, systemUi.AnimationsEnabled);
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
        var measured = surface.MeasureSurfaceContent(new(constraint.Width, constraint.Height), SurfaceHints);
        return new(measured.Width, measured.Height);
    }
}
