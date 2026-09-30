namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal static class StartupCatalogPolicy
{
    // Only already-admitted descriptors may open. An explicit initial widget
    // keeps its priority; a missing choice falls back only after discovery ends.
    internal static bool CanOpenInitial(bool complete, string? requested, IEnumerable<string> admittedIds) =>
        complete || admittedIds.Contains(requested ?? "settings", StringComparer.Ordinal);
}
