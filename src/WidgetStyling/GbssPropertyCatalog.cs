using System.Globalization;
using System.Text.RegularExpressions;

namespace GameBarAlternative.WidgetStyling;

public static partial class GbssPropertyCatalog
{
    private enum PropertyType
    {
        Color,
        Length,
        Spacing,
        Opacity,
        Scale,
        Duration,
        FontWeight,
        FontFamily,
        Align,
        Justify,
        Direction,
        Overflow,
        TextAlign,
        Ratio,
        ObjectFit,
        ObjectPosition,
        Shape,
        LineHeight,
        PositiveInteger,
        TextOverflow,
        TextTransform,
        TransitionEasing,
        BoundedNumber,
        LengthOrAuto,
        FlexWrap,
    }

    private sealed record Definition(PropertyType Type, double Minimum = 0, double Maximum = 0, bool AllowNegative = false);

    private static readonly IReadOnlyDictionary<string, Definition> Definitions =
        new Dictionary<string, Definition>(StringComparer.Ordinal)
        {
            ["background"] = new(PropertyType.Color),
            ["color"] = new(PropertyType.Color),
            ["border-color"] = new(PropertyType.Color),
            ["outline-color"] = new(PropertyType.Color),
            ["shadow-color"] = new(PropertyType.Color),
            ["image-tint"] = new(PropertyType.Color),
            ["scrim-color"] = new(PropertyType.Color),
            ["width"] = new(PropertyType.Length, 0, 4096),
            ["height"] = new(PropertyType.Length, 0, 4096),
            ["min-width"] = new(PropertyType.Length, 0, 4096),
            ["min-height"] = new(PropertyType.Length, 0, 4096),
            ["max-width"] = new(PropertyType.Length, 0, 4096),
            ["max-height"] = new(PropertyType.Length, 0, 4096),
            ["font-size"] = new(PropertyType.Length, 8, 128),
            ["letter-spacing"] = new(PropertyType.Length, -2, 32, true),
            ["corner-radius"] = new(PropertyType.Length, 0, 256),
            ["outline-width"] = new(PropertyType.Length, 0, 16),
            ["outline-offset"] = new(PropertyType.Length, -8, 16, true),
            ["border-width"] = new(PropertyType.Length, 0, 16),
            ["background-blur"] = new(PropertyType.Length, 0, 64),
            ["shadow-blur"] = new(PropertyType.Length, 0, 64),
            ["shadow-offset-x"] = new(PropertyType.Length, -256, 256, true),
            ["shadow-offset-y"] = new(PropertyType.Length, -256, 256, true),
            ["gap"] = new(PropertyType.Spacing, 0, 256),
            ["padding"] = new(PropertyType.Spacing, 0, 256),
            ["margin"] = new(PropertyType.Spacing, -256, 256, true),
            ["opacity"] = new(PropertyType.Opacity, 0, 1),
            ["scale"] = new(PropertyType.Scale, 0.5, 2),
            ["transition-duration"] = new(PropertyType.Duration, 0, 2000),
            ["aspect-ratio"] = new(PropertyType.Ratio, 0.2, 5),
            ["object-fit"] = new(PropertyType.ObjectFit),
            ["object-position"] = new(PropertyType.ObjectPosition),
            ["shape"] = new(PropertyType.Shape),
            ["font-weight"] = new(PropertyType.FontWeight),
            ["font-family"] = new(PropertyType.FontFamily),
            ["line-height"] = new(PropertyType.LineHeight, 0.8, 3),
            ["max-lines"] = new(PropertyType.PositiveInteger, 1, 8),
            ["text-overflow"] = new(PropertyType.TextOverflow),
            ["text-transform"] = new(PropertyType.TextTransform),
            ["transition-easing"] = new(PropertyType.TransitionEasing),
            ["flex-grow"] = new(PropertyType.BoundedNumber, 0, 8),
            ["flex-shrink"] = new(PropertyType.BoundedNumber, 0, 8),
            ["flex-basis"] = new(PropertyType.LengthOrAuto, 0, 4096),
            ["flex-wrap"] = new(PropertyType.FlexWrap),
            ["align"] = new(PropertyType.Align),
            ["justify"] = new(PropertyType.Justify),
            ["direction"] = new(PropertyType.Direction),
            ["overflow"] = new(PropertyType.Overflow),
            ["text-align"] = new(PropertyType.TextAlign),
        };

