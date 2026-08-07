using System.Collections.ObjectModel;

namespace GameBarAlternative.WidgetStyling;

public static class GbssLimits
{
    public const int MaximumSourceCharacters = 1_048_576;
    public const long MaximumSourceBytes = MaximumSourceCharacters * 4L;
    public const int MaximumStatements = 2_048;
    public const int MaximumImports = 64;
    public const int MaximumSelectorsPerRule = 16;
    public const int MaximumDeclarationsPerRule = 128;
    public const int MaximumSelectorCharacters = 512;
    public const int MaximumValueCharacters = 4_096;
    public const int MaximumExpandedValueCharacters = 16_384;
}

public enum GbssDiagnosticSeverity
{
    Warning,
    Error,
}

public sealed record GbssSourceLocation(string Source, int Line, int Column);

public sealed record GbssDiagnostic(
    string Source,
    int Line,
    int Column,
    GbssDiagnosticSeverity Severity,
    string Code,
    string Message)
{
    public override string ToString() =>
        $"{Source}({Line},{Column}): {Severity.ToString().ToLowerInvariant()} {Code}: {Message}";
}

public abstract record GbssStatement(GbssSourceLocation Location);

public sealed record GbssImport(string Path, GbssSourceLocation Location) : GbssStatement(Location);

public sealed record GbssRule(
    IReadOnlyList<GbssSelector> Selectors,
    IReadOnlyList<GbssDeclaration> Declarations,
    GbssSourceLocation Location) : GbssStatement(Location);

public sealed record GbssDeclaration(
    string Property,
    string Value,
    GbssSourceLocation Location,
    int Order);

public enum GbssPseudoState
{
    Focused,
    Pressed,
    Selected,
    Disabled,
}

public sealed record GbssSelector(
    string? Role,
    string? Id,
    IReadOnlyList<string> Classes,
    IReadOnlySet<GbssPseudoState> States,
    bool IsRoot,
    int Specificity,
    string Text)
{
    public bool Matches(GbssElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (IsRoot) return false;
        if (Role is not null && Role != "*" && !string.Equals(Role, element.Role, StringComparison.Ordinal)) return false;
        if (Id is not null && !string.Equals(Id, element.Id, StringComparison.Ordinal)) return false;
        if (Classes.Any(@class => !element.Classes.Contains(@class))) return false;
        return States.All(element.States.Contains);
    }
}

public sealed record GbssDocument(
    string Source,
    IReadOnlyList<GbssStatement> Statements);

public sealed record GbssParseResult(
    GbssDocument Document,
    IReadOnlyList<GbssDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.All(item => item.Severity != GbssDiagnosticSeverity.Error);
}

public sealed record GbssPackageResult(
    IReadOnlyList<GbssDocument> Documents,
    IReadOnlyList<GbssDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.All(item => item.Severity != GbssDiagnosticSeverity.Error);
}

public sealed record GbssElement(
    string Role,
    string? Id = null,
    IReadOnlySet<string>? StyleClasses = null,
    IReadOnlySet<GbssPseudoState>? PseudoStates = null)
{
    public IReadOnlySet<string> Classes { get; } = StyleClasses ?? new HashSet<string>(StringComparer.Ordinal);
    public IReadOnlySet<GbssPseudoState> States { get; } = PseudoStates ?? new HashSet<GbssPseudoState>();
}

public enum GbssValueKind
{
    Color,
    Length,
    LengthList,
    Number,
    Integer,
    Ratio,
    Duration,
    Keyword,
    FontFamily,
}

public sealed record GbssComputedValue(
    GbssValueKind Kind,
    string Text,
    double? Number = null,
    string? Unit = null);

public sealed class GbssResolvedStyle
{
    internal GbssResolvedStyle(SortedDictionary<string, GbssComputedValue> properties) =>
        Properties = new ReadOnlyDictionary<string, GbssComputedValue>(properties);

    public IReadOnlyDictionary<string, GbssComputedValue> Properties { get; }

