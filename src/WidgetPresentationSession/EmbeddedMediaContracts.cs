using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetPresentationSession;

/// <summary>Sealed package bytes admitted through WidgetBridge; contains no filesystem or browser authority.</summary>
public sealed class WidgetPresentationEmbeddedMediaResource
{
    private readonly byte[] bytes;
    internal WidgetPresentationEmbeddedMediaResource(string path, string contentType, string sha256, byte[] bytes)
    { Path = path; ContentType = contentType; Sha256 = sha256; this.bytes = bytes; }
    public string Path { get; }
    public string ContentType { get; }
    public string Sha256 { get; }
    public int Length => bytes.Length;
    /// <summary>Independent, read-only, non-exportable stream over the verified bytes.</summary>
    public Stream OpenRead() => new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: false);
}

/// <summary>
/// One resolved adapter document. Hold it while GetEmbeddedMediaState returns non-null;
/// compatible presentation updates do not require resource resolution or document recreation.
/// The frontend owns its renderer, resource request filtering and document teardown.
/// </summary>
public sealed class WidgetPresentationEmbeddedMediaDocument
{
    internal WidgetPresentationEmbeddedMediaDocument(WidgetPresentationSession owner, object epoch,
        WidgetPresentationAuthority authority, string sessionId, string entryAsset,
        IReadOnlyList<WidgetPresentationEmbeddedMediaResource> resources)
    { Owner = owner; Epoch = epoch; Authority = authority; SessionId = sessionId; EntryAsset = entryAsset; Resources = resources; }
    internal WidgetPresentationSession Owner { get; }
    internal object Epoch { get; }
    /// <summary>Exact admission origin. Use GetEmbeddedMediaState for current presentation/command authority.</summary>
    public WidgetPresentationAuthority Authority { get; }
    public string SessionId { get; }
    public string EntryAsset { get; }
    public IReadOnlyList<WidgetPresentationEmbeddedMediaResource> Resources { get; }
}

public sealed record WidgetPresentationEmbeddedMediaState(
    WidgetPresentationAuthority Authority, EmbeddedMediaSession Declaration);
