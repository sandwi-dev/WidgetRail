namespace GameBarAlternative.WidgetSdk;

public static class StyleExtensions
{
    public static T Classes<T>(this T element, params string[] classNames) where T : WidgetElement
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(classNames);
        if (classNames.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Style class names cannot be empty.", nameof(classNames));
        return (T)(element with { StyleClasses = classNames.ToArray() });
    }
}
