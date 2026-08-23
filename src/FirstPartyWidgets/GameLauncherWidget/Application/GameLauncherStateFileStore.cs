using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.GameLauncher;

internal sealed class GameLauncherStateFileStore
{
    private const int MaximumStateBytes = 64 * 1024;
    private const int MaximumEnvelopeBytes = 72 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    internal GameLauncherStateFileStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("A state path is required.", nameof(path));
        _path = Path.GetFullPath(path);
    }

    internal async ValueTask<WidgetPrivateStateValue<GameLauncherPrivateState>> ReadAsync(
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await LoadAsync(cancellationToken).ConfigureAwait(false);
            return document is null
                ? new(false, null, 0)
                : new(true, document.State, document.Revision);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async ValueTask<WidgetPrivateStateMutation> WriteAsync(
        GameLauncherPrivateState state,
        long? expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (expectedRevision < 0) throw new ArgumentOutOfRangeException(
            nameof(expectedRevision));
        var stateBytes = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        if (stateBytes.Length > MaximumStateBytes)
            throw new InvalidDataException("Game Launcher organization exceeds 64 KiB.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var revision = current?.Revision ?? 0;
            if (expectedRevision is { } expected && expected != revision)
                throw new WidgetCapabilityException(
                    "state_conflict", "Game Launcher organization changed concurrently.");
            if (revision == long.MaxValue)
                throw new InvalidDataException("Game Launcher state revision is exhausted.");
            var next = new StateDocument(1, revision + 1, state);
            await WriteAtomicAsync(next, cancellationToken).ConfigureAwait(false);
            return new(next.Revision);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<StateDocument?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return null;
        await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is <= 0 or > MaximumEnvelopeBytes)
            throw new InvalidDataException("Game Launcher organization is invalid.");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        var currentBytes = RetireLegacyPresentationIdentity(bytes);
        var document = JsonSerializer.Deserialize<StateDocument>(currentBytes, JsonOptions) ??
            throw new InvalidDataException("Game Launcher organization is invalid.");
        if (document.SchemaVersion != 1 || document.Revision <= 0 ||
            document.State is null)
            throw new InvalidDataException("Game Launcher organization is invalid.");
        return document;
    }

    private async Task WriteAtomicAsync(
        StateDocument document,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        if (bytes.Length > MaximumEnvelopeBytes)
            throw new InvalidDataException("Game Launcher organization is invalid.");
        var temporary = Path.Combine(directory, $".organization.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static JsonSerializerOptions CreateJsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>
    /// Removes the one inert presentation identity written by package version 0.2.0.
    /// All organization data and the revision remain unchanged. Remove this
    /// pre-release tombstone when that private-state generation is retired.
    /// </summary>
    private static byte[] RetireLegacyPresentationIdentity(byte[] bytes)
    {
        var root = JsonNode.Parse(bytes)?.AsObject()
            ?? throw new InvalidDataException("Game Launcher organization is invalid.");
        if (root["state"] is JsonObject state)
            state.Remove("experienceId");
        return JsonSerializer.SerializeToUtf8Bytes(root);
    }

    private sealed record StateDocument(
        [property: JsonRequired] int SchemaVersion,
        [property: JsonRequired] long Revision,
        [property: JsonRequired] GameLauncherPrivateState State);
}
