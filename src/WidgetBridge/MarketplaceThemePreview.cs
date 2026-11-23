using System.Security.Cryptography;
using WidgetRail.PackageDistribution;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetStyling;

namespace WidgetRail.WidgetBridge;

internal static class MarketplaceThemePreview
{
    // The caller owns the downloaded bytes. Verify the same stable copy that
    // the archive reader will inspect. Preview never acquires an installed slot.
    internal static async Task<IReadOnlyDictionary<string, BridgeNodeRenderStyles>> ReadAsync(
        ReadOnlyMemory<byte> package, string expectedId, string expectedVersion, string expectedSha256,
        ThemeCatalog catalog, CancellationToken token, string? expectedPublisher = null, string? expectedName = null)
    {
        var inspection = await InspectAsync(package, expectedId, expectedVersion, expectedSha256, token).ConfigureAwait(false);
        if (expectedPublisher is not null && inspection.Manifest.Publisher != expectedPublisher ||
            expectedName is not null && inspection.Manifest.Name != expectedName)
            throw new InvalidDataException("The theme metadata does not match its catalogue listing.");
        var compiled = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package, new([], []), inspection.VerifiedStyles);
        if (!compiled.IsValid) throw new InvalidDataException("The verified theme could not be composed with the platform theme.");
        return PlatformAppearanceService.ResolveShellStyles(compiled.Theme!).ToDictionary(role => role.Key,
            role => new BridgeNodeRenderStyles { Base = role.Value, Focused = role.Value, Pressed = role.Value });
    }

    internal static async Task<ThemePackageInspection> InspectAsync(ReadOnlyMemory<byte> package, string expectedId,
        string expectedVersion, string expectedSha256, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (package.Length == 0 || package.Length > ThemePackage.MaximumArchiveBytes)
            throw new InvalidDataException("The theme package size is outside the supported range.");
        var bytes = package.ToArray();
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The theme package differs from the reviewed version.");
        using var stream = new MemoryStream(bytes, writable: false);
        var inspection = await ThemePackage.InspectArchiveAsync(stream, token).ConfigureAwait(false);
        if (inspection.Manifest.Id != expectedId || inspection.Manifest.Version != expectedVersion)
            throw new InvalidDataException("The theme manifest does not match its catalogue listing.");
        return inspection;
    }
}
