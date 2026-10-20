using System.Text.Json.Nodes;

namespace WidgetRail.Samples.PlayniteLibrary;

// Staging and recovery live outside Extensions: Playnite scans that directory
// recursively, so an in-place "backup" would be loaded as another plugin.
internal sealed class PlayniteSetupDeployment
{
    private readonly PlayniteSetupInstallation installation;
    private readonly PlayniteSetupPackage package;
    private readonly string target;
    private readonly string root;
    private string Journal => Path.Combine(root, "transaction.json");
    private string Previous => Path.Combine(root, "previous");
    private string Staging => Path.Combine(root, "staging");
    private string MainConfig => Path.Combine(installation.DataDirectory, "config.json");
    private string PluginConfig => Path.Combine(PlayniteSetupFiles.PluginData(installation.DataDirectory), "config.json");

    internal PlayniteSetupDeployment(PlayniteSetupInstallation installation, string target, PlayniteSetupPackage package)
    {
        this.installation = installation;
        this.package = package;
        this.target = PlayniteSetupFiles.LocalPath(target);
        var dataExtensions = Path.Combine(installation.DataDirectory, "Extensions");
        var programExtensions = Path.Combine(installation.ProgramDirectory, "Extensions");
        var owner = PlayniteSetupFiles.Within(dataExtensions, target) ? installation.DataDirectory :
            PlayniteSetupFiles.Within(programExtensions, target) ? installation.ProgramDirectory :
            throw new PlayniteSetupException("The integration folder is outside the selected Playnite installation.");
        root = Path.Combine(owner, "WidgetRail.Setup", "PlayniteBridge");
        PlayniteSetupFiles.NoLinks(root);
        PlayniteSetupFiles.NoLinks(target);
    }

    internal bool NeedsRecovery => File.Exists(Journal) && PlayniteSetupFiles.ReadObject(Journal)["complete"]?.GetValue<bool>() != true;

    internal void Prepare(bool replaceFiles)
    {
        if (NeedsRecovery) throw new PlayniteSetupException("An interrupted integration update must be recovered before continuing.");
        Directory.CreateDirectory(root);
        DeleteOwned(Staging);
        if (replaceFiles)
        {
            if (Directory.Exists(target)) CopyTree(target, Staging);
            package.Extract(Staging);
        }
        // A prior successful backup is retired only after the replacement package
        // has been staged and verified. Keep one rollback copy, not a growing history.
        DeleteOwned(Previous);
        Backup(MainConfig, "main-config.before");
        Backup(PluginConfig, "plugin-config.before");
        var record = new JsonObject
        {
            ["target"] = target, ["originalPresent"] = Directory.Exists(target), ["replaceFiles"] = replaceFiles,
            ["originalDigest"] = File.Exists(Path.Combine(target, "PlayniteBridge.dll")) ? PlayniteSetupFiles.Digest(Path.Combine(target, "PlayniteBridge.dll")) : null,
            ["newDigest"] = replaceFiles ? package.DllDigest : null,
            ["mainConfigPresent"] = File.Exists(MainConfig), ["pluginConfigPresent"] = File.Exists(PluginConfig),
            ["complete"] = false, ["settingsWritten"] = false,
        };
        PlayniteSetupFiles.WriteObject(Journal, record);
    }

    internal void Commit()
    {
        var record = Record();
        if (record["replaceFiles"]!.GetValue<bool>())
        {
            PlayniteSetupFiles.NoLinks(target);
            if (Directory.Exists(target)) Directory.Move(target, Previous);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Directory.Move(Staging, target);
        }
        // Record before writing: rollback is correct even if the process exits
        // between the two atomic settings-file replacements.
        record["settingsWritten"] = true;
        PlayniteSetupFiles.WriteObject(Journal, record);
        var settings = File.Exists(MainConfig) ? PlayniteSetupFiles.ReadObject(MainConfig, 32 * 1024 * 1024) : null;
        if (settings?["DisabledPlugins"] is JsonArray disabled)
        {
            for (var index = disabled.Count - 1; index >= 0; index--)
                if (disabled[index]?.GetValue<string>() is PlayniteSetupFiles.ExtensionId or PlayniteSetupFiles.PluginId) disabled.RemoveAt(index);
            PlayniteSetupFiles.WriteObject(MainConfig, settings);
        }
        var plugin = File.Exists(PluginConfig) ? PlayniteSetupFiles.ReadObject(PluginConfig) : new JsonObject();
        plugin["NetworkPromptDismissed"] = true;
        PlayniteSetupFiles.WriteObject(PluginConfig, plugin);
    }

