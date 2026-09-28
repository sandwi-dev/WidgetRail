using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed record PresentationMemoryOwner(string WidgetId, string Instance, string Runtime, string Presentation, long Session)
{
    internal static PresentationMemoryOwner From(WidgetPresentationAuthority authority) =>
        new(authority.WidgetId, authority.WidgetInstanceId, authority.RuntimeGeneration, authority.PresentationGeneration, authority.SessionGeneration);
}
internal sealed record GroupFocusMemento(WidgetElementIdentity Group, WidgetElementIdentity Child);
internal sealed record IndexedFocusMemento(WidgetElementIdentity Element, IndexedCollectionFocusTarget Target);
internal sealed record IndexedViewportMemento(WidgetElementIdentity Element, string Source, long Query,
    CollectionLayoutKind Layout, ScrollAxis Axis, int Index, string Key, double ClippedFraction);
internal sealed record ScrollViewportMemento(WidgetElementIdentity Element, WidgetElementIdentity Anchor,
    ScrollAxis Axis, double ClippedFraction);

/// <summary>Bounded values only: no native views, frames, row leases or image payloads.</summary>
internal sealed record WidgetPresentationMemento(PresentationMemoryOwner Owner, long ConsumedFocusRequest,
    IReadOnlyList<WidgetElementIdentity> Focus, IReadOnlyList<GroupFocusMemento> Groups,
    IReadOnlyList<IndexedFocusMemento> IndexedFocus, IReadOnlyList<IndexedViewportMemento> IndexedViewports,
    IReadOnlyList<ScrollViewportMemento> ScrollViewports);
