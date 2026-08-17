using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>
/// One logical destination rendered as a compact tab and an expanded rail item.
/// <paramref name="Id"/> identifies the destination, not either presentation.
/// </summary>
public sealed record NavigationShellDestination(
    string Id,
    string Label,
    string ActionId,
    WidgetGlyph Glyph,
    string? AccessibilityLabel = null,
    bool IsDisabled = false);

public static partial class UI
{
    public const int MaximumNavigationShellDestinations = 8;

    /// <summary>
    /// Composes one controller-first destination set as compact horizontal tabs
    /// and an expanded vertical rail around one shared content subtree. The host
    /// selects the active presentation from logical-DIP surface dimensions.
    /// </summary>
    /// <param name="id">Stable ID for the complete shell.</param>
    /// <param name="selectedDestinationId">ID of the selected destination.</param>
    /// <param name="contentEntryFocusId">
    /// Stable focusable descendant used when navigating down from compact tabs
    /// or right from the expanded rail.
    /// </param>
    /// <param name="content">The single content subtree shared by both modes.</param>
    /// <param name="destinations">Two to eight stable destinations.</param>
    /// <param name="expandedPane">
    /// Optional persistent pane shown between the rail and content only in
    /// expanded mode. Its subtree is authored once.
    /// </param>
    /// <param name="expandedPaneEntryFocusId">
    /// Optional focusable descendant of <paramref name="expandedPane"/> used
    /// when navigating right from the rail. The content entry is the fallback.
    /// </param>
    public static StackElement NavigationShell(
        string id,
        string selectedDestinationId,
        string contentEntryFocusId,
        WidgetElement content,
        IReadOnlyList<NavigationShellDestination> destinations,
        WidgetElement? expandedPane = null,
        string? expandedPaneEntryFocusId = null)
    {
        StableIdentifier.Validate(id, nameof(id));
        StableIdentifier.Validate(selectedDestinationId, nameof(selectedDestinationId));
        StableIdentifier.Validate(contentEntryFocusId, nameof(contentEntryFocusId));
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(destinations);
        if (destinations.Count is < 2 or > MaximumNavigationShellDestinations)
            throw new ArgumentOutOfRangeException(nameof(destinations),
                $"A navigation shell requires 2-{MaximumNavigationShellDestinations} destinations.");
        if (destinations.Any(destination => destination is null))
            throw new ArgumentException("Destinations cannot contain null values.",
                nameof(destinations));
        if (expandedPaneEntryFocusId is not null)
        {
            if (expandedPane is null)
                throw new ArgumentException(
                    "An expanded-pane focus target requires an expanded pane.",
                    nameof(expandedPaneEntryFocusId));
            StableIdentifier.Validate(expandedPaneEntryFocusId,
                nameof(expandedPaneEntryFocusId));
        }

        var destinationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var destination in destinations)
        {
            StableIdentifier.Validate(destination.Id, nameof(destinations));
            ArgumentException.ThrowIfNullOrWhiteSpace(destination.Label);
            StableIdentifier.Validate(destination.ActionId, nameof(destinations));
            if (!Enum.IsDefined(destination.Glyph))
                throw new ArgumentOutOfRangeException(nameof(destinations),
                    "A navigation destination uses an unsupported glyph.");
            if (destination.AccessibilityLabel is not null &&
                string.IsNullOrWhiteSpace(destination.AccessibilityLabel))
                throw new ArgumentException(
                    "A navigation accessibility label cannot be empty.",
                    nameof(destinations));
            if (!destinationIds.Add(destination.Id))
                throw new ArgumentException("Destination IDs must be unique.",
                    nameof(destinations));
        }
        if (!destinationIds.Contains(selectedDestinationId))
            throw new ArgumentException(
                "The selected destination ID must identify a destination.",
                nameof(selectedDestinationId));