    public GbssComputedValue? Get(string property) =>
        Properties.TryGetValue(property, out var value) ? value : null;
}

public sealed record GbssCompileOptions
{
    public IReadOnlyDictionary<string, string> BuiltinVariables { get; init; } = DefaultBuiltinVariables;

    public static IReadOnlyDictionary<string, string> DefaultBuiltinVariables { get; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["--canvas-veil"] = "rgba(0, 0, 0, 0.64)",
            ["--accent"] = "#8f80ff",
            ["--brand-media"] = "#fc3f6c",
            ["--surface"] = "rgba(23, 26, 34, 0.98)",
            ["--surface-raised"] = "rgba(33, 37, 48, 0.98)",
            ["--surface-muted"] = "rgba(44, 49, 62, 0.92)",
            ["--text"] = "#f7f7fa",
            ["--text-muted"] = "#aeb5c3",
            ["--text-subdued"] = "#7f8796",
            ["--focus"] = "#ff7898",
            ["--scrim"] = "rgba(0, 0, 0, 0.64)",
            ["--success"] = "#48d597",
            ["--warning"] = "#ffc857",
            ["--error"] = "#ff5d73",
        });
}

/// <summary>
/// A trusted cascade layer. Rules in a higher-priority layer override rules in a
/// lower-priority layer before selector specificity is considered.
/// </summary>
public sealed record GbssThemeLayer(
    int Priority,
    IReadOnlyList<GbssDocument> Documents);

public sealed record GbssCompileResult(
    GbssTheme? Theme,
    IReadOnlyList<GbssDiagnostic> Diagnostics)
{
    public bool IsValid => Theme is not null && Diagnostics.All(item => item.Severity != GbssDiagnosticSeverity.Error);
}

public sealed class GbssTheme
{
    private readonly IReadOnlyList<CompiledRule> _rules;

    internal GbssTheme(IReadOnlyList<CompiledRule> rules) => _rules = rules;

    public GbssResolvedStyle Resolve(GbssElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrWhiteSpace(element.Role);
        var winners = new Dictionary<string, Winner>(StringComparer.Ordinal);
        foreach (var rule in _rules)
        {
            var specificity = rule.Selectors
                .Where(selector => selector.Matches(element))
                .Select(selector => (int?)selector.Specificity)
                .Max();
            if (specificity is null) continue;
            foreach (var declaration in rule.Declarations)
            {
                var candidate = new Winner(
                    declaration.Value,
                    rule.LayerPriority,
                    specificity.Value,
                    rule.CascadeOrder,
                    declaration.Order);
                if (!winners.TryGetValue(declaration.Property, out var existing) || candidate.Beats(existing))
                    winners[declaration.Property] = candidate;
            }
        }

        var sorted = new SortedDictionary<string, GbssComputedValue>(StringComparer.Ordinal);
        foreach (var (property, winner) in winners) sorted[property] = winner.Value;
        return new GbssResolvedStyle(sorted);
    }

    internal sealed record CompiledDeclaration(string Property, GbssComputedValue Value, int Order);
    internal sealed record CompiledRule(
        IReadOnlyList<GbssSelector> Selectors,
        IReadOnlyList<CompiledDeclaration> Declarations,
        int LayerPriority,
        int CascadeOrder);
    private sealed record Winner(
        GbssComputedValue Value,
        int LayerPriority,
        int Specificity,
        int RuleOrder,
        int DeclarationOrder)
    {
        public bool Beats(Winner other) =>
            LayerPriority > other.LayerPriority ||
            LayerPriority == other.LayerPriority && Specificity > other.Specificity ||
            LayerPriority == other.LayerPriority && Specificity == other.Specificity && RuleOrder > other.RuleOrder ||
            LayerPriority == other.LayerPriority && Specificity == other.Specificity && RuleOrder == other.RuleOrder &&
            DeclarationOrder > other.DeclarationOrder;
    }
}
