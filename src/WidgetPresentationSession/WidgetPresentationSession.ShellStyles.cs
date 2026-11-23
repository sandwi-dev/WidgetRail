using System.Text.Json;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    private static readonly HashSet<string> ShellStyleRoles = new(["canvas", "backdrop", "panel", "preview", "preview-footer", "tray", "tray-item", "tray-item:selected",
        "tray-item:focused", "tray-item:selected:focused", "title", "body", "hint", "controller-glyph", "status",
        "tray-clock", "tray-date", "tray-status-icon", "dialog", "dialog-title", "dialog-description", "dialog-footer",
        "dialog-choice", "dialog-choice:selected", "dialog-choice:focused", "dialog-button", "dialog-button:focused",
        "data-red", "data-blue", "data-green", "data-gold", "data-purple", "data-orange"], StringComparer.Ordinal);

    /// <summary>Read the bridge-resolved host shell palette without changing display or settings.</summary>
    public async Task<IReadOnlyDictionary<string, BridgeNodeRenderStyles>> ReadShellStylesAsync(CancellationToken cancellationToken = default)
    {
        var response = await RequestAsync(BridgeMessageTypes.GetPlatformAppearance, new BridgeEmptyPayload(),
            BridgeMessageTypes.PlatformAppearance, cancellationToken).ConfigureAwait(false);
        if (response.Payload.ValueKind != JsonValueKind.Object || !response.Payload.TryGetProperty("shellStyles", out var raw) ||
            raw.ValueKind != JsonValueKind.Object || raw.EnumerateObject().Count() > ShellStyleRoles.Count)
            throw new BridgeProtocolException("Shell style inventory exceeds its bound.");
        var styles = new Dictionary<string, BridgeNodeRenderStyles>(StringComparer.Ordinal);
        foreach (var property in raw.EnumerateObject())
        {
            if (!ShellStyleRoles.Contains(property.Name) || styles.ContainsKey(property.Name) || property.Value.ValueKind != JsonValueKind.Object ||
                property.Value.EnumerateObject().Count() > BridgeRenderStyleLimits.MaximumPropertiesPerState)
                throw new BridgeProtocolException("Invalid shell style role.");
            var state = property.Value.Deserialize(BridgeJson.TypeInfo<Dictionary<string, BridgeComputedStyleValue>>())
                ?? throw new BridgeProtocolException("Missing shell style state.");
            styles.Add(property.Name, new() { Base = state, Focused = state, Pressed = state });
        }
        return BridgeRenderStyleContract.ValidateAndFreeze(styles, ShellStyleRoles, requireComplete: false);
    }

    public async Task<IReadOnlyDictionary<string, BridgeNodeRenderStyles>> ReadCatalogueThemePreviewAsync(
        WidgetPresentationFrame displayed, WidgetActionEvent action, CancellationToken token)
    {
        BridgeCatalogueThemePreviewRequest request;
        using (_gate.Enter())
        {
            var current = ValidateDisplayedOrdinaryFrameLocked(displayed, action.InputScopeId);
            var authority = displayed.Authority;
            var prefix = ThemeCataloguePublication.PreviewActionPrefix;
            if (authority.WidgetId != "marketplace" || action.SourceElementId != "theme.try" || action.InputScopeId != "marketplace.page" ||
                action.Phase != ControllerEventPhase.Pressed || action.RequestedValue is not null || action.CommittedText is not null ||
                !action.ActionId.StartsWith(prefix, StringComparison.Ordinal) || action.ActionId.Length != prefix.Length + 64 ||
                ResolveDisplayedAction(displayed.Snapshot, action) != ResolveDisplayedAction(current.Snapshot, action))
                throw OrdinaryInputStale("The theme preview requires a current Catalogue action.");
            request = new(authority.WidgetId, authority.RuntimeGeneration, authority.PresentationGeneration, action.ActionId[prefix.Length..]);
        }
        var response = await RequestAsync(BridgeMessageTypes.PreviewCatalogueTheme, request, BridgeMessageTypes.CatalogueThemeStyles, token).ConfigureAwait(false);
        using (_gate.Enter())
        {
            var current = ValidateDisplayedOrdinaryFrameLocked(displayed, action.InputScopeId);
            if (ResolveDisplayedAction(displayed.Snapshot, action) != ResolveDisplayedAction(current.Snapshot, action))
                throw OrdinaryInputStale("The selected Catalogue theme changed.");
        }
        return BridgeRenderStyleContract.ValidateAndFreeze(
            BridgeJson.FromElement<Dictionary<string, BridgeNodeRenderStyles>>(response.Payload), ShellStyleRoles, requireComplete: false);
    }
}
