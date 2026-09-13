using System.Globalization;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Tests.WidgetSwitchFixture;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var pipeName = RequiredValue(args, "--widget-pipe");
        var instanceId = RequiredValue(args, "--widget-instance");
        var sessionNonce = RequiredValue(args, "--widget-session-nonce");
        var maximumBytes = int.Parse(
            RequiredValue(args, "--max-message-bytes"),
            NumberStyles.None,
            CultureInfo.InvariantCulture);
        var firstSnapshotSignal = OptionalValue(args, "--first-snapshot-signal");
        var refreshSignal = OptionalValue(args, "--refresh-signal");
        var blockSnapshotTrigger = OptionalValue(args, "--block-snapshot-trigger");
        var blockSnapshotArmed = OptionalValue(args, "--block-snapshot-armed");
        var blockSnapshotSignal = OptionalValue(args, "--block-snapshot-signal");
        var blockSnapshotRelease = OptionalValue(args, "--block-snapshot-release");
        var blockSnapshotComplete = OptionalValue(args, "--block-snapshot-complete");
        var actionSignal = OptionalValue(args, "--action-signal");

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
                    new SwitchFixtureWidget(
                        instanceId,
                        firstSnapshotSignal,
                        refreshSignal,
                        blockSnapshotTrigger,
                        blockSnapshotArmed,
                        blockSnapshotSignal,
                        blockSnapshotRelease,
                        blockSnapshotComplete,
                        actionSignal),
                    instanceId,
                    pipeName,
                    maximumBytes,
                    sessionNonce: sessionNonce)
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
        string? firstSnapshotSignal,
        string? refreshSignal,
        string? blockSnapshotTrigger,
        string? blockSnapshotArmed,
        string? blockSnapshotSignal,
        string? blockSnapshotRelease,
        string? blockSnapshotComplete,
        string? actionSignal) : Widget
    {
        private readonly SurfaceDefinition _surface = ResolveSurface(instanceId);
        private bool _firstSnapshotDelayed;
        private int _renderCount;
        private volatile bool _blockNextSnapshot;
        private long _blockedEpoch;
        private double _sliderValue = 50;

        public override WidgetView Render()
        {
            var renderOrdinal = Interlocked.Increment(ref _renderCount);
            if (!_firstSnapshotDelayed)
            {
                _firstSnapshotDelayed = true;
                // Let reload tests hold the new generation's first snapshot
                // until they finish observing the retained presentation.
                if (TryConsumeBlockEpoch(out var firstEpoch))
                {
                    Interlocked.Exchange(ref _blockedEpoch, firstEpoch);
                    _blockNextSnapshot = true;
                }
                if (firstSnapshotSignal is not null)
                    File.WriteAllText(
                        firstSnapshotSignal,
                        Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
                if (_surface.FirstSnapshotDelayMilliseconds > 0)
                {
                    Thread.Sleep(_surface.FirstSnapshotDelayMilliseconds);
                }
            }
            if (_blockNextSnapshot)
            {
                _blockNextSnapshot = false;
                var epoch = Interlocked.Read(ref _blockedEpoch);
                if (blockSnapshotSignal is not null)
                    File.WriteAllText(
                        blockSnapshotSignal,
                        $"epoch={epoch.ToString(CultureInfo.InvariantCulture)} " +
                        $"render-sequence={renderOrdinal.ToString(CultureInfo.InvariantCulture)} " +
                        $"pid={Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}");
                if (blockSnapshotRelease is not null)
                {
                    while (!ReleaseMatchesEpoch(epoch))
                        Thread.Sleep(10);
                }
                if (blockSnapshotComplete is not null)
                    File.WriteAllText(blockSnapshotComplete,
                        $"epoch={epoch.ToString(CultureInfo.InvariantCulture)} completed");
            }
            if (_surface.Id == "pinned-slider")
            {
                return new WidgetView(
                    UI.Stack(
                        "pinned-slider-root",
                        UI.Slider(
                                _sliderValue, 0, 100, 5, "fixture.slider.changed",
                                "pinned-slider", "Position", $"{_sliderValue:F0}")
                            .RequireControllerActivation()
                            .FocusRight("pinned-slider-peer"),
                        UI.Button("Peer", "fixture.peer", "pinned-slider-peer")
                            .FocusLeft("pinned-slider")),
                    InitialFocusId: "pinned-slider",
                    Surface: _surface.Hints);
            }
            return new WidgetView(
                UI.Stack(
                    $"{_surface.Id}-root",
                    UI.Text(_surface.Title, $"{_surface.Id}-title", _surface.Title)
                        .Classes("switch-title"),
                    UI.Text("Production host transition fixture", $"{_surface.Id}-detail")
                        .Classes("switch-detail"),
                    UI.Button("Ready", "fixture.ready", $"{_surface.Id}-ready")
                        .Shortcut(ControllerButton.X, actionId: "fixture.ready")
                        .FocusDown($"{_surface.Id}-more")
                        .Classes("switch-button"),
                    UI.Button("More", "fixture.more", $"{_surface.Id}-more")
                        .FocusUp($"{_surface.Id}-ready")
                        .Classes("switch-button"))
                    .Classes("switch-surface", _surface.ClassName),
                InitialFocusId: $"{_surface.Id}-ready",
                Surface: _surface.Hints);
        }

        public override ValueTask OnActionAsync(
            WidgetActionEvent action,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (action is
                {
                    ActionId: "fixture.slider.changed",
                    SourceElementId: "pinned-slider",
                    RequestedValue: { } requested
                })
            {
                _sliderValue = requested;
                if (actionSignal is not null)
                    File.AppendAllText(
                        actionSignal,
                        $"sequence={action.Sequence.ToString(CultureInfo.InvariantCulture)} " +
                        $"action={action.ActionId} source={action.SourceElementId} " +
                        $"value={requested.ToString("F0", CultureInfo.InvariantCulture)}\n");
                Invalidate();
                return ValueTask.CompletedTask;
            }
            if (action.ActionId == "fixture.ready")
            {
                if (TryConsumeBlockEpoch(out var epoch))
                {
                    Interlocked.Exchange(ref _blockedEpoch, epoch);
                    _blockNextSnapshot = true;
                    Invalidate();
                    if (blockSnapshotArmed is not null)
                        File.AppendAllText(blockSnapshotArmed,
                            $"epoch={epoch.ToString(CultureInfo.InvariantCulture)} armed\n");
                    return ValueTask.CompletedTask;
                }
                Invalidate();
                return ValueTask.CompletedTask;
            }
            if (action.ActionId == "refresh" && refreshSignal is not null)
            {
                File.AppendAllText(refreshSignal, "refresh\n");
                return ValueTask.CompletedTask;
            }
            return base.OnActionAsync(action, cancellationToken);
        }

        private bool TryConsumeBlockEpoch(out long epoch)
        {
            epoch = 0;
            if (blockSnapshotTrigger is null || !File.Exists(blockSnapshotTrigger))
                return false;
            var trigger = File.ReadAllText(blockSnapshotTrigger);
            File.Delete(blockSnapshotTrigger);
            return TryParseEpoch(trigger, out epoch);
        }

        private bool ReleaseMatchesEpoch(long epoch)
        {
            if (blockSnapshotRelease is null || !File.Exists(blockSnapshotRelease))
                return false;
            return TryParseEpoch(File.ReadAllText(blockSnapshotRelease), out var released) &&
                released == epoch;
        }

        private static bool TryParseEpoch(string value, out long epoch)
        {
            const string prefix = "epoch=";
            if (!value.StartsWith(prefix, StringComparison.Ordinal))
            {
                epoch = 0;
                return false;
            }
            var terminal = value.IndexOfAny([' ', '\r', '\n'], prefix.Length);
            var token = terminal < 0 ? value[prefix.Length..] : value[prefix.Length..terminal];
            return long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out epoch) &&
                epoch > 0;
        }

        private static SurfaceDefinition ResolveSurface(string value)
        {
            if (value.StartsWith("pinned-slider.", StringComparison.Ordinal))
                return new("pinned-slider", "Pinned Slider", "settings-surface", 0,
                    new WidgetSurfaceHints
                    {
                        Mode = WidgetSurfaceMode.Compact,
                        PreferredWidth = 520,
                        PreferredHeight = 360,
                        MinimumWidth = 320,
                        MinimumHeight = 260,
                    });
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
                    WidthMode = WidgetSurfaceAxisMode.FillAvailable,
                    PreferredWidth = 560,
                    PreferredHeight = 700,
                    MinimumWidth = 320,
                    MinimumHeight = 420,
                });
            if (value.StartsWith("now-playing.", StringComparison.Ordinal))
                return new("now-playing", "Now Playing", "now-playing-surface", 180, new WidgetSurfaceHints
                {
                    Mode = WidgetSurfaceMode.Adaptive,
                    PreferredWidth = 760,
                    PreferredHeight = 480,
                    MinimumWidth = 420,
                    MinimumHeight = 320,
                });
            if (value.StartsWith("yt-music.", StringComparison.Ordinal))
                return new("yt-music", "YT Music", "yt-music-surface", 240, new WidgetSurfaceHints
                {
                    Mode = WidgetSurfaceMode.Adaptive,
                    PreferredWidth = 900,
                    PreferredHeight = 600,
                    MinimumWidth = 540,
                    MinimumHeight = 380,
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
            if (value.StartsWith("wide-peer.", StringComparison.Ordinal))
                return new("wide-peer", "Wide Peer", "wide-peer-surface", 420, new WidgetSurfaceHints
                {
                    Mode = WidgetSurfaceMode.Wide,
                    PreferredWidth = 980,
                    PreferredHeight = 700,
                    MinimumWidth = 620,
                    MinimumHeight = 480,
                });
            if (value.StartsWith("settings.", StringComparison.Ordinal))
                return new("settings", "Settings", "settings-surface", 0, new WidgetSurfaceHints
                {
                    Mode = WidgetSurfaceMode.Standard,
                    HeightMode = WidgetSurfaceAxisMode.Content,
                    PreferredWidth = 700,
                    PreferredHeight = 650,
                    MinimumWidth = 420,
                    MinimumHeight = 380,
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
