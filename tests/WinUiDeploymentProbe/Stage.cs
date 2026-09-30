using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace WinUiDeploymentProbe;

internal sealed record Stage(string Receipt, string Manifest, string ExternalLocation,
    string PackageName, string Publisher, Version Version, string ManifestSha256)
{
    internal static Stage Load(string receipt)
    {
        receipt = ExistingPath(receipt, directory: false);
        var root = Path.GetDirectoryName(receipt)!;
        using var json = JsonDocument.Parse(File.ReadAllText(receipt));
        var data = json.RootElement;
        if (data.GetProperty("schemaVersion").GetInt32() != 1 || data.GetProperty("signed").GetBoolean())
            throw new InvalidDataException("Only unsigned development stage receipts are admitted.");
        string Read(string key) => data.GetProperty(key).GetString() ?? throw new InvalidDataException(key);
        var name = Read("packageName");
        if (!Regex.IsMatch(name, @"\AWidgetRail\.WinUI\.External[A-Za-z0-9]*Probe\z", RegexOptions.CultureInvariant) || name.Length > 50)
            throw new InvalidDataException("Only exact isolated WidgetRail.WinUI.External*Probe identities are admitted.");
        var manifest = ExistingPath(Read("manifest"), directory: false);
        var external = ExistingPath(Read("externalLocation"), directory: true);
        if (!SamePath(manifest, Path.Combine(root, "identity", "AppxManifest.xml")) ||
            !SamePath(external, Path.Combine(root, "external")))
            throw new InvalidDataException("Manifest and external directory must use the supplied stage's owned layout.");
        using var reader = XmlReader.Create(manifest, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var document = XDocument.Load(reader);
        XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        XNamespace uap10 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/10";
        var package = document.Root ?? throw new InvalidDataException("Missing package root.");
        var identity = package.Element(ns + "Identity") ?? throw new InvalidDataException("Missing identity.");
        var version = Version.Parse(Read("version"));
        var publisher = Read("publisher");
        if (package.Name != ns + "Package" || identity.Attribute("Name")?.Value != name ||
            identity.Attribute("Publisher")?.Value != publisher || identity.Attribute("Version")?.Value != version.ToString() ||
            package.Element(ns + "Properties")?.Element(uap10 + "AllowExternalContent")?.Value != "true")
            throw new InvalidDataException("Manifest and receipt identity/external-content declarations differ.");
        var applications = package.Element(ns + "Applications")?.Elements(ns + "Application").ToArray() ?? [];
        if (Read("applicationId") != "App" || applications.Length != 1 || applications[0].Attribute("Id")?.Value != "App" ||
            applications[0].Attribute("Executable")?.Value != "OverlayFrontend.WinUI.exe")
            throw new InvalidDataException("Only the isolated frontend application is admitted.");
        if (package.Descendants().Any(element => element.Name.LocalName == "Capability" && element.Attribute("Name")?.Value == "allowElevation"))
            throw new InvalidDataException("Elevated package capabilities are not admitted.");
        VerifyHash(Path.Combine(external, "OverlayFrontend.WinUI.exe"), Read("embeddedExeSha256"));
        VerifyHash(Path.Combine(external, "OverlayFrontend.WinUI.dll"), Read("assemblySha256"));
        return new(receipt, manifest, external, name, publisher, version, Hash(manifest));
    }

    internal void Revalidate()
    {
        if (Load(Receipt) != this) throw new InvalidDataException("Stage changed during the operation.");
    }

    internal static bool SamePath(string first, string second) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase);

    internal static string ExistingPath(string value, bool directory)
    {
        if (!Path.IsPathFullyQualified(value)) throw new InvalidDataException("Stage paths must be absolute.");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        if (directory ? !Directory.Exists(full) : !File.Exists(full)) throw new FileNotFoundException("Stage path is missing.", full);
        for (FileSystemInfo? item = directory ? new DirectoryInfo(full) : new FileInfo(full); item is not null;
             item = item is DirectoryInfo folder ? folder.Parent : ((FileInfo)item).Directory)
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse-point stage paths are not admitted.");
        return full;
    }

    private static void VerifyHash(string path, string expected)
    {
        ExistingPath(path, directory: false);
        if (!string.Equals(Hash(path), expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Staged frontend hash differs: " + Path.GetFileName(path));
    }
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
