namespace WidgetRail.WidgetStyling;

/// <summary>
/// Source-level guidance for known layout declarations without a WinUI mapping.
/// This supplements parsing/compilation; it does not validate rendered geometry
/// or infer whether a selector matches a particular widget declaration.
/// </summary>
public static class WinUiStyleDiagnostics
{
    /// <summary>
    /// Checks a parsed source, including state rules, without resolving imports
    /// or changing its declarations. Run for every document in a loaded package.
    /// </summary>
    public static IReadOnlyList<WrssDiagnostic> Analyze(WrssDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var diagnostics = new List<WrssDiagnostic>();
        foreach (var rule in document.Statements.OfType<WrssRule>())
        {
            if (rule.Selectors.All(selector => selector.IsRoot)) continue;
            foreach (var declaration in rule.Declarations)
            {
                var guidance = declaration.Property switch
                {
                    "flex-shrink" => "Use bounded native Grid tracks and explicit minimum/maximum sizes; WinUI does not use flex shrink weights.",
                    "flex-basis" => "Use UI.Grid Auto/Pixel/Star tracks or explicit width/height; WinUI does not use a flex basis.",
                    "flex-wrap" => "Use UI.ResponsiveGrid for a small reflowing set, or an indexed CollectionGrid for virtualized items; WinUI Row/Stack flow does not wrap.",
                    _ => null,
                };
                if (guidance is null) continue;
                var location = declaration.Location;
                diagnostics.Add(new(location.Source, location.Line, location.Column,
                    WrssDiagnosticSeverity.Warning, "winui_unmapped_layout",
                    $"'{declaration.Property}' has no WinUI layout mapping. {guidance}"));
            }
        }
        return diagnostics;
    }
}
