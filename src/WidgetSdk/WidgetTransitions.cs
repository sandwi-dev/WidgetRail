using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public static class WidgetTransitionExtensions
{
    /// <summary>
    /// Slides between sections inside this element's clipped bounds. Keep the
    /// element ID and group ID stable; change the section key only on navigation.
    /// The host owns timing, interrupted transitions and reduced-motion behavior.
    /// </summary>
    public static WidgetElement TransitionContent(this WidgetElement element,
        string groupId, string sectionKey, int sectionOrder) =>
        new TransitionElement(element, new(groupId, sectionKey, sectionOrder));

    /// <summary>
    /// Animates this element's position after a section change, synchronized with
    /// content in the same group. It does not animate scrolling or ordinary updates.
    /// </summary>
    public static WidgetElement TransitionLayout(this WidgetElement element,
        string groupId, string sectionKey, int sectionOrder) =>
        new TransitionElement(element, new(groupId, sectionKey, sectionOrder, WidgetTransitionKind.Layout));

    private sealed record TransitionElement : WidgetElement
    {
        private readonly WidgetElement _child;
        private readonly WidgetTransition _transition;

        internal TransitionElement(WidgetElement child, WidgetTransition transition)
            : base((child ?? throw new ArgumentNullException(nameof(child))).Id)
        {
            StableIdentifier.Validate(transition.GroupId, nameof(transition.GroupId));
            StableIdentifier.Validate(transition.Key, nameof(transition.Key));
            if (transition.Order is < -ProtocolConstants.MaximumWidgetTransitionOrder or > ProtocolConstants.MaximumWidgetTransitionOrder)
                throw new ArgumentOutOfRangeException(nameof(transition.Order));
            _child = child;
            _transition = transition;
            RequiredStyleClasses = child.RequiredStyleClasses;
            AuthorStyleClasses = child.AuthorStyleClasses;
        }

        internal override ViewNode ToProtocolNode() => _child.ToProtocolNode() with
        {
            Transition = _transition,
            StyleClasses = StyleClasses,
        };
    }
}
