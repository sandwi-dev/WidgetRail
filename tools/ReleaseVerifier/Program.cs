using System.Text.Json;
using WidgetRail.WidgetBridge;

if (args.Length != 1) { Console.Error.WriteLine("Usage: ReleaseVerifier <release-folder>"); return 2; }
try
{
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
    Console.WriteLine($"Verified catalog, package integrity and icon admission: {actual.Length} widgets.");
    return 0;
}
catch (Exception error) when (error is not OutOfMemoryException)
{
    Console.Error.WriteLine("Release catalog verification failed: " + error.Message);
    return 1;
}
