using System.Text.Json;
using System.Security.Cryptography;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetBridge;

if (args.Length != 1 && !(args.Length == 2 && args[0] == "--frontend"))
{
    Console.Error.WriteLine("Usage: ReleaseVerifier <release-folder> | --frontend <published-frontend-folder>");
    return 2;
}
try
{
    if (args.Length == 2)
    {
        FrontendDependencyVerification.Verify(Path.GetFullPath(args[1]));
        FrontendCleanupVerification.Verify(Path.GetFullPath(args[1]));
        return 0;
    }
    var root = Path.GetFullPath(args[0]);
    using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "release.json")));
    var expected = manifest.RootElement.GetProperty("widgets").EnumerateArray()
        .Select(item => item.GetProperty("id").GetString()).ToArray();
    // Load only the shipped catalog. No installed user catalog, widgets or Windows
    // providers are started, and no controller or account state is modified.
    var result = BridgeCatalog.LoadTrustedObserved(Path.Combine(root, "widget-catalog.json"), Path.Combine(root, ".unused-installed-catalog"));
    if (result.Warnings.Count != 0 || result.WidgetRejections.Count != 0) throw new InvalidOperationException(string.Join("; ", result.Warnings));
    var actual = result.Catalog.Widgets.Select(widget => result.Catalog.GetConfigured(widget.Id).PackageId).ToArray();
    if (!expected.SequenceEqual(actual)) throw new InvalidOperationException("Loaded widget identities/order differ from release metadata.");
    using var initial = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "initial-widgets.json")));
    if (initial.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
        throw new InvalidOperationException("Unknown initial widget schema.");
    var initialWidgets = initial.RootElement.GetProperty("widgets").EnumerateArray().ToArray();
    if (initialWidgets.Length > 64 || (manifest.RootElement.GetProperty("edition").GetString() == "bare" && initialWidgets.Length != 0))
        throw new InvalidOperationException("Invalid initial widget count for bundle.");
    var installer = new WidgetRail.WidgetCatalog.WidgetCatalog(Path.Combine(root, ".unused-installed-catalog")).CreateInstaller();
    var seen = new HashSet<string>(StringComparer.Ordinal);
    foreach (var widget in initialWidgets)
    {
        var id = widget.GetProperty("id").GetString()!;
        var relative = widget.GetProperty("package").GetString()!;
        if (!WidgetManifestValidator.IsValidPackageIdentity(id) || !seen.Add(id) ||
            id is "widgetrail.firstparty.settings" or "widgetrail.firstparty.marketplace" ||
            relative != $"initial-widgets/{id}.wrwidget")
            throw new InvalidOperationException("Invalid initial widget reference.");
        using var archive = File.OpenRead(Path.Combine(root, relative));
        var hash = Convert.ToHexString(SHA256.HashData(archive));
        if (!hash.Equals(widget.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Initial widget archive hash mismatch.");
        archive.Position = 0;
        var inspection = await installer.ValidateAsync(archive);
        if (inspection.Id != id || WidgetManifestTrust.Resolve(inspection.Manifest) != WidgetExecutionTrust.Sandboxed ||
            !WidgetHostCompatibility.Evaluate(inspection.Manifest).IsSupported)
            throw new InvalidOperationException("Initial widget must be compatible, sandboxed, and match its identity.");
    }
    Console.WriteLine($"Verified initial optional widget archives: {initialWidgets.Length}; no installation or capability grants.");
    Console.WriteLine($"Verified catalog, package integrity and icon admission: {actual.Length} widgets.");
    return 0;
}
catch (Exception error) when (error is not OutOfMemoryException)
{
    Console.Error.WriteLine("Release verification failed: " + error.Message);
    return 1;
}
