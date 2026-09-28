using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    internal WidgetPresentationBinding? CurrentBinding => presentation;
    private bool IsBindingCurrent(WidgetPresentationBinding origin) => presentationInputEnabled && presentationActive && !disposed && !applying &&
        !presentationOnly && origin.SameInput(presentation);

    private async Task<bool> AdmitBindingAsync(WidgetPresentationBinding origin, CancellationToken token = default) =>
        IsBindingCurrent(origin) && await AdmitInteractionAsync(origin.Frame.Authority, token) && IsBindingCurrent(origin);

    private async Task DispatchCapturedActionAsync(WidgetPresentationBinding origin, WidgetActionEvent action)
    {
        if (!IsBindingCurrent(origin)) return;
        if (origin.Selection is { } selection && origin.Projection is { } projection)
        {
            if (Session is not { } session || !await AdmitBindingAsync(origin)) return;
            try { await session.SendPinnedActionAsync(selection, projection, action); }
            catch (WidgetPresentationSessionException error) when (IsRetiredInput(error)) { }
        }
        else if (DispatchActionAsync is { } dispatch) await dispatch(new(origin.Frame, action));
    }

    private static bool IsRetiredInput(WidgetPresentationSessionException error) => error.Code is
        "snapshot_stale" or "input_scope_stale" or "presentation_stale" or "ordinary_input_stale" or
        "pinned_input_stale" or "stale_pinned_input_authority" or "indexed_input_stale" or "indexed_retired";
}
