using System.Text;

namespace GameBarAlternative.WidgetStyling;

public static class GbssThemeCompiler
{
    public static GbssCompileResult Compile(GbssPackageResult package, GbssCompileOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        var compiled = Compile(package.Documents, options);
        var diagnostics = package.Diagnostics.Concat(compiled.Diagnostics).ToArray();
        var hasErrors = diagnostics.Any(item => item.Severity == GbssDiagnosticSeverity.Error);
        return new GbssCompileResult(hasErrors ? null : compiled.Theme, diagnostics);
    }

    public static GbssCompileResult Compile(
        IEnumerable<GbssDocument> documents,
        GbssCompileOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(documents);
        return Compile([new GbssThemeLayer(0, documents.ToArray())], options);
    }

    public static GbssCompileResult Compile(
        IEnumerable<GbssThemeLayer> layers,
        GbssCompileOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(layers);
        options ??= new GbssCompileOptions();
        var sourceDocuments = layers
            .Select((layer, index) => new { Layer = layer, Index = index })
            .OrderBy(item => item.Layer.Priority)
            .ThenBy(item => item.Index)
            .SelectMany(item => item.Layer.Documents.Select(document =>
                new LayeredDocument(document, item.Layer.Priority)))
            .ToArray();
        var diagnostics = new List<GbssDiagnostic>();
        var variables = new Dictionary<string, VariableDefinition>(StringComparer.Ordinal);
        foreach (var (name, value) in options.BuiltinVariables.OrderBy(item => item.Key, StringComparer.Ordinal))
            variables[name] = new VariableDefinition(value, new GbssSourceLocation("<host>", 1, 1));

        foreach (var sourceDocument in sourceDocuments)
        {
            foreach (var rule in sourceDocument.Document.Statements.OfType<GbssRule>())
            {
                var root = rule.Selectors.Count != 0 && rule.Selectors.All(selector => selector.IsRoot);
                foreach (var declaration in rule.Declarations.Where(item => item.Property.StartsWith("--", StringComparison.Ordinal)))
                {
                    if (!root)
                    {
                        Add(declaration.Location, "variable_scope", "Variables may be declared only in :root rules.");
                        continue;
                    }
                    variables[declaration.Property] = new VariableDefinition(declaration.Value, declaration.Location);
                }
            }
        }

        var resolvedVariables = new Dictionary<string, string>(StringComparer.Ordinal);
        var resolving = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in variables.Keys.Order(StringComparer.Ordinal)) ResolveVariable(name);

        var compiledRules = new List<GbssTheme.CompiledRule>();
        var cascadeOrder = 0;
        foreach (var sourceDocument in sourceDocuments)
        {
            foreach (var rule in sourceDocument.Document.Statements.OfType<GbssRule>())
            {
                if (rule.Selectors.All(selector => selector.IsRoot)) continue;
                var declarations = new List<GbssTheme.CompiledDeclaration>();
                foreach (var declaration in rule.Declarations)
                {
                    if (declaration.Property.StartsWith("--", StringComparison.Ordinal)) continue;
                    if (!GbssPropertyCatalog.IsAllowed(declaration.Property)) continue;
                    if (!TrySubstituteVariables(declaration.Value, variables, resolvedVariables, resolving, out var value, out var substitutionError))
                    {
                        Add(declaration.Location, "invalid_variable", substitutionError);
                        continue;
                    }
                    if (!GbssPropertyCatalog.TryCompute(declaration.Property, value, out var computed, out var clamped, out var valueError))
                    {
                        Add(declaration.Location, "invalid_value", $"{declaration.Property}: {valueError}");
                        continue;
                    }
                    if (clamped)
                        Add(declaration.Location, "value_clamped", $"{declaration.Property} was clamped to {computed!.Text}.", GbssDiagnosticSeverity.Warning);
                    declarations.Add(new GbssTheme.CompiledDeclaration(declaration.Property, computed!, declaration.Order));
                }
                compiledRules.Add(new GbssTheme.CompiledRule(
                    rule.Selectors.Where(item => !item.IsRoot).ToArray(),
                    declarations,
                    sourceDocument.LayerPriority,
                    cascadeOrder++));
            }
        }

        var hasErrors = diagnostics.Any(item => item.Severity == GbssDiagnosticSeverity.Error);
        return new GbssCompileResult(hasErrors ? null : new GbssTheme(compiledRules), diagnostics);

