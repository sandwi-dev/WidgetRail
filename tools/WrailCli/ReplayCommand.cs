using System.Text.Json;
using System.Text.Json.Serialization;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WrailCli;

public sealed record InputReplay
{
    public int Version { get; init; } = 1;
    public string? InitialFocusId { get; init; }
    public required IReadOnlyList<ReplayInputEvent> Events { get; init; }
}

public sealed record ReplayInputEvent
{
    public required ControllerButton Button { get; init; }
    public ControllerEventPhase Phase { get; init; } = ControllerEventPhase.Pressed;
    public string? CommittedText { get; init; }
}

public sealed record ReplayStep(
    int Sequence,
    ControllerButton Button,
    ControllerEventPhase Phase,
    string? FocusBefore,
    string? FocusAfter,
    string? ActionId)
{
    public string? CommittedText { get; init; }
}

public static class ControllerReplay
{
    public static IReadOnlyList<ReplayStep> Run(ViewSnapshot snapshot, InputReplay replay)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(replay);
        if (replay.Version != 1) throw new CliOperationException($"Unsupported replay version {replay.Version}.");
        if (replay.Events is null) throw new CliOperationException("Replay events cannot be null.");

        var nodes = Flatten(snapshot.Root).ToDictionary(node => node.Id, StringComparer.Ordinal);
        var focus = replay.InitialFocusId ?? snapshot.InitialFocusId ?? nodes.Values.FirstOrDefault(node => node.IsFocusable)?.Id;
        if (focus is not null && (!nodes.TryGetValue(focus, out var initial) || !initial.IsFocusable))
            throw new CliOperationException($"Initial focus '{focus}' is not a focusable node.");

        var steps = new List<ReplayStep>();
        for (var index = 0; index < replay.Events.Count; index++)
        {
            var input = replay.Events[index];
            var before = focus;
            string? action = null;
            string? committedText = null;

            if (focus is not null && nodes.TryGetValue(focus, out var current))
            {
                if (input.Phase is ControllerEventPhase.Pressed or ControllerEventPhase.Repeated)
                {
                    var target = DirectionTarget(current.Focus, input.Button);
                    if (target is not null) focus = target;
                    else if (input.Button == ControllerButton.A &&
                             input.Phase == ControllerEventPhase.Pressed)
                    {
                        if (current.Kind == ViewNodeKind.TextEntry)
                        {
                            if (input.CommittedText is { } value)
                            {
                                var maximumLength = current.TextEntryMaximumLength ??
                                    ProtocolConstants.MaximumTextEntryLength;
                                if (value.Length > maximumLength || value.Any(char.IsControl))
                                    throw new CliOperationException(
                                        $"Committed text for '{current.Id}' is invalid.");
                                action = current.ActionId;
                                committedText = value;
                            }
                        }
                        else
                        {
                            if (input.CommittedText is not null)
                                throw new CliOperationException(
                                    "Committed text is valid only for a text-entry activation.");
                            action = current.ActionId;
                        }
                    }
                }

                action ??= current.Shortcuts
                    .FirstOrDefault(shortcut => shortcut.Button == input.Button && shortcut.Phase == input.Phase)
                    ?.ActionId;
            }

            action ??= nodes.Values
                .SelectMany(node => node.Shortcuts)
                .FirstOrDefault(shortcut => shortcut.Button == input.Button && shortcut.Phase == input.Phase)
                ?.ActionId;
            steps.Add(new ReplayStep(index + 1, input.Button, input.Phase, before, focus, action)
                { CommittedText = committedText });
        }
        return steps;
    }

    private static string? DirectionTarget(FocusNeighbors? focus, ControllerButton button) => button switch
    {
        ControllerButton.DPadUp => focus?.Up,
        ControllerButton.DPadDown => focus?.Down,
        ControllerButton.DPadLeft => focus?.Left,
        ControllerButton.DPadRight => focus?.Right,
        _ => null,
    };

    private static IEnumerable<ViewNode> Flatten(ViewNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Flatten(child))
                yield return descendant;
    }
}

internal static class ReplayCommand
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public static async Task<int> RunAsync(string[] args, TextWriter output)
    {
        var parsed = new CommandArguments(args);
        if (parsed.Positionals.Count != 2)
            throw new CliUsageException("Usage: wrail replay <snapshot.json> <input-replay.json>");
        var snapshotPath = Path.GetFullPath(parsed.Positionals[0]);
        var replayPath = Path.GetFullPath(parsed.Positionals[1]);
        if (!File.Exists(snapshotPath) || !File.Exists(replayPath))
            throw new CliUsageException("Snapshot and replay files must exist.");

        try
        {
            var snapshot = SnapshotJson.Deserialize(await File.ReadAllBytesAsync(snapshotPath));
            var replay = JsonSerializer.Deserialize<InputReplay>(await File.ReadAllBytesAsync(replayPath), JsonOptions)
                ?? throw new JsonException("Replay payload was null.");
            var result = ControllerReplay.Run(snapshot, replay);
            await output.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions));
            return 0;
        }
        catch (Exception exception) when (exception is JsonException or ProtocolValidationException)
        {
            throw new CliOperationException($"Replay input is invalid: {exception.Message}", exception);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
