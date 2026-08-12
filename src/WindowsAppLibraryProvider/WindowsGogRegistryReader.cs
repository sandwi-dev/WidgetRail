using Microsoft.Win32;

namespace GameBarAlternative.WindowsAppLibraryProvider;

/// <summary>
/// Reads only GOG's machine-wide installed-game registration roots. Registry
/// values remain inside the trusted provider and are validated by the focused
/// application source before they become launch authority.
/// </summary>
internal sealed class WindowsGogRegistryReader : IGogRegistryReader
{
    internal const int MaximumRegistrations = 4_096;
    private const string GamesKey = @"SOFTWARE\GOG.com\Games";
    internal const string Registry32 = "registry32";
    internal const string Registry64 = "registry64";

    public GogRegistrySnapshot Enumerate(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            return new(false, []);

        try
        {
            var records = new List<GogRegistryRecord>();
            ReadView(RegistryView.Registry32, Registry32, records, cancellationToken);
            ReadView(RegistryView.Registry64, Registry64, records, cancellationToken);
            return records.Count > MaximumRegistrations
                ? new(false, [])
                : new(true, records.AsReadOnly());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (IsRegistryFailure(exception))
        {
            return new(false, []);
        }
    }

    public GogRegistryRecord? ReadExact(
        string registryView,
        string keyName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || !ValidKeyName(keyName) ||
            !TryView(registryView, out var view)) return null;
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var games = machine.OpenSubKey(GamesKey, writable: false);
            using var game = games?.OpenSubKey(keyName, writable: false);
            return game is null ? null : Read(registryView, keyName, game);
        }
        catch (Exception exception) when (IsRegistryFailure(exception))
        {
            return null;
        }
    }

    private static void ReadView(
        RegistryView view,
        string viewName,
        List<GogRegistryRecord> records,
        CancellationToken cancellationToken)
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
        using var games = machine.OpenSubKey(GamesKey, writable: false);
        if (games is null) return;
        var names = games.GetSubKeyNames()
            .Take(MaximumRegistrations + 1)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (names.Length > MaximumRegistrations)
        {
            records.AddRange(Enumerable.Repeat(
                new GogRegistryRecord(viewName, string.Empty, null, null, null),
                MaximumRegistrations + 1));
            return;
        }

        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var game = games.OpenSubKey(name, writable: false);
            records.Add(game is null
                ? new(viewName, name, null, null, null)
                : Read(viewName, name, game));
        }
    }

    private static GogRegistryRecord Read(
        string view,
        string keyName,
        RegistryKey key) => new(
        view,
        keyName,
        key.GetValue("gameID", null, RegistryValueOptions.DoNotExpandEnvironmentNames),
        key.GetValue("gameName", null, RegistryValueOptions.DoNotExpandEnvironmentNames),
        key.GetValue("path", null, RegistryValueOptions.DoNotExpandEnvironmentNames));

    private static bool TryView(string value, out RegistryView view)
    {
        view = value switch
        {
            Registry32 => RegistryView.Registry32,
            Registry64 => RegistryView.Registry64,
            _ => default,
        };
        return value is Registry32 or Registry64;
    }

    private static bool ValidKeyName(string value) =>
        value.Length is > 0 and <= 20 && value.All(char.IsAsciiDigit);

    private static bool IsRegistryFailure(Exception exception) => exception is
        IOException or UnauthorizedAccessException or InvalidOperationException or
        System.Security.SecurityException or ArgumentException or NotSupportedException;
}
