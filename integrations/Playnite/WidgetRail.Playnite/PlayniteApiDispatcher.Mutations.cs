using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK.Models;

namespace WidgetRail.Playnite;

internal sealed partial class PlayniteApiDispatcher
{
    private async Task<LocalApiResponse> RunMutationAsync(LocalApiRequest request, Func<object> action, CancellationToken waitingToken)
    {
        if (request.CommandId == null || !Guid.TryParseExact(request.CommandId, "D", out _)) throw new ApiException(400, "command_id_required");
        var signature = CommandSignature(request);
        var work = commands.Execute(request.CommandId, signature, async () =>
        {
            try { return WireJson.Response(await OnUiAsync(action, lifetime.Token).ConfigureAwait(false)); }
            catch (ApiException error) { return LocalApiResponse.Error(error.Status, error.Code); }
            catch (ArgumentException) { return LocalApiResponse.Error(400, "invalid_request"); }
            catch (OperationCanceledException) { throw; }
            catch { return LocalApiResponse.Error(500, "operation_failed"); }
        });
        // Cancelling the socket wait does not cancel an admitted Playnite action.
        // The bounded registry retains its result for safe reconciliation/replay.
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (waitingToken.Register(() => cancelled.TrySetResult(true)))
        {
            if (await Task.WhenAny(work, cancelled.Task).ConfigureAwait(false) != work) waitingToken.ThrowIfCancellationRequested();
            return await work.ConfigureAwait(false);
        }
    }
    private static string CommandSignature(LocalApiRequest request)
    {
        using (var hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(request.Method + "\n" + request.Path + "\n" + request.Query["running"] + "\n" + Convert.ToBase64String(request.Body)))).Replace("-", "");
    }
    private object UpdateGame(Guid id, Dictionary<string, object> body)
    {
        if (body.Count == 0 || body.Keys.Any(key => key != "favorite" && key != "hidden")) throw new ApiException(400, "unknown_field");
        if (body.Values.Any(value => !(value is bool))) throw new ApiException(400, "boolean_required");
        var game = RequireGame(id);
        if (body.TryGetValue("favorite", out var favorite)) game.Favorite = (bool)favorite;
        if (body.TryGetValue("hidden", out var hidden)) game.Hidden = (bool)hidden;
        api.Database.Games.Update(game);
        return ProjectGame(game, false);
    }
    private object RequestGameAction(Guid id, string action, string? approval)
    {
        var game = RequireGame(id);
        if (action == "launch" || action == "return")
        {
            if (!game.IsInstalled) throw new ApiException(409, "game_not_installed");
            var running = game.IsRunning;
            if (action == "return" && !running) throw new ApiException(404, "game_not_running");
            var starting = game.IsLaunching;
            if (approval != null && !running && !starting && launchTracker.Conflict(id, approval) is { } conflict)
                return new { conflict = conflict.Revision, count = conflict.Count, name = api.Database.Games.Get(conflict.Game)?.Name ?? "Another game" };
            var observation = launchTracker.Begin(id, running, starting);
            if (!running && !starting) api.StartGame(id);
            return new { ok = true, gameId = id.ToString("D"), action = "launch_requested", observation };
        }
        else if (action == "install" && !game.IsInstalled) api.InstallGame(id);
        else if (action == "uninstall" && game.IsInstalled) api.UninstallGame(id);
        return new { ok = true, gameId = id.ToString("D"), action = action + "_requested" };
    }
    private static string RequiredName(Dictionary<string, object> body, string key)
    {
        if (body.Count != 1 || !body.TryGetValue(key, out var value) || !(value is string name) ||
            string.IsNullOrWhiteSpace(name) || name.Length > 96 || name != name.Trim() || name.Any(char.IsControl))
            throw new ApiException(400, "invalid_name");
        return name;
    }
    private object CreateCategory(Dictionary<string, object> body)
    {
        var name = RequiredName(body, "name");
        var existing = UniqueNamed(api.Database.Categories, name);
        if (existing == null) { existing = new Category(name); api.Database.Categories.Add(existing); }
        LibraryChanged();
        return new { ok = true, id = existing.Id.ToString("D"), name = existing.Name };
    }
    private object SetOrganization(Guid id, string field, Dictionary<string, object> body)
    {
        var game = RequireGame(id);
        if (field == "status")
        {
            var name = RequiredName(body, "status");
            var status = UniqueNamed(api.Database.CompletionStatuses, name);
            if (status == null) throw new ApiException(404, "completion_status_not_found");
            game.CompletionStatusId = status.Id;
        }
        else
        {
            if (body.Keys.Any(key => key != "categories")) throw new ApiException(400, "unknown_field");
            var names = ReadNames(body, "categories", 64, 96);
            // Validate every existing identity before adding any category. A
            // later ambiguous name must not leave earlier partial creations.
            var existing = names.Select(name => UniqueNamed(api.Database.Categories, name)).ToArray();
            var ids = new List<Guid>();
            using (api.Database.BufferedUpdate())
            {
                for (var index = 0; index < names.Length; index++)
                {
                    var name = names[index];
                    var category = existing[index];
                    if (category == null) { category = new Category(name); api.Database.Categories.Add(category); }
                    ids.Add(category.Id);
                }
                game.CategoryIds = ids;
                api.Database.Games.Update(game);
            }
            return new { ok = true, gameId = id.ToString("D") };
        }
        api.Database.Games.Update(game);
        return new { ok = true, gameId = id.ToString("D") };
    }
    private static T? UniqueNamed<T>(IEnumerable<T> items, string name) where T : DatabaseObject
    {
        var matches = items.Where(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (matches.Length > 1) throw new ApiException(409, "ambiguous_name");
        return matches.FirstOrDefault();
    }
}
