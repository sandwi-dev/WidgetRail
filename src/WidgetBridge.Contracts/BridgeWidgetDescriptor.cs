using System.Text.Json.Serialization;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

public sealed record BridgeQuickActionDescriptor
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string ActionId { get; init; }
    public required string SourceElementId { get; init; }
    public ControllerButton? ControllerButton { get; init; }
}

public sealed record BridgePackageIconAssetDescriptor(
    [property: JsonPropertyName("id")] string AssetId,
    string SourceSha256,
    string NormalizedSha256,
    int SourceBytes,
    int NormalizedBytes);

public sealed record BridgeWidgetDescriptor
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string InstanceId { get; init; }
    public required string RuntimeGeneration { get; init; }
    public required string PresentationGeneration { get; init; }
    public required WidgetGlyph Icon { get; init; }
    public WidgetPackageIcon? PackageIcon { get; init; }
    public required string PackageContentDigest { get; init; }
    public IReadOnlyList<BridgePackageIconAssetDescriptor> IconAssets { get; init; } = [];
    public bool PinningSupported { get; init; }
    public bool FullWidgetPinningSupported { get; init; }
    /// <summary>
    /// Trusted host policy for the bundled Network Controls credential prompt.
    /// This is derived by the bridge and cannot be declared by a widget package.
    /// </summary>
    public bool ProtectedWifiPromptSupported { get; init; }
    public IReadOnlyList<BridgeQuickActionDescriptor> QuickActions { get; init; } = [];
}
