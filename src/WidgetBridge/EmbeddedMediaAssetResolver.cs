using System.Security.Cryptography;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

internal static class EmbeddedMediaAssetResolver
{
    internal static BridgeEmbeddedMediaBundle Resolve(
        BridgeEmbeddedMediaRequest request,
        BridgeClientSnapshot publication)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(publication);
        var media = publication.Snapshot.EmbeddedMedia ??
            throw new BridgeProtocolException("The current snapshot has no embedded media surface.");
        var configured = publication.Configured;
        if (string.IsNullOrWhiteSpace(configured.PackageRoot) ||
            configured.VerifiedPackageFiles.Count == 0)
            throw new BridgeProtocolException(
                "Embedded media requires a sealed package-local asset inventory.");

        var packageRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(configured.PackageRoot));
        var resources = new List<BridgeEmbeddedMediaResource>();
        long aggregate = 0;
        foreach (var resource in media.Resources)
        {
            if (!configured.VerifiedPackageFiles.TryGetValue(
                    resource.Path, out var verified))
                throw new BridgeProtocolException(
                    "An embedded media resource is absent from the sealed package inventory.");
            if (verified.Length is < 0 or > ProtocolConstants.MaximumEmbeddedMediaResourceBytes)
                throw new BridgeProtocolException(
                    "An embedded media resource exceeds the bounded transfer limit.");
            aggregate = checked(aggregate + verified.Length);
            if (aggregate > ProtocolConstants.MaximumEmbeddedMediaAggregateBytes)
                throw new BridgeProtocolException(
                    "Embedded media resources exceed the aggregate transfer limit.");

            var fullPath = Path.GetFullPath(
                resource.Path.Replace('/', Path.DirectorySeparatorChar), packageRoot);
            var relative = Path.GetRelativePath(packageRoot, fullPath);
            if (relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                Path.IsPathRooted(relative))
                throw new BridgeProtocolException(
                    "An embedded media resource escaped its sealed package root.");
            RejectReparsePoints(packageRoot, fullPath);
            byte[] bytes;
            try
            {
                using var input = new FileStream(
                    fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    64 * 1024, FileOptions.SequentialScan);
                if (input.Length != verified.Length)
                    throw new BridgeProtocolException(
                        "An embedded media resource changed after package verification.");
                bytes = new byte[verified.Length];
                input.ReadExactly(bytes);
            }
            catch (BridgeProtocolException) { throw; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new BridgeProtocolException(
                    "An embedded media resource could not be read safely.", exception);
            }

            try
            {
                var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (!string.Equals(digest, verified.Sha256, StringComparison.Ordinal))
                    throw new BridgeProtocolException(
                        "An embedded media resource failed sealed digest validation.");
                resources.Add(new(
                    resource.Path, resource.ContentType, digest,
                    Convert.ToBase64String(bytes)));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        var descriptor = configured.PublicDescriptor();
        return new(
            request.WidgetId,
            publication.Snapshot.WidgetInstanceId,
            descriptor.RuntimeGeneration,
            descriptor.PresentationGeneration,
            publication.Snapshot.Sequence,
            media.Id,
            media.EntryAsset,
            media.Surface,
            media.AspectRatio,
            media.AccessibleName,
            media.Commands,
            media.AllowedFrameOrigins,
            media.AllowedFrameDomainFamilies,
            media.PendingCommand,
            media.CompactPinnedPresentation,
            media.CompactPinnedSeekStepSeconds,
            resources);
    }

    private static void RejectReparsePoints(string root, string target)
    {
        var current = root;
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new BridgeProtocolException(
                "The embedded media package root is a reparse point.");
        var relative = Path.GetRelativePath(root, target);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new BridgeProtocolException(
                    "An embedded media resource traverses a reparse point.");
        }
    }
}
