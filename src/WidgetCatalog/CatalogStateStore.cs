using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameBarAlternative.WidgetCatalog;

internal sealed record CatalogState
{
    public int Version { get; init; } = 1;
    public IReadOnlyList<CatalogStateEntry> Widgets { get; init; } = [];
}

internal sealed record CatalogStateEntry
{
    public required string Id { get; init; }
    public bool Enabled { get; init; }
    public required int Order { get; init; }
}

internal sealed class CatalogStateStore
{
    private static readonly TimeSpan CrossProcessLockTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CrossProcessLockRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    private readonly string _root;
    private readonly string _path;
    private readonly string _lockPath;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public CatalogStateStore(string root)
    {
        _root = root;
        _path = Path.Combine(root, "catalog-state.json");
        _lockPath = Path.Combine(root, ".catalog-state.lock");
    }

    public async Task<CatalogState> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new CatalogState();
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var state = await JsonSerializer.DeserializeAsync<CatalogState>(stream, JsonOptions, cancellationToken)
                ?? throw new JsonException("Catalog state was null.");
            ValidateState(state);
            return state;
        }
        catch (JsonException exception)
        {
            throw new WidgetPackageException("invalid_catalog_state", $"Catalog state is invalid: {exception.Message}", exception);
        }
    }

    public async Task MutateAsync(Func<CatalogState, CatalogState> mutation, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await using var crossProcessLock = await AcquireCrossProcessLockAsync(cancellationToken);
            var current = await LoadAsync(cancellationToken);
            var updated = mutation(current);
            ValidateState(updated);
            await SaveAtomicAsync(updated, cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<FileStream> AcquireCrossProcessLockAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        FileSystemSafety.EnsureNoReparsePoints(_root, _root);
        var deadline = DateTime.UtcNow + CrossProcessLockTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    _lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous | FileOptions.WriteThrough);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(CrossProcessLockRetryDelay, cancellationToken);
            }
            catch (IOException exception)
            {
                throw new WidgetPackageException(
                    "catalog_busy",
                    "The widget catalog is busy in another process. Wait for the other operation to finish and retry.",
                    exception);
            }
        }
    }

    private async Task SaveAtomicAsync(CatalogState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        FileSystemSafety.EnsureNoReparsePoints(_root, _root);
        var temporary = Path.Combine(_root, $".catalog-state.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void ValidateState(CatalogState state)
    {
        if (state.Version != 1) throw new JsonException($"Unsupported catalog state version {state.Version}.");
        if (state.Widgets is null) throw new JsonException("Widget state list cannot be null.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var orders = new HashSet<int>();
        foreach (var entry in state.Widgets)
        {
            if (string.IsNullOrWhiteSpace(entry.Id) || !ids.Add(entry.Id))
                throw new JsonException("Widget state IDs must be non-empty and unique.");
            if (entry.Order < 0 || !orders.Add(entry.Order))
                throw new JsonException("Widget state order values must be non-negative and unique.");
        }
    }
}
