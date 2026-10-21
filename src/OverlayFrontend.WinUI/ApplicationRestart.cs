namespace WidgetRail.OverlayFrontend.WinUI;

internal enum ApplicationRestartMode { Visible, Hidden }

internal static class ApplicationRestart
{
    internal static string[] Arguments(IReadOnlyList<string> arguments, ApplicationRestartMode mode) =>
        [.. arguments.Where(argument => argument is not ("--hidden" or "--show")),
            mode == ApplicationRestartMode.Hidden ? "--hidden" : "--show"];
}