    public static IReadOnlyList<string> AllowedProperties { get; } =
        Definitions.Keys.Order(StringComparer.Ordinal).ToArray();

    public static bool IsAllowed(string property) => Definitions.ContainsKey(property);

    internal static bool TryCompute(
        string property,
        string value,
        out GbssComputedValue? computed,
        out bool clamped,
        out string error)
    {
        computed = null;
        clamped = false;
        error = string.Empty;
        if (!Definitions.TryGetValue(property, out var definition))
        {
            error = $"Property '{property}' is not supported.";
            return false;
        }

        switch (definition.Type)
        {
            case PropertyType.Color:
                if (!TryColor(value, out var normalizedColor))
                {
                    error = "Expected transparent, #RGB, #RGBA, #RRGGBB, #RRGGBBAA, rgb(), or rgba() with in-range components.";
                    return false;
                }
                computed = new GbssComputedValue(GbssValueKind.Color, normalizedColor);
                return true;
            case PropertyType.Length:
                return TryLength(value, definition, out computed, out clamped, out error);
            case PropertyType.Spacing:
                return TrySpacing(value, definition, out computed, out clamped, out error);
            case PropertyType.Opacity:
            case PropertyType.Scale:
                if (!TryInvariantDouble(value, out var number))
                {
                    error = "Expected a finite number.";
                    return false;
                }
                var bounded = Clamp(number, definition.Minimum, definition.Maximum, ref clamped);
                computed = new GbssComputedValue(GbssValueKind.Number, Format(bounded), bounded);
                return true;
            case PropertyType.Duration:
                var durationMatch = DurationRegex().Match(value);
                if (!durationMatch.Success || !TryInvariantDouble(durationMatch.Groups["number"].Value, out var duration))
                {
                    error = "Expected a duration in ms or s.";
                    return false;
                }
                if (durationMatch.Groups["unit"].Value == "s") duration *= 1000;
                duration = Clamp(duration, definition.Minimum, definition.Maximum, ref clamped);
                computed = new GbssComputedValue(GbssValueKind.Duration, $"{Format(duration)}ms", duration, "ms");
                return true;
            case PropertyType.FontWeight:
                if (value is "normal" or "bold")
                {
                    computed = new GbssComputedValue(GbssValueKind.Keyword, value);
                    return true;
                }
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var weight) || weight is < 100 or > 900 || weight % 100 != 0)
                {
                    error = "Font weight must be normal, bold, or a 100-900 multiple of 100.";
                    return false;
                }
                computed = new GbssComputedValue(GbssValueKind.Number, weight.ToString(CultureInfo.InvariantCulture), weight);
                return true;
            case PropertyType.FontFamily:
                if (!FontFamilyRegex().IsMatch(value))
                {
                    error = "Font family may contain only quoted or unquoted family names, spaces, commas, and hyphens.";
                    return false;
                }
                computed = new GbssComputedValue(GbssValueKind.FontFamily, NormalizeWhitespace(value));
                return true;
            case PropertyType.Align:
                return TryKeyword(value, ["start", "center", "end", "stretch"], out computed, out error);
            case PropertyType.Justify:
                return TryKeyword(value, ["start", "center", "end", "space-between", "space-around"], out computed, out error);
            case PropertyType.Direction:
                return TryKeyword(value, ["row", "column"], out computed, out error);
            case PropertyType.Overflow:
                return TryKeyword(value, ["clip", "visible"], out computed, out error);
            case PropertyType.TextAlign:
                return TryKeyword(value, ["start", "center", "end"], out computed, out error);
            case PropertyType.Ratio:
                return TryRatio(value, definition, out computed, out clamped, out error);
            case PropertyType.ObjectFit:
                return TryKeyword(value, ["cover", "contain", "fill", "none"], out computed, out error);
            case PropertyType.ObjectPosition:
                return TryKeyword(value, ["center", "top", "right", "bottom", "left", "top-left", "top-right", "bottom-left", "bottom-right"], out computed, out error);
            case PropertyType.Shape:
                return TryKeyword(value, ["rectangle", "rounded", "pill", "circle"], out computed, out error);
            case PropertyType.LineHeight:
                if (!TryInvariantDouble(value, out var lineHeight))
                {
                    error = "Expected a unitless finite line-height.";
                    return false;
                }
                lineHeight = Clamp(lineHeight, definition.Minimum, definition.Maximum, ref clamped);
                computed = new GbssComputedValue(GbssValueKind.Number, Format(lineHeight), lineHeight);
                return true;
            case PropertyType.PositiveInteger:
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var integer))
                {
                    error = "Expected a positive integer.";
                    return false;
                }
                var boundedInteger = (int)Clamp(integer, definition.Minimum, definition.Maximum, ref clamped);
                computed = new GbssComputedValue(GbssValueKind.Integer, boundedInteger.ToString(CultureInfo.InvariantCulture), boundedInteger);
                return true;
            case PropertyType.TextOverflow:
                return TryKeyword(value, ["clip", "ellipsis"], out computed, out error);
            case PropertyType.TextTransform:
                return TryKeyword(value, ["none", "uppercase", "lowercase"], out computed, out error);
            case PropertyType.TransitionEasing:
                return TryKeyword(value, ["linear", "ease-out", "ease-in-out", "spring"], out computed, out error);
            case PropertyType.BoundedNumber:
                if (!TryInvariantDouble(value, out var flexFactor) || flexFactor < 0)
                {
                    error = "Expected a non-negative finite number.";
                    return false;
                }
                flexFactor = Clamp(flexFactor, definition.Minimum, definition.Maximum, ref clamped);
                computed = new GbssComputedValue(GbssValueKind.Number, Format(flexFactor), flexFactor);
                return true;
            case PropertyType.LengthOrAuto:
                if (value == "auto")
                {
                    computed = new GbssComputedValue(GbssValueKind.Keyword, value);
                    return true;
                }
                return TryLength(value, definition, out computed, out clamped, out error);
            case PropertyType.FlexWrap:
                return TryKeyword(value, ["nowrap", "wrap"], out computed, out error);
            default:
                throw new InvalidOperationException("Unknown GBSS property type.");
        }
    }

    private static bool TryLength(
        string value,
        Definition definition,
        out GbssComputedValue? computed,
        out bool clamped,
        out string error)
    {
        computed = null;
        clamped = false;
        error = string.Empty;
        var match = LengthRegex().Match(value);
        if (!match.Success || !TryInvariantDouble(match.Groups["number"].Value, out var number))
        {
            error = "Expected a length using px, em, rem, %, vw, vh, or unitless zero.";
            return false;
        }
        var unit = match.Groups["unit"].Value;
        if (unit.Length == 0 && number != 0)
        {
            error = "Non-zero lengths require px, em, rem, %, vw, or vh.";
            return false;
        }
        var (minimum, maximum) = BoundsForUnit(definition, unit);
        if (!definition.AllowNegative) minimum = Math.Max(0, minimum);
        number = Clamp(number, minimum, maximum, ref clamped);
        computed = new GbssComputedValue(GbssValueKind.Length, $"{Format(number)}{unit}", number, unit);
        return true;
    }

    private static bool TrySpacing(
        string value,
        Definition definition,
        out GbssComputedValue? computed,
        out bool clamped,
        out string error)
    {
        computed = null;
        clamped = false;
        error = string.Empty;
        var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 4)
        {
            error = "Spacing accepts one to four lengths.";
            return false;
        }
        var normalized = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (!TryLength(part, definition, out var item, out var itemClamped, out error)) return false;
            clamped |= itemClamped;
            normalized.Add(item!.Text);
        }
        computed = new GbssComputedValue(GbssValueKind.LengthList, string.Join(' ', normalized));
        return true;
    }

    private static bool TryRatio(
        string value,
        Definition definition,
        out GbssComputedValue? computed,
        out bool clamped,
        out string error)
    {
        computed = null;
        clamped = false;
        error = string.Empty;
        double ratio;
        var match = RatioRegex().Match(value);
        if (match.Success)
        {
            if (!TryInvariantDouble(match.Groups["width"].Value, out var width) ||
                !TryInvariantDouble(match.Groups["height"].Value, out var height) ||
                width <= 0 || height <= 0)
            {
                error = "Aspect ratio components must be finite and greater than zero.";
                return false;
            }
            ratio = width / height;
        }
        else if (!TryInvariantDouble(value, out ratio) || ratio <= 0)
        {
            error = "Expected a positive ratio such as 16/9 or 1.5.";
            return false;
        }
        ratio = Clamp(ratio, definition.Minimum, definition.Maximum, ref clamped);
        computed = new GbssComputedValue(GbssValueKind.Ratio, Format(ratio), ratio);
        return true;
    }

    private static (double Minimum, double Maximum) BoundsForUnit(Definition definition, string unit) => unit switch
    {
        "%" => (definition.AllowNegative ? -100 : 0, 100),
        "em" or "rem" => (definition.AllowNegative ? -16 : 0, Math.Min(16, definition.Maximum)),
        "vw" or "vh" => (definition.AllowNegative ? -100 : 0, 100),
        _ => (definition.Minimum, definition.Maximum),
    };

    private static bool TryKeyword(string value, IReadOnlyList<string> allowed, out GbssComputedValue? computed, out string error)
    {
        if (allowed.Contains(value, StringComparer.Ordinal))
        {
            computed = new GbssComputedValue(GbssValueKind.Keyword, value);
            error = string.Empty;
            return true;
        }
        computed = null;
        error = $"Expected one of: {string.Join(", ", allowed)}.";
        return false;
    }

    private static double Clamp(double value, double minimum, double maximum, ref bool clamped)
    {
        var result = Math.Clamp(value, minimum, maximum);
        clamped |= result != value;
        return result;
    }

    private static bool TryInvariantDouble(string value, out double number) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);

    private static bool TryColor(string value, out string normalized)
    {
        normalized = string.Empty;
        if (value == "transparent")
        {
            normalized = value;
            return true;
        }
        if (HexColorRegex().IsMatch(value))
        {
            normalized = value.ToLowerInvariant();
            return true;
        }
        var match = ColorFunctionRegex().Match(value);
        if (!match.Success) return false;
        var name = match.Groups["name"].Value;
        var parts = match.Groups["arguments"].Value.Split(',').Select(item => item.Trim()).ToArray();
        if (parts.Length != (name == "rgb" ? 3 : 4)) return false;
        for (var index = 0; index < 3; index++)
        {
            var percentage = parts[index].EndsWith('%');
            var numberText = percentage ? parts[index][..^1] : parts[index];
            if (!TryInvariantDouble(numberText, out var component) || component < 0 || component > (percentage ? 100 : 255)) return false;
        }
        if (name == "rgba")
        {
            var percentage = parts[3].EndsWith('%');
            var numberText = percentage ? parts[3][..^1] : parts[3];
            if (!TryInvariantDouble(numberText, out var alpha) || alpha < 0 || alpha > (percentage ? 100 : 1)) return false;
        }
        normalized = $"{name}({string.Join(", ", parts)})".ToLowerInvariant();
        return true;
    }

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string NormalizeWhitespace(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [GeneratedRegex("^(?:#[0-9a-fA-F]{3}|#[0-9a-fA-F]{4}|#[0-9a-fA-F]{6}|#[0-9a-fA-F]{8})$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorRegex();

    [GeneratedRegex("^(?<name>rgb|rgba)\\((?<arguments>[^()]*)\\)$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorFunctionRegex();

    [GeneratedRegex("^(?<number>[+-]?(?:[0-9]+(?:\\.[0-9]+)?|\\.[0-9]+))(?<unit>px|em|rem|%|vw|vh)?$", RegexOptions.CultureInvariant)]
    private static partial Regex LengthRegex();

    [GeneratedRegex("^(?<width>(?:[0-9]+(?:\\.[0-9]+)?|\\.[0-9]+))\\s*/\\s*(?<height>(?:[0-9]+(?:\\.[0-9]+)?|\\.[0-9]+))$", RegexOptions.CultureInvariant)]
    private static partial Regex RatioRegex();

    [GeneratedRegex("^(?<number>[+]?(?:[0-9]+(?:\\.[0-9]+)?|\\.[0-9]+))(?<unit>ms|s)$", RegexOptions.CultureInvariant)]
    private static partial Regex DurationRegex();

    [GeneratedRegex("^(?:[A-Za-z][A-Za-z0-9 -]*|[\"'][A-Za-z][A-Za-z0-9 -]*[\"'])(?:\\s*,\\s*(?:[A-Za-z][A-Za-z0-9 -]*|[\"'][A-Za-z][A-Za-z0-9 -]*[\"']))*$", RegexOptions.CultureInvariant)]
    private static partial Regex FontFamilyRegex();
}
