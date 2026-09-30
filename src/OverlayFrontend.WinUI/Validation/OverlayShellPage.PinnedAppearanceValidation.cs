using WidgetRail.PlatformSettings;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task ValidatePinnedAppearanceAsync(PinnedSurface current, Action<bool, string> check)
    {
        var previousAppearance = savedAppearance;
        var previousMain = activeDisplayId;
        var previousPin = current.DisplayId;
        var previousBounds = current.Window.Bounds;
        var previousLimits = current.Limits;
        var previousPlacement = current.LogicalPlacement;
        var durable = pinnedPreferences;
        var baseFont = current.Presenter!.FontSize / AppearanceForDisplay(current.DisplayId).TextScale;
        try
        {
            savedAppearance = previousAppearance with
            {
                InterfaceScale = .9, TextScale = 1.1,
                DisplayScales = new Dictionary<string, DisplayScaleSettings>
                { ["validation.main"] = new(.75, 1), ["validation.pin"] = new(1.25, 1.5) },
            };
            activeDisplayId = "validation.main";
            current.DisplayId = "validation.pin";
            ApplyEffectiveAppearance();
            await Task.Delay(100, lifetime.Token);
            check(Appearance.InterfaceScale == .75 && current.Window.InterfaceScale == 1.25,
                "main and pinned native roots use their own display interface scale");
            check(AppearanceForDisplay(current.DisplayId).TextScale == 1.5 && Appearance.TextScale == 1 &&
                Math.Abs(current.Presenter.FontSize - baseFont * 1.5) < .01,
                "pin resolves independent text scale without changing the main Settings display context");
            check(ReferenceEquals(durable, pinnedPreferences) && !current.Window.Interactive,
                "display appearance reconciliation neither persists temporary placement nor activates the pin");
            var revision = ++current.DisplayRevision;
            check(!TryApplyPinnedDisplay(current, revision - 1, "validation.main") && current.DisplayId == "validation.pin",
                "late monitor resolution cannot overwrite the pin's newer identity");
            var other = new PinnedSurface(current.Selection, current.Presenter, current.Window, current.Limits, current.Monitor);
            check(!TryApplyPinnedDisplay(other, other.DisplayRevision, "validation.main"),
                "retired surface monitor result cannot modify the current pin");
            check(TryApplyPinnedDisplay(current, revision, null) && current.Window.InterfaceScale == .9 && Appearance.InterfaceScale == .75,
                "unavailable pin monitor uses global defaults rather than another window's override");
            check(TryApplyPinnedDisplay(current, revision, "validation.pin") && current.Window.InterfaceScale == 1.25,
                "a current monitor result restores the pin's display preference");
            using var menu = NativePopupTheme.Menu(new MenuFlyout(), current.Presenter!);
            check(menu is not null, "ordinary pinned controls have their own native popup theme owner");
        }
        finally
        {
            ++current.DisplayRevision;
            savedAppearance = previousAppearance; activeDisplayId = previousMain; current.DisplayId = previousPin;
            ApplyEffectiveAppearance();
            current.Limits = previousLimits; current.Window.Place(previousBounds); current.LogicalPlacement = previousPlacement;
        }
    }
}
