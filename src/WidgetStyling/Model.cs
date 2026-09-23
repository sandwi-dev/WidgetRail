using System.Collections.ObjectModel;

namespace WidgetRail.WidgetStyling;

public static class WrssLimits
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

public enum WrssSourceReadStatus
{
    Success,
    Missing,
    UnsafePath,
    TooLarge,
    ChangedDuringRead,
    InvalidEncoding,
    DigestMismatch,
    IoUnavailable,
}

public sealed record WrssSourceReadResult
{
    private WrssSourceReadResult(WrssSourceReadStatus status, string? source = null)
    {
        Status = status;
        Source = source;
    }

    public WrssSourceReadStatus Status { get; }

    public string? Source { get; }

    public static WrssSourceReadResult FromSource(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(WrssSourceReadStatus.Success, source);
    }

    public static WrssSourceReadResult Failure(WrssSourceReadStatus status)
    {
        if (status == WrssSourceReadStatus.Success || !Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        return new(status);
    }
}

public enum WrssDiagnosticSeverity
{
    Warning,
    Error,
}

public sealed record WrssSourceLocation(string Source, int Line, int Column);

public sealed record WrssDiagnostic(
    string Source,
    int Line,
    int Column,
    WrssDiagnosticSeverity Severity,
    string Code,
    string Message)
{
    public override string ToString() =>
        $"{Source}({Line},{Column}): {Severity.ToString().ToLowerInvariant()} {Code}: {Message}";
}

public abstract record WrssStatement(WrssSourceLocation Location);

public sealed record WrssImport(string Path, WrssSourceLocation Location) : WrssStatement(Location);

public sealed record WrssRule(
    IReadOnlyList<WrssSelector> Selectors,
    IReadOnlyList<WrssDeclaration> Declarations,
    WrssSourceLocation Location) : WrssStatement(Location);

public sealed record WrssDeclaration(
    string Property,
    string Value,
    WrssSourceLocation Location,
    int Order);

public enum WrssPseudoState
{
    Focused,
    Pressed,
    Selected,
    Disabled,
    Busy,
}

public sealed record WrssSelector(
    string? Role,
    string? Id,
    IReadOnlyList<string> Classes,
    IReadOnlySet<WrssPseudoState> States,
    bool IsRoot,
    int Specificity,
    string Text)
{
    public bool Matches(WrssElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (IsRoot) return false;
        if (Role is not null && Role != "*" && !string.Equals(Role, element.Role, StringComparison.Ordinal)) return false;
        if (Id is not null && !string.Equals(Id, element.Id, StringComparison.Ordinal)) return false;
        if (Classes.Any(@class => !element.Classes.Contains(@class))) return false;
        return States.All(element.States.Contains);
    }
}

public sealed record WrssDocument(
    string Source,
    IReadOnlyList<WrssStatement> Statements);

public sealed record WrssParseResult(
    WrssDocument Document,
    IReadOnlyList<WrssDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.All(item => item.Severity != WrssDiagnosticSeverity.Error);
}

public sealed record WrssPackageResult(
    IReadOnlyList<WrssDocument> Documents,
    IReadOnlyList<WrssDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.All(item => item.Severity != WrssDiagnosticSeverity.Error);
}

public sealed record WrssElement(
    string Role,
    string? Id = null,
    IReadOnlySet<string>? StyleClasses = null,
    IReadOnlySet<WrssPseudoState>? PseudoStates = null)
{
    public IReadOnlySet<string> Classes { get; } = StyleClasses ?? new HashSet<string>(StringComparer.Ordinal);
    public IReadOnlySet<WrssPseudoState> States { get; } = PseudoStates ?? new HashSet<WrssPseudoState>();
}

public enum WrssValueKind
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

public sealed record WrssComputedValue(
    WrssValueKind Kind,
    string Text,
    double? Number = null,
    string? Unit = null);

public sealed class WrssResolvedStyle
{
    internal WrssResolvedStyle(SortedDictionary<string, WrssComputedValue> properties) =>
        Properties = new ReadOnlyDictionary<string, WrssComputedValue>(properties);

    public IReadOnlyDictionary<string, WrssComputedValue> Properties { get; }

    public WrssComputedValue? Get(string property) =>
        Properties.TryGetValue(property, out var value) ? value : null;
}

public sealed record WrssCompileOptions
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
            ["--scrollbar-track"] = "#343946",
            ["--scrollbar-thumb"] = "#aeb5c3",
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
public sealed record WrssThemeLayer(
    int Priority,
    IReadOnlyList<WrssDocument> Documents);

public sealed record WrssCompileResult(
    WrssTheme? Theme,
    IReadOnlyList<WrssDiagnostic> Diagnostics)
{
    public bool IsValid => Theme is not null && Diagnostics.All(item => item.Severity != WrssDiagnosticSeverity.Error);
}

public sealed class WrssTheme
{
    private readonly IReadOnlyList<CompiledRule> _rules;

    internal WrssTheme(IReadOnlyList<CompiledRule> rules) => _rules = rules;

    public WrssResolvedStyle Resolve(WrssElement element)
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

        var sorted = new SortedDictionary<string, WrssComputedValue>(StringComparer.Ordinal);
        foreach (var (property, winner) in winners) sorted[property] = winner.Value;
        return new WrssResolvedStyle(sorted);
    }

    internal sealed record CompiledDeclaration(string Property, WrssComputedValue Value, int Order);
    internal sealed record CompiledRule(
        IReadOnlyList<WrssSelector> Selectors,
        IReadOnlyList<CompiledDeclaration> Declarations,
        int LayerPriority,
        int CascadeOrder);
    private sealed record Winner(
        WrssComputedValue Value,
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
