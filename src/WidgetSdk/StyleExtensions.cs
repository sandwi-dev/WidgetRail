using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.WidgetSdk;

public static class StyleExtensions
{
    public static T Classes<T>(this T element, params string[] classNames) where T : WidgetElement
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(classNames);
        ValidateClassCount(classNames.Length, nameof(classNames));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var className in classNames)
        {
            ValidateClassName(className, nameof(classNames));
            if (!seen.Add(className))
                throw new ArgumentException($"Style class '{className}' is repeated.", nameof(classNames));
        }
        return (T)(element with { StyleClasses = classNames.ToArray() });
    }

    /// <summary>
    /// Adds theme classes without removing semantic classes supplied by an SDK
    /// component. Existing order is retained and duplicate names are ignored.
    /// </summary>
    public static T AddClasses<T>(this T element, params string[] classNames) where T : WidgetElement
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(classNames);
        if (element.StyleClasses is null)
            throw new ArgumentException("The element's style-class collection cannot be null.", nameof(element));
        var result = element.StyleClasses.ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var existingClass in result)
        {
            ValidateClassName(existingClass, nameof(element));
            if (!seen.Add(existingClass))
                throw new ArgumentException($"The element's style class '{existingClass}' is repeated.", nameof(element));
        }
        foreach (var className in classNames)
        {
            ValidateClassName(className, nameof(classNames));
            if (seen.Add(className)) result.Add(className);
        }
        ValidateClassCount(result.Count, nameof(classNames));
        return (T)(element with { StyleClasses = result.ToArray() });
    }

    private static void ValidateClassName(string? className, string parameterName)
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

    private static void ValidateClassCount(int count, string parameterName)
    {
        if (count > ProtocolConstants.MaximumStyleClassCount)
            throw new ArgumentException(
                $"An element may declare at most {ProtocolConstants.MaximumStyleClassCount} style classes.",
                parameterName);
    }
}
