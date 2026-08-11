using System.Globalization;
using GameBarAlternative.Samples.YtMusicWidget;
using GameBarAlternative.WidgetRuntime;

namespace GameBarAlternative.Tests.AdvancedActionFailureFixture;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var failOnceMarker = OptionalValue(args, "--fail-once");
        if (failOnceMarker is not null && !File.Exists(failOnceMarker))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(failOnceMarker))!);
            using var marker = new FileStream(
                failOnceMarker, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            return 65;
        }
        var pipeName = RequiredValue(args, "--widget-pipe");
        var instanceId = RequiredValue(args, "--widget-instance");
        var maximumBytes = int.Parse(
            RequiredValue(args, "--max-message-bytes"),
            NumberStyles.None,
            CultureInfo.InvariantCulture);

        using var shutdown = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            var widget = new YtMusicWidget(new DeterministicFailureClient());
            await new WidgetWorkerServer(widget, instanceId, pipeName, maximumBytes)
                .RunAsync(shutdown.Token)
                .ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static string RequiredValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            throw new ArgumentException($"Missing required argument {name}.");
        return args[index + 1];
    }

    private static string? OptionalValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0) return null;
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            throw new ArgumentException($"Missing value for {name}.");
        return args[index + 1];
    }

    private sealed class DeterministicFailureClient : IYtMusicClient
    {
        private static readonly YtMusicPlaybackSnapshot ConnectedSnapshot = new(
            TrackId: "dlv014-track",
            Title: "DLV-014 Song",
            Artist: "Fixture Artist",
            Album: "Fixture Album",
            ArtworkUrl: string.Empty,
            IsPlaying: true,
            IsLiked: false,
            IsDisliked: false,
            PositionSeconds: 12,
            DurationSeconds: 180,
            IsShuffleEnabled: false,
            RepeatMode: YtMusicRepeatMode.Off,
            MetadataTrackId: "dlv014-track",
            HasCompleteMetadata: true);

        public Task<YtMusicConnectionInfo> GetStatusAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new YtMusicConnectionInfo(
                AuthRequired: false,
                HasCredential: false));
        }

        public Task<YtMusicPlaybackSnapshot> GetSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ConnectedSnapshot);
        }

        public Task SendCommandAsync(
            YtMusicCommand command,
            CancellationToken cancellationToken = default,
            bool? toggleState = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new OperationCanceledException("DLV014_SECRET_SENTINEL");
        }

        public Task<YtMusicPairingCode> RequestPairingCodeAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new YtMusicPairingCode("739204"));
        }

        public Task CompletePairingAsync(
            string code,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task ClearCredentialAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