    internal void Complete()
    {
        var record = Record();
        record["complete"] = true;
        PlayniteSetupFiles.WriteObject(Journal, record);
    }

    internal void Rollback()
    {
        if (!File.Exists(Journal)) return;
        var record = Record();
        if (record["complete"]?.GetValue<bool>() == true) return;
        if (record["replaceFiles"]!.GetValue<bool>())
        {
            var originalPresent = record["originalPresent"]!.GetValue<bool>();
            var current = File.Exists(Path.Combine(target, "PlayniteBridge.dll")) ? PlayniteSetupFiles.Digest(Path.Combine(target, "PlayniteBridge.dll")) : null;
            var originalDigest = record["originalDigest"]?.GetValue<string>();
            var newDigest = record["newDigest"]?.GetValue<string>();
            if (Directory.Exists(target) && current != originalDigest && current != newDigest)
                throw new PlayniteSetupException("The integration changed after setup began. Its backup was kept; automatic recovery stopped to preserve your changes.");
            if (Directory.Exists(Previous))
            {
                if (Directory.Exists(target)) DeleteTarget();
                Directory.Move(Previous, target);
            }
            else if (!originalPresent && Directory.Exists(target)) DeleteTarget();
            else if (originalPresent && !Directory.Exists(target))
                throw new PlayniteSetupException("The original integration backup is missing. Use manual setup to restore Playnite Bridge.");
        }
        if (record["settingsWritten"]?.GetValue<bool>() == true)
        {
            Restore(MainConfig, "main-config.before", record["mainConfigPresent"]!.GetValue<bool>());
            Restore(PluginConfig, "plugin-config.before", record["pluginConfigPresent"]!.GetValue<bool>());
        }
        DeleteOwned(Staging);
        record["complete"] = true;
        record["rolledBack"] = true;
        PlayniteSetupFiles.WriteObject(Journal, record);
    }

    private JsonObject Record()
    {
        var record = PlayniteSetupFiles.ReadObject(Journal, 16 * 1024);
        if (!string.Equals(record["target"]?.GetValue<string>(), target, StringComparison.OrdinalIgnoreCase))
            throw new PlayniteSetupException("The saved recovery record belongs to another integration folder. Use manual setup.");
        return record;
    }
    private void Backup(string path, string name)
    {
        var copy = Path.Combine(root, name);
        if (File.Exists(path)) PlayniteSetupFiles.Write(copy, PlayniteSetupFiles.Read(path, 32 * 1024 * 1024));
        else if (File.Exists(copy)) File.Delete(copy);
    }
    private void Restore(string path, string name, bool existed)
    {
        PlayniteSetupFiles.NoLinks(path);
        if (existed) PlayniteSetupFiles.Write(path, PlayniteSetupFiles.Read(Path.Combine(root, name), 32 * 1024 * 1024));
        else if (File.Exists(path)) File.Delete(path);
    }
    private void DeleteTarget()
    {
        PlayniteSetupFiles.NoLinks(target);
        ValidateTree(target);
        Directory.Delete(target, true);
    }
    private void DeleteOwned(string path)
    {
        if (!PlayniteSetupFiles.Within(root, path)) throw new InvalidOperationException("Recovery path escaped its owner.");
        PlayniteSetupFiles.NoLinks(path);
        if (!Directory.Exists(path)) return;
        ValidateTree(path);
        Directory.Delete(path, true);
    }
    private static void ValidateTree(string path) => Walk(path, null);
    private static void CopyTree(string source, string destination) => Walk(source, destination);
    private static void Walk(string source, string? destination)
    {
        PlayniteSetupFiles.NoLinks(source);
        var pending = new Stack<string>();
        pending.Push(source);
        int entries = 0;
        long bytes = 0;
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if (destination is not null) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                PlayniteSetupFiles.NoLinks(entry);
                if (++entries > 2048) throw new PlayniteSetupException("The integration folder is too large for automatic setup. Use manual setup.");
                if (Directory.Exists(entry)) pending.Push(entry);
                else
                {
                    bytes += new FileInfo(entry).Length;
                    if (bytes > 128L * 1024 * 1024) throw new PlayniteSetupException("The integration folder is too large for automatic setup. Use manual setup.");
                    if (destination is not null) File.Copy(entry, Path.Combine(destination, Path.GetRelativePath(source, entry)), true);
                }
            }
        }
    }
}
