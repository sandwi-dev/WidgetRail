using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

internal static class WindowPreviewResolver
{
    internal static async Task<IReadOnlyList<string>> AllowedWindowIdsAsync(
        ConsentStore? store, ConfiguredWidget widget, CancellationToken cancellationToken)
    {
        if (store is null || !widget.DeclaredCapabilities.Contains(PlatformCapabilities.TaskWindowsPreviewV1)) return [];
        try
        {
            var identity = new BrokerWidgetIdentity(widget.PackageId, widget.PublisherId, widget.InstanceId);
            if (await store.GetDecisionAsync(identity, PlatformCapabilities.TaskWindowsPreviewV1,
                    cancellationToken).ConfigureAwait(false) != ConsentDecision.Grant) return [];
            return WindowPreviewRegistry.ActiveIds(identity);
        }
        catch (Exception error) when (error is BrokerException or IOException or UnauthorizedAccessException)
        { return []; }
    }

    internal static async Task<IReadOnlyDictionary<string, NativeWindowPreviewTarget>> ResolveAsync(
        ConsentStore? consentStore, ConfiguredWidget configured, ViewSnapshot snapshot, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, NativeWindowPreviewTarget>(StringComparer.Ordinal);
        if (!configured.DeclaredCapabilities.Contains(PlatformCapabilities.TaskWindowsPreviewV1) ||
            consentStore is null) return result;
        var identity = new BrokerWidgetIdentity(configured.PackageId, configured.PublisherId, configured.InstanceId);
        try
        {
            if (await consentStore.GetDecisionAsync(identity, PlatformCapabilities.TaskWindowsPreviewV1,
                    cancellationToken).ConfigureAwait(false) != ConsentDecision.Grant) return result;
        }
        catch (Exception error) when (error is BrokerException or IOException or UnauthorizedAccessException)
        {
            // Preview admission fails closed, while the ordinary widget remains usable.
            return result;
        }
        var pending = new Stack<ViewNode>();
        pending.Push(snapshot.Root);
        while (pending.TryPop(out var node))
        {
            if (node.Kind == ViewNodeKind.WindowPreview && node.WindowId is { } id &&
                WindowPreviewRegistry.Resolve(identity, id) is { } target)
                result.TryAdd(id, target);
            foreach (var child in node.Children) pending.Push(child);
            if (node.FocusPresentation is { } focus) pending.Push(focus);
            if (node.DefaultFocusPresentation is { } fallback) pending.Push(fallback);
        }
        return result;
    }
}
