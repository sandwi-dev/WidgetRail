using WidgetRail.WidgetProtocol;
namespace WidgetRail.WidgetSdk;

public static class WidgetDocumentCapabilities
{
    public const string Permission = "presentation.documents.v1";
    public static WidgetCapabilityOperation<ProviderDocumentContent, ProviderDocumentReference> Create { get; } = new(Permission, "document.create");
    public static WidgetCapabilityOperation<ProviderDocumentReference, WidgetCapabilityAcknowledgement> Discard { get; } = new(Permission, "document.discard");
}

/// <summary>Publish bounded HTML for restricted host rendering. Scripts, forms and background network access are blocked.</summary>
public sealed class WidgetDocumentService
{
    private readonly IWidgetCapabilityClient client;
    internal WidgetDocumentService(IWidgetCapabilityClient client) => this.client = client;
    public async ValueTask<ProviderDocumentReference> CreateAsync(IReadOnlyList<string> html, CancellationToken cancellationToken = default)
    {
        if (!ProviderDocumentContent.IsValid(html)) throw new ArgumentException("Invalid document content.", nameof(html));
        var result = await client.InvokeAsync(WidgetDocumentCapabilities.Create, new(html), cancellationToken).ConfigureAwait(false);
        if (result?.IsWellFormed() != true) throw new WidgetCapabilityException("malformed_response", "Invalid document response.");
        return result;
    }
    public async ValueTask DiscardAsync(ProviderDocumentReference reference, CancellationToken cancellationToken = default)
    {
        if (reference?.IsWellFormed() != true) throw new ArgumentException("Invalid document reference.", nameof(reference));
        var result = await client.InvokeAsync(WidgetDocumentCapabilities.Discard, reference, cancellationToken).ConfigureAwait(false);
        if (result?.Acknowledged != true) throw new WidgetCapabilityException("malformed_response", "Invalid document response.");
    }
}