        var ids = WidgetIds.Scope(id);
        var compactIds = destinations
            .Select(destination => ids.KeyedId("compact", destination.Id))
            .ToArray();
        var railIds = destinations
            .Select(destination => ids.KeyedId("rail", destination.Id))
            .ToArray();
        var focusPersistenceIds = destinations
            .Select(destination => ids.KeyedId("focus", destination.Id))
            .ToArray();
        if (compactIds.Concat(railIds).Distinct(StringComparer.Ordinal).Count() !=
            destinations.Count * 2)
            throw new InvalidOperationException(
                "Navigation destination IDs did not produce unique controls.");

        var compactButtons = new ButtonElement[destinations.Count];
        var railButtons = new ButtonElement[destinations.Count];
        var expandedEntry = expandedPaneEntryFocusId ?? contentEntryFocusId;
        for (var index = 0; index < destinations.Count; index++)
        {
            var destination = destinations[index];
            var selected = string.Equals(destination.Id, selectedDestinationId,
                StringComparison.Ordinal);
            var state = selected ? "Selected" : "Not selected";
            var accessibility = string.IsNullOrWhiteSpace(destination.AccessibilityLabel)
                ? destination.Label
                : destination.AccessibilityLabel;

            compactButtons[index] = new ButtonElement(
                compactIds[index], destination.Label, destination.ActionId)
            {
                AccessibilityLabel = $"{accessibility}, {state}",
                Glyph = destination.Glyph,
                IsSelected = selected ? true : null,
                IsDisabled = destination.IsDisabled ? true : null,
                FocusPersistenceId = focusPersistenceIds[index],
                FocusNeighbors = new FocusNeighbors(
                    Down: contentEntryFocusId,
                    Left: compactIds[(index - 1 + destinations.Count) % destinations.Count],
                    Right: compactIds[(index + 1) % destinations.Count]),
                StyleClasses =
                [
                    "wrail-navigation-shell__compact-item",
                    selected
                        ? "wrail-navigation-shell__item--selected"
                        : "wrail-navigation-shell__item--idle",
                ],
            };
            railButtons[index] = new ButtonElement(
                railIds[index], destination.Label, destination.ActionId)
            {
                AccessibilityLabel = $"{accessibility}, {state}",
                Glyph = destination.Glyph,
                IsSelected = selected ? true : null,
                IsDisabled = destination.IsDisabled ? true : null,
                FocusPersistenceId = focusPersistenceIds[index],
                FocusNeighbors = new FocusNeighbors(
                    Up: railIds[(index - 1 + destinations.Count) % destinations.Count],
                    Down: railIds[(index + 1) % destinations.Count],
                    Right: expandedEntry),
                StyleClasses =
                [
                    "wrail-navigation-shell__rail-item",
                    selected
                        ? "wrail-navigation-shell__item--selected"
                        : "wrail-navigation-shell__item--idle",
                ],
            };
        }

        var compact = new RowElement(ids.Id("compact"), compactButtons)
        {
            StyleClasses = ["wrail-navigation-shell__compact"],
        }.VisibleWhen(ResponsiveVisibility.CompactOnly);
        var rail = new StackElement(ids.Id("rail"), railButtons)
        {
            StyleClasses = ["wrail-navigation-shell__rail"],
        }.VisibleWhen(ResponsiveVisibility.ExpandedOnly);

        var bodyChildren = new List<WidgetElement> { rail };
        if (expandedPane is not null)
        {
            bodyChildren.Add(new StackElement(ids.Id("persistent"), [expandedPane])
            {
                StyleClasses = ["wrail-navigation-shell__persistent"],
            }.VisibleWhen(ResponsiveVisibility.ExpandedOnly));
        }
        bodyChildren.Add(new StackElement(ids.Id("content"), [content])
        {
            StyleClasses = ["wrail-navigation-shell__content"],
        });

        return new StackElement(id,
        [
            compact,
            new RowElement(ids.Id("body"), bodyChildren)
            {
                StyleClasses = ["wrail-navigation-shell__body"],
            },
        ])
        {
            StyleClasses = ["wrail-navigation-shell"],
        };
    }
}
