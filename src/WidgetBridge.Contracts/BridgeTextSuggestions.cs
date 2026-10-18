using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

internal sealed record BridgeTextSuggestionsRequest(string WidgetId, string RuntimeGeneration,
    string PresentationGeneration, TextSuggestionQuery Query);
