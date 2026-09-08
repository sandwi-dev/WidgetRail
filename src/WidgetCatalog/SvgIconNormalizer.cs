using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetCatalog;

internal sealed record NormalizedSvgIcon(
    byte[] Bytes,
    string SourceSha256,
    string NormalizedSha256);

/// <summary>Validates and canonicalizes the static SVG subset accepted by the private native decoder.</summary>
internal static partial class SvgIconNormalizer
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static readonly HashSet<string> Elements = new(StringComparer.Ordinal)
    {
        "svg", "g", "path", "rect", "circle", "ellipse", "line", "polyline", "polygon",
        "defs", "style",
    };
    private static readonly HashSet<string> PresentationAttributes = new(StringComparer.Ordinal)
    {
        "fill", "stroke", "stroke-width", "opacity", "fill-opacity", "stroke-opacity",
        "fill-rule", "clip-rule", "transform",
    };
    private static readonly HashSet<string> GeometryAttributes = new(StringComparer.Ordinal)
    {
        "viewBox", "width", "height", "preserveAspectRatio", "d", "x", "y", "x1", "y1",
        "x2", "y2", "cx", "cy", "r", "rx", "ry", "points",
    };
    private static readonly HashSet<string> StyleProperties = new(StringComparer.Ordinal)
    {
        "fill", "stroke", "stroke-width", "opacity", "fill-opacity", "stroke-opacity",
        "fill-rule", "clip-rule",
    };
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal const int MaximumDepth = 32;
    internal const int MaximumElements = 512;
    internal const int MaximumAttributes = 2_048;
    internal const int MaximumPathTokens = 8_192;
    internal const int MaximumTransformOperations = 256;
    internal const int MaximumStyleRules = 64;

    internal static NormalizedSvgIcon Normalize(ReadOnlySpan<byte> source)
    {
        if (source.Length is < 1 or > ProtocolConstants.MaximumPackageIconBytes)
            throw Invalid("svg_size", "SVG icon bytes are outside the supported bound.");
        string text;
        try { text = StrictUtf8.GetString(source); }
        catch (DecoderFallbackException exception)
        {
            throw Invalid("svg_utf8", "SVG icon must use canonical UTF-8.", exception);
        }

        XDocument document;
        try
        {
            using var textReader = new StringReader(text);
            using var reader = XmlReader.Create(textReader, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = ProtocolConstants.MaximumPackageIconBytes,
                MaxCharactersFromEntities = 0,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
            });
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException)
        {
            throw Invalid("svg_xml", "SVG icon XML is invalid or contains prohibited markup.", exception);
        }

        var root = document.Root;
        if (root is null || root.Name != Svg + "svg")
            throw Invalid("svg_root", "SVG icon requires one SVG-namespace root element.");
        if (root.Attribute("viewBox") is not { } viewBox)
            throw Invalid("svg_viewbox", "SVG icon requires a finite four-number viewBox.");
        ValidateViewBox(viewBox.Value);

        var styles = ReadStyles(root);
        var elementCount = 0;
        var attributeCount = 0;
        var pathTokens = 0;
        var transformOperations = 0;
        Visit(root, 1);
        foreach (var style in root.Descendants(Svg + "style").ToArray()) style.Remove();
        foreach (var definitions in root.Descendants(Svg + "defs").ToArray())
            if (!definitions.Elements().Any()) definitions.Remove();

        byte[] normalized;
        using (var memory = new MemoryStream())
        {
            using (var writer = XmlWriter.Create(memory, new XmlWriterSettings
            {
                Encoding = StrictUtf8,
                OmitXmlDeclaration = true,
                Indent = false,
                NewLineHandling = NewLineHandling.None,
            })) document.Save(writer);
            normalized = memory.ToArray();
        }
        if (normalized.Length > ProtocolConstants.MaximumPackageIconBytes)
            throw Invalid("svg_normalized_size", "Normalized SVG icon exceeds its byte bound.");
        return new(
            normalized,
            Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant(),
            Convert.ToHexString(SHA256.HashData(normalized)).ToLowerInvariant());

        void Visit(XElement element, int depth)
        {
            if (++elementCount > MaximumElements || depth > MaximumDepth)
                throw Invalid("svg_complexity", "SVG icon element count or depth exceeds its bound.");
            if (element.Name.Namespace != Svg || !Elements.Contains(element.Name.LocalName))
                throw Invalid("svg_element", $"Unsupported SVG element '{element.Name.LocalName}'.");
            if (element.Name.LocalName == "defs" && element.Elements().Any(child => child.Name != Svg + "style"))
                throw Invalid("svg_defs", "SVG defs may contain only bounded class style declarations.");
            if (element.Name.LocalName == "style") return;

            // IDs have no semantic purpose in the closed subset because use,
            // href, fragment URLs, animation, and script are all prohibited.
            // Removing inert editor-export IDs admits ordinary design-tool
            // output without creating a reference namespace.
            element.Attribute("id")?.Remove();

            var classNames = (element.Attribute("class")?.Value ?? string.Empty)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            foreach (var className in classNames)
            {
                if (!styles.TryGetValue(className, out var declarations))
                    throw Invalid("svg_class", $"SVG class '{className}' has no supported declaration.");
                foreach (var declaration in declarations)
                    element.SetAttributeValue(declaration.Key, declaration.Value);
            }
            element.Attribute("class")?.Remove();
            if (element.Attribute("style") is { } inline)
            {
                foreach (var declaration in ParseDeclarations(inline.Value))
                    element.SetAttributeValue(declaration.Key, declaration.Value);
                inline.Remove();
            }

            foreach (var attribute in element.Attributes().ToArray())
            {
                if (attribute.IsNamespaceDeclaration) continue;
                if (++attributeCount > MaximumAttributes)
                    throw Invalid("svg_complexity", "SVG icon attribute count exceeds its bound.");
                var name = attribute.Name.LocalName;
                if (attribute.Name.Namespace != XNamespace.None ||
                    name.StartsWith("on", StringComparison.OrdinalIgnoreCase) ||
                    (!PresentationAttributes.Contains(name) && !GeometryAttributes.Contains(name)))
                    throw Invalid("svg_attribute", $"Unsupported SVG attribute '{name}'.");
                ValidateValue(name, attribute.Value);
                if (name == "d" || name == "points")
                    pathTokens = checked(pathTokens + NumericOrCommandTokenRegex().Matches(attribute.Value).Count);
                if (name == "transform")
                    transformOperations = checked(transformOperations + TransformRegex().Matches(attribute.Value).Count);
                if (pathTokens > MaximumPathTokens || transformOperations > MaximumTransformOperations)
                    throw Invalid("svg_complexity", "SVG path or transform complexity exceeds its bound.");
            }
            foreach (var child in element.Elements().ToArray()) Visit(child, depth + 1);
        }
    }

    private static Dictionary<string, Dictionary<string, string>> ReadStyles(XElement root)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var rules = 0;
        foreach (var style in root.Descendants(Svg + "style"))
        {
            var text = style.Value;
            var offset = 0;
            foreach (Match match in StyleRuleRegex().Matches(text))
            {
                if (match.Index != offset && !string.IsNullOrWhiteSpace(text[offset..match.Index]))
                    throw Invalid("svg_style", "SVG style contains an unsupported selector or token.");
                if (++rules > MaximumStyleRules)
                    throw Invalid("svg_style", "SVG style rule count exceeds its bound.");
                var className = match.Groups[1].Value;
                if (!WidgetManifestValidator.IsPackageIconAssetId(className) ||
                    !result.TryAdd(className, ParseDeclarations(match.Groups[2].Value)))
                    throw Invalid("svg_style", "SVG style classes must be unique simple identifiers.");
                offset = match.Index + match.Length;
            }
            if (!string.IsNullOrWhiteSpace(text[offset..]))
                throw Invalid("svg_style", "SVG style contains unsupported trailing content.");
        }
        return result;
    }

    private static Dictionary<string, string> ParseDeclarations(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var segment in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = segment.Split(':', 2);
            var name = pair[0].Trim();
            var value = pair.Length == 2 ? pair[1].Trim() : string.Empty;
            if (!StyleProperties.Contains(name) || value.Length is < 1 or > 96 ||
                !result.TryAdd(name, value))
                throw Invalid("svg_style", "SVG style contains an unsupported or duplicate declaration.");
            ValidateValue(name, value);
        }
        return result;
    }

    private static void ValidateValue(string name, string value)
    {
        if (value.Length is < 1 or > ProtocolConstants.MaximumPackageIconBytes ||
            value.Contains("url(", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("javascript:", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("data:", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("NaN", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Infinity", StringComparison.OrdinalIgnoreCase))
            throw Invalid("svg_value", $"SVG attribute '{name}' contains an unsafe value.");
        if (name is "fill" or "stroke" &&
            !ColorRegex().IsMatch(value) && value is not "none" and not "currentColor")
            throw Invalid("svg_color", $"SVG {name} color is unsupported.");
        if (name == "transform") ValidateTransform(value);
        if (name == "viewBox") ValidateViewBox(value);
        if (name == "d") ValidatePathData(value);
        if (name == "points") ValidateNumberList(value, exactCount: null, evenCount: true);
        if (name == "preserveAspectRatio" && !PreserveAspectRatioRegex().IsMatch(value))
            throw Invalid("svg_geometry", "SVG preserveAspectRatio is unsupported.");
        if (GeometryAttributes.Contains(name) &&
            name is not ("viewBox" or "d" or "points" or "preserveAspectRatio") &&
            !LengthRegex().IsMatch(value))
            throw Invalid("svg_geometry", $"SVG geometry attribute '{name}' is unsupported.");
        foreach (Match number in NumberRegex().Matches(value))
            if (!double.TryParse(number.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
                !double.IsFinite(parsed) || Math.Abs(parsed) > 1_000_000)
                throw Invalid("svg_geometry", "SVG geometry must be finite and bounded.");
    }

    private static void ValidateViewBox(string value)
    {
        var values = ValidateNumberList(value, exactCount: 4, evenCount: false);
        if (values[2] <= 0 || values[3] <= 0)
            throw Invalid("svg_viewbox", "SVG viewBox width and height must be positive.");
    }

    private static void ValidateTransform(string value)
    {
        var offset = 0;
        var operations = 0;
        foreach (Match match in TransformRegex().Matches(value))
        {
            if (!OnlySeparators(value.AsSpan(offset, match.Index - offset)))
                throw Invalid("svg_transform", "SVG transform syntax is unsupported.");
            if (++operations > MaximumTransformOperations)
                throw Invalid("svg_complexity", "SVG transform complexity exceeds its bound.");
            var name = match.Groups["name"].Value;
            var arguments = ValidateNumberList(
                match.Groups["args"].Value, exactCount: null, evenCount: false);
            var validArity = name switch
            {
                "matrix" => arguments.Length == 6,
                "translate" or "scale" => arguments.Length is 1 or 2,
                "rotate" => arguments.Length is 1 or 3,
                "skewX" or "skewY" => arguments.Length == 1,
                _ => false,
            };
            if (!validArity)
                throw Invalid("svg_transform", $"SVG transform '{name}' has an invalid arity.");
            offset = match.Index + match.Length;
        }
        if (operations == 0 || !OnlySeparators(value.AsSpan(offset)))
            throw Invalid("svg_transform", "SVG transform syntax is unsupported.");
    }

    private static void ValidatePathData(string value)
    {
        var matches = PathTokenRegex().Matches(value).Cast<Match>().ToArray();
        if (matches.Length == 0 || matches.Length > MaximumPathTokens ||
            !matches[0].Value.Equals("M", StringComparison.OrdinalIgnoreCase))
            throw Invalid("svg_geometry", "SVG path data is unsupported.");
        var offset = 0;
        foreach (var match in matches)
        {
            if (!OnlySeparators(value.AsSpan(offset, match.Index - offset)))
                throw Invalid("svg_geometry", "SVG path data contains an unsupported token.");
            offset = match.Index + match.Length;
        }
        if (!OnlySeparators(value.AsSpan(offset)))
            throw Invalid("svg_geometry", "SVG path data contains an unsupported token.");
    }

    private static double[] ValidateNumberList(
        string value,
        int? exactCount,
        bool evenCount)
    {
        var matches = NumberRegex().Matches(value).Cast<Match>().ToArray();
        if (matches.Length == 0 || (exactCount is { } count && matches.Length != count) ||
            (evenCount && matches.Length % 2 != 0))
            throw Invalid("svg_geometry", "SVG geometry has an invalid numeric shape.");
        var result = new double[matches.Length];
        var offset = 0;
        for (var index = 0; index < matches.Length; index++)
        {
            var match = matches[index];
            if (!OnlySeparators(value.AsSpan(offset, match.Index - offset)) ||
                !double.TryParse(match.Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out result[index]) ||
                !double.IsFinite(result[index]) || Math.Abs(result[index]) > 1_000_000)
                throw Invalid("svg_geometry", "SVG geometry must be finite and bounded.");
            offset = match.Index + match.Length;
        }
        if (!OnlySeparators(value.AsSpan(offset)))
            throw Invalid("svg_geometry", "SVG geometry contains an unsupported token.");
        return result;
    }

    private static bool OnlySeparators(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
            if (!char.IsWhiteSpace(character) && character != ',') return false;
        return true;
    }

    private static WidgetPackageException Invalid(string code, string message, Exception? inner = null) =>
        new(code, message, inner);

    [GeneratedRegex(@"\.([a-z][a-z0-9]*(?:[._-][a-z0-9]+)*)\s*\{([^{}]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex StyleRuleRegex();
    [GeneratedRegex(@"[A-Za-z]|[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumericOrCommandTokenRegex();
    [GeneratedRegex(@"(?<name>matrix|translate|scale|rotate|skewX|skewY)\s*\((?<args>[^()]*)\)", RegexOptions.CultureInvariant)]
    private static partial Regex TransformRegex();
    [GeneratedRegex(@"^(?:none|currentColor|#[0-9A-Fa-f]{3,8}|rgb\(\s*\d{1,3}\s*,\s*\d{1,3}\s*,\s*\d{1,3}\s*\)|black|white|transparent)$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorRegex();
    [GeneratedRegex(@"^(?:[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?)(?:px|%)?$", RegexOptions.CultureInvariant)]
    private static partial Regex LengthRegex();
    [GeneratedRegex(@"^(?:none|x(?:Min|Mid|Max)Y(?:Min|Mid|Max)(?:\s+(?:meet|slice))?)$", RegexOptions.CultureInvariant)]
    private static partial Regex PreserveAspectRatioRegex();
    [GeneratedRegex(@"[MmLlHhVvCcSsQqTtAaZz]|[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex PathTokenRegex();
    [GeneratedRegex(@"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();
}
