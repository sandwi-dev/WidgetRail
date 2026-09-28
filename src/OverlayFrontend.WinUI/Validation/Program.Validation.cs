namespace WidgetRail.OverlayFrontend.WinUI;

internal static partial class Program
{
    // Match ConfigureValidation's route precedence, not arbitrary --validate-* prefixes:
    // production switch/replay diagnostics still elect their real settings profile.
    static partial void IsValidationLaunch(IReadOnlyList<string> arguments, ref bool fixture)
    {
        string[] early = ["--validate-controller", "--replay-controller", "--validate-window-preview",
            "--validate-shell-status", "--validate-production-shell", "--validate-gamepad-boundary",
            "--validate-embedded-media", "--validate-package-icons", "--validate-shell-sizing",
            "--validate-shell-appearance", "--validate-shell-chrome", "--validate-pinned-window",
            "--validate-slider", "--validate-context-menu"];
        if (arguments.Any(early.Contains)) { fixture = true; return; }
        if (Shell.FrontendArguments.Value(arguments, "--shell-config") is not null) return;
        string[] remaining = ["--validate-text-entry", "--validate-styles", "--validate-glyphs",
            "--validate-modals", "--validate-motion", "--validate-select", "--validate-surfaces",
            "--validate-grouped", "--validate-grouped-flat", "--validate-grouped-adapted",
            "--validate-controls", "--validate-focus-policy", "--validate-indexed",
            "--validate-external-surface", "--validate-collection", "--validate-gridview", "--gallery"];
        fixture = arguments.Any(remaining.Contains) || Shell.FrontendArguments.Value(arguments, "--indexed-validation-pipe") is not null ||
            Shell.FrontendArguments.Value(arguments, "--widget-config") is not null;
    }
}
