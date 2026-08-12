using System.Collections.ObjectModel;

namespace GameBarAlternative.LauncherExperienceCatalog;

public static class LauncherExperienceBuiltIns
{
    public const string Publisher = "org.gbar.builtin";
    public const string Version = "1.0.0";

    public static IReadOnlyList<LauncherExperiencePackage> RecoveryPackages { get; } =
        new ReadOnlyCollection<LauncherExperiencePackage>(
        [
            Create("hero-rail", "Hero Rail", LauncherLayoutPreset.HeroRail, BottomRail()),
            Create("cover-wall", "Cover Wall", LauncherLayoutPreset.CoverWall, CoverWall()),
            Create("carousel", "Carousel", LauncherLayoutPreset.Carousel, Carousel()),
            Create("compact-grid", "Compact Grid", LauncherLayoutPreset.CompactGrid, CompactGrid()),
        ]);

    public static LauncherExperiencePackage RecoveryFor(LauncherLayoutPreset preset) =>
        RecoveryPackages.Single(item => item.Descriptor.LayoutPreset == preset);

    public static LauncherLayoutRecipe BottomRailReferenceRecipe => BottomRail();
    public static LauncherLayoutRecipe LeftRailGlassPanelReferenceRecipe => LeftRail();

    private static LauncherExperiencePackage Create(
        string suffix,
        string name,
        LauncherLayoutPreset preset,
        LauncherLayoutRecipe recipe)
    {
        var id = $"{Publisher}.{suffix}";
        var descriptor = new LauncherExperienceDescriptor(id, Publisher, name, new Version(1, 0, 0), preset, true, $"builtin:{suffix}:1");
        var manifest = new LauncherExperienceManifest(
            1, id, Publisher, name, descriptor.Version, preset, null,
            "styles/launcher.gbss", "assets/preview.png", LauncherExperienceParameters.Empty);
        return new LauncherExperiencePackage(
            descriptor, manifest, recipe, "builtin", Array.AsReadOnly(Array.Empty<string>()));
    }

    private static LauncherLayoutRecipe BottomRail() => Recipe(
        Node(LauncherLayoutPrimitive.Overlay, LauncherRegion.Full,
            Slot(LauncherSlot.HeroBackground, LauncherRegion.Full),
            Slot(LauncherSlot.DetailsPanel, new(0.07, 0.08, 0.54, 0.42)),
            Slot(LauncherSlot.SourceStatus, new(0.67, 0.08, 0.26, 0.1)),
            Slot(LauncherSlot.GameRail, new(0.06, 0.62, 0.88, 0.25), LauncherOrientation.Horizontal),
            Slot(LauncherSlot.ControllerHints, new(0.58, 0.9, 0.36, 0.07))));

    private static LauncherLayoutRecipe LeftRail() => Recipe(
        Node(LauncherLayoutPrimitive.Overlay, LauncherRegion.Full,
            Slot(LauncherSlot.HeroBackground, LauncherRegion.Full),
            Slot(LauncherSlot.GameRail, new(0.04, 0.08, 0.22, 0.78), LauncherOrientation.Vertical),
            Slot(LauncherSlot.DetailsPanel, new(0.32, 0.18, 0.47, 0.5), surface: LauncherSurfaceRole.Glass),
            Slot(LauncherSlot.SourceStatus, new(0.81, 0.08, 0.15, 0.12)),
            Slot(LauncherSlot.ControllerHints, new(0.58, 0.9, 0.38, 0.07))));

    private static LauncherLayoutRecipe CoverWall() => Recipe(
        Node(LauncherLayoutPrimitive.Overlay, LauncherRegion.Full,
            Slot(LauncherSlot.HeroBackground, LauncherRegion.Full),
            Slot(LauncherSlot.GameRail, new(0.05, 0.18, 0.64, 0.66), LauncherOrientation.Vertical),
            Slot(LauncherSlot.DetailsPanel, new(0.72, 0.2, 0.23, 0.5)),
            Slot(LauncherSlot.SourceStatus, new(0.72, 0.08, 0.23, 0.08)),
            Slot(LauncherSlot.ControllerHints, new(0.58, 0.9, 0.37, 0.07))));

    private static LauncherLayoutRecipe Carousel() => Recipe(
        Node(LauncherLayoutPrimitive.Overlay, LauncherRegion.Full,
            Slot(LauncherSlot.HeroBackground, LauncherRegion.Full),
            Slot(LauncherSlot.DetailsPanel, new(0.12, 0.1, 0.52, 0.3)),
            Slot(LauncherSlot.SourceStatus, new(0.7, 0.1, 0.23, 0.08)),
            Slot(LauncherSlot.GameRail, new(0.08, 0.5, 0.84, 0.32), LauncherOrientation.Horizontal),
            Slot(LauncherSlot.ControllerHints, new(0.58, 0.9, 0.34, 0.07))));

    private static LauncherLayoutRecipe CompactGrid() => Recipe(
        Node(LauncherLayoutPrimitive.Overlay, LauncherRegion.Full,
            Slot(LauncherSlot.GameRail, new(0.04, 0.14, 0.68, 0.7), LauncherOrientation.Vertical),
            Slot(LauncherSlot.DetailsPanel, new(0.74, 0.18, 0.22, 0.44)),
            Slot(LauncherSlot.SourceStatus, new(0.74, 0.07, 0.22, 0.08)),
            Slot(LauncherSlot.ControllerHints, new(0.52, 0.9, 0.44, 0.07))));

    private static LauncherLayoutRecipe Recipe(LauncherLayoutNode root) =>
        new(1, new ReadOnlyDictionary<LauncherResponsiveBranch, LauncherLayoutNode>(
            new Dictionary<LauncherResponsiveBranch, LauncherLayoutNode>
            {
                [LauncherResponsiveBranch.Compact] = root,
                [LauncherResponsiveBranch.Standard] = root,
                [LauncherResponsiveBranch.Wide] = root,
            }));

    private static LauncherLayoutNode Node(LauncherLayoutPrimitive type, LauncherRegion region, params LauncherLayoutNode[] children) =>
        new(type, region, LauncherInsets.None, LauncherAlignment.Stretch, LauncherAlignment.Stretch,
            null, null, null, null, Array.AsReadOnly(children));

    private static LauncherLayoutNode Slot(
        LauncherSlot slot,
        LauncherRegion region,
        LauncherOrientation? orientation = null,
        LauncherSurfaceRole? surface = null,
        LauncherContentDensity? density = null) =>
        new(LauncherLayoutPrimitive.Region, region, LauncherInsets.None, LauncherAlignment.Stretch,
            LauncherAlignment.Stretch, slot, orientation, null, null,
            Array.AsReadOnly(Array.Empty<LauncherLayoutNode>()), surface, density);
}
