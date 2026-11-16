using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WidgetRail.PlatformBroker;

public sealed partial class CaptureLibraryStore
{
    private static string SourceRetiredPath(string directory, string id) => Path.Combine(directory, "source-" + id + ".retired");
    private static string SourceOwnerRetiredPath(string directory, string id) => Path.Combine(directory, "source-" + id + ".owner-retired");
    private static string SourceAccessPath(string directory, string id, BrokerWidgetIdentity identity) => Path.Combine(directory,
        "source-" + id + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', identity.PackageId, identity.PublisherId, identity.InstanceId)))).ToLowerInvariant() + ".access");
    private static bool SourceMarkerExists(string path) { PlainPath(path); return File.Exists(path); }
    private bool SourceEnabled(SourceGrant grant) => SourceEnabled(root, grant);
    private static bool SourceEnabled(string directory, SourceGrant grant) => grant.Enabled &&
        SourceMarkerExists(Path.Combine(directory, "source-" + grant.Id + ".grant")) && !SourceMarkerExists(SourceRetiredPath(directory, grant.Id));
    private bool HasSourceAccess(SourceGrant grant, BrokerWidgetIdentity identity) => HasSourceAccess(root, grant, identity);
    private static bool HasSourceAccess(string directory, SourceGrant grant, BrokerWidgetIdentity identity) => SourceEnabled(directory, grant) &&
        (SameOwner(grant.Owner, identity) && !SourceMarkerExists(SourceOwnerRetiredPath(directory, grant.Id)) || SourceMarkerExists(SourceAccessPath(directory, grant.Id, identity)));

    /// <summary>Called only by reviewed package settings transfer using verified old/new installation identities.</summary>
    internal static async Task KeepReviewedSourceAccessAsync(string directory, BrokerWidgetIdentity previous, BrokerWidgetIdentity replacement, CancellationToken token)
    {
        previous.Validate(); replacement.Validate();
        if (previous.PackageId != replacement.PackageId) throw Error("invalid_payload");
        if (!Directory.Exists(directory)) return;
        using var ownership = await SourceAuthorityLockAsync(directory, token).ConfigureAwait(false);
        foreach (var grant in ReadAuthorityGrants(directory))
        {
            token.ThrowIfCancellationRequested();
            if (HasSourceAccess(directory, grant, previous)) WriteSourceMarker(SourceAccessPath(directory, grant.Id, replacement));
        }
    }
    /// <summary>Durable uninstall cleanup removes exact retired installation access, preserving separately approved successors.</summary>
    internal static async Task RetireSourceOwnersAsync(string directory, IReadOnlyList<BrokerWidgetIdentity> owners, CancellationToken token)
    {
        foreach (var owner in owners) owner.Validate();
        if (!Directory.Exists(directory)) return;
        using var ownership = await SourceAuthorityLockAsync(directory, token).ConfigureAwait(false);
        foreach (var grant in ReadAuthorityGrants(directory))
        {
            if (!SourceEnabled(directory, grant)) continue;
            foreach (var owner in owners)
            {
                token.ThrowIfCancellationRequested();
                if (SameOwner(grant.Owner, owner)) WriteSourceMarker(SourceOwnerRetiredPath(directory, grant.Id));
                var access = SourceAccessPath(directory, grant.Id, owner); PlainPath(access);
                if (File.Exists(access)) File.Delete(access);
            }
            if (SourceMarkerExists(SourceOwnerRetiredPath(directory, grant.Id)) &&
                !Directory.EnumerateFiles(directory, "source-" + grant.Id + "-*.access").Any(SourceMarkerExists))
                WriteSourceMarker(SourceRetiredPath(directory, grant.Id));
        }
    }
    private static IEnumerable<SourceGrant> ReadAuthorityGrants(string directory)
    {
        PlainPath(directory);
        foreach (var path in Directory.EnumerateFiles(directory, "source-*.grant"))
        {
            PlainPath(path);
            if (new FileInfo(path).Length is <= 0 or > 64 * 1024) throw Error("capture_library_source_unavailable");
            SourceGrant grant;
            try { grant = JsonSerializer.Deserialize<SourceGrant>(File.ReadAllText(path)) ?? throw Error("capture_library_source_unavailable"); }
            catch (JsonException) { throw Error("capture_library_source_unavailable"); }
            if (grant.Owner is null) throw Error("capture_library_source_unavailable");
            DemandId(grant.Id); grant.Owner.Validate();
            if (Path.GetFileName(path) != "source-" + grant.Id + ".grant") throw Error("capture_library_source_unavailable");
            yield return grant;
        }
    }
    private static void WriteSourceMarker(string path)
    {
        PlainPath(path);
        using var marker = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read, 1, FileOptions.WriteThrough);
        marker.Flush(flushToDisk: true);
    }
    private static async Task<FileStream> SourceAuthorityLockAsync(string directory, CancellationToken token)
    {
        PlainPath(directory); var path = Path.Combine(directory, ".lock"); PlainPath(path);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { await Task.Delay(25, deadline.Token).ConfigureAwait(false); }
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw Error("capture_library_source_busy"); }
    }
}
