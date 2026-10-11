using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformDiagnostics;

public static class MarketplaceContract
{
    public static void ValidateCommand(MarketplaceCommand command)
    { if (MarketplaceValidation.CommandError(command) is { } error) throw new PlatformDiagnosticsException(error); }
    public static void ValidateSnapshot(MarketplaceSnapshot snapshot)
    { if (MarketplaceValidation.SnapshotError(snapshot) is { } error) throw new PlatformDiagnosticsException(error); }
}