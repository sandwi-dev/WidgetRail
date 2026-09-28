using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using System.Runtime.CompilerServices;

namespace WidgetRail.WidgetPresentationSession;

/// <summary>A host-owned presentation of one admitted media document. It cannot survive capability or scope retirement.</summary>
public sealed class WidgetMediaPresentation
{
    internal WidgetMediaPresentation(WidgetPresentationSession owner, WidgetPresentationEmbeddedMediaDocument document,
        object epoch, MediaPresentationKind kind)
    { Owner = owner; Document = document; Epoch = epoch; Kind = kind; }
    internal WidgetPresentationSession Owner { get; }
    internal object Epoch { get; }
    public WidgetPresentationEmbeddedMediaDocument Document { get; }
    public MediaPresentationKind Kind { get; }
}

public sealed partial class WidgetPresentationSession
{
    public const string EnterMediaFullscreenAction = "host.embeddedMediaSession.enterFullscreen";
    private sealed record MediaPresentationEpoch(string Scope);
    private readonly ConditionalWeakTable<WidgetPresentationFrame, MediaPresentationEpoch> _fullscreenOrigins = new();

    private void ReconcileMediaPresentationsLocked(MediaDocumentEpoch document, WidgetPresentationFrame frame)
    {
        var supported = frame.Snapshot.EmbeddedMediaSession!.SupportedPresentations;
        foreach (var kind in document.Presentations.Keys.ToArray())
            if (!supported.Contains(kind) || document.Presentations[kind].Scope != frame.Authority.ActiveInputScopeId)
                document.Presentations.Remove(kind);
        foreach (var kind in supported)
            document.Presentations.TryAdd(kind, new(frame.Authority.ActiveInputScopeId));
        if (document.Presentations.TryGetValue(MediaPresentationKind.OverlayFullscreen, out var fullscreen))
            _fullscreenOrigins.GetValue(frame, _ => fullscreen);
    }

    /// <summary>
    /// Admit the reserved host action from a genuine displayed frame, without sending
    /// it to the widget. The frontend must additionally prove that this document is
    /// resident in its visible ordinary viewport and that no pinned owner holds it.
    /// </summary>
    public WidgetMediaPresentation EnterEmbeddedMediaFullscreen(WidgetPresentationFrame displayed,
        WidgetActionEvent action, WidgetPresentationEmbeddedMediaDocument document)
    {
        ArgumentNullException.ThrowIfNull(displayed);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(document);
        lock (_gate)
        {
            var current = ValidateDisplayedOrdinaryFrameLocked(displayed, action.InputScopeId);
            if (action.ActionId != EnterMediaFullscreenAction || action.Phase != ControllerEventPhase.Pressed ||
                ResolveDisplayedAction(displayed.Snapshot, action) != ResolveDisplayedAction(current.Snapshot, action))
                throw OrdinaryInputStale("The displayed fullscreen action is unavailable.");
            if (!IsMediaDocumentCurrentLocked(document) || !SameMediaOwner(document.Authority, current.Authority) ||
                !((MediaDocumentEpoch)document.Epoch).Presentations.TryGetValue(MediaPresentationKind.OverlayFullscreen, out var epoch) ||
                !_fullscreenOrigins.TryGetValue(displayed, out var originEpoch) || !ReferenceEquals(originEpoch, epoch))
                throw RetiredMedia(current.Authority.WidgetId);
            return new(this, document, epoch, MediaPresentationKind.OverlayFullscreen);
        }
    }

    /// <summary>
    /// False is permanent for this receipt, even if a removed capability or old
    /// input scope later returns. Hiding, widget switching and pinned takeover are
    /// additional frontend retirement conditions, independent of document lifetime.
    /// </summary>
    public bool IsMediaPresentationCurrent(WidgetMediaPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        lock (_gate)
            return ReferenceEquals(presentation.Owner, this) && IsMediaDocumentCurrentLocked(presentation.Document) &&
                ((MediaDocumentEpoch)presentation.Document.Epoch).Presentations.TryGetValue(presentation.Kind, out var epoch) &&
                ReferenceEquals(epoch, presentation.Epoch);
    }

    /// <summary>Resolve a reserved host shortcut with the same displayed/current binding checks as worker input.</summary>
    public WidgetActionEvent? ResolveEmbeddedMediaFullscreenInput(WidgetPresentationFrame displayed, ControllerInputEvent input)
    {
        ArgumentNullException.ThrowIfNull(displayed);
        ArgumentNullException.ThrowIfNull(input);
        ValidateDisplayedControllerOrigin(displayed, input);
        lock (_gate)
        {
            var current = ValidateDisplayedOrdinaryFrameLocked(displayed, input.ActiveInputScopeId);
            var binding = ResolveDisplayedController(displayed.Snapshot, input);
            if (binding != ResolveDisplayedController(current.Snapshot, input))
                throw OrdinaryInputStale("The displayed controller binding changed.");
            return binding.ActionId == EnterMediaFullscreenAction && input.Phase == ControllerEventPhase.Pressed
                ? new(binding.ActionId, binding.Owner.Id, input.Button, input.Phase, input.Sequence, input.MonotonicTimestampMicroseconds,
                    InputScopeId: input.ActiveInputScopeId) { FocusedElementId = input.FocusedElementId }
                : null;
        }
    }
}
