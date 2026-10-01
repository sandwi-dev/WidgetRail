using System.Text;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>One authenticated CLI generation; never inferred from a normal launch.</summary>
internal sealed record DevelopmentLaunchOptions(string CatalogRoot, string SettingsRoot,
    string ReadyPath, string Nonce, string WidgetId, string InstanceId, string JobName,
    bool ProbeOnly, bool Inspector)
{
    internal static DevelopmentLaunchOptions? Parse(IReadOnlyList<string> arguments, string defaultSettingsRoot)
    {
        if (!arguments.Any(value => value.StartsWith("--development-", StringComparison.Ordinal))) return null;
        foreach (var argument in arguments.Where(value => value.StartsWith("--development-", StringComparison.Ordinal)))
        {
            var key = argument.Split('=', 2)[0];
            if (key is not ("--development-catalog-root" or "--development-ready-path" or "--development-ready-nonce" or
                "--development-widget-id" or "--development-widget-instance" or "--development-job-name" or
                "--development-probe-only" or "--development-inspector"))
                throw new InvalidDataException("Unknown development startup argument.");
            if (key is "--development-probe-only" or "--development-inspector" &&
                (argument != key || arguments.Count(value => value == key) != 1))
                throw new InvalidDataException("Invalid or duplicate development switch.");
        }
        string Required(string name)
        {
            var matches = arguments.Where(value => value == name || value.StartsWith(name + "=", StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1 || matches[0].Length <= name.Length + 1)
                throw new InvalidDataException($"Development startup requires one value for {name}.");
            var value = matches[0][(name.Length + 1)..];
            if (value.Any(char.IsControl)) throw new InvalidDataException("Development arguments contain control characters.");
            return value;
        }
        var catalog = Absolute(Required("--development-catalog-root"));
        var settings = Absolute(Required("--settings-root"));
        var ready = Absolute(Required("--development-ready-path"));
        var nonce = Required("--development-ready-nonce");
        var widget = Required("--development-widget-id");
        var instance = Required("--development-widget-instance");
        var job = Required("--development-job-name");
        if (nonce.Length != 64 || !nonce.All(char.IsAsciiHexDigit) ||
            !ShellPreferences.ValidId(widget) || !ShellPreferences.ValidId(instance) ||
            job != "Local\\WidgetRail.Dev." + nonce)
            throw new InvalidDataException("The development generation identity is invalid.");
        if (string.Equals(settings, Absolute(defaultSettingsRoot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Development startup requires an isolated settings profile.");
        if (arguments.Any(value => value == "--shell-config" || value.StartsWith("--shell-config=", StringComparison.Ordinal) ||
                                   value == "--installed-catalog-root" || value.StartsWith("--installed-catalog-root=", StringComparison.Ordinal) ||
                                   value == "--widget" || value.StartsWith("--widget=", StringComparison.Ordinal)))
            throw new InvalidDataException("Development startup cannot be combined with normal catalog or widget overrides.");
        var probe = arguments.Contains("--development-probe-only");
        var inspector = arguments.Contains("--development-inspector");
        if (probe && inspector) throw new InvalidDataException("A hidden development probe cannot open the inspector.");
        if (!probe && arguments.Contains("--hidden")) throw new InvalidDataException("Interactive development startup cannot be hidden.");
        return new(catalog, settings, ready, nonce, widget, instance, job, probe, inspector);

        static string Absolute(string value) => !string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value)
            ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(value))
            : throw new InvalidDataException("Development paths must be absolute.");
    }

    internal void PublishReady(bool inspectorReady)
    {
        if (Inspector && !inspectorReady) throw new InvalidOperationException("Developer inspector is not ready.");
        var payload = "wrail-dev-ready-v1\n" + Nonce + "\n" + CatalogRoot + "\n" + WidgetId + "\n" + InstanceId + "\n" +
            (Inspector ? "inspector-v1\n" : "");
        var bytes = Encoding.UTF8.GetBytes(payload);
        if (bytes.Length > 4096) throw new InvalidDataException("Development readiness record exceeds its limit.");
        // The CLI owns the existing handshake directory. Never replace a record
        // or leave a partially written success marker on failure.
        var temporary = ReadyPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var created = false;
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            { created = true; file.Write(bytes); file.Flush(flushToDisk: true); }
            File.Move(temporary, ReadyPath);
        }
        finally { if (created && File.Exists(temporary)) File.Delete(temporary); }
    }
}
