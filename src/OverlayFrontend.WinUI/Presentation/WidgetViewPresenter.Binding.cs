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
            try
            {
                if (session.HasPinnedIntentAction(selection, projection, action))
                {
                    if (DispatchActionAsync is { } intent) await intent(new(origin.Frame, action)
                        { PinnedSelection = selection, PinnedProjection = projection });
                }
                else await session.SendPinnedActionAsync(selection, projection, action);
            }
            catch (WidgetPresentationSessionException error) when (IsRetiredInput(error)) { session.RequestInputRefresh(origin.Frame); }
        }
        else if (DispatchActionAsync is { } dispatch) await dispatch(new(origin.Frame, action));
    }

    private static bool IsRetiredInput(WidgetPresentationSessionException error) => WidgetInputFailure.IsStale(error);
}
