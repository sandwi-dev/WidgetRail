using System.Security.Cryptography;
using System.Text.Json;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    public Task<WidgetPresentationPackageIcon> ResolvePackageIconAsync(WidgetPresentationTarget target,
        WidgetPackageIcon icon, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(icon);
        return icon.Catalog is null ? ResolvePackageIconAsync(target, icon.AssetId, cancellationToken)
            : ResolveCatalogIconAsync(target, icon, cancellationToken);
    }

    private async Task<WidgetPresentationPackageIcon> ResolveCatalogIconAsync(WidgetPresentationTarget target,
        WidgetPackageIcon icon, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!WidgetCatalogIconValidation.IsValid(icon.Catalog!) || !WidgetManifestValidator.IsPackageIconAssetId(icon.AssetId))
            throw new ArgumentException("Invalid catalog icon identity.", nameof(icon));
        token.ThrowIfCancellationRequested();
        var descriptor = target.Descriptor;
        var source = icon.Catalog!;
        // Same bounded byte cache, demand pool and transport slots as package icons.
        // The catalog revision also retires disabled-package management references.
        var key = new PackageIconKey(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration,
            $"catalog:{target.CatalogRevision}:{source.BuiltIn}:{source.PackageId}:{source.Version}:{source.ContentDigest}", icon.AssetId, "", "");
        TaskCompletionSource<byte[]> demand;
        CancellationTokenSource? deadline = null;
        using (_gate.Enter())
        {
            _ = ValidateTarget(target);
            if (_packageIcons.TryGetValue(key, out var cached)) return Result(cached);
            if (!_packageIconDemands.TryGetValue((target.CatalogRevision, key), out demand!))
            {
                if (_packageIconDemands.Count >= 16)
                    throw new WidgetPresentationSessionException("package_icon_saturated", "Package icon demand capacity is full.");
                demand = new(TaskCreationOptions.RunContinuationsAsynchronously);
                deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                deadline.CancelAfter(_options.ArtworkTimeout);
                _packageIconDemands.Add((target.CatalogRevision, key), demand);
                _ = demand.Task.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        if (deadline is not null)
            _ = CompletePackageIconDemandAsync(target, key, demand, deadline, LoadAsync);
        var bytes = await demand.Task.WaitAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        using (_gate.Enter()) { _ = ValidateTarget(target); return Result(bytes); }

        WidgetPresentationPackageIcon Result(byte[] data) => new(icon.AssetId, Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(), data.ToArray());

        async Task<byte[]> LoadAsync(CancellationToken loadToken)
        {
            await _packageIconSlots.WaitAsync(loadToken).ConfigureAwait(false);
            try
            {
                using (_gate.Enter()) { _ = ValidateTarget(target); }
                var reply = await RequestAsync(BridgeMessageTypes.ResolvePackageIcon,
                    new BridgePackageIconRequest(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration,
                        "", icon.AssetId, "", "", source), BridgeMessageTypes.PackageIcon, loadToken).ConfigureAwait(false);
                var payload = reply.Payload;
                RequireObjectProperties(payload, "widgetId", "runtimeGeneration", "presentationGeneration", "catalog", "assetId",
                    "sourceSha256", "normalizedSha256", "normalizedBytes", "normalizedSvgBase64");
                if (ReadString(payload, "widgetId") != descriptor.Id || ReadString(payload, "runtimeGeneration") != descriptor.RuntimeGeneration ||
                    ReadString(payload, "presentationGeneration") != descriptor.PresentationGeneration || ReadString(payload, "assetId") != icon.AssetId ||
                    BridgeJson.FromElement<WidgetCatalogIconSource>(payload.GetProperty("catalog")) != source)
                    throw new BridgeProtocolException("Catalog icon response authority does not match the request.");
                var hash = ReadString(payload, "normalizedSha256");
                var sourceHash = ReadString(payload, "sourceSha256");
                var encoded = payload.GetProperty("normalizedSvgBase64");
                if (payload.GetProperty("normalizedBytes").ValueKind != JsonValueKind.Number || !payload.GetProperty("normalizedBytes").TryGetInt32(out var length) || length is < 1 or > ProtocolConstants.MaximumPackageIconBytes ||
                    hash is not { Length: 64 } || !hash.All(Uri.IsHexDigit) || sourceHash is not { Length: 64 } || !sourceHash.All(Uri.IsHexDigit) ||
                    encoded.ValueKind != JsonValueKind.String || encoded.GetString() is not { } text ||
                    text.Length > ((ProtocolConstants.MaximumPackageIconBytes + 2) / 3) * 4)
                    throw new BridgeProtocolException("Catalog icon payload exceeds its bound.");
                byte[] result;
                try { result = Convert.FromBase64String(text); }
                catch (FormatException) { throw new BridgeProtocolException("Catalog icon is not valid base64."); }
                if (result.Length != length || !Convert.ToHexString(SHA256.HashData(result)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                    throw new BridgeProtocolException("Catalog icon payload does not match its verified hash.");
                loadToken.ThrowIfCancellationRequested();
                using (_gate.Enter()) { _ = ValidateTarget(target); CachePackageIconLocked(key, result); }
                return result;
            }
            finally { _packageIconSlots.Release(); }
        }
    }
}
