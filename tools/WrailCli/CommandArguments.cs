namespace WidgetRail.WrailCli;

internal sealed class CommandArguments
{
    private readonly List<string> _positionals = [];
    private readonly Dictionary<string, string> _options = new(StringComparer.Ordinal);
    private readonly HashSet<string> _flags = new(StringComparer.Ordinal);

    public CommandArguments(IReadOnlyList<string> args, params string[] knownOptions)
        : this(args, knownOptions, [])
    {
    }

    public CommandArguments(
        IReadOnlyList<string> args,
        IReadOnlyList<string> knownOptions,
        IReadOnlyList<string> knownFlags)
    {
        var known = knownOptions.ToHashSet(StringComparer.Ordinal);
        var flags = knownFlags.ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; index++)
        {
            var value = args[index];
            if (!value.StartsWith("--", StringComparison.Ordinal))
            {
                _positionals.Add(value);
                continue;
            }

            if (flags.Contains(value))
            {
                if (!_flags.Add(value))
                    throw new CliUsageException($"Flag '{value}' was supplied more than once.");
                continue;
            }
            if (!known.Contains(value)) throw new CliUsageException($"Unknown option '{value}'.");
            if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new CliUsageException($"Option '{value}' requires a value.");
            if (!_options.TryAdd(value, args[++index]))
                throw new CliUsageException($"Option '{value}' was supplied more than once.");
        }
    }

    public IReadOnlyList<string> Positionals => _positionals;
    public string? Option(string name) => _options.GetValueOrDefault(name);
    public bool HasFlag(string name) => _flags.Contains(name);
}
