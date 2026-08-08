namespace GameBarAlternative.WidgetProtocol;

public static class ProtocolConstants
{
    public const int MinimumSupportedVersion = 1;
    public const int BaselineVersion = 1;
    public const int CurrentVersion = 8;
    public const int ScrollContainerVersion = 2;
    public const int SurfaceHintsVersion = 2;
    public const int SliderVersion = 3;
    public const int DashboardGestureAuthorityVersion = 4;
    public const int LoadingIndicatorVersion = 5;
    public const int InlinePngImageVersion = 6;
    public const int ActionSurfaceVersion = 7;
    public const int ResponsiveGridVersion = 8;
    public const double MinimumSurfaceWidth = 240;
    public const double MaximumSurfaceWidth = 1_600;
    public const double MinimumSurfaceHeight = 180;
    public const double MaximumSurfaceHeight = 1_200;
    public const int CurrentManifestVersion = 1;
    // Package host API and per-snapshot declarative protocol evolve
    // independently. Optional protocol-v2 nodes do not invalidate API-1 apps.
    public const int CurrentHostApiMajor = 1;
    public const int MaximumTreeDepth = 32;
    public const int MaximumNodeCount = 2_048;
    public const int MaximumStringLength = 4_096;
    public const int MaximumInlinePngBytes = 12 * 1024;
    public const int MaximumInlinePngDimension = 64;
    public const int MaximumInlinePngSourceLength =
        22 + ((MaximumInlinePngBytes + 2) / 3) * 4;
    public const int MaximumQuickActionCount = 3;
    public const int MaximumActionSurfaceDirectChildren = 8;
    public const int MaximumActionSurfaceDescendants = 32;
    public const int MaximumActionSurfaceRelativeDepth = 4;
    public const double MinimumGridColumnWidth = 44;
    public const double MaximumGridColumnWidth = 1_600;
    public const int MaximumGridColumns = 32;
    public const int MaximumStyleClassCount = 32;
    public const int MaximumStyleClassLength = 64;
    public const int MaximumManifestPermissionCount = 32;
    public const int MaximumCapabilityIdLength = 128;
}
