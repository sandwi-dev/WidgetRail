using System.Text;
using System.Text.RegularExpressions;

namespace GameBarAlternative.WidgetStyling;

public static partial class GbssParser
{
    private static readonly IReadOnlyDictionary<string, GbssPseudoState> PseudoStates =
        new Dictionary<string, GbssPseudoState>(StringComparer.Ordinal)
        {
            ["focused"] = GbssPseudoState.Focused,
            ["pressed"] = GbssPseudoState.Pressed,
            ["selected"] = GbssPseudoState.Selected,
            ["disabled"] = GbssPseudoState.Disabled,
        };

    public static GbssParseResult Parse(string source, string sourceName = "<memory>")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        var diagnostics = new List<GbssDiagnostic>();
        if (source.Length > GbssLimits.MaximumSourceCharacters)
        {
            diagnostics.Add(Diagnostic(sourceName, source, GbssLimits.MaximumSourceCharacters,
                "source_too_large", $"A GBSS source may contain at most {GbssLimits.MaximumSourceCharacters} characters."));
            return new GbssParseResult(new GbssDocument(sourceName, []), diagnostics);
        }
        var content = RemoveComments(source, sourceName, diagnostics);
        var statements = new List<GbssStatement>();
        var index = 0;
        var sawRule = false;
        var importCount = 0;

        while (true)
        {
            SkipWhitespace(content, ref index);
            if (index >= content.Length) break;
            if (statements.Count >= GbssLimits.MaximumStatements)
            {
                Add(index, "too_many_statements", $"A GBSS source may contain at most {GbssLimits.MaximumStatements} statements.");
                break;
            }
            var start = index;
            if (content[index] == '@')
            {
                var end = FindTopLevelTerminator(content, index, ';', stopAtBrace: true);
                if (end < 0)
                {
                    Add(start, "missing_semicolon", "Top-level statements must end with ';'.");
                    break;
                }
                var raw = content[start..end].Trim();
                var match = ImportRegex().Match(raw);
                if (!match.Success)
                {
                    Add(start, "invalid_statement", "Only a quoted package-relative @import is allowed at the top level.");
                }
                else
                {
                    if (sawRule) Add(start, "import_after_rule", "Imports must appear before style rules.");
                    var path = match.Groups["path"].Value;
                    if (!GbssPackageLoader.IsSafePackagePath(path))
                        Add(start, "unsafe_import", "Import must be a normalized package-relative .gbss path without traversal or a URI scheme.");
                    else if (++importCount > GbssLimits.MaximumImports)
                        Add(start, "too_many_imports", $"A GBSS source may import at most {GbssLimits.MaximumImports} files.");
                    else
                        statements.Add(new GbssImport(path, Location(sourceName, content, start)));
                }
                index = end + 1;
                continue;
            }

            var brace = FindTopLevelTerminator(content, index, '{', stopAtBrace: false);
            if (brace < 0)
            {
                Add(start, "missing_brace", "A style rule requires an opening brace.");
                break;
            }
            var selectorText = content[start..brace].Trim();
            var selectors = ParseSelectors(selectorText, sourceName, content, start, diagnostics);
            var close = FindClosingBrace(content, brace + 1);
            if (close < 0)
            {
                Add(brace, "missing_brace", "Rule is missing a closing brace.");
                close = content.Length;
            }
            var declarations = ParseDeclarations(
                content[(brace + 1)..close], sourceName, content, brace + 1, diagnostics);
            if (selectors.Count != 0)
                statements.Add(new GbssRule(selectors, declarations, Location(sourceName, content, start)));
            sawRule = true;
            index = close < content.Length ? close + 1 : close;
        }

        return new GbssParseResult(new GbssDocument(sourceName, statements), diagnostics);

