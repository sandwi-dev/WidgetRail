using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed class PlayniteSetupDiscoveryService(IPlayniteSetupPlatform platform, string stateRoot)
{
    internal string SelectionPath => Path.Combine(stateRoot, "playnite-installation.json");

    internal async ValueTask<PlayniteSetupDiscovery> DiscoverAsync(string? programPath, string? dataPath, CancellationToken token)
    {
        var running = await platform.ProcessesAsync(token).ConfigureAwait(false);
        var candidates = new Dictionary<string, PlayniteSetupInstallation>(StringComparer.Ordinal);
        var rememberedPrograms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? message = null;
        if (!string.IsNullOrWhiteSpace(programPath))
        {
            var program = PlayniteSetupFiles.LocalPath(programPath);
            if (program.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) program = Path.GetDirectoryName(program)!;
            var active = running.FirstOrDefault(process => string.Equals(Path.GetDirectoryName(process.Executable), program, StringComparison.OrdinalIgnoreCase));
            if (active is not null && active.CommandLine is null)
                throw new PlayniteSetupException("Playnite's launch arguments are unavailable. Close Playnite before selecting its folders.");
            var data = string.IsNullOrWhiteSpace(dataPath) ? PlayniteSetupPlatform.DataDirectory(program, active?.CommandLine) : PlayniteSetupFiles.LocalPath(dataPath);
            if (!string.IsNullOrWhiteSpace(dataPath) && !Directory.Exists(data))
                throw new PlayniteSetupException("The selected data folder does not exist. Choose your existing Playnite profile folder, or leave it empty for automatic detection.");
            var candidate = Candidate(program, data, active is not null, active?.Fullscreen ?? false);
            if (active is not null && !PlayniteSetupPlatform.Matches(active, candidate))
                throw new PlayniteSetupException("The selected data folder differs from the running Playnite profile. Close Playnite before switching profiles.");
            return new([candidate]);
        }
        foreach (var process in running)
        {
            if (process.CommandLine is null) { message = "Playnite's active data folder could not be determined. Close it or enter the folders under Advanced."; continue; }
            TryAdd(Path.GetDirectoryName(process.Executable)!, PlayniteSetupPlatform.DataDirectory(Path.GetDirectoryName(process.Executable)!, process.CommandLine), true, process.Fullscreen);
        }
        if (File.Exists(SelectionPath))
        {
            try
            {
                var saved = PlayniteSetupFiles.ReadObject(SelectionPath, 8192);
                if (saved["program"]?.GetValue<string>() is { } program && saved["data"]?.GetValue<string>() is { } data &&
                    !running.Any(process => string.Equals(Path.GetDirectoryName(process.Executable), program, StringComparison.OrdinalIgnoreCase)))
                {
                    rememberedPrograms.Add(program);
                    if (Directory.Exists(data)) TryAdd(program, data, false, false);
                    else message = "The saved Playnite data folder is unavailable. Restore it or choose its current location under Advanced.";
                }
            }
            catch (Exception error) when (error is IOException or System.Text.Json.JsonException or PlayniteSetupException) { }
        }
        foreach (var program in platform.InstallationFolders())
        {
            if (rememberedPrograms.Contains(program) ||
                running.Any(process => string.Equals(Path.GetDirectoryName(process.Executable), program, StringComparison.OrdinalIgnoreCase)) ||
                candidates.Values.Any(candidate => string.Equals(candidate.ProgramDirectory, program, StringComparison.OrdinalIgnoreCase))) continue;
            TryAdd(program, PlayniteSetupPlatform.DataDirectory(program, null), false, false);
        }
        return new(candidates.Values.OrderByDescending(candidate => candidate.Running).ThenBy(candidate => candidate.DisplayName).ToArray(),
            message ?? (candidates.Count == 0 ? "Playnite was not found. Install it first, or choose its folders under Advanced." :
                candidates.Count > 1 ? "Choose the Playnite installation and profile you want to connect." : null));

        void TryAdd(string program, string data, bool active, bool fullscreen)
        {
            try { var item = Candidate(program, data, active, fullscreen); candidates.TryAdd(item.Id, item); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException or PlayniteSetupException) { }
        }
    }

    internal static PlayniteSetupInstallation Candidate(string program, string data, bool running, bool fullscreen)
    {
        program = PlayniteSetupFiles.LocalPath(program);
        data = PlayniteSetupFiles.LocalPath(data);
        PlayniteSetupFiles.NoLinks(program);
        PlayniteSetupFiles.NoLinks(data);
        if (!Directory.Exists(data))
            throw new PlayniteSetupException("The Playnite data folder is unavailable. Start Playnite once to initialize it, or choose your existing profile under Advanced.");
        if (!File.Exists(Path.Combine(program, "Playnite.DesktopApp.exe")) || !File.Exists(Path.Combine(program, "Playnite.dll")))
            throw new PlayniteSetupException("That folder does not contain a Playnite installation.");
        var sdk = Path.Combine(program, "Playnite.SDK.dll");
        var identity = AssemblyName.GetAssemblyName(sdk);
        if (identity.Name != "Playnite.SDK" || identity.Version is null || identity.Version < new Version(6, 15, 0, 0))
            throw new PlayniteSetupException("Update Playnite before installing this integration. Playnite SDK 6.15 or newer is required.");
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((program + "\n" + data).ToUpperInvariant())))[..24];
        return new(id, program, data, (running ? "Running · " : "") + data, running, fullscreen);
    }

    internal void Remember(PlayniteSetupInstallation installation) => PlayniteSetupFiles.WriteObject(SelectionPath,
        new() { ["program"] = installation.ProgramDirectory, ["data"] = installation.DataDirectory });
}
