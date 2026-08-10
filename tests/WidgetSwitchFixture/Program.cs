using System.Globalization;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.Tests.WidgetSwitchFixture;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var pipeName = RequiredValue(args, "--widget-pipe");
        var instanceId = RequiredValue(args, "--widget-instance");
        var maximumBytes = int.Parse(
            RequiredValue(args, "--max-message-bytes"),
            NumberStyles.None,
            CultureInfo.InvariantCulture);
        var firstSnapshotSignal = OptionalValue(args, "--first-snapshot-signal");

        using var shutdown = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            await new WidgetWorkerServer(
                    new SwitchFixtureWidget(instanceId, firstSnapshotSignal),
                    instanceId,
                    pipeName,
                    maximumBytes)
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

    private sealed class SwitchFixtureWidget(
        string instanceId,
        string? firstSnapshotSignal) : Widget
    {
        private readonly SurfaceDefinition _surface = ResolveSurface(instanceId);
        private bool _firstSnapshotDelayed;

        public override WidgetView Render()
        {
            if (!_firstSnapshotDelayed)
            {
                _firstSnapshotDelayed = true;
                if (_surface.FirstSnapshotDelayMilliseconds > 0)
                {
                    if (firstSnapshotSignal is not null)
                        File.WriteAllText(
                            firstSnapshotSignal,
                            Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
                    Thread.Sleep(_surface.FirstSnapshotDelayMilliseconds);
                }
            }
            return new WidgetView(
                UI.Stack(
                    $"{_surface.Id}-root",
                    UI.Text(_surface.Title, $"{_surface.Id}-title", _surface.Title)
                        .Classes("switch-title"),
                    UI.Text("Production host transition fixture", $"{_surface.Id}-detail")
                        .Classes("switch-detail"),
                    UI.Button("Ready", "fixture.ready", $"{_surface.Id}-ready")
                        .Classes("switch-button"))
                    .Classes("switch-surface", _surface.ClassName),
                InitialFocusId: $"{_surface.Id}-ready",
                Surface: _surface.Hints);
        }

        private static SurfaceDefinition ResolveSurface(string value)
        {
            if (value.StartsWith("audio-mixer.", StringComparison.Ordinal))
                return new("audio", "Audio Mixer", "audio-surface", 0, new WidgetSurfaceHints
                {
                    Mode = WidgetSurfaceMode.Compact,
                    PreferredWidth = 520,
                    PreferredHeight = 520,
                    MinimumWidth = 320,
                    MinimumHeight = 360,
                });
            if (value.StartsWith("network-controls.", StringComparison.Ordinal))
                return new("network", "Network Controls", "network-surface", 0, new WidgetSurfaceHints
                {
                    Mode = WidgetSurfaceMode.Compact,
                    PreferredWidth = 560,
                    PreferredHeight = 700,
                    MinimumWidth = 320,
                    MinimumHeight = 420,
                });
            if (value.StartsWith("spotify.", StringComparison.Ordinal))
                return new("spotify", "Spotify", "spotify-surface", 240, new WidgetSurfaceHints
                {
                    Mode = WidgetSurfaceMode.Adaptive,
                    PreferredWidth = 980,
                    PreferredHeight = 560,
                    MinimumWidth = 620,
                    MinimumHeight = 400,
                });
            if (value.StartsWith("games-apps.", StringComparison.Ordinal))
                return new("games", "Games & Apps", "games-surface", 320, new WidgetSurfaceHints
                {
                    Mode = WidgetSurfaceMode.Standard,
                    PreferredWidth = 820,
                    PreferredHeight = 430,
                    MinimumWidth = 420,
                    MinimumHeight = 300,
                });
            throw new ArgumentException($"Unsupported switch fixture instance {value}.");
        }

        private sealed record SurfaceDefinition(
            string Id,
            string Title,
            string ClassName,
            int FirstSnapshotDelayMilliseconds,
            WidgetSurfaceHints Hints);
    }
}