        void Add(int offset, string code, string message) =>
            diagnostics.Add(Diagnostic(sourceName, content, offset, code, message));
    }

    private static IReadOnlyList<GbssSelector> ParseSelectors(
        string source,
        string sourceName,
        string fullSource,
        int offset,
        List<GbssDiagnostic> diagnostics)
    {
        var selectors = new List<GbssSelector>();
        if (string.IsNullOrWhiteSpace(source))
        {
            diagnostics.Add(Diagnostic(sourceName, fullSource, offset, "missing_selector", "A rule requires a selector."));
            return selectors;
        }
        if (source.Length > GbssLimits.MaximumSelectorCharacters)
        {
            diagnostics.Add(Diagnostic(sourceName, fullSource, offset, "selector_too_long",
                $"A selector list may contain at most {GbssLimits.MaximumSelectorCharacters} characters."));
            return selectors;
        }

        var rawSelectors = source.Split(',');
        if (rawSelectors.Length > GbssLimits.MaximumSelectorsPerRule)
        {
            diagnostics.Add(Diagnostic(sourceName, fullSource, offset, "too_many_selectors",
                $"A rule may contain at most {GbssLimits.MaximumSelectorsPerRule} selectors."));
            return selectors;
        }

        var cursor = 0;
        foreach (var raw in rawSelectors)
        {
            var text = raw.Trim();
            var relative = source.IndexOf(raw, cursor, StringComparison.Ordinal);
            cursor = Math.Max(cursor, relative + raw.Length);
            var selectorOffset = offset + Math.Max(0, relative) + (raw.Length - raw.TrimStart().Length);
            if (!TryParseSelector(text, out var selector, out var error))
                diagnostics.Add(Diagnostic(sourceName, fullSource, selectorOffset, "invalid_selector", error));
            else
                selectors.Add(selector!);
        }
        return selectors;
    }

    private static bool TryParseSelector(string text, out GbssSelector? selector, out string error)
    {
        selector = null;
        error = string.Empty;
        if (text == ":root")
        {
            selector = new GbssSelector(null, null, [], new HashSet<GbssPseudoState>(), true, 0, text);
            return true;
        }
        if (string.IsNullOrWhiteSpace(text) || text.Any(char.IsWhiteSpace) || text.IndexOfAny(['>', '+', '~', '[', ']']) >= 0)
        {
            error = $"Unsupported selector syntax: '{text}'. Use one semantic role with optional ID, classes, and pseudo-states.";
            return false;
        }

        var index = 0;
        string? role = null;
        string? id = null;
        var classes = new List<string>();
        var states = new HashSet<GbssPseudoState>();
        if (text[index] == '*')
        {
            role = "*";
            index++;
        }
        else if (text[index] is not ('.' or '#' or ':'))
        {
            if (!ReadIdentifier(text, ref index, out role))
            {
                error = $"Invalid semantic role in selector '{text}'.";
                return false;
            }
        }

        while (index < text.Length)
        {
            var prefix = text[index++];
            if (!ReadIdentifier(text, ref index, out var identifier))
            {
                error = $"Expected an identifier after '{prefix}' in selector '{text}'.";
                return false;
            }
            if (prefix == '.') classes.Add(identifier!);
            else if (prefix == '#')
            {
                if (id is not null)
                {
                    error = $"Selector '{text}' contains more than one ID.";
                    return false;
                }
                id = identifier;
            }
            else if (prefix == ':')
            {
                if (!PseudoStates.TryGetValue(identifier!, out var state))
                {
                    error = $"Pseudo-state ':{identifier}' is not supported.";
                    return false;
                }
                if (!states.Add(state))
                {
                    error = $"Pseudo-state ':{identifier}' is repeated.";
                    return false;
                }
            }
            else
            {
                error = $"Unexpected selector token '{prefix}'.";
                return false;
            }
        }

        if (role is null && id is null && classes.Count == 0 && states.Count == 0)
        {
            error = "A selector cannot be empty.";
            return false;
        }
        var specificity = (id is null ? 0 : 100) + ((classes.Count + states.Count) * 10) + (role is null or "*" ? 0 : 1);
        selector = new GbssSelector(role, id, classes, states, false, specificity, text);
        return true;
    }

    private static IReadOnlyList<GbssDeclaration> ParseDeclarations(
        string block,
        string sourceName,
        string fullSource,
        int blockOffset,
        List<GbssDiagnostic> diagnostics)
    {
        var declarations = new List<GbssDeclaration>();
        var declarationCount = 0;
        var start = 0;
        var quote = '\0';
        var depth = 0;
        for (var index = 0; index <= block.Length; index++)
        {
            var ch = index < block.Length ? block[index] : ';';
            if (quote != '\0')
            {
                if (ch == quote && !IsEscaped(block, index)) quote = '\0';
                continue;
            }
            if (ch is '"' or '\'') { quote = ch; continue; }
            if (ch == '(') depth++;
            else if (ch == ')')
            {
                if (depth == 0)
                    diagnostics.Add(Diagnostic(sourceName, fullSource, blockOffset + index, "unbalanced_parenthesis", "Unexpected closing parenthesis."));
                else depth--;
            }
            if (ch != ';' || depth != 0) continue;

            var raw = block[start..index];
            var trimmed = raw.Trim();
            var trimOffset = raw.Length - raw.TrimStart().Length;
            var declarationOffset = blockOffset + start + trimOffset;
            start = index + 1;
            if (trimmed.Length == 0) continue;
            if (++declarationCount > GbssLimits.MaximumDeclarationsPerRule)
            {
                diagnostics.Add(Diagnostic(sourceName, fullSource, declarationOffset, "too_many_declarations",
                    $"A rule may contain at most {GbssLimits.MaximumDeclarationsPerRule} declarations."));
                break;
            }
            var colon = FindDeclarationColon(trimmed);
            if (colon <= 0 || colon == trimmed.Length - 1)
            {
                diagnostics.Add(Diagnostic(sourceName, fullSource, declarationOffset, "invalid_declaration", "Expected 'property: value'."));
                continue;
            }
            var property = trimmed[..colon].Trim();
            var value = trimmed[(colon + 1)..].Trim();
            if (value.Length > GbssLimits.MaximumValueCharacters)
            {
                diagnostics.Add(Diagnostic(sourceName, fullSource, declarationOffset + colon + 1, "value_too_long",
                    $"A value may contain at most {GbssLimits.MaximumValueCharacters} characters."));
                continue;
            }
            if (!CustomPropertyRegex().IsMatch(property) && !GbssPropertyCatalog.IsAllowed(property))
                diagnostics.Add(Diagnostic(sourceName, fullSource, declarationOffset, "unknown_property", $"Property '{property}' is not supported."));
            if (ContainsUnsafeValue(value))
                diagnostics.Add(Diagnostic(sourceName, fullSource, declarationOffset + colon + 1, "unsafe_value", "URLs, scripts, expressions, imports, and filesystem values are not allowed in GBSS declarations."));
            else if (!HasBalancedFunctions(value))
                diagnostics.Add(Diagnostic(sourceName, fullSource, declarationOffset + colon + 1, "invalid_value", "Value contains unbalanced parentheses or an unterminated string."));
            declarations.Add(new GbssDeclaration(property, value, Location(sourceName, fullSource, declarationOffset), declarations.Count));
        }
        if (quote != '\0')
            diagnostics.Add(Diagnostic(sourceName, fullSource, blockOffset + Math.Max(0, block.Length - 1), "unterminated_string", "String is not terminated."));
        if (depth != 0)
            diagnostics.Add(Diagnostic(sourceName, fullSource, blockOffset + Math.Max(0, block.Length - 1), "unbalanced_parenthesis", "Value is missing a closing parenthesis."));
        return declarations;
    }

    internal static bool ContainsUnsafeValue(string value)
    {
        if (DangerousValueRegex().IsMatch(value) || value.Contains('@')) return true;
        foreach (Match match in FunctionRegex().Matches(value))
            if (match.Groups["name"].Value is not ("var" or "rgb" or "rgba")) return true;
        return false;
    }

    private static bool HasBalancedFunctions(string value)
    {
        var depth = 0;
        var quote = '\0';
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (quote != '\0')
            {
                if (ch == quote && !IsEscaped(value, index)) quote = '\0';
                continue;
            }
            if (ch is '"' or '\'') quote = ch;
            else if (ch == '(') depth++;
            else if (ch == ')' && --depth < 0) return false;
        }
        return quote == '\0' && depth == 0;
    }

    private static int FindDeclarationColon(string value)
    {
        var quote = '\0';
        var depth = 0;
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (quote != '\0')
            {
                if (ch == quote && !IsEscaped(value, index)) quote = '\0';
                continue;
            }
            if (ch is '"' or '\'') quote = ch;
            else if (ch == '(') depth++;
            else if (ch == ')') depth--;
            else if (ch == ':' && depth == 0) return index;
        }
        return -1;
    }

    private static int FindTopLevelTerminator(string source, int start, char target, bool stopAtBrace)
    {
        var quote = '\0';
        var depth = 0;
        for (var index = start; index < source.Length; index++)
        {
            var ch = source[index];
            if (quote != '\0')
            {
                if (ch == quote && !IsEscaped(source, index)) quote = '\0';
                continue;
            }
            if (ch is '"' or '\'') quote = ch;
            else if (ch == '(') depth++;
            else if (ch == ')') depth = Math.Max(0, depth - 1);
            else if (depth == 0 && ch == target) return index;
            else if (stopAtBrace && depth == 0 && ch is '{' or '}') return -1;
        }
        return -1;
    }

    private static int FindClosingBrace(string source, int start)
    {
        var quote = '\0';
        var parenthesis = 0;
        for (var index = start; index < source.Length; index++)
        {
            var ch = source[index];
            if (quote != '\0')
            {
                if (ch == quote && !IsEscaped(source, index)) quote = '\0';
                continue;
            }
            if (ch is '"' or '\'') quote = ch;
            else if (ch == '(') parenthesis++;
            else if (ch == ')') parenthesis = Math.Max(0, parenthesis - 1);
            else if (ch == '}' && parenthesis == 0) return index;
            else if (ch == '{' && parenthesis == 0) return -1;
        }
        return -1;
    }

    private static string RemoveComments(string source, string sourceName, List<GbssDiagnostic> diagnostics)
    {
        var result = source.ToCharArray();
        var index = 0;
        while (index < source.Length)
        {
            var start = source.IndexOf("/*", index, StringComparison.Ordinal);
            if (start < 0) break;
            var end = source.IndexOf("*/", start + 2, StringComparison.Ordinal);
            if (end < 0)
            {
                diagnostics.Add(Diagnostic(sourceName, source, start, "unterminated_comment", "Comment is not terminated."));
                end = source.Length - 2;
            }
            for (var cursor = start; cursor < Math.Min(end + 2, result.Length); cursor++)
                if (result[cursor] is not ('\r' or '\n')) result[cursor] = ' ';
            index = Math.Max(start + 2, end + 2);
        }
        return new string(result);
    }

    private static bool ReadIdentifier(string text, ref int index, out string? identifier)
    {
        var start = index;
        if (index >= text.Length || !(char.IsAsciiLetter(text[index]) || text[index] == '_'))
        {
            identifier = null;
            return false;
        }
        index++;
        while (index < text.Length && (char.IsAsciiLetterOrDigit(text[index]) || text[index] is '_' or '-')) index++;
        identifier = text[start..index];
        return true;
    }

    private static void SkipWhitespace(string source, ref int index)
    {
        while (index < source.Length && char.IsWhiteSpace(source[index])) index++;
    }

    private static bool IsEscaped(string source, int index)
    {
        var slashes = 0;
        for (var cursor = index - 1; cursor >= 0 && source[cursor] == '\\'; cursor--) slashes++;
        return (slashes & 1) != 0;
    }

    internal static GbssSourceLocation Location(string sourceName, string source, int offset)
    {
        var (line, column) = GetLineColumn(source, offset);
        return new GbssSourceLocation(sourceName, line, column);
    }

    internal static GbssDiagnostic Diagnostic(string sourceName, string source, int offset, string code, string message,
        GbssDiagnosticSeverity severity = GbssDiagnosticSeverity.Error)
    {
        var location = Location(sourceName, source, offset);
        return new GbssDiagnostic(location.Source, location.Line, location.Column, severity, code, message);
    }

    private static (int Line, int Column) GetLineColumn(string source, int offset)
    {
        var line = 1;
        var column = 1;
        for (var index = 0; index < Math.Clamp(offset, 0, source.Length); index++)
        {
            if (source[index] == '\n') { line++; column = 1; }
            else column++;
        }
        return (line, column);
    }

    [GeneratedRegex("^--[a-z][a-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CustomPropertyRegex();

    [GeneratedRegex("^@import\\s+[\"'](?<path>[^\"']+)[\"']\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ImportRegex();

    [GeneratedRegex("(?:url\\s*\\(|expression\\s*\\(|javascript\\s*:|file\\s*:|https?\\s*:|data\\s*:|vbscript\\s*:|(?:^|[^a-z])(?:eval|script)\\s*\\()", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DangerousValueRegex();

    [GeneratedRegex("(?<name>[A-Za-z][A-Za-z0-9-]*)\\s*\\(", RegexOptions.CultureInvariant)]
    private static partial Regex FunctionRegex();
}
