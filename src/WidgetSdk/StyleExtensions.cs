using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public static class StyleExtensions
{
    /// <summary>
    /// Replaces only author-owned classes. SDK-required semantic classes on a
    /// composed component remain intrinsic and keep their original order.
    /// </summary>
    public static T Classes<T>(this T element, params string[] classNames) where T : WidgetElement
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(classNames);
        var authorClasses = StyleClassOwnership.NormalizeAuthor(
            element.RequiredStyleClasses, classNames, nameof(classNames));
        return (T)(element with { AuthorStyleClasses = authorClasses });
    }

    /// <summary>
    /// Appends author-owned classes without removing semantic classes supplied
    /// by an SDK component. Existing order is retained and duplicate names are
    /// ignored.
    /// </summary>
    public static T AddClasses<T>(this T element, params string[] classNames) where T : WidgetElement
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(classNames);
        var combinedAuthor = new List<string>(
            element.AuthorStyleClasses.Count + classNames.Length);
        combinedAuthor.AddRange(element.AuthorStyleClasses);
        combinedAuthor.AddRange(classNames);
        var result = StyleClassOwnership.NormalizeAuthor(
            element.RequiredStyleClasses, combinedAuthor, nameof(classNames));
        return (T)(element with { AuthorStyleClasses = result });
    }

    internal static void ValidateClassName(string? className, string parameterName)
    {
        if (className is null)
            throw new ArgumentException("Style class names cannot be null.", parameterName);
        if (className.Length > ProtocolConstants.MaximumStyleClassLength)
            throw new ArgumentException(
                $"A style class may not exceed {ProtocolConstants.MaximumStyleClassLength} characters.",
                parameterName);
        if (!StyleClassContract.IsValidIdentifier(className))
            throw new ArgumentException(
                "A style class must start with an ASCII letter or '_' and then contain only ASCII letters, digits, '_' or '-'.",
                parameterName);
    }

    internal static void ValidateClassCount(int count, string parameterName)
    {
        if (count > ProtocolConstants.MaximumStyleClassCount)
            throw new ArgumentException(
                $"An element may declare at most {ProtocolConstants.MaximumStyleClassCount} style classes.",
                parameterName);
    }
}

internal static class StyleClassOwnership
{
    internal static IReadOnlyList<string> EmptyClasses { get; } =
        Array.AsReadOnly(Array.Empty<string>());

    internal static IReadOnlyList<string> Freeze(
        IReadOnlyList<string> classes,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(classes);
        if (classes.Count == 0) return EmptyClasses;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var frozen = new List<string>(classes.Count);
        foreach (var className in classes)
        {
            StyleExtensions.ValidateClassName(className, parameterName);
            if (seen.Add(className)) frozen.Add(className);
        }
        StyleExtensions.ValidateClassCount(frozen.Count, parameterName);
        return Array.AsReadOnly(frozen.ToArray());
    }

    internal static IReadOnlyList<string> NormalizeAuthor(
        IReadOnlyList<string> required,
        IReadOnlyList<string> author,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(required);
        ArgumentNullException.ThrowIfNull(author);
        var seen = new HashSet<string>(required, StringComparer.Ordinal);
        var normalized = new List<string>(author.Count);
        foreach (var className in author)
        {
            StyleExtensions.ValidateClassName(className, parameterName);
            if (seen.Add(className)) normalized.Add(className);
        }
        StyleExtensions.ValidateClassCount(seen.Count, parameterName);
        return normalized.Count == 0
            ? EmptyClasses
            : Array.AsReadOnly(normalized.ToArray());
    }

    internal static IReadOnlyList<string> Combine(
        IReadOnlyList<string> required,
        IReadOnlyList<string> author)
    {
        ArgumentNullException.ThrowIfNull(required);
        ArgumentNullException.ThrowIfNull(author);
        if (author.Count == 0) return required;
        if (required.Count == 0) return author;

        var result = new List<string>(required.Count + author.Count);
        result.AddRange(required);
        var seen = new HashSet<string>(required, StringComparer.Ordinal);
        foreach (var className in author)
            if (seen.Add(className)) result.Add(className);
        StyleExtensions.ValidateClassCount(result.Count, nameof(author));
        return Array.AsReadOnly(result.ToArray());
    }
}
