using System.Text;

namespace WidgetRail.PlatformSettings;

/// <summary>Shared installer/host preference, independent of the versioned appearance document.</summary>
public sealed class AppUpdatePreference(string profileRoot)
{
    public const string FileName = "automatic-app-updates.txt";
    private readonly string path = Path.Combine(Path.GetFullPath(profileRoot), FileName);
    public bool Read()
    {
        if (!File.Exists(path)) return true;
        if (new FileInfo(path).Length > 32) throw new IOException("Invalid update-check preference.");
        return File.ReadAllText(path).Trim() switch
        { "enabled" => true, "disabled" => false, _ => throw new IOException("Invalid update-check preference.") };
    }
    public void Write(bool enabled)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, enabled ? "enabled" : "disabled", new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
