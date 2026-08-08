using GameBarAlternative.PlatformSettings;

namespace GameBarAlternative.GbarCli;

internal static class ConfigCommand
{
    private const string Usage =
        "Usage: gbar config <set|get|list|remove|clear> <widget-id> [key] [value] " +
        "--publisher <publisher-id> [--settings-root <root>]";

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            await output.WriteLineAsync(Usage);
            await output.WriteLineAsync(
                "Configuration is non-secret. OAuth tokens, passwords, and client secrets are rejected by design.");
            return 0;
        }

        var parsed = new CommandArguments(args, "--publisher", "--settings-root");
        var positionals = parsed.Positionals;
        if (positionals.Count < 2) throw new CliUsageException(Usage);
        var action = positionals[0];
        var packageId = positionals[1];
        var publisherId = parsed.Option("--publisher") ??
            throw new CliUsageException("--publisher is required so configuration authority is unambiguous.");
        var root = ResolveSettingsRoot(parsed.Option("--settings-root"));
        var store = new WidgetConfigurationStore(new PlatformSettingsPaths(root));

        switch (action)
        {
            case "set" when positionals.Count == 4:
                RejectSecretKey(positionals[2]);
                await store.SetAsync(packageId, publisherId, positionals[2], positionals[3], cancellationToken)
                    .ConfigureAwait(false);
                await output.WriteLineAsync($"Configured {packageId}:{positionals[2]}.");
                return 0;
            case "get" when positionals.Count == 3:
            {
                var snapshot = await store.ReadAsync(packageId, publisherId, cancellationToken)
                    .ConfigureAwait(false);
                if (!snapshot.Values.TryGetValue(positionals[2], out var value))
                    throw new CliUsageException(
                        $"Configuration key '{positionals[2]}' is not set for '{packageId}'.");
                await output.WriteLineAsync(value);
                return 0;
            }
            case "list" when positionals.Count == 2:
            {
                var snapshot = await store.ReadAsync(packageId, publisherId, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var key in snapshot.Values.Keys.Order(StringComparer.Ordinal))
                    await output.WriteLineAsync(key);
                return 0;
            }
            case "remove" when positionals.Count == 3:
                await store.RemoveAsync(packageId, publisherId, positionals[2], cancellationToken)
                    .ConfigureAwait(false);
                await output.WriteLineAsync($"Removed {packageId}:{positionals[2]}.");
                return 0;
            case "clear" when positionals.Count == 2:
                await store.ClearAsync(packageId, publisherId, cancellationToken).ConfigureAwait(false);
                await output.WriteLineAsync($"Cleared configuration for {packageId}.");
                return 0;
            default:
                throw new CliUsageException(Usage);
        }
    }

    private static void RejectSecretKey(string key)
    {
        if (key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("credential", StringComparison.OrdinalIgnoreCase))
            throw new CliUsageException(
                "Secret-like values cannot be stored in widget configuration. Use a brokered credential capability.");
    }

    private static string ResolveSettingsRoot(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested)) return Path.GetFullPath(requested);
        return PlatformSettingsPaths.CreateDefault().RootDirectory;
    }
}
