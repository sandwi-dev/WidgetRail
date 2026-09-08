using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class PackageSvgIconTests
{
    internal static Task Run()
    {
        var tinted = WidgetIcon.PackageSvg(
            "controls.play", WidgetPackageIconColorMode.ThemeTint, WidgetGlyph.Play);
        var original = WidgetIcon.PackageSvg(
            "brand.mark", WidgetPackageIconColorMode.OriginalColor, WidgetGlyph.Music);
        var view = new WidgetView(
            UI.Stack("icons.root",
                UI.Button("Play", "play", "icons.play").Icon(tinted),
                UI.Icon(original, "icons.brand", "Brand"),
                UI.Select("Mode",
                [
                    new SelectOption("one", "One", "select.one", true)
                        { Icon = tinted },
                    new SelectOption("two", "Two", "select.two", false,
                        WidgetGlyph.Settings),
                ], "icons.select"),
                UI.Tile("Game", "Ready", "open.game", "icons.tile",
                    artwork: TileArtwork.FromPackageSvg(
                        "brand.mark", WidgetPackageIconColorMode.OriginalColor,
                        WidgetGlyph.Music, "Game artwork")),
                UI.SettingsRow(tinted, "Playback", new("Open", "settings.open"),
                    "icons.settings"),
                UI.ValueRow(tinted, "Quality", "High", "icons.value"),
                UI.ChoiceRow(tinted, "Living room", "choice.room", "icons.choice"),
                UI.StatusBadge(tinted, "Online", StatusTone.Success, "icons.status"),
                UI.Alert(tinted, "Attention", "Check playback", AlertTone.Warning,
                    "icons.alert"),
                UI.EmptyState(tinted, "Nothing queued", "Choose a track", "icons.empty"),
                UI.ToastWithIcon(tinted, "Saved", "Playback setting saved",
                    ToastTone.Success, "icons.toast")),
            InitialFocusId: "icons.play");
        var snapshot = view.CreateSnapshot("icons.instance", 1);

        Require(snapshot.ProtocolVersion == ProtocolConstants.PackageSvgIconVersion,
            "Package SVG icon snapshots must negotiate protocol v46.");
        var button = Find(snapshot.Root, "icons.play");
        Require(button.Glyph == WidgetGlyph.Play &&
                button.PackageIcon == new WidgetPackageIcon(
                    "controls.play", WidgetPackageIconColorMode.ThemeTint),
            "Button package icon must retain exact asset, mode, and fallback glyph.");
        var icon = Find(snapshot.Root, "icons.brand");
        Require(icon.Glyph == WidgetGlyph.Music &&
                icon.PackageIcon?.ColorMode == WidgetPackageIconColorMode.OriginalColor,
            "Icon package visual must preserve its semantic fallback.");
        var select = Find(snapshot.Root, "icons.select");
        Require(select.SelectOptions![0].PackageIcon?.AssetId == "controls.play" &&
                select.SelectOptions[0].Glyph == WidgetGlyph.Play,
            "Select option icon authority must materialize without changing action identity.");
        var tileIcon = Find(snapshot.Root, "icons.tile.artwork");
        Require(tileIcon.PackageIcon?.AssetId == "brand.mark" &&
                tileIcon.Glyph == WidgetGlyph.Music,
            "Tile package artwork must reuse the same icon primitive and fallback.");
        foreach (var id in new[]
                 {
                     "icons.settings.icon", "icons.value.icon", "icons.choice",
                     "icons.status.icon", "icons.alert.icon", "icons.empty.icon",
                     "icons.toast.icon",
                 })
        {
            var compound = Find(snapshot.Root, id);
            Require(compound.Glyph == WidgetGlyph.Play &&
                    compound.PackageIcon?.AssetId == "controls.play",
                $"Compound component '{id}' did not retain canonical WidgetIcon materialization.");
        }
        _ = UI.Toast("Legacy", "Null duration stays source-compatible",
            ToastTone.Neutral, "legacy.toast", null);

        var manifest = new WidgetManifest
        {
            Id = "dev.test.package-icons",
            Publisher = "dev.test",
            Name = "Package icons",
            Version = "1.0.0",
            HostApi = new("1.0", 1),
            Entrypoint = new(WidgetEntrypointRuntimes.DotNetWorker,
                Assembly: "Widget.dll", Type: "Test.Widget"),
            IconAssets = new Dictionary<string, WidgetPackageIconAsset>(StringComparer.Ordinal)
            {
                ["controls.play"] = new("assets/play.svg"),
                ["brand.mark"] = new("assets/brand.svg"),
            },
            Presentation = new WidgetPresentation(WidgetGlyph.Connection)
            {
                PackageIcon = new(
                    "brand.mark", WidgetPackageIconColorMode.OriginalColor),
            },
        };
        Require(WidgetManifestValidator.Validate(manifest).Count == 0,
            "A declared package icon inventory and presentation reference must validate.");
        var missing = manifest with
        {
            Presentation = manifest.Presentation with
            {
                PackageIcon = new(
                    "missing", WidgetPackageIconColorMode.ThemeTint),
            },
        };
        Require(WidgetManifestValidator.Validate(missing).Any(
                error => error.Code == "undeclared_icon_asset"),
            "Undeclared package icon references must fail closed.");
        RequireThrows<ArgumentException>(() => WidgetIcon.PackageSvg(
            "../escape", WidgetPackageIconColorMode.ThemeTint, WidgetGlyph.Play));
        return Task.CompletedTask;
    }

    private static ViewNode Find(ViewNode root, string id)
    {
        if (root.Id == id) return root;
        foreach (var child in root.Children)
        {
            var found = FindOrNull(child, id);
            if (found is not null) return found;
        }
        throw new InvalidOperationException($"Node '{id}' was not found.");
    }

    private static ViewNode? FindOrNull(ViewNode root, string id)
    {
        if (root.Id == id) return root;
        foreach (var child in root.Children)
        {
            var found = FindOrNull(child, id);
            if (found is not null) return found;
        }
        return null;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireThrows<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
