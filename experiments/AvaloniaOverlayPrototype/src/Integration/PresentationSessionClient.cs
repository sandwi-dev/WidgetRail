using GameBarAlternative.WidgetBridge;
using GameBarAlternative.WidgetPresentationSession;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.AvaloniaPrototype.Integration;

public interface IPresentationSessionClient : IAsyncDisposable
{
    event EventHandler<WidgetPresentationChangedEventArgs>? PresentationChanged;
    event EventHandler<WidgetPresentationInvalidatedEventArgs>? Invalidated;
    event EventHandler<WidgetPresentationDiagnosticEventArgs>? DiagnosticPublished;

    Task<WidgetPresentationCatalog> ListWidgetsAsync(CancellationToken cancellationToken = default);
    WidgetPresentationTarget GetTarget(string widgetId);
    Task<WidgetPresentationFrame> EstablishPresentationAsync(
        WidgetPresentationTarget target,
        WidgetLifecycleState state,
        CancellationToken cancellationToken = default);
    Task SetLifecycleAsync(
        WidgetPresentationTarget target,
        WidgetLifecycleState state,
        CancellationToken cancellationToken = default);
    Task<WidgetPresentationFrame> RefreshAsync(
        WidgetPresentationAuthority authority,
        CancellationToken cancellationToken = default);
    Task<WidgetLifecycleState> RestartAsync(
        WidgetPresentationTarget target,
        CancellationToken cancellationToken = default);
    Task<WidgetOperationAdmission> SendActionAsync(
        WidgetPresentationAuthority authority,
        WidgetActionEvent action,
        CancellationToken cancellationToken = default);
    Task<bool> SendControllerInputAsync(
        WidgetPresentationAuthority authority,
        ControllerInputEvent input,
        CancellationToken cancellationToken = default);
    Task<WidgetOperationAdmission> InvokeQuickActionAsync(
        WidgetPresentationTarget target,
        string quickActionId,
        long sequence,
        long monotonicTimestampMicroseconds,
        CancellationToken cancellationToken = default);
    Task<WidgetPresentationArtwork> ResolveArtworkAsync(
        WidgetPresentationAuthority authority,
        string artworkHandle,
        CancellationToken cancellationToken = default);
}

internal sealed class PresentationSessionClient(
    GameBarAlternative.WidgetPresentationSession.WidgetPresentationSession session) : IPresentationSessionClient
{
    public event EventHandler<WidgetPresentationChangedEventArgs>? PresentationChanged
    {
        add => session.PresentationChanged += value;
        remove => session.PresentationChanged -= value;
    }

    public event EventHandler<WidgetPresentationInvalidatedEventArgs>? Invalidated
    {
        add => session.Invalidated += value;
        remove => session.Invalidated -= value;
    }

    public event EventHandler<WidgetPresentationDiagnosticEventArgs>? DiagnosticPublished
    {
        add => session.DiagnosticPublished += value;
        remove => session.DiagnosticPublished -= value;
    }

    public Task<WidgetPresentationCatalog> ListWidgetsAsync(CancellationToken cancellationToken = default) =>
        session.ListWidgetsAsync(cancellationToken);

    public WidgetPresentationTarget GetTarget(string widgetId) => session.GetTarget(widgetId);

    public Task<WidgetPresentationFrame> EstablishPresentationAsync(
        WidgetPresentationTarget target,
        WidgetLifecycleState state,
        CancellationToken cancellationToken = default) =>
        session.EstablishPresentationAsync(target, state, cancellationToken);

    public Task SetLifecycleAsync(
        WidgetPresentationTarget target,
        WidgetLifecycleState state,
        CancellationToken cancellationToken = default) =>
        session.SetLifecycleAsync(target, state, cancellationToken);

    public Task<WidgetPresentationFrame> RefreshAsync(
        WidgetPresentationAuthority authority,
        CancellationToken cancellationToken = default) =>
        session.RefreshAsync(authority, cancellationToken);

    public Task<WidgetLifecycleState> RestartAsync(
        WidgetPresentationTarget target,
        CancellationToken cancellationToken = default) =>
        session.RestartAsync(target, cancellationToken);

    public Task<WidgetOperationAdmission> SendActionAsync(
        WidgetPresentationAuthority authority,
        WidgetActionEvent action,
        CancellationToken cancellationToken = default) =>
        session.SendActionAsync(authority, action, cancellationToken);

    public Task<bool> SendControllerInputAsync(
        WidgetPresentationAuthority authority,
        ControllerInputEvent input,
        CancellationToken cancellationToken = default) =>
        session.SendControllerInputAsync(authority, input, cancellationToken);

    public Task<WidgetOperationAdmission> InvokeQuickActionAsync(
        WidgetPresentationTarget target,
        string quickActionId,
        long sequence,
        long monotonicTimestampMicroseconds,
        CancellationToken cancellationToken = default) =>
        session.InvokeQuickActionAsync(target, quickActionId, sequence, monotonicTimestampMicroseconds, cancellationToken);

    public Task<WidgetPresentationArtwork> ResolveArtworkAsync(
        WidgetPresentationAuthority authority,
        string artworkHandle,
        CancellationToken cancellationToken = default) =>
        session.ResolveArtworkAsync(authority, artworkHandle, cancellationToken);

    public ValueTask DisposeAsync() => session.DisposeAsync();
}
