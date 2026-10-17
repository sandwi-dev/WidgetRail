using System.Text;
using System.Text.Json;

namespace WidgetRail.Samples.PobCompanion;

internal sealed record PobPendingChange(string Xml, string Kind, int NodeId = 0, string? Slot = null);
internal sealed record PobWorkState(string CommittedXml, string[] Undo, string[] Redo,
    PobPendingChange? Pending = null, string? ItemDraft = null, PobTradeListing? TradeListing = null,
    bool TradeBrowse = false, PobTradeCandidate[]? TradeCandidates = null);

internal sealed record PobTradeCandidate(string Raw, PobTradeListing Listing, string Slot);

/// <summary>One atomic document owns committed work, pending work and its history.</summary>
internal static class PobWorkStore
{
    internal const int MaximumBytes = 16 * 1024 * 1024;
    private sealed record Envelope(int Version, int Game, PobWorkState Work);
    private static string PathFor(string directory, string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new IOException("Invalid saved build.");
        return Path.Combine(directory, id + ".work.json");
    }

    private static Envelope? ReadEnvelope(string directory, string id, int game)
    {
        var path = PathFor(directory, id);
        if (!File.Exists(path)) return null;
        using var stream = File.OpenRead(path);
        if (stream.Length > MaximumBytes) throw new IOException("Saved work is too large to reopen.");
        var envelope = JsonSerializer.Deserialize<Envelope>(stream);
        if (envelope is not { Version: 1 } || envelope.Game != game || envelope.Work is null)
            throw new IOException("This saved work format is not supported. Your saved work has been retained.");
        return envelope;
    }

    internal static bool Exists(string directory, string id) => File.Exists(PathFor(directory,id));

    internal static string? ReadCommitted(string directory, string id, int game)
    {
        var path=PathFor(directory,id);
        if(!File.Exists(path))return null;
        using var stream=File.OpenRead(path);
        if(stream.Length>MaximumBytes)throw new IOException("Saved work is too large to reopen.");
        using var document=JsonDocument.Parse(stream);
        var root=document.RootElement;
        // History and preview may have invalid shapes as well as invalid XML.
        // Recover the committed property without deserializing those fields.
        if(root.ValueKind!=JsonValueKind.Object ||
            !root.TryGetProperty("Version",out var version)||version.ValueKind!=JsonValueKind.Number||!version.TryGetInt32(out var v)||v!=1||
            !root.TryGetProperty("Game",out var savedGame)||savedGame.ValueKind!=JsonValueKind.Number||!savedGame.TryGetInt32(out var g)||g!=game||
            !root.TryGetProperty("Work",out var work)||work.ValueKind!=JsonValueKind.Object||
            !work.TryGetProperty("CommittedXml",out var committed)||committed.ValueKind!=JsonValueKind.String)
            throw new IOException("No supported committed build in recovery record.");
        return BuildDocument.Decode(committed.GetString()!,game);
    }

    internal static string PreserveRecovery(string directory, string id)
    {
        var source=PathFor(directory,id);
        var preserved=Path.Combine(directory,id+".recovery."+DateTime.UtcNow.ToString("yyyyMMddTHHmmss")+"."+Guid.NewGuid().ToString("N")+".json");
        File.Copy(source,preserved,overwrite:false);
        return preserved;
    }

    internal static PobWorkState? Read(string directory, string id, int game)
    {
        var envelope=ReadEnvelope(directory,id,game);
        if(envelope is null)return null;
        var work = envelope.Work;
        if (work.Undo is null || work.Redo is null || work.Undo.Length + work.Redo.Length > 12 ||
            work.ItemDraft?.Length > 32768 || work.Pending is { Kind: not ("node" or "item" or "character") } ||
            work.Pending is { Kind: "node", NodeId: <= 0 } || work.Pending?.Slot?.Length > 128 ||
            work.Pending is { Kind: "item" } && string.IsNullOrWhiteSpace(work.ItemDraft))
            throw new IOException("Saved work is invalid. Your saved work has been retained.");
        string Decode(string value) => value is null ? throw new IOException("Saved work is incomplete.") : BuildDocument.Decode(value, game);
        return work with
        {
            TradeBrowse=work.TradeBrowse&&work.ItemDraft is not null&&work.TradeListing?.IsValid(game)==true,
            TradeCandidates=(work.TradeCandidates??[]).Take(8).Where(c=>c is not null&&c.Raw is {Length:>0 and <=32768}&&c.Listing?.IsValid(game)==true&&c.Slot is {Length:>0 and <=128}).ToArray(),
            TradeListing=work.ItemDraft is not null&&work.TradeListing?.IsValid(game)==true?work.TradeListing:null,
            CommittedXml = Decode(work.CommittedXml),
            Undo = work.Undo.Select(Decode).ToArray(), Redo = work.Redo.Select(Decode).ToArray(),
            Pending = work.Pending is {} pending ? pending with { Xml = Decode(pending.Xml) } : null,
        };
    }

    internal static void Save(string directory, string id, int game, PobWorkState work)
    {
        // Compress XML snapshots, not calculated state or the Lua heap. Restore
        // always recalculates through the current engine.
        var encoded = work with
        {
            CommittedXml = BuildDocument.Encode(work.CommittedXml),
            Undo = work.Undo.Select(BuildDocument.Encode).ToArray(),
            Redo = work.Redo.Select(BuildDocument.Encode).ToArray(),
            Pending = work.Pending is {} pending ? pending with { Xml = BuildDocument.Encode(pending.Xml) } : null,
        };
        var json = JsonSerializer.Serialize(new Envelope(1, game, encoded));
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes)
            throw new IOException("This change exceeds the saved-work size limit. Your previous work is unchanged.");
        BuildDocument.SaveAtomic(PathFor(directory, id), json);
    }
}
