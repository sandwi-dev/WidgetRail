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
    public const string ExitMediaWidgetAction = "host.embeddedMediaSession.back";
    private sealed record MediaPresentationEpoch(string Scope);
    private readonly ConditionalWeakTable<WidgetPresentationFrame, MediaPresentationEpoch> _fullscreenOrigins = new();
    private readonly ConditionalWeakTable<WidgetPresentationFrame, MediaPresentationEpoch> _compactOrigins = new();

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
        if (document.Presentations.TryGetValue(MediaPresentationKind.CompactPinned, out var compact))
            _compactOrigins.GetValue(frame, _ => compact);
    }

    /// <summary>Admit a host compact-media destination from a genuine displayed
    /// frame. This is not an authored pinned layout and sends no widget command.
    /// Native ownership, visibility and interaction are enforced by the frontend.</summary>
    public WidgetMediaPresentation EnterEmbeddedMediaCompact(WidgetPresentationFrame displayed,
        WidgetPresentationEmbeddedMediaDocument document)
    {
        ArgumentNullException.ThrowIfNull(displayed);
        ArgumentNullException.ThrowIfNull(document);
        using (_gate.Enter())
        {
            var current = ValidateDisplayedOrdinaryFrameLocked(displayed, displayed.Authority.ActiveInputScopeId);
            if (!current.Descriptor.PinningSupported || !IsMediaDocumentCurrentLocked(document) ||
                !SameMediaOwner(document.Authority, current.Authority) ||
                !((MediaDocumentEpoch)document.Epoch).Presentations.TryGetValue(MediaPresentationKind.CompactPinned, out var epoch) ||
                !_compactOrigins.TryGetValue(displayed, out var origin) || !ReferenceEquals(origin, epoch))
                throw RetiredMedia(current.Authority.WidgetId);
            return new(this, document, epoch, MediaPresentationKind.CompactPinned);
        }
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
        using (_gate.Enter())
        {
            var current = ValidateDisplayedMediaActionLocked(displayed, action, EnterMediaFullscreenAction);
            if (!IsMediaDocumentCurrentLocked(document) || !SameMediaOwner(document.Authority, current.Authority) ||
                !((MediaDocumentEpoch)document.Epoch).Presentations.TryGetValue(MediaPresentationKind.OverlayFullscreen, out var epoch) ||
                !_fullscreenOrigins.TryGetValue(displayed, out var originEpoch) || !ReferenceEquals(originEpoch, epoch))
                throw RetiredMedia(current.Authority.WidgetId);
            return new(this, document, epoch, MediaPresentationKind.OverlayFullscreen);
        }
    }

    public void ValidateEmbeddedMediaBack(WidgetPresentationFrame displayed, WidgetActionEvent action)
    {
        ArgumentNullException.ThrowIfNull(displayed); ArgumentNullException.ThrowIfNull(action);
        using (_gate.Enter()) _ = ValidateDisplayedMediaActionLocked(displayed, action, ExitMediaWidgetAction);
    }

    private WidgetPresentationFrame ValidateDisplayedMediaActionLocked(WidgetPresentationFrame displayed, WidgetActionEvent action, string expected)
    {
        var current = ValidateDisplayedOrdinaryFrameLocked(displayed, action.InputScopeId);
        if (current.Snapshot.EmbeddedMediaSession is null || action.ActionId != expected || action.Phase != ControllerEventPhase.Pressed ||
            ResolveDisplayedAction(displayed.Snapshot, action) != ResolveDisplayedAction(current.Snapshot, action))
            throw OrdinaryInputStale("The displayed media action is unavailable.");
        return current;
    }

    /// <summary>
    /// False is permanent for this receipt, even if a removed capability or old
    /// input scope later returns. Hiding, widget switching and pinned takeover are
    /// additional frontend retirement conditions, independent of document lifetime.
    /// </summary>
    public bool IsMediaPresentationCurrent(WidgetMediaPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        using (_gate.Enter())
            return ReferenceEquals(presentation.Owner, this) && IsMediaDocumentCurrentLocked(presentation.Document) &&
                ((MediaDocumentEpoch)presentation.Document.Epoch).Presentations.TryGetValue(presentation.Kind, out var epoch) &&
                ReferenceEquals(epoch, presentation.Epoch);
    }

    /// <summary>Resolve a reserved host shortcut with the same displayed/current binding checks as worker input.</summary>
    public WidgetActionEvent? ResolveEmbeddedMediaHostInput(WidgetPresentationFrame displayed, ControllerInputEvent input)
    {
        ArgumentNullException.ThrowIfNull(displayed);
        ArgumentNullException.ThrowIfNull(input);
        ValidateDisplayedControllerOrigin(displayed, input);
        using (_gate.Enter())
        {
            var current = ValidateDisplayedOrdinaryFrameLocked(displayed, input.ActiveInputScopeId);
            var binding = ResolveDisplayedController(displayed.Snapshot, input);
            if (binding != ResolveDisplayedController(current.Snapshot, input))
                throw OrdinaryInputStale("The displayed controller binding changed.");
            return binding.ActionId is EnterMediaFullscreenAction or ExitMediaWidgetAction && input.Phase == ControllerEventPhase.Pressed
                ? new(binding.ActionId, binding.Owner.Id, input.Button, input.Phase, input.Sequence, input.MonotonicTimestampMicroseconds,
                    InputScopeId: input.ActiveInputScopeId) { FocusedElementId = input.FocusedElementId }
                : null;
        }
    }
}
