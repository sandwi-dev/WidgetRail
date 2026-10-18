using System.Text.Json;
using WidgetRail.Samples.PobCompanion;

internal static class ArchitectureTests
{
    internal static void Run(Action<bool, string> check)
    {
        var first = PobGame.For(1); var second = PobGame.For(2);
        check(first.TradeApiPath == "/api/trade/" && second.TradeApiPath == "/api/trade2/" &&
            first.TradeSearchPath == "/trade/search/" && second.TradeSearchPath == "/trade2/search/", "Game descriptors keep website and captured-response endpoints separate");
        check(first.SupportsRealm("pc") && first.SupportsRealm("sony") && !first.SupportsRealm("poe2") &&
            second.SupportsRealm("poe2") && !second.SupportsRealm("pc"), "Currency realms cannot cross games");
        check(!first.SupportsGameFileExport && second.SupportsGameFileExport && !first.CompressedSearch && second.CompressedSearch,
            "Fixed export and query contracts remain game specific");
        try { PobGame.For(3); check(false, "Unknown game cannot silently use PoE1"); }
        catch (ArgumentOutOfRangeException) { check(true, "Unknown game cannot silently use PoE1"); }
        foreach (var game in new[] { first, second })
        {
            var xml = $"<{game.DocumentRoot}><Build level=\"1\"/></{game.DocumentRoot}>";
            check(BuildDocument.Decode(xml, game.Id) == xml, "Descriptor matches document root for game " + game.Id);
        }

        foreach (var json in new[] { "null", "{}", "{\"ok\":\"true\"}", "{\"ok\":true,\"result\":{\"state\":{},\"xml\":null}}",
                     "{\"ok\":true,\"result\":{\"state\":[],\"xml\":\"build\"}}", "{\"ok\":true,\"result\":{\"state\":{},\"xml\":\" \"}}" })
        {
            try { using var document = JsonDocument.Parse(json); PobResult.Read(document.RootElement); check(false, "Malformed engine envelope rejected"); }
            catch (IOException) { check(true, "Malformed engine envelope rejected"); }
        }
        var flags = new { canCreateLoadout = true, skillWeaponSets = false, canEditNodeNotes = true, weaponTreeModes = false, timelessFinder = true };
        var reply = PobResult.Read(JsonSerializer.SerializeToElement(new { ok = true, result = new { xml = "build", state = new { capabilities = flags } } }));
        check(reply.Capabilities.CanCreateLoadout && reply.Capabilities.CanEditNodeNotes && reply.Capabilities.TimelessFinder && !reply.Capabilities.WeaponTreeModes,
            "Capabilities come from engine state rather than game identity");
        check(PobResult.Read(JsonSerializer.SerializeToElement(new { ok = true, result = new { xml = "build", state = new { } } })).Capabilities == PobEngineCapabilities.None,
            "Operation-only response has no optional build capabilities");
        foreach (var capabilities in new[] { "null", "{}", "{\"canCreateLoadout\":\"yes\"}" })
        {
            try { using var document = JsonDocument.Parse("{\"capabilities\":" + capabilities + "}"); PobEngineCapabilities.Read(document.RootElement); check(false, "Incomplete capability declaration rejected"); }
            catch (IOException) { check(true, "Incomplete capability declaration rejected"); }
        }
        try { PobResult.Read(JsonSerializer.SerializeToElement(new { ok = false, error = "Expected rejection" })); check(false, "Engine command errors stay distinct"); }
        catch (PobCommandException error) { check(error.Message == "Expected rejection", "Engine command errors stay distinct"); }

        var directory = Path.Combine(Path.GetTempPath(), "pob-library-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var library = new PobBuildLibrary();
            var id = Guid.NewGuid().ToString("N"); var recovery = Guid.NewGuid().ToString("N");
            var xmlPath = Path.Combine(directory, id + ".xml");
            File.WriteAllText(xmlPath, "<PathOfBuilding><Build className=\"Witch\" ascendClassName=\"Occultist\"/></PathOfBuilding>");
            File.WriteAllText(Path.Combine(directory, id + ".work.json"), "{}");
            File.WriteAllText(Path.Combine(directory, id + ".name.txt"), "Named build");
            File.WriteAllText(Path.Combine(directory, recovery + ".work.json"), "{}");
            File.WriteAllText(Path.Combine(directory, "unrelated.xml"), "not a build");
            var snapshot = library.Read(directory);
            check(snapshot.Entries.Length == 2 && snapshot.Entries.Single(b => b.Id == id).Name == "Named build" && snapshot.Metadata[id].Class == "Witch",
                "Library deduplicates recovery records and retains recovery-only builds with safe identities");
            check(library.Read(directory).MetadataRevision == snapshot.MetadataRevision, "Unchanged library reuses metadata without revision churn");
            var stamp = File.GetLastWriteTimeUtc(xmlPath);
            File.WriteAllText(xmlPath, "<PathOfBuilding><Build className=\"Ranger\"/></PathOfBuilding>");
            File.SetLastWriteTimeUtc(xmlPath, stamp);
            var revised = library.Read(directory);
            check(revised.Metadata[id].Class == "Ranger" && snapshot.Metadata[id].Class == "Witch" && revised.MetadataRevision > snapshot.MetadataRevision,
                "Changed file size invalidates metadata while published snapshots remain stable");
            File.WriteAllText(xmlPath, "<!DOCTYPE a [<!ENTITY x SYSTEM 'file:///invalid'>]><a>&x;</a>");
            check(library.Read(directory).Metadata[id].Class == "", "Invalid XML and external entities cannot supply library metadata");
            File.Delete(xmlPath);
            var removed = library.Read(directory);
            check(!removed.Metadata.ContainsKey(id) && removed.Entries.Any(b => b.Id == id), "Removed XML clears stale class but preserves recovery entry");
            Directory.Delete(directory, true);
            check(library.Read(directory).Entries.Length == 0, "Removed library clears published entries");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
