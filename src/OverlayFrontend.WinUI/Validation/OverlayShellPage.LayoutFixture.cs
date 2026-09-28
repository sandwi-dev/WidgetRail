using Microsoft.UI.Xaml;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void ApplyLayoutFixture(WidgetPresentationFrame frame, AppearanceSettings appearance,
        IReadOnlyList<BridgeWidgetDescriptor> catalog)
    {
        if (startup is not null || owner is not null) throw new InvalidOperationException("Layout fixture cannot alter a running bridge shell.");
        Appearance = appearance;
        authoredSurfaceHints = frame.Snapshot.Surface;
        if (!catalogItems.SequenceEqual(catalog))
        {
            catalogItems.Clear();
            foreach (var descriptor in catalog) catalogItems.Add(descriptor);
        }
        if (surface is null)
        {
            surface = new WidgetViewPresenter();
            surface.SetAutomaticFocusEnabled(false);
            WidgetSurfaces.Children.Add(surface);
        }
        surface.Apply(frame);
        ShowPresentationStatus(frame.Descriptor.Name);
    }
    internal async Task<IReadOnlyList<string>> ValidateRecoveryGuideFixtureAsync()
    {
        if (startup is not null || owner is not null) throw new InvalidOperationException("Recovery fixture cannot alter a running bridge shell.");
        var checks = new List<string>();
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
        SetInteractive(false);
        ShowRecovery("Fixture recovery", true);
        UpdateLayout();
        Check(trayGuide.HelpText.Contains("Open widget") && !trayGuide.HelpText.Contains("Retry"),
            "background recovery preserves normal tray input hints");
        SetInteractive(true);
        UpdateLayout();
        Check(trayGuide.DisplayedHints.Any(hint => hint.Button == ControllerButton.A && hint.Label == "Retry") &&
            !trayGuide.DisplayedHints.Any(hint => hint.Button == ControllerButton.Y),
            "interactive recovery replaces underlying widget shortcuts with Retry");
        ShowRecovery("Fixture cannot retry", false);
        UpdateLayout();
        Check(trayGuide.DisplayedHints.All(hint => hint.Button != ControllerButton.A) &&
            trayGuide.DisplayedHints.Any(hint => hint.Button == ControllerButton.B),
            "nonretryable recovery retains Back without advertising A");
        await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
        await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
        UpdateLayout();
        Check(!interactive && trayGuide.HelpText.Contains("Open widget"), "recovery Back uses existing routing and restores tray guide");
        SetInteractive(true);
        ShowPresentationStatus("Fixture ready");
        UpdateLayout();
        Check(!RecoveryVisible && trayGuide.DisplayedHints.Any(hint => hint.Button == ControllerButton.Y && hint.Label == "Fixture refresh") &&
            !trayGuide.HelpText.Contains("Retry"), "successful recovery restores current widget declarations");
        return checks;
    }

    internal async Task DisposeLayoutFixtureAsync()
    {
        DisposeShellChrome();
        if (surface is not null) { await surface.DisposeAsync(); surface = null; }
        await DisposeAsync();
    }
}
