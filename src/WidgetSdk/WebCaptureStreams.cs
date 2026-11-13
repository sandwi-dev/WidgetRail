using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public static class WidgetWebCaptureCapabilities
{
    public static WidgetCapabilityOperation<WebCaptureStreamRequest,WebCaptureStreamStatus> Prepare { get; } =
        new(WidgetCaptureCapabilities.Permission,"capture.web-prepare");
    public static WidgetCapabilityOperation<WebCaptureStreamRequest,WebCaptureStreamStatus> PrepareConstrained { get; } =
        new(WidgetCaptureCapabilities.Permission,"capture.web-prepare-constrained");
    public static WidgetCapabilityOperation<WebCaptureStreamTicket,WebCaptureStreamStatus> Status { get; } =
        new(WidgetCaptureCapabilities.Permission,"capture.web-status");
    public static WidgetCapabilityOperation<WebCaptureStreamTicket,WidgetCapabilityAcknowledgement> Stop { get; } =
        new(WidgetCaptureCapabilities.Permission,"capture.web-stop");
}

/// <summary>Host-selected application capture delivered to a worker-owned WebView backend.
/// Requires capture consent. Prepare opens the host's source/audio chooser; it never selects
/// a window supplied by the widget. Provider broadcast commands remain the widget's responsibility.</summary>
public sealed class WidgetWebCaptureService
{
    private readonly IWidgetCapabilityClient client;
    internal WidgetWebCaptureService(IWidgetCapabilityClient client) => this.client=client;

    public async ValueTask<WebCaptureStreamStatus> PrepareAsync(WebSessionReference session,
        WebCaptureStreamOptions? options=null,CancellationToken cancellationToken=default)
    {
        var request=new WebCaptureStreamRequest(session,options??new());
        if(!request.IsWellFormed())throw new ArgumentException("An owned session and supported capture options are required.");
        var result=await client.InvokeAsync(WidgetWebCaptureCapabilities.Prepare,request,cancellationToken).ConfigureAwait(false);
        Validate(result,session);
        ValidateChoice(result,WebCaptureStreamOptions.LegacyQualities);
        return result;
    }

    /// <summary>Opens the host chooser restricted to provider-approved quality choices.
    /// A distinct operation makes unsupported older hosts fail closed. The list is
    /// copied; preferred settings must be a member. The host still owns source/audio consent.</summary>
    public ValueTask<WebCaptureStreamStatus> PrepareAsync(WebSessionReference session,
        WebCaptureStreamOptions preferred,IReadOnlyList<WebCaptureStreamOptions> allowedQualities,CancellationToken cancellationToken)
        =>PrepareAsync(session,preferred,allowedQualities,allowContentPreferenceSelection:true,cancellationToken);

    /// <summary>Restricts provider modes and optionally fixes the content preference.
    /// With selection disabled, the host omits that selector and preserves the supplied preference.</summary>
    public async ValueTask<WebCaptureStreamStatus> PrepareAsync(WebSessionReference session,
        WebCaptureStreamOptions preferred,IReadOnlyList<WebCaptureStreamOptions> allowedQualities,
        bool allowContentPreferenceSelection,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(allowedQualities);
        var request=new WebCaptureStreamRequest(session,preferred){AllowedQualities=allowedQualities,FixedContentPreference=!allowContentPreferenceSelection};
        if(!request.IsWellFormed())throw new ArgumentException("Supported, distinct quality choices including the preferred setting are required.");
        var result=await client.InvokeAsync(WidgetWebCaptureCapabilities.PrepareConstrained,request,cancellationToken).ConfigureAwait(false);
        Validate(result,session);ValidateChoice(result,request.AllowedQualities!);
        if(request.FixedContentPreference && result.Quality is { } selected && selected.ContentPreference!=preferred.ContentPreference)throw Malformed();
        return result;
    }

    public async ValueTask<WebCaptureStreamStatus> GetStatusAsync(WebCaptureStreamTicket ticket,CancellationToken cancellationToken=default)
    {
        if(ticket?.IsWellFormed()!=true)throw new ArgumentException("Invalid web capture ticket.",nameof(ticket));
        var result=await client.InvokeAsync(WidgetWebCaptureCapabilities.Status,ticket,cancellationToken).ConfigureAwait(false);
        Validate(result,ticket.Session);
        if(result.Ticket!=ticket)throw Malformed();
        return result;
    }

    public async ValueTask StopAsync(WebCaptureStreamTicket ticket,CancellationToken cancellationToken=default)
    {
        if(ticket?.IsWellFormed()!=true)throw new ArgumentException("Invalid web capture ticket.",nameof(ticket));
        var result=await client.InvokeAsync(WidgetWebCaptureCapabilities.Stop,ticket,cancellationToken).ConfigureAwait(false);
        if(result?.Acknowledged!=true)throw Malformed();
    }

    private static void Validate(WebCaptureStreamStatus? value,WebSessionReference session)
    {
        if(value?.IsWellFormed()!=true || value.Ticket is { } ticket && ticket.Session!=session)throw Malformed();
    }
    private static void ValidateChoice(WebCaptureStreamStatus value,IReadOnlyList<WebCaptureStreamOptions> choices)
    {if(value.Quality is { } selected&&!choices.Any(selected.IsSameQuality))throw Malformed();}
    private static WidgetCapabilityException Malformed()=>new("malformed_response","The host returned an invalid capture result.");
}
