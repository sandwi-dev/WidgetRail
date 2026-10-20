using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

// Check the actual publication without loading app assemblies. A copied DLL is
// insufficient: the self-contained runtime must also resolve it through deps.json.
internal static class FrontendDependencyVerification
{
    internal static void Verify(string directory)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "OverlayFrontend.WinUI.deps.json")));
        var target = document.RootElement.GetProperty("runtimeTarget").GetProperty("name").GetString()!;
        var assets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in document.RootElement.GetProperty("targets").GetProperty(target).EnumerateObject())
        {
            if (library.Value.TryGetProperty("runtime", out var runtime))
                foreach (var asset in runtime.EnumerateObject()) assets.Add(Path.GetFileName(asset.Name));
            if (library.Value.TryGetProperty("runtimeTargets", out var targets))
                foreach (var asset in targets.EnumerateObject())
                    if (asset.Value.TryGetProperty("assetType", out var kind) && kind.GetString() == "runtime")
                        assets.Add(Path.GetFileName(asset.Name));
        }
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var checkedReferences = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*.dll"))
        {
            using var stream = File.OpenRead(file);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) continue;
            var metadata = pe.GetMetadataReader();
            foreach (var reference in metadata.AssemblyReferences)
            {
                var name = metadata.GetString(metadata.GetAssemblyReference(reference).Name) + ".dll";
                if (!File.Exists(Path.Combine(directory, name))) continue; // External/facade resolution is outside this targeted check.
                ++checkedReferences;
                if (!assets.Contains(name)) missing.Add(name);
            }
        }
        if (missing.Count > 0)
            throw new InvalidOperationException("Published frontend dependency manifest omits referenced runtime assemblies: " +
                string.Join(", ", missing) + ". Regenerate the trimmed publish dependency file; do not distribute this payload.");
        Console.WriteLine($"Verified published dependency manifest: {checkedReferences} local managed references.");
    }
}
