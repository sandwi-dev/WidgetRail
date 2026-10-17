namespace WidgetRail.WidgetProtocol;

/// <summary>Shared installation/resource bounds, independent of view-message budgets.</summary>
public static class WidgetPackageLimits
{
    public const long MaximumEntryBytes = 32L * 1024 * 1024;
    public const long MaximumExpandedBytes = 256L * 1024 * 1024;
    public const long MaximumDownloadBytes = 272L * 1024 * 1024;
    public const long MaximumRetainedUpdateBytes = 544L * 1024 * 1024;
}
