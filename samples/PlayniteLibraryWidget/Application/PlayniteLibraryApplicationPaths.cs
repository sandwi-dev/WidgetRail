namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteLibraryApplicationPaths(string Root, string StateFile)
{
    private const string RootOverride = "WRAIL_PLAYNITE_LIBRARY_DATA_ROOT";

    internal static PlayniteLibraryApplicationPaths CreateDefault()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(RootOverride);
        var local = string.IsNullOrWhiteSpace(overrideRoot)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : overrideRoot;
        if (string.IsNullOrWhiteSpace(local) || !Path.IsPathFullyQualified(local))
            throw new InvalidOperationException("Playnite Library local state is unavailable.");
        var productRoot = string.IsNullOrWhiteSpace(overrideRoot)
            ? Path.Combine(local, "WidgetRail")
            : Path.GetFullPath(local);
        var root = Path.Combine(productRoot, "community-apps",
            "widgetrail.samples.playnite-library");
        return new(root, Path.Combine(root, "organization.json"));
    }
}
