using System.Collections.ObjectModel;

namespace GameBarAlternative.LauncherExperienceCatalog;

public enum LauncherLayoutPreset
{
    HeroRail,
    CoverWall,
    Carousel,
    CompactGrid,
}

public enum LauncherResponsiveBranch
{
    Compact,
    Standard,
    Wide,
}

public enum LauncherLayoutPrimitive
{
    Region,
    Grid,
    Stack,
    Overlay,
    Inset,
}

public enum LauncherSlot
{
    HeroBackground,
    GameRail,
    DetailsPanel,
    CollectionTabs,
    SourceStatus,
    OperationStatus,
    SystemStatus,
    ControllerHints,
}

public enum LauncherOrientation
{
    Horizontal,
    Vertical,
}

public enum LauncherAlignment
{
    Start,
    Center,
    End,
    Stretch,
}

public enum LauncherSurfaceRole
{
    Solid,
    Glass,
}

public enum LauncherContentDensity
{
    Compact,
    Standard,
    Expanded,
}

public sealed record LauncherRegion(double X, double Y, double Width, double Height)
{
    public static LauncherRegion Full { get; } = new(0, 0, 1, 1);
}

public sealed record LauncherInsets(double Left, double Top, double Right, double Bottom)
{
    public static LauncherInsets None { get; } = new(0, 0, 0, 0);
}

public sealed record LauncherLayoutNode(
    LauncherLayoutPrimitive Type,
    LauncherRegion Region,
    LauncherInsets Insets,
    LauncherAlignment HorizontalAlignment,
    LauncherAlignment VerticalAlignment,
    LauncherSlot? Slot,
    LauncherOrientation? Orientation,
    int? Rows,
    int? Columns,
    IReadOnlyList<LauncherLayoutNode> Children,
    LauncherSurfaceRole? Surface = null,
    LauncherContentDensity? Density = null);

public sealed record LauncherLayoutRecipe(
    int SchemaVersion,
    IReadOnlyDictionary<LauncherResponsiveBranch, LauncherLayoutNode> Branches);

public sealed record LauncherExperienceParameters(
    string? BackgroundMode,
    string? Accent,
    string? TileSize,
    string? MetadataDensity,
    string? MotionIntensity,
    bool? ShowSystemStatus,
    string? FocusEffect)
{
    public static LauncherExperienceParameters Empty { get; } = new(null, null, null, null, null, null, null);
}

public sealed record LauncherExperienceManifest(
    int SchemaVersion,
    string Id,
    string Publisher,
    string Name,
    Version Version,
    LauncherLayoutPreset LayoutPreset,
    string? CompositionFile,
    string StyleFile,
    string PreviewFile,
    LauncherExperienceParameters Parameters);

public sealed record LauncherExperienceDescriptor(
    string Id,
    string Publisher,
    string Name,
    Version Version,
    LauncherLayoutPreset LayoutPreset,
    bool IsBuiltIn,
    string ContentDigest);

public sealed record LauncherExperienceDiagnostic(string Path, string Code, string Message);

public sealed record LauncherExperiencePackage(
    LauncherExperienceDescriptor Descriptor,
    LauncherExperienceManifest Manifest,
    LauncherLayoutRecipe? Recipe,
    string PackageRoot,
    IReadOnlyList<string> Files);

public sealed record LauncherExperienceValidationResult(
    LauncherExperiencePackage? Package,
    IReadOnlyList<LauncherExperienceDiagnostic> Diagnostics)
{
    public bool IsValid => Package is not null && Diagnostics.Count == 0;

    internal static LauncherExperienceValidationResult Invalid(List<LauncherExperienceDiagnostic> diagnostics) =>
        new(null, new ReadOnlyCollection<LauncherExperienceDiagnostic>(diagnostics));
}

public sealed record LauncherExperienceCatalogEntry(
    LauncherExperienceDescriptor Descriptor,
    bool IsValid,
    IReadOnlyList<LauncherExperienceDiagnostic> Diagnostics,
    LauncherExperiencePackage? Package = null);

public sealed record LauncherExperienceCatalogSnapshot(IReadOnlyList<LauncherExperienceCatalogEntry> Experiences);
