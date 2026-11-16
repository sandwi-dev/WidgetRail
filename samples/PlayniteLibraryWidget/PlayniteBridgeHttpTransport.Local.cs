using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WidgetRail.Playnite;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed partial class PlayniteBridgeHttpTransport
{
    private IPlayniteLocalConnectionSource? localSource;
    private readonly object connectionGate = new();
    private readonly SemaphoreSlim handshakeGate = new(1, 1);
    private CancellationTokenSource connectionLifetime = new();
    private int connectionGeneration;
    private string? expectedBinding, verifiedInstance;
    private bool connectionDisposed;
    public bool UsesProfileConnection => localSource is not null;

    internal PlayniteBridgeHttpTransport(IPlayniteLocalConnectionSource source)
    {
        localSource = source ?? throw new ArgumentNullException(nameof(source));
        var handler = CreateHandler();
        // A long poll must not occupy the only connection needed by interaction.
        handler.MaxConnectionsPerServer = 4;
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }
    public void ResetConnection()
    {
        CancellationTokenSource previous;
        lock (connectionGate)
        {
            if (connectionDisposed) return;
            previous = connectionLifetime; connectionLifetime = new(); connectionGeneration++;
            expectedBinding = verifiedInstance = null;
        }
        previous.Cancel(); previous.Dispose();
    }
    private void DisposeConnection()
    {
        lock (connectionGate) { if (connectionDisposed) return; connectionDisposed = true; connectionGeneration++; }
        connectionLifetime.Cancel(); connectionLifetime.Dispose();
    }
    public async ValueTask<PlayniteBridgeResponse?> SendConnectedAsync(PlayniteBridgeCommand command, CancellationToken token)
    {
        if (localSource is null) throw new InvalidOperationException("No local profile connection source.");
        int generation; CancellationToken owner;
        lock (connectionGate)
        { ObjectDisposedException.ThrowIf(connectionDisposed, this); generation = connectionGeneration; owner = connectionLifetime.Token; }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, owner);
        timeout.CancelAfter(command.Kind == PlayniteBridgeCommandKind.IntegrationEvents ? TimeSpan.FromSeconds(32) : RequestTimeout);
        try
        {
            var connection = await localSource.OpenAsync(timeout.Token).ConfigureAwait(false);
            if (connection is null) return null;
            lock (connectionGate)
            {
                if (generation != connectionGeneration) throw new PlayniteBridgeTransportException(isMalformed: false);
                expectedBinding ??= connection.BindingId;
                if (expectedBinding != connection.BindingId) throw new PlayniteBridgeTransportException(isMalformed: false);
                if (command.ExpectedConnectionId is { } expectedConnection && expectedConnection != connection.BindingId)
                    throw new PlayniteBridgeTransportException(isMalformed: false);
                // Do not transparently move a pending mutation onto a restarted
                // server. A subsequent connection check/read can establish it.
                if (IsMutation(command.Kind) && verifiedInstance is not null && verifiedInstance != connection.Descriptor.InstanceId)
                    return new(409, "{\"error\":{\"code\":\"server_instance_changed\"}}");
            }
            await handshakeGate.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                bool verified;
                lock (connectionGate) verified = generation == connectionGeneration && verifiedInstance == connection.Descriptor.InstanceId;
                if (!verified || command.Kind == PlayniteBridgeCommandKind.Probe)
                {
                    var handshake = await SendLocalAsync(connection, HttpMethod.Get, "v1/connection", null, null,
                        MaximumJsonBytes, timeout.Token).ConfigureAwait(false);
                    if (handshake.StatusCode != 200) return handshake;
                    ValidateHandshake(handshake, connection);
                    lock (connectionGate)
                    {
                        if (generation != connectionGeneration) throw new PlayniteBridgeTransportException(isMalformed: false);
                        verifiedInstance = connection.Descriptor.InstanceId;
                    }
                    if (command.Kind == PlayniteBridgeCommandKind.Probe) return handshake;
                }
            }
            finally { handshakeGate.Release(); }
            var route = ResolveLocal(command);
            return await SendLocalAsync(connection, route.Method, route.RelativePath, command.JsonBody,
                IsMutation(command.Kind) ? command.CommandId ?? Guid.NewGuid().ToString("D") : null,
                route.MaximumBytes, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (PlayniteBridgeTransportException) { throw; }
        catch (Exception error) when (error is OperationCanceledException or HttpRequestException or IOException or
            UnauthorizedAccessException or JsonException or PlayniteSetupException or PlayniteCredentialException)
        { throw new PlayniteBridgeTransportException(isMalformed: error is JsonException, error); }
    }
    private async Task<PlayniteBridgeResponse> SendLocalAsync(PlayniteLocalConnection connection, HttpMethod method,
        string relativePath, string? body, string? commandId, int maximumBytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!connection.Descriptor.IsValid() || !connection.IsCurrent()) throw new PlayniteBridgeTransportException(isMalformed: false);
        using var request = new HttpRequestMessage(method, new Uri(connection.Origin, relativePath))
            { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.Credential);
        request.Headers.Add("X-WidgetRail-Instance", connection.Descriptor.InstanceId);
        if (commandId is not null) request.Headers.Add("X-WidgetRail-Command", commandId);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        var bytes = await ReadBoundedAsync(response.Content, maximumBytes, token).ConfigureAwait(false);
        if (!connection.IsCurrent()) throw new PlayniteBridgeTransportException(isMalformed: false);
        return new((int)response.StatusCode, bytes, response.Content.Headers.ContentType?.MediaType) { ConnectionId = connection.BindingId };
    }
    private static void ValidateHandshake(PlayniteBridgeResponse response, PlayniteLocalConnection connection)
    {
        using var data = JsonDocument.Parse(response.Body, new() { MaxDepth = 16 });
        var root = data.RootElement;
        bool Matches(string name, string expected) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() == expected;
        if (root.ValueKind != JsonValueKind.Object || !Matches("pluginId", IntegrationIdentity.PluginId) ||
            !Matches("profileId", connection.Descriptor.ProfileId) || !Matches("instanceId", connection.Descriptor.InstanceId) ||
            !root.TryGetProperty("protocolVersion", out var protocol) || protocol.ValueKind != JsonValueKind.Number || !protocol.TryGetInt32(out var version) || version != IntegrationIdentity.ProtocolVersion ||
            !root.TryGetProperty("capabilities", out var capabilities) || capabilities.ValueKind != JsonValueKind.Array ||
            IntegrationIdentity.RequiredCapabilities.Any(required =>
                !capabilities.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String && value.GetString() == required)))
            throw new PlayniteBridgeTransportException(isMalformed: true);
    }
    private static bool IsMutation(PlayniteBridgeCommandKind kind) => kind is PlayniteBridgeCommandKind.Launch or
        PlayniteBridgeCommandKind.Install or PlayniteBridgeCommandKind.Uninstall or PlayniteBridgeCommandKind.SetFavorite or
        PlayniteBridgeCommandKind.SetHidden or PlayniteBridgeCommandKind.SetCategories or PlayniteBridgeCommandKind.SetCompletionStatus or
        PlayniteBridgeCommandKind.CreateCategory or PlayniteBridgeCommandKind.RefreshAchievements or
        PlayniteBridgeCommandKind.UpdateCaptureSettings or PlayniteBridgeCommandKind.ConfigureCaptureIntegration;
    internal static (HttpMethod Method, string RelativePath, int MaximumBytes) ResolveLocal(PlayniteBridgeCommand command)
    {
        string Game() => "v1/games/" + PlayniteBridgeClient.CanonicalGameId(command.GameId ?? "");
        return command.Kind switch
        {
            PlayniteBridgeCommandKind.CaptureSettings => (HttpMethod.Get, "v1/capture-settings", MaximumJsonBytes),
            PlayniteBridgeCommandKind.UpdateCaptureSettings => (HttpMethod.Patch, "v1/capture-settings", MaximumJsonBytes),
            PlayniteBridgeCommandKind.CaptureIntegration => (HttpMethod.Get, "v1/capture-integration", MaximumJsonBytes),
            PlayniteBridgeCommandKind.ConfigureCaptureIntegration => (HttpMethod.Put, "v1/capture-integration", MaximumJsonBytes),
            PlayniteBridgeCommandKind.AchievementOverview when command.OverviewOffset is >= 0 and <= 10000 && command.Revision?.Length is not > 80 =>
                (HttpMethod.Get, "v1/achievements/overview?limit=200&offset=" + command.OverviewOffset.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    (command.Revision is null ? "" : "&revision=" + Uri.EscapeDataString(command.Revision)), MaximumLibraryPageBytes),
            PlayniteBridgeCommandKind.Probe => (HttpMethod.Get, "v1/connection", MaximumJsonBytes),
            PlayniteBridgeCommandKind.QueryGames when command.Query is { } query => (HttpMethod.Get, QueryPath(query, "v1/games"), MaximumLibraryPageBytes),
            PlayniteBridgeCommandKind.ResolveGame => (HttpMethod.Get, Game(), MaximumDetailBytes),
            PlayniteBridgeCommandKind.Achievements => (HttpMethod.Get, Game() + "/achievements", MaximumDetailBytes),
            PlayniteBridgeCommandKind.Activity => (HttpMethod.Get, Game() + "/activity", MaximumDetailBytes),
            PlayniteBridgeCommandKind.ResolveArtwork => (HttpMethod.Get, Game() + "/artwork/" + (command.ArtworkKind ?? PlayniteBridgeArtworkKind.Cover).ToString().ToLowerInvariant(), MaximumArtworkBytes),
            PlayniteBridgeCommandKind.ResolveArtworkPaths => (HttpMethod.Get,
                $"v1/artwork?offset={command.ArtworkOffset}&limit={command.ArtworkLimit}" + (command.Revision is null ? "" : "&revision=" + Uri.EscapeDataString(command.Revision)), MaximumDetailBytes),
            PlayniteBridgeCommandKind.Launch => (HttpMethod.Post, Game() + "/launch", MaximumJsonBytes),
            PlayniteBridgeCommandKind.Install => (HttpMethod.Post, Game() + "/install", MaximumJsonBytes),
            PlayniteBridgeCommandKind.Uninstall => (HttpMethod.Post, Game() + "/uninstall", MaximumJsonBytes),
            PlayniteBridgeCommandKind.SetFavorite or PlayniteBridgeCommandKind.SetHidden => (HttpMethod.Patch, Game(), MaximumDetailBytes),
            PlayniteBridgeCommandKind.SetCategories => (HttpMethod.Put, Game() + "/categories", MaximumJsonBytes),
            PlayniteBridgeCommandKind.SetCompletionStatus => (HttpMethod.Put, Game() + "/status", MaximumJsonBytes),
            PlayniteBridgeCommandKind.ListCategories => (HttpMethod.Get, "v1/categories", MaximumJsonBytes),
            PlayniteBridgeCommandKind.CreateCategory => (HttpMethod.Post, "v1/categories", MaximumJsonBytes),
            PlayniteBridgeCommandKind.ListCompletionStatuses => (HttpMethod.Get, "v1/completion-statuses", MaximumJsonBytes),
            PlayniteBridgeCommandKind.RefreshAchievements => (HttpMethod.Post, Game() + "/achievements/refresh", MaximumJsonBytes),
            PlayniteBridgeCommandKind.OperationStatus => (HttpMethod.Get, "v1/operations/" + PlayniteBridgeClient.CanonicalGameId(command.OperationId ?? ""), MaximumJsonBytes),
            PlayniteBridgeCommandKind.IntegrationEvents => (HttpMethod.Get, $"v1/events?after={(command.AfterSequence == -1 ? "now" : command.AfterSequence.ToString(System.Globalization.CultureInfo.InvariantCulture))}&wait={command.WaitMilliseconds}", MaximumDetailBytes),
            _ => throw new ArgumentException("Unsupported local integration command.", nameof(command)),
        };
    }
}
