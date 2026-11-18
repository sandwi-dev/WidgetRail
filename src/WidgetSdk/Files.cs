using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>Host-granted file custody; never grants arbitrary path access.</summary>
public static class WidgetFileCapabilities
{
    public const string Capability = "files.references.v1";
    public static WidgetCapabilityOperation<WidgetFileChoiceRequest, WidgetFileSelection> Choose { get; } = new(Capability, "files.choose");
    public static WidgetCapabilityOperation<WidgetFileFromCaptureRequest, WidgetFileReference> FromCapture { get; } = new(Capability, "files.from-capture");
    public static WidgetCapabilityOperation<WidgetFileIdReference, WidgetFileReference> Get { get; } = new(Capability, "files.get");
    public static WidgetCapabilityOperation<WidgetFileReadRequest, WidgetFileReadChunk> Read { get; } = new(Capability, "files.read");
    public static WidgetCapabilityOperation<WidgetFileIdReference, WidgetCapabilityAcknowledgement> Release { get; } = new(Capability, "files.release");
    public static WidgetCapabilityOperation<WidgetFileOfferRequest, WidgetFileShareOffer> Offer { get; } = new(Capability, "files.offer");
    public static WidgetCapabilityOperation<WidgetFileIdReference, WidgetFileSelection> Accept { get; } = new(Capability, "files.accept");
    public static WidgetCapabilityOperation<WidgetFileIdReference, WidgetCapabilityAcknowledgement> CancelOffer { get; } = new(Capability, "files.cancel-offer");
}

/// <summary>Explicit file selection and sharing using bounded reads from original files.</summary>
public sealed class WidgetFileService
{
    private readonly IWidgetCapabilityClient client;
    internal WidgetFileService(IWidgetCapabilityClient client) => this.client = client;

    public async ValueTask<IReadOnlyList<WidgetFileReference>> ChooseAsync(WidgetFileChoiceRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new();
        if (!request.IsWellFormed()) throw new ArgumentException("Invalid file choice.", nameof(request));
        return Selection(await client.InvokeAsync(WidgetFileCapabilities.Choose, request, cancellationToken).ConfigureAwait(false), request.Multiple ? WidgetFileContract.MaximumFiles : 1);
    }

    /// <summary>Retains a read-only file reference from this widget's existing capture authority.</summary>
    public async ValueTask<WidgetFileReference> FromCaptureAsync(CaptureAttachment attachment, string? name = null, CancellationToken cancellationToken = default)
    {
        if (attachment?.IsWellFormed() != true || name is not null && !WidgetFileContract.Name(name)) throw new ArgumentException("Invalid capture file.");
        var result = await client.InvokeAsync(WidgetFileCapabilities.FromCapture, new(attachment, name), cancellationToken).ConfigureAwait(false);
        if (result?.IsWellFormed() != true || result.ByteLength != attachment.ByteLength || result.ContentType != attachment.ContentType || name is not null && result.Name != name) throw Malformed();
        return result;
    }

    public async ValueTask<WidgetFileReference> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        DemandId(id);
        var result = await client.InvokeAsync(WidgetFileCapabilities.Get, new(id), cancellationToken).ConfigureAwait(false);
        if (result?.IsWellFormed() != true || result.Id != id) throw Malformed();
        return result;
    }

    /// <summary>Creates an opaque offer, not a recipient read grant. Send its ID through the standard file-share intent.</summary>
    public async ValueTask<WidgetFileShareOffer> CreateShareOfferAsync(IReadOnlyList<WidgetFileReference> files, CancellationToken cancellationToken = default)
    {
        if (files is not { Count: > 0 and <= WidgetFileContract.MaximumFiles } || files.Any(file => file?.IsWellFormed() != true) || files.Select(file => file.Id).Distinct().Count() != files.Count) throw new ArgumentException("Invalid files.", nameof(files));
        var snapshot = files.ToArray();
        var result = await client.InvokeAsync(WidgetFileCapabilities.Offer, new(snapshot.Select(file => file.Id).ToArray()), cancellationToken).ConfigureAwait(false);
        if (result?.IsWellFormed() != true || !result.Files.SequenceEqual(snapshot.Select(file => new WidgetFileMetadata(file.Name, file.ContentType, file.ByteLength)))) throw Malformed();
        return result with { Files = result.Files.ToArray() };
    }

    /// <summary>Accepts only an offer authorized by the host for this exact worker through intent delivery.</summary>
    public async ValueTask<IReadOnlyList<WidgetFileReference>> AcceptShareAsync(string offerId, CancellationToken cancellationToken = default)
    {
        DemandId(offerId);
        var result = Selection(await client.InvokeAsync(WidgetFileCapabilities.Accept, new(offerId), cancellationToken).ConfigureAwait(false), WidgetFileContract.MaximumFiles);
        return result.Count == 0 ? throw Malformed() : result;
    }

    /// <summary>Returns a seekable, read-only stream. Reads transfer at most 64 KiB; disposing the stream does not release the reference.</summary>
    public async ValueTask<Stream> OpenReadAsync(WidgetFileReference file, CancellationToken cancellationToken = default)
    {
        if (file?.IsWellFormed() != true) throw new ArgumentException("Invalid file.", nameof(file));
        var current = await GetAsync(file.Id, cancellationToken).ConfigureAwait(false);
        if (current.Name != file.Name || current.ContentType != file.ContentType || current.ByteLength != file.ByteLength ||
            file.ContentIdentity is not null && current.ContentIdentity != file.ContentIdentity) throw Malformed();
        return new WidgetFileReadStream(client, current);
    }

    public async ValueTask ReleaseAsync(WidgetFileReference file, CancellationToken cancellationToken = default)
    {
        if (file?.IsWellFormed() != true) throw new ArgumentException("Invalid file.", nameof(file));
        await client.InvokeAsync(WidgetFileCapabilities.Release, new(file.Id), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask CancelShareOfferAsync(string offerId, CancellationToken cancellationToken = default)
    {
        DemandId(offerId);
        await client.InvokeAsync(WidgetFileCapabilities.CancelOffer, new(offerId), cancellationToken).ConfigureAwait(false);
    }

    private static void DemandId(string id) { if (!WidgetFileContract.Token(id)) throw new ArgumentException("Invalid file reference.", nameof(id)); }
    private static IReadOnlyList<WidgetFileReference> Selection(WidgetFileSelection result, int limit)
    {
        if (result?.Files is null || result.Files.Count > limit || result.Files.Any(file => file?.IsWellFormed() != true) || result.Files.Select(file => file.Id).Distinct().Count() != result.Files.Count) throw Malformed();
        return result.Files.ToArray();
    }
    internal static InvalidDataException Malformed() => new("The host returned invalid file data.");
}