        string? ResolveVariable(string name)
        {
            if (resolvedVariables.TryGetValue(name, out var existing)) return existing;
            if (!variables.TryGetValue(name, out var definition)) return null;
            if (!resolving.Add(name))
            {
                Add(definition.Location, "variable_cycle", $"Variable cycle detected at '{name}'.");
                return null;
            }
            if (!TrySubstituteVariables(definition.Value, variables, resolvedVariables, resolving, out var resolved, out var error))
            {
                Add(definition.Location, "invalid_variable", $"{name}: {error}");
                resolving.Remove(name);
                return null;
            }
            resolving.Remove(name);
            resolvedVariables[name] = resolved;
            return resolved;
        }

        void Add(GbssSourceLocation location, string code, string message, GbssDiagnosticSeverity severity = GbssDiagnosticSeverity.Error) =>
            diagnostics.Add(new GbssDiagnostic(location.Source, location.Line, location.Column, severity, code, message));
    }

    private static bool TrySubstituteVariables(
        string source,
        IReadOnlyDictionary<string, VariableDefinition> variables,
        Dictionary<string, string> resolved,
        HashSet<string> resolving,
        out string result,
        out string error)
    {
        var output = new StringBuilder();
        for (var index = 0; index < source.Length;)
        {
            var varIndex = source.IndexOf("var(", index, StringComparison.Ordinal);
            if (varIndex < 0)
            {
                if (output.Length + source.Length - index > GbssLimits.MaximumExpandedValueCharacters)
                {
                    result = string.Empty;
                    error = $"Expanded value exceeds {GbssLimits.MaximumExpandedValueCharacters} characters.";
                    return false;
                }
                output.Append(source, index, source.Length - index);
                break;
            }
            if (output.Length + varIndex - index > GbssLimits.MaximumExpandedValueCharacters)
            {
                result = string.Empty;
                error = $"Expanded value exceeds {GbssLimits.MaximumExpandedValueCharacters} characters.";
                return false;
            }
            output.Append(source, index, varIndex - index);
            var close = FindMatchingParenthesis(source, varIndex + 3);
            if (close < 0)
            {
                result = string.Empty;
                error = "var() is missing a closing parenthesis.";
                return false;
            }
            var argument = source[(varIndex + 4)..close];
            SplitVariableArgument(argument, out var name, out var fallback);
            if (!name.StartsWith("--", StringComparison.Ordinal))
            {
                result = string.Empty;
                error = $"'{name}' is not a custom property name.";
                return false;
            }

            string? replacement = null;
            if (resolved.TryGetValue(name, out var ready))
            {
                replacement = ready;
            }
            else if (variables.TryGetValue(name, out var definition))
            {
                if (!resolving.Add(name))
                {
                    result = string.Empty;
                    error = $"Variable cycle detected at '{name}'.";
                    return false;
                }
                if (!TrySubstituteVariables(definition.Value, variables, resolved, resolving, out replacement, out error))
                {
                    resolving.Remove(name);
                    result = string.Empty;
                    return false;
                }
                resolving.Remove(name);
                resolved[name] = replacement;
            }
            else if (fallback is not null)
            {
                if (!TrySubstituteVariables(fallback, variables, resolved, resolving, out replacement, out error))
                {
                    result = string.Empty;
                    return false;
                }
            }
            if (replacement is null)
            {
                result = string.Empty;
                error = $"Variable '{name}' is not defined and has no fallback.";
                return false;
            }
            if (output.Length + replacement.Length > GbssLimits.MaximumExpandedValueCharacters)
            {
                result = string.Empty;
                error = $"Expanded value exceeds {GbssLimits.MaximumExpandedValueCharacters} characters.";
                return false;
            }
            output.Append(replacement);
            index = close + 1;
        }
        result = output.ToString().Trim();
        error = string.Empty;
        return true;
    }

    private static int FindMatchingParenthesis(string source, int openIndex)
    {
        var depth = 0;
        for (var index = openIndex; index < source.Length; index++)
        {
            if (source[index] == '(') depth++;
            else if (source[index] == ')' && --depth == 0) return index;
        }
        return -1;
    }

    private static void SplitVariableArgument(string value, out string name, out string? fallback)
    {
        var depth = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '(') depth++;
            else if (value[index] == ')') depth--;
            else if (value[index] == ',' && depth == 0)
            {
                name = value[..index].Trim();
                fallback = value[(index + 1)..].Trim();
                return;
            }
        }
        name = value.Trim();
        fallback = null;
    }

    private sealed record VariableDefinition(string Value, GbssSourceLocation Location);
    private sealed record LayeredDocument(GbssDocument Document, int LayerPriority);
}
