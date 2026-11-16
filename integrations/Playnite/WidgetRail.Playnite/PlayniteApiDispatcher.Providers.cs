using System.Collections.Generic;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetRail.Playnite;

internal sealed partial class PlayniteApiDispatcher
{
    private readonly object providerRefreshGate = new object();
    private readonly Dictionary<Guid, Task<LocalApiResponse>> providerRefreshes = new Dictionary<Guid, Task<LocalApiResponse>>();
    private async Task<LocalApiResponse> ReadOptionalDataAsync(Guid id, string kind, CancellationToken token)
    {
        await OnUiAsync(() => RequireGame(id).Id, token).ConfigureAwait(false);
        await optionalReads.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (kind == "achievements")
            {
                object? cached = null;
                try { cached = await OnUiAsync(() => achievements.Read(id), token).ConfigureAwait(false); }
                catch (Exception error) when (error is TargetInvocationException || error is InvalidOperationException || error is ArgumentException || error is TypeLoadException)
                { /* Missing/incompatible optional provider falls back to existing SuccessStory. */ }
                if (cached != null) return WireJson.Response(cached);
            }
            token.ThrowIfCancellationRequested();
            try { return WireJson.Response(kind == "activity" ? optionalData.Activity(id) : optionalData.Achievements(id)); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is InvalidOperationException)
            { return WireJson.Response(new { gameId = id.ToString("D"), installed = false, code = "provider_data_unavailable" }); }
        }
        finally { optionalReads.Release(); }
    }
    private LocalApiResponse StartAchievementRefresh(LocalApiRequest request, Guid id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request.Body.Length != 0 && WireJson.Body(request).Count != 0) throw new ApiException(400, "unknown_field");
        if (request.CommandId == null || !Guid.TryParseExact(request.CommandId, "D", out _)) throw new ApiException(400, "command_id_required");
        var task = commands.Execute(request.CommandId, CommandSignature(request), () => RefreshProviderOnce(id));
        _ = task.ContinueWith(done => { _ = done.Exception; }, TaskScheduler.Default);
        return WireJson.Response(new { operationId = request.CommandId, state = "accepted" }, 202);
    }
    private Task<LocalApiResponse> RefreshProviderOnce(Guid id)
    {
        lock (providerRefreshGate)
        {
            if (providerRefreshes.TryGetValue(id, out var pending)) return pending;
            var next = RefreshProviderCoreAsync(id);
            providerRefreshes.Add(id, next);
            _ = next.ContinueWith(done => { lock (providerRefreshGate) providerRefreshes.Remove(id); _ = done.Exception; }, TaskScheduler.Default);
            return next;
        }
    }
    private async Task<LocalApiResponse> RefreshProviderCoreAsync(Guid id)
    {
            try
            {
                // Public provider API owns its network work and user notices.
                // It accepts no cancellation token: report operation state instead
                // of claiming a dropped HTTP request stopped the provider.
                var refresh = await OnUiAsync(() => { RequireGame(id); return achievements.Refresh(id); }, lifetime.Token).ConfigureAwait(false);
                await refresh.ConfigureAwait(false);
                changes.Publish(new IntegrationEvent("achievements-changed", id.ToString("D")));
                return WireJson.Response(new { ok = true, gameId = id.ToString("D"), action = "provider_refresh_completed" });
            }
            catch (OperationCanceledException) { throw; }
            catch (ApiException error) { return LocalApiResponse.Error(error.Status, error.Code); }
            catch { return LocalApiResponse.Error(502, "provider_refresh_failed"); }
    }
}
