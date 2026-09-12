using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.SpotifyWidget;

internal sealed record SpotifySearchCollectionItem(SpotifySearchItem Value, WidgetCollectionItemKey Key);
internal sealed record SpotifySearchPresentation(string Query, SpotifySearchKind Kind,
    SpotifyCursorPresentation<SpotifySearchCollectionItem> Results);

