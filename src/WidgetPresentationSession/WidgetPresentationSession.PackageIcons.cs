using System.Security.Cryptography;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetPresentationSession;

/// <summary>Bytes admitted by the bridge's manifest inventory and normalized SVG validator.</summary>
public sealed record WidgetPresentationPackageIcon(string AssetId, string NormalizedSha256, ReadOnlyMemory<byte> NormalizedSvg);

public sealed partial class WidgetPresentationSession
{
    private readonly record struct PackageIconKey(string WidgetId, string Runtime, string Presentation, string Digest, string AssetId, string SourceHash, string Hash);
    private readonly Dictionary<PackageIconKey, byte[]> _packageIcons = [];
    private int _packageIconBytes;
    private int _pendingPackageIcons;
    private readonly SemaphoreSlim _packageIconSlots = new(4, 4);

    /// <summary>
    /// Resolve a manifest-declared static icon through bridge package authority.
    /// Neither filesystem paths nor SVG supplied by a widget are accepted.
    /// Snapshot changes do not retire static assets; catalog replacement does.
    /// </summary>
    public async Task<WidgetPresentationPackageIcon> ResolvePackageIconAsync(WidgetPresentationTarget target,
        string assetId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!WidgetManifestValidator.IsPackageIconAssetId(assetId)) throw new ArgumentException("Invalid icon asset ID.", nameof(assetId));
        cancellationToken.ThrowIfCancellationRequested();
        var descriptor = target.Descriptor;
        BridgePackageIconAssetDescriptor asset;
        PackageIconKey key;
        using (_gate.Enter())
        {
            _ = ValidateTarget(target);
            if (descriptor.IconAssets is null || descriptor.IconAssets.Count > ProtocolConstants.MaximumPackageIconAssetCount ||
                descriptor.IconAssets.Any(candidate => candidate is null) ||
                descriptor.IconAssets.Select(candidate => candidate.AssetId).Distinct(StringComparer.Ordinal).Count() != descriptor.IconAssets.Count)
                throw new BridgeProtocolException("Package icon inventory is invalid.");
            asset = descriptor.IconAssets.SingleOrDefault(candidate => candidate.AssetId == assetId)
                ?? throw new WidgetPresentationSessionException("package_icon_unavailable", "The package icon is not in the admitted inventory.");
            ValidatePackageIconMetadata(descriptor, asset);
            key = new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, descriptor.PackageContentDigest, assetId, asset.SourceSha256, asset.NormalizedSha256);
            if (_packageIcons.TryGetValue(key, out var cached)) return new(assetId, asset.NormalizedSha256, cached.ToArray());
            if (_pendingPackageIcons >= 16) throw new WidgetPresentationSessionException("package_icon_saturated", "Package icon demand capacity is full.");
            ++_pendingPackageIcons;
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        deadline.CancelAfter(_options.ArtworkTimeout);
        var admitted = false;
        try
        {
            await _packageIconSlots.WaitAsync(deadline.Token).ConfigureAwait(false); admitted = true;
            using (_gate.Enter())
            {
                _ = ValidateTarget(target);
                if (_packageIcons.TryGetValue(key, out var cached)) return new(assetId, asset.NormalizedSha256, cached.ToArray());
            }
            var exchange = RequestAsync(BridgeMessageTypes.ResolvePackageIcon,
                new BridgePackageIconRequest(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration,
                    descriptor.PackageContentDigest, assetId, asset.SourceSha256, asset.NormalizedSha256),
                BridgeMessageTypes.PackageIcon, deadline.Token);
            _ = exchange.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            var reply = await exchange.WaitAsync(deadline.Token).ConfigureAwait(false);
            var payload = reply.Payload;
            RequireObjectProperties(payload, "widgetId", "runtimeGeneration", "presentationGeneration", "packageContentDigest",
                "assetId", "sourceSha256", "normalizedSha256", "normalizedSvgBase64");
            if (ReadString(payload, "widgetId") != descriptor.Id || ReadString(payload, "runtimeGeneration") != descriptor.RuntimeGeneration ||
                ReadString(payload, "presentationGeneration") != descriptor.PresentationGeneration || ReadString(payload, "packageContentDigest") != descriptor.PackageContentDigest ||
                ReadString(payload, "assetId") != assetId || ReadString(payload, "sourceSha256") != asset.SourceSha256 || ReadString(payload, "normalizedSha256") != asset.NormalizedSha256)
                throw new BridgeProtocolException("Package icon response authority does not match the request.");
            var encoded = payload.GetProperty("normalizedSvgBase64");
            if (encoded.ValueKind != System.Text.Json.JsonValueKind.String || encoded.GetString() is not { } text ||
                text.Length > ((ProtocolConstants.MaximumPackageIconBytes + 2) / 3) * 4)
                throw new BridgeProtocolException("Package icon payload exceeds its bound.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(text); }
            catch (FormatException) { throw new BridgeProtocolException("Package icon payload is not valid base64."); }
            if (bytes.Length != asset.NormalizedBytes || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(asset.NormalizedSha256, StringComparison.OrdinalIgnoreCase))
                throw new BridgeProtocolException("Package icon payload does not match admitted bytes and hash.");
            deadline.Token.ThrowIfCancellationRequested();
            using (_gate.Enter())
            {
                _ = ValidateTarget(target);
                // Aggregate session retention is bounded independently of widget count.
                if (!_packageIcons.ContainsKey(key))
                {
                    while (_packageIcons.Count >= 128 || _packageIconBytes + bytes.Length > 2 * 1024 * 1024)
                    { var first = _packageIcons.First(); _packageIconBytes -= first.Value.Length; _packageIcons.Remove(first.Key); }
                    _packageIcons.Add(key, bytes); _packageIconBytes += bytes.Length;
                }
            }
            return new(assetId, asset.NormalizedSha256, bytes.ToArray());
        }
        finally { if (admitted) _packageIconSlots.Release(); using (_gate.Enter()) --_pendingPackageIcons; }
    }

    private static void ValidatePackageIconMetadata(BridgeWidgetDescriptor descriptor, BridgePackageIconAssetDescriptor asset)
    {
        static bool Hash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
        if (!Hash(descriptor.PackageContentDigest) || !Hash(asset.SourceSha256) || !Hash(asset.NormalizedSha256) ||
            asset.SourceBytes is < 1 or > ProtocolConstants.MaximumPackageIconBytes ||
            asset.NormalizedBytes is < 1 or > ProtocolConstants.MaximumPackageIconBytes ||
            descriptor.IconAssets.Count > ProtocolConstants.MaximumPackageIconAssetCount ||
            descriptor.IconAssets.Sum(item => (long)item.NormalizedBytes) > ProtocolConstants.MaximumPackageIconAggregateBytes)
            throw new BridgeProtocolException("Package icon inventory metadata is invalid.");
    }
}
