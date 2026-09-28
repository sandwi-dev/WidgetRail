using System.Globalization;

namespace WidgetRail.WidgetStyling;

public static partial class WrssPropertyCatalog
{
    /// <summary>
    /// Evaluates after token substitution. alpha() changes only a paint color;
    /// the computed protocol value remains a conventional RGBA color string.
    /// Peel nested functions iteratively, so untrusted syntax cannot grow the stack.
    /// </summary>
    private static bool TryColor(string value, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        var multiplier = 1d;
        var functions = 0;
        while (value.StartsWith("alpha(", StringComparison.Ordinal))
        {
            if (++functions > WrssLimits.MaximumColorFunctionNesting)
            {
                error = $"Colors support at most {WrssLimits.MaximumColorFunctionNesting} nested alpha() functions.";
                return false;
            }
            if (!TryAlphaArguments(value, out var color, out var factor))
            {
                error = "Expected alpha(color, multiplier) with exactly two arguments and a finite multiplier from 0 to 1 or 0% to 100%.";
                return false;
            }
            multiplier *= factor;
            value = color;
        }
        if (!TryBaseColor(value, out normalized))
        {
            error = "Expected transparent, #RGB, #RGBA, #RRGGBB, #RRGGBBAA, rgb(), rgba(), or alpha(color, multiplier) with in-range components.";
            return false;
        }
        if (functions == 0) return true;

        // Base validation has already bounded every channel. Preserve RGB
        // percentage/fractional syntax; do not quantize channels or nested alpha.
        string[] channels;
        var existingAlpha = 1d;
        if (normalized == "transparent")
        {
            channels = ["0", "0", "0"];
            existingAlpha = 0;
        }
        else if (normalized.StartsWith('#'))
        {
            var hex = normalized[1..];
            if (hex.Length is 3 or 4) hex = string.Concat(hex.Select(ch => new string(ch, 2)));
            channels = Enumerable.Range(0, 3)
                .Select(index => byte.Parse(hex.AsSpan(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                    .ToString(CultureInfo.InvariantCulture)).ToArray();
            if (hex.Length == 8) existingAlpha = byte.Parse(hex.AsSpan(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
        }
        else
        {
            var open = normalized.IndexOf('(');
            var arguments = normalized[(open + 1)..^1].Split(',').Select(part => part.Trim()).ToArray();
            channels = arguments[..3];
            if (arguments.Length == 4) TryAlphaMultiplier(arguments[3], out existingAlpha);
        }
        normalized = $"rgba({string.Join(", ", channels)}, {(existingAlpha * multiplier).ToString("0.################", CultureInfo.InvariantCulture)})";
        return true;
    }

    private static bool TryAlphaArguments(string value, out string color, out double factor)
    {
        color = string.Empty;
        factor = 0;
        if (!value.EndsWith(')')) return false;
        var depth = 0;
        var separator = -1;
        // The outer parentheses are excluded. Only a comma at depth zero
        // separates arguments; rgba() and substituted fallback commas stay local.
        for (var index = 6; index < value.Length - 1; ++index)
        {
            var character = value[index];
            if (character == '(') ++depth;
            else if (character == ')' && --depth < 0) return false;
            else if (character == ',' && depth == 0)
            {
                if (separator >= 0) return false;
                separator = index;
            }
        }
        if (depth != 0 || separator < 0) return false;
        color = value[6..separator].Trim();
        return color.Length != 0 && TryAlphaMultiplier(value[(separator + 1)..^1].Trim(), out factor);
    }

    private static bool TryAlphaMultiplier(string value, out double factor)
    {
        var percentage = value.EndsWith('%');
        if (!TryInvariantDouble(percentage ? value[..^1] : value, out factor) || factor < 0 || factor > (percentage ? 100 : 1)) return false;
        if (percentage) factor /= 100;
        return true;
    }
}
