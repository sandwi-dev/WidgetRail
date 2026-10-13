using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

public sealed partial class CaptureLibraryStore
{
    private SqliteConnection? database;
    private FileSystemWatcher? metadataWatcher, mediaWatcher;
    private readonly ConcurrentDictionary<string, byte> changes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<(string OldName, string NewName)> renames = new();
    private readonly Dictionary<string, (long Length, long Stamp, string Hash)> verified = new();
    private readonly HashSet<string> pendingDeletes = new(StringComparer.OrdinalIgnoreCase);
    private bool initialized;
    private int rescan;
    internal long MetadataReads { get; private set; }
    internal long Reconciliations { get; private set; }
    internal Func<long>? AvailableFreeBytes { get; init; }

    private IDisposable Own()
    {
        PlainPath(root); Directory.CreateDirectory(root);
        if (captureDirectory.Length == 0) throw Error("capture_library_folder_unavailable");
        PlainPath(captureDirectory); Directory.CreateDirectory(captureDirectory);
        var lockPath = Path.Combine(root, ".lock"); PlainPath(lockPath);
        var ownership = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var dbPath = Path.Combine(root, "library.sqlite"); PlainPath(dbPath);
            if(!File.Exists(dbPath)) initialized=false;
            PlainPath(dbPath+"-journal");
            database = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString());
            try { database.Open(); if (!initialized) CreateSchema(); }
            catch (SqliteException error) when (error.SqliteErrorCode is 11 or 26)
            {
                database.Dispose();
                var backup=dbPath+".corrupt-"+Guid.NewGuid().ToString("N");
                File.Move(dbPath,backup);
                foreach(var suffix in new[]{"-journal","-wal","-shm"})
                { PlainPath(dbPath+suffix); if(File.Exists(dbPath+suffix)) File.Move(dbPath+suffix,backup+suffix); }
                database=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=dbPath,Pooling=false}.ToString());
                database.Open(); CreateSchema(); initialized=false;
            }
            if (!initialized)
            {
                metadataWatcher ??= Watch(root); mediaWatcher ??= Watch(captureDirectory);
                Reconcile(); initialized = true;
            }
            else if (Interlocked.Exchange(ref rescan, 0) != 0) Reconcile();
            DrainChanges();
            foreach (var path in pendingDeletes.ToArray())
                TryFinishDelete(path);
            return new Lease(this, ownership);
        }
        catch { database?.Dispose(); database = null; ownership.Dispose(); throw; }
    }
    private void CreateSchema()
    {
        Execute("""
                    CREATE TABLE IF NOT EXISTS captures(id TEXT PRIMARY KEY, saved_at INTEGER NOT NULL, hash TEXT NOT NULL,
                      name TEXT, available INTEGER NOT NULL, bytes INTEGER NOT NULL, metadata_stamp INTEGER NOT NULL, data TEXT NOT NULL);
                    CREATE INDEX IF NOT EXISTS capture_order ON captures(saved_at DESC,id);
                    CREATE INDEX IF NOT EXISTS capture_hash ON captures(hash);
                    CREATE INDEX IF NOT EXISTS capture_name ON captures(name);
                    CREATE TABLE IF NOT EXISTS totals(id INTEGER PRIMARY KEY CHECK(id=1), count INTEGER NOT NULL, bytes INTEGER NOT NULL);
                    INSERT OR IGNORE INTO totals VALUES(1,0,0);
                    CREATE TRIGGER IF NOT EXISTS capture_added AFTER INSERT ON captures BEGIN
                      UPDATE totals SET count=count+1,bytes=bytes+new.bytes WHERE id=1; END;
                    CREATE TRIGGER IF NOT EXISTS capture_removed AFTER DELETE ON captures BEGIN
                      UPDATE totals SET count=count-1,bytes=bytes-old.bytes WHERE id=1; END;
                    CREATE TRIGGER IF NOT EXISTS capture_changed AFTER UPDATE ON captures BEGIN
                      UPDATE totals SET bytes=bytes-old.bytes+new.bytes WHERE id=1; END;
                    """);
    }
    private sealed class Lease(CaptureLibraryStore owner, FileStream file) : IDisposable
    { public void Dispose() { owner.database?.Dispose(); owner.database = null; file.Dispose(); } }

    private FileSystemWatcher Watch(string directory)
    {
        var watcher = new FileSystemWatcher(directory) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
        void Queue(string path)
        {
            if (Path.GetExtension(path).ToLowerInvariant() is not (".json" or ".png" or ".mp4" or ".pending" or ".deleted")) return;
            if (changes.Count >= 1024) { Interlocked.Exchange(ref rescan, 1); return; }
            changes.TryAdd(path, 0);
        }
        watcher.Created += (_, e) => Queue(e.FullPath); watcher.Changed += (_, e) => Queue(e.FullPath);
        watcher.Deleted += (_, e) => Queue(e.FullPath);
        watcher.Renamed += (_, e) => { if (directory == captureDirectory && renames.Count < 1024) renames.Enqueue((e.OldName!, e.Name!)); else Interlocked.Exchange(ref rescan,1); Queue(e.OldFullPath); Queue(e.FullPath); };
        watcher.Error += (_, _) => Interlocked.Exchange(ref rescan, 1);
        watcher.EnableRaisingEvents = true; return watcher;
    }
    private void DrainChanges()
    {
        while (renames.TryDequeue(out var rename))
        {
            var saved = IndexRows("WHERE name=$name", ("$name",rename.OldName)).FirstOrDefault();
            if (saved is null || !ValidName(rename.NewName) || !Path.GetExtension(rename.NewName).Equals(Extension(saved.Item.Media),StringComparison.OrdinalIgnoreCase)) continue;
            var replacement = saved with { FileName = rename.NewName };
            PlainPath(MediaPath(replacement));
            if (!File.Exists(MediaPath(replacement))) continue;
            WriteMetadata(replacement); RefreshEntry(saved.Item.Id);
        }
        foreach (var path in changes.Keys.Take(128).ToArray())
        {
            changes.TryRemove(path, out _);
            var name = Path.GetFileName(path);
            if (Path.GetDirectoryName(path)!.Equals(root,StringComparison.OrdinalIgnoreCase))
            {
                var id = Path.GetFileNameWithoutExtension(path);
                if (CaptureLimits.ValidToken(id) && Path.GetExtension(path) == ".json") RefreshEntry(id);
                else if (Path.GetExtension(path) == ".deleted" && File.Exists(path)) { pendingDeletes.Add(path); }
            }
            else
            {
                foreach (var saved in IndexRows("WHERE name=$name", ("$name", name))) RefreshEntry(saved.Item.Id);
            }
        }
    }
    private void Reconcile()
    {
        ++Reconciliations;
        foreach (var path in Directory.EnumerateFiles(root,"*.deleted"))
            TryFinishDelete(path);
        foreach (var path in Directory.EnumerateFiles(root,"*.json.pending"))
        {
            var id = Path.GetFileName(path)[..^13]; if (!CaptureLimits.ValidToken(id)) continue;
            if (!File.Exists(MetadataPath(id)))
            {
                try { var pending = ReadMetadata(path); if (DeleteOwned(MediaPath(pending))) DeleteOwned(path); }
                catch (Exception error) when (error is JsonException or BrokerException) { /* An unreadable marker cannot authorize deleting a public file. */ }
            }
            else DeleteOwned(path);
        }
        foreach (var path in Directory.EnumerateFiles(root,"context-*.png")) DeleteOwned(path);
        var ids = Directory.EnumerateFiles(root,"*.json").Select(Path.GetFileNameWithoutExtension)
            .Where(CaptureLimits.ValidToken).Select(id => id!).ToHashSet(StringComparer.Ordinal);
        foreach (var saved in IndexRows("")) ids.Add(saved.Item.Id);
        foreach(var id in ids) RefreshEntry(id);
    }
    private void RefreshEntry(string id)
    {
        var previous = IndexRows("WHERE id=$id", ("$id",id)).FirstOrDefault();
        var metadata = MetadataPath(id); PlainPath(metadata);
        Saved saved;
        try
        {
            using var stampQuery = Command("SELECT metadata_stamp FROM captures WHERE id=$id", ("$id",id));
            var stamp = stampQuery.ExecuteScalar();
            saved = previous is { MetadataValid:true } && File.Exists(metadata) && stamp is long recorded && recorded == File.GetLastWriteTimeUtc(metadata).Ticks
                ? previous : ReadMetadata(metadata);

        }
        catch (Exception error) when (error is JsonException or IOException or BrokerException)
        {
            if (previous is null) return;
            saved = previous with { MetadataValid=false };
        }
        PlainPath(MediaPath(saved));
        var file = new FileInfo(MediaPath(saved));
        var unchanged = file.Exists && file.Length == saved.Item.Media.ByteLength && file.LastWriteTimeUtc.Ticks == saved.MediaStamp;
        // Visible artwork/open requests inspect edits lazily; listing never decodes or hashes the whole folder.
        var available = saved.MetadataValid && file.Exists && (unchanged || inspect is not null && file.Length is > 0 and <= CaptureLimits.MaximumAttachmentBytes);
        if (!unchanged) { verified.Remove(id); DeleteOwned(Path.Combine(root,id+".thumbnail")); }
        IndexPut(saved with { Item = saved.Item with { Available=available,Revision=file.Exists?file.LastWriteTimeUtc.Ticks:saved.MediaStamp } },file.Exists?file.Length:0);
    }
    private Saved ReadMetadata(string path)
    {
        PlainPath(path); ++MetadataReads;
        if(new FileInfo(path).Length>8192) throw Error("capture_library_invalid_media");
        var saved=JsonSerializer.Deserialize<Saved>(File.ReadAllText(path)) ?? throw Error("capture_library_invalid_media");
        if(saved.Schema != 2 || !CaptureLibraryLimits.Valid(saved.Item) || !Path.GetFileName(path).StartsWith(saved.Item.Id+".",StringComparison.Ordinal) || saved.Sha256 is not {Length:64} || !saved.Sha256.All(char.IsAsciiHexDigit) ||
            (saved.FileName is not { } name || !ValidName(name) || !Path.GetExtension(name).Equals(Extension(saved.Item.Media),StringComparison.OrdinalIgnoreCase))) throw Error("capture_library_invalid_media");
        return saved;
    }
    private void WriteMetadata(Saved saved)
    { var path=MetadataPath(saved.Item.Id); PlainPath(path); var temporary=path+".pending"; PlainPath(temporary); File.WriteAllText(temporary,JsonSerializer.Serialize(saved)); File.Move(temporary,path,true); }
    private static string PublicName(CaptureLibraryItem item) => $"WidgetRail-{DateTimeOffset.FromUnixTimeMilliseconds(item.SavedAtUnixMilliseconds):yyyyMMdd-HHmmss}-{item.Id}{Extension(item.Media)}";
    private static bool ValidName(string name) => name.Length is >0 and <=240 && Path.GetFileName(name)==name && name.IndexOfAny(Path.GetInvalidFileNameChars())<0;
    private void DemandFreeSpace(long bytes)
    {
        long? free;
        try { var drive=new DriveInfo(Path.GetPathRoot(captureDirectory)!); free=AvailableFreeBytes?.Invoke() ?? (drive.IsReady?drive.AvailableFreeSpace:null); }
        catch(ArgumentException) { free=null; } // Some redirected folders cannot report a local drive quota; writes still report disk-full.
        if(free is { } available && available < bytes+16L*1024*1024) throw Error("capture_library_disk_full");
    }
    private void FinishDelete(string marker,Saved saved)
    {
        if(DeleteOwned(MediaPath(saved)) && DeleteOwned(Path.Combine(root,saved.Item.Id+".thumbnail")))
        { DeleteOwned(MetadataPath(saved.Item.Id)); IndexRemove(saved.Item.Id); File.Delete(marker); pendingDeletes.Remove(marker); }
        else pendingDeletes.Add(marker);
    }
    private void TryFinishDelete(string marker)
    {
        try { if(File.Exists(marker)) FinishDelete(marker,ReadMetadata(marker)); else pendingDeletes.Remove(marker); }
        catch(Exception error) when(error is JsonException or IOException or BrokerException)
        { pendingDeletes.Remove(marker); } // Unreadable deletion metadata cannot authorize removing an original or block the other captures.
    }
    private SqliteCommand Command(string sql, params (string Name,object Value)[] parameters)
    { var command=database!.CreateCommand(); command.CommandText=sql; foreach(var p in parameters)command.Parameters.AddWithValue(p.Name,p.Value); return command; }
    private void Execute(string sql,params (string Name,object Value)[] parameters) { using var command=Command(sql,parameters); command.ExecuteNonQuery(); }
    private Saved[] IndexRows(string suffix,params (string Name,object Value)[] parameters)
    { using var command=Command("SELECT data FROM captures "+suffix,parameters); using var reader=command.ExecuteReader(); var values=new List<Saved>(); while(reader.Read()) values.Add(JsonSerializer.Deserialize<Saved>(reader.GetString(0))!); return values.ToArray(); }
    private (int Count,long Bytes) IndexTotals()
    { using var command=Command("SELECT count,bytes FROM totals WHERE id=1"); using var reader=command.ExecuteReader(); reader.Read(); return(reader.GetInt32(0),reader.GetInt64(1)); }
    private void IndexPut(Saved saved,long bytes) => Execute("""
        INSERT INTO captures VALUES($id,$time,$hash,$name,$available,$bytes,$stamp,$data)
        ON CONFLICT(id) DO UPDATE SET saved_at=excluded.saved_at,hash=excluded.hash,name=excluded.name,
          available=excluded.available,bytes=excluded.bytes,metadata_stamp=excluded.metadata_stamp,data=excluded.data
          WHERE captures.data<>excluded.data OR captures.bytes<>excluded.bytes OR captures.metadata_stamp<>excluded.metadata_stamp
        """,("$id",saved.Item.Id),("$time",saved.Item.SavedAtUnixMilliseconds),("$hash",saved.Sha256),
        ("$name",(object?)saved.FileName??DBNull.Value),("$available",saved.Item.Available?1:0),("$bytes",bytes),
        ("$stamp",File.Exists(MetadataPath(saved.Item.Id))?File.GetLastWriteTimeUtc(MetadataPath(saved.Item.Id)).Ticks:0),("$data",JsonSerializer.Serialize(saved)));
    private void IndexRemove(string id) => Execute("DELETE FROM captures WHERE id=$id",("$id",id));
    public void Dispose() { metadataWatcher?.Dispose(); mediaWatcher?.Dispose(); database?.Dispose(); gate.Dispose(); }
}
