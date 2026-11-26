using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK.Models;

namespace WidgetRail.Playnite;

internal sealed partial class PlayniteApiDispatcher
{
    private static readonly Lazy<string?> steamMediaRoot = new(() =>
    {
        try { return Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string; }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    });
    private readonly SemaphoreSlim snapshotsGate = new SemaphoreSlim(1, 1);
    private readonly List<LibrarySnapshot> snapshots = new List<LibrarySnapshot>();
    private sealed class LibrarySnapshot
    {
        internal readonly string Revision;
        internal readonly Dictionary<string, object?>[] Games;
        internal LibrarySnapshot(string revision, Dictionary<string, object?>[] games) { Revision = revision; Games = games; }
    }
    private Game RequireGame(Guid id) => api.Database.Games.Get(id) ?? throw new ApiException(404, "game_not_found");
    private static object[] NamedCatalog<T>(IEnumerable<T> source) where T : DatabaseObject
    {
        var items = source.Take(257).ToArray();
        if (items.Length > 256 || items.Any(item => string.IsNullOrEmpty(item.Name) || item.Name.Length > 96))
            throw new ApiException(413, "named_catalog_limit");
        return items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => (object)new { id = item.Id.ToString("D"), name = item.Name }).ToArray();
    }
    private static string[] Names<T>(IEnumerable<T>? items) where T : DatabaseObject
    {
        var names = items?.Take(65).Select(item => item.Name).ToArray() ?? Array.Empty<string>();
        // Never silently truncate collection identity: a later replacement edit
        // could otherwise drop associations the user was not shown.
        if (names.Length > 64 || names.Any(name => string.IsNullOrEmpty(name) || name.Length > 256))
            throw new ApiException(413, "metadata_limit");
        return names;
    }
    private static long Count(ulong value) => value > long.MaxValue ? long.MaxValue : (long)value;
    private static Dictionary<string, object?> ProjectGame(Game game, bool detailed)
    {
        var result = new Dictionary<string, object?>
        {
            ["id"] = game.Id.ToString("D"), ["name"] = WireJson.Text(game.Name), ["source"] = WireJson.Text(game.Source?.Name, 128),
            ["isInstalled"] = game.IsInstalled, ["favorite"] = game.Favorite, ["hidden"] = game.Hidden,
            ["completionStatus"] = game.CompletionStatus == null ? null : WireJson.Text(game.CompletionStatus.Name, 128),
            ["categories"] = Names(game.Categories), ["genres"] = Names(game.Genres), ["platforms"] = Names(game.Platforms),
            ["playtime"] = Count(game.Playtime), ["lastActivity"] = game.LastActivity?.ToString("o", CultureInfo.InvariantCulture),
        };
        if (!detailed) return result;
        result["steamAppId"] = SteamGameIdentity.Resolve(game.PluginId, game.GameId, game.Links?.Select(link => link.Url));
        result["description"] = WireJson.Text(game.Description, 128 * 1024);
        result["notes"] = WireJson.Text(game.Notes, 32 * 1024);
        result["developers"] = Names(game.Developers); result["publishers"] = Names(game.Publishers);
        result["features"] = Names(game.Features); result["tags"] = Names(game.Tags);
        result["series"] = Names(game.Series); result["ageRatings"] = Names(game.AgeRatings);
        result["links"] = game.Links?.Take(16).Select(link => new { name = WireJson.Text(link.Name, 96), url = WireJson.Text(link.Url, 2048) }).ToArray();
        result["releaseDate"] = game.ReleaseDate?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        result["installSize"] = game.InstallSize; result["playCount"] = Count(game.PlayCount);
        result["communityScore"] = game.CommunityScore; result["criticScore"] = game.CriticScore;
        result["userScore"] = game.UserScore; result["version"] = WireJson.Text(game.Version);
        return result;
    }
    private async Task<LibrarySnapshot> SnapshotAsync(string? expected, CancellationToken token)
    {
        if (expected?.Length > 80) throw new ApiException(400, "invalid_revision");
        await snapshotsGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (expected != null)
                return snapshots.FirstOrDefault(snapshot => snapshot.Revision == expected) ?? throw new ApiException(409, "snapshot_expired");
            var version = Interlocked.Read(ref libraryRevision);
            var revision = connection.InstanceId + ":" + version.ToString(CultureInfo.InvariantCulture);
            var existing = snapshots.FirstOrDefault(snapshot => snapshot.Revision == revision);
            if (existing != null) return existing;
            var records = await OnUiAsync(() =>
            {
                var games = api.Database.Games.Take(10001).ToArray();
                if (games.Length > 10000) throw new ApiException(413, "library_limit");
                var projected = new List<Dictionary<string, object?>>(games.Length);
                long characters = 0;
                foreach (var game in games)
                {
                    var item = ProjectGame(game, false);
                    foreach (var value in item.Values)
                        characters += value is string text ? text.Length : value is string[] names ? names.Sum(name => name.Length) : 0;
                    if (characters > 4 * 1024 * 1024) throw new ApiException(413, "library_metadata_limit");
                    projected.Add(item);
                }
                return projected.ToArray();
            }, token).ConfigureAwait(false);
            if (Interlocked.Read(ref libraryRevision) != version) throw new ApiException(409, "library_changed");
            var sorted = records.OrderBy(game => (string)game["name"]!, StringComparer.OrdinalIgnoreCase)
                .ThenBy(game => (string)game["id"]!, StringComparer.Ordinal).ToArray();
            var result = new LibrarySnapshot(revision, sorted);
            snapshots.Add(result); while (snapshots.Count > 2) snapshots.RemoveAt(0);
            return result;
        }
        finally { snapshotsGate.Release(); }
    }
    private async Task<LocalApiResponse> GetGamesAsync(LocalApiRequest request, CancellationToken token)
    {
        var offset = WireJson.Integer(request.Query["offset"], 0, 10000);
        var limit = WireJson.Integer(request.Query["limit"], 128, 256);
        if (limit == 0) throw new ApiException(400, "invalid_range");
        string? Filter(string key)
        {
            var value = request.Query[key];
            if (value?.Length > 256) throw new ApiException(400, "filter_too_long");
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        bool Flag(string key)
        {
            var value = request.Query[key];
            if (value == null || value == "false") return false;
            if (value == "true") return true;
            throw new ApiException(400, "invalid_filter");
        }
        var search = Filter("q"); var source = Filter("source"); var category = Filter("category"); var completion = Filter("completionStatus");
        var installed = Flag("installed"); var favorite = Flag("favorite"); var hidden = Flag("hidden");
        var snapshot = await SnapshotAsync(request.Query["revision"], token).ConfigureAwait(false);
        bool Matches(Dictionary<string, object?> game) =>
            (!installed || (bool)game["isInstalled"]!) && (!favorite || (bool)game["favorite"]!) && (hidden || !(bool)game["hidden"]!) &&
            (search == null || ((string)game["name"]!).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) &&
            (source == null || string.Equals((string?)game["source"], source, StringComparison.OrdinalIgnoreCase)) &&
            (completion == null || string.Equals((string?)game["completionStatus"], completion, StringComparison.OrdinalIgnoreCase)) &&
            (category == null || ((string[])game["categories"]!).Contains(category, StringComparer.OrdinalIgnoreCase));
        var filtered = snapshot.Games.Where(Matches).ToArray();
        if (offset > filtered.Length) throw new ApiException(400, "invalid_offset");
        return WireJson.Response(new { revision = snapshot.Revision, total = filtered.Length, offset, limit, games = filtered.Skip(offset).Take(limit).ToArray() });
    }
    private string? ArtworkPath(Guid id, string kind)
    {
        var game = RequireGame(id);
        var value = kind == "cover" ? game.CoverImage : kind == "background" ? game.BackgroundImage : kind == "icon" ? game.Icon :
            throw new ApiException(400, "invalid_artwork_kind");
        if (string.IsNullOrWhiteSpace(value)) return null;
        var path = api.Database.GetFullFilePath(value);
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.Length > 2048)
            return null;
        return Path.GetFullPath(path);
    }
    private static object ArtworkDescriptor(string id, string kind, string? path)
    {
        string? revision = null;
        if (path != null)
        {
            try
            {
                var file = new FileInfo(path);
                if (file.Exists) revision = file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) + ":" + file.Length.ToString(CultureInfo.InvariantCulture);
                else path = null;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { path = null; }
        }
        return new { gameId = id, kind, path, revision };
    }
    private async Task<LocalApiResponse> ResolveArtworkAsync(LocalApiRequest request, CancellationToken token)
    {
        var body = WireJson.Body(request);
        if (body.Keys.Any(key => key != "gameIds" && key != "kinds")) throw new ApiException(400, "unknown_field");
        var ids = ReadNames(body, "gameIds", 128, 36).Select(value => Guid.TryParseExact(value, "D", out var id) ? id : throw new ApiException(400, "invalid_game_id")).ToArray();
        var kinds = ReadNames(body, "kinds", 3, 16);
        if (kinds.Any(kind => kind != "cover" && kind != "background" && kind != "icon")) throw new ApiException(400, "invalid_artwork_kind");
        var paths = await OnUiAsync(() => ids.SelectMany(id => kinds.Select(kind => new
        { Id = id.ToString("D"), Kind = kind, Path = api.Database.Games.Get(id) == null ? null : ArtworkPath(id, kind) })).ToArray(), token).ConfigureAwait(false);
        return WireJson.Response(new { artwork = paths.Select(item => ArtworkDescriptor(item.Id, item.Kind, item.Path)).ToArray() });
    }
    private async Task<LocalApiResponse> GetArtworkManifestAsync(LocalApiRequest request, CancellationToken token)
    {
        var offset = WireJson.Integer(request.Query["offset"], 0, 10000);
        var limit = WireJson.Integer(request.Query["limit"], 128, 128);
        if (limit == 0) throw new ApiException(400, "invalid_range");
        var snapshot = await SnapshotAsync(request.Query["revision"], token).ConfigureAwait(false);
        if (offset > snapshot.Games.Length) throw new ApiException(400, "invalid_offset");
        var ids = snapshot.Games.Skip(offset).Take(limit).Select(game => Guid.Parse((string)game["id"]!)).ToArray();
        var paths = await OnUiAsync(() => ids.Select(id => new
        {
            Id = id.ToString("D"),
            Cover = api.Database.Games.Get(id) == null ? null : ArtworkPath(id, "cover"),
            Background = api.Database.Games.Get(id) == null ? null : ArtworkPath(id, "background"),
        }).ToArray(), token).ConfigureAwait(false);
        string? Revision(string? path)
        {
            if (path == null) return null;
            try { var file = new FileInfo(path); return file.Exists ? file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) + ":" + file.Length : null; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return null; }
        }
        return WireJson.Response(new { revision = snapshot.Revision, total = snapshot.Games.Length, offset, limit,
            artwork = paths.Select(item => new { id = item.Id, cover = item.Cover, background = item.Background,
                coverRevision = Revision(item.Cover), backgroundRevision = Revision(item.Background) }).ToArray() });
    }
    private async Task<LocalApiResponse> ReadArtworkAsync(Guid id, string kind, CancellationToken token)
    {
        var path = await OnUiAsync(() => ArtworkPath(id, kind), token).ConfigureAwait(false);
        if (path == null || !File.Exists(path)) throw new ApiException(404, "artwork_unavailable");
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var contentType = extension == ".png" ? "image/png" : extension == ".jpg" || extension == ".jpeg" ? "image/jpeg" :
            extension == ".webp" ? "image/webp" : extension == ".ico" ? "image/x-icon" : extension == ".gif" ? "image/gif" : null;
        if (contentType == null) throw new ApiException(415, "unsupported_artwork");
        using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 8192, true))
        using (var output = new MemoryStream())
        {
            if (input.Length > IntegrationIdentity.MaximumResponseBytes) throw new ApiException(413, "artwork_too_large");
            var buffer = new byte[8192]; int count;
            while ((count = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) != 0)
            { if (output.Length + count > IntegrationIdentity.MaximumResponseBytes) throw new ApiException(413, "artwork_too_large"); output.Write(buffer, 0, count); }
            return new LocalApiResponse(200, output.ToArray(), contentType);
        }
    }
    private static string[] ReadNames(Dictionary<string, object> body, string field, int count, int length)
    {
        if (!body.TryGetValue(field, out var value) || !(value is object[] array) || array.Length > count)
            throw new ApiException(400, "invalid_array");
        if (array.Any(item => !(item is string name) || string.IsNullOrWhiteSpace(name) || name.Length > length || name.Any(char.IsControl)))
            throw new ApiException(400, "invalid_name");
        var names = array.Cast<string>().ToArray();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length) throw new ApiException(400, "duplicate_name");
        return names;
    }
}
