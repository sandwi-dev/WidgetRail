using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using WidgetRail.WindowsDisplayProvider;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private sealed record PreparedPin(ResolvedPinnedPlacement Placement, PinnedPlacementLimits Limits, string? DisplayId);

    private AppearanceSettings AppearanceForDisplay(string? displayId)
    {
        var scale = DisplayScalePolicy.Resolve(savedAppearance, displayId);
        return savedAppearance with { InterfaceScale = scale.InterfaceScale, TextScale = scale.TextScale };
    }

    private async Task<PreparedPin> PreparePinnedPlacementAsync(PinnedWidgetWindow window, WidgetSurfaceHints? hints, PinnedPlacement? saved)
    {
        // Place a hidden peer to identify its actual monitor, then resolve the
        // final native layout before showing it. No main-display context is sent
        // to the broker: Settings continues to describe the main overlay.
        var provisional = PinnedPlacementPolicy.Resolve(PinnedDisplayAreas.Read(), saved, PinnedPlacementLimits.Default)
            ?? throw new InvalidOperationException("No display has enough room for this pinned layout.");
        window.Place(provisional.Bounds);
        var displayId = await ReadPinnedDisplayAsync(window.Handle);
        var appearance = AppearanceForDisplay(displayId);
        var limits = ResolvePinnedLimits(hints, appearance.InterfaceScale);
        var placement = PinnedPlacementPolicy.Resolve(PinnedDisplayAreas.Read(), saved, limits,
            (hints?.PreferredWidth ?? 480) * appearance.InterfaceScale,
            (hints?.PreferredHeight ?? 270) * appearance.InterfaceScale)
            ?? throw new InvalidOperationException("No display has enough room for this pinned layout.");
        if (placement.Monitor.Id != provisional.Monitor.Id)
        {
            displayId = null;
            appearance = AppearanceForDisplay(null);
            limits = ResolvePinnedLimits(hints, appearance.InterfaceScale);
            placement = PinnedPlacementPolicy.Resolve(PinnedDisplayAreas.Read(), saved, limits,
                (hints?.PreferredWidth ?? 480) * appearance.InterfaceScale,
                (hints?.PreferredHeight ?? 270) * appearance.InterfaceScale)
                ?? throw new InvalidOperationException("No display has enough room for this pinned layout.");
        }
        return new(placement, limits, displayId);
    }

    private Task<string?> ReadPinnedDisplayAsync(nint window) => Task.Run(() =>
    {
        var context = WindowDisplayContext.Read(window);
        return DisplayScaleIdentity.Resolve(context.DevicePaths);
    }, lifetime.Token);

    private async Task RefreshPinnedDisplayAsync(PinnedSurface current)
    {
        var revision = ++current.DisplayRevision;
        try
        {
            var id = await ReadPinnedDisplayAsync(current.Window.Handle);
            TryApplyPinnedDisplay(current, revision, id);
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error)
        {
            if (!retired && ReferenceEquals(pinned, current) && current.DisplayRevision == revision)
                Diagnostics.FrontendFailureLog.Current.Write("pinned-display", error);
        }
    }

    private bool TryApplyPinnedDisplay(PinnedSurface current, long revision, string? id)
    {
        if (retired || !ReferenceEquals(pinned, current) || !current.IsCurrent || current.DisplayRevision != revision) return false;
        current.DisplayId = id;
        ApplyPinnedAppearance(current);
        return true;
    }
}
