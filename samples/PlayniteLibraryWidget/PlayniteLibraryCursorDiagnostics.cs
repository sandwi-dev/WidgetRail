using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

// Candidate-only diagnostics: counts and closed state values, never game identities,
// search text, credentials, provider payloads, or exception messages.
internal static class PlayniteLibraryCursorDiagnostics
{
    private static readonly object Gate = new();
    internal static void Record(string stage, string code, PlayniteLibraryRoute route,
        WidgetLifecycleState lifecycle, WidgetCursorResourceSnapshot<PlayniteLibraryItem> snapshot,
        bool busy, int resultCount = -1)
    {
        if (Environment.GetEnvironmentVariable("WRAIL_CURSOR_TEST") != "1") return;
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WidgetRail", "community-apps", "widgetrail.samples.playnite-library", "cursor-diagnostics.log");
            var line = FormattableString.Invariant(
                $"{DateTimeOffset.UtcNow:O} pid={Environment.ProcessId} stage={stage} code={code} route={route} lifecycle={lifecycle} state={snapshot.Status} busy={busy} items={snapshot.Items.Count} before={snapshot.HasBefore} after={snapshot.HasAfter} revision={snapshot.Revision} generation={snapshot.WindowGeneration} result-count={resultCount}");
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length >= 2 * 1024 * 1024)
                    File.WriteAllText(path, string.Empty);
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch (Exception) { }
    }
}

