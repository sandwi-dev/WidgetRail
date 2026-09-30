using System.Text;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Windows launch argument tokenization; command-line args are already tokenized.</summary>
internal static class FrontendArguments
{
    internal static string[] Parse(string launchArguments, IEnumerable<string> commandLineArguments) =>
        commandLineArguments.Concat(Tokenize(launchArguments)).Distinct(StringComparer.Ordinal).ToArray();

    internal static string? Value(IEnumerable<string> arguments, string name) => arguments
        .FirstOrDefault(argument => argument.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length + 1)..];

    internal static string Serialize(IEnumerable<string> arguments) => string.Join(" ", arguments.Select(Quote));

    private static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { ++slashes; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            slashes = 0;
            result.Append(character);
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    // Matches Windows quoting for an argument tail (not argv[0]); backslashes
    // are special only immediately before a quote. Quoted paths retain spaces.
    private static IEnumerable<string> Tokenize(string arguments)
    {
        var token = new StringBuilder();
        var quoted = false;
        var started = false;
        for (var index = 0; index < arguments.Length; ++index)
        {
            var value = arguments[index];
            if (!quoted && char.IsWhiteSpace(value))
            {
                if (started) { yield return token.ToString(); token.Clear(); started = false; }
                continue;
            }
            started = true;
            if (value == '\\')
            {
                var start = index;
                while (index < arguments.Length && arguments[index] == '\\') ++index;
                var count = index - start;
                if (index < arguments.Length && arguments[index] == '"')
                {
                    token.Append('\\', count / 2);
                    if ((count & 1) == 0) quoted = !quoted;
                    else token.Append('"');
                }
                else { token.Append('\\', count); --index; }
            }
            else if (value == '"') quoted = !quoted;
            else token.Append(value);
        }
        if (started) yield return token.ToString();
    }
}
