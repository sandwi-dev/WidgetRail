using System.Collections.ObjectModel;

namespace WidgetRail.WidgetPresentationSession;

/// <summary>
/// Short-lived host capture permission, bound to exact broker identities. A frontend
/// must call IsWindowPreviewCurrent before displaying/delivering frames and renew
/// before expiry. Native identity, capture exclusion, visibility and GPU budgets
/// are separate renderer responsibilities. This never grants widget access to pixels.
/// </summary>
public sealed class WidgetWindowPreviewGrant
{
    internal WidgetWindowPreviewGrant(WidgetPresentationSession owner, WidgetPresentationAuthority authority,
        Dictionary<string, object> epochs, Dictionary<string, WidgetHostWindowTarget> targets, object permissionEpoch, long issuedAt)
    {
        Owner = owner; Authority = authority; Epochs = epochs; PermissionEpoch = permissionEpoch; IssuedAt = issuedAt;
        Targets = new ReadOnlyDictionary<string, WidgetHostWindowTarget>(targets);
    }
    internal WidgetPresentationSession Owner { get; }
    internal WidgetPresentationAuthority Authority { get; }
    internal IReadOnlyDictionary<string, object> Epochs { get; }
    internal long IssuedAt { get; }
    internal object PermissionEpoch { get; }
    public IReadOnlyDictionary<string, WidgetHostWindowTarget> Targets { get; }
}
