using System.Text.Json;
using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.GbarCli;

internal static class RenderCommand
{
    // Match the absolute worker/bridge message ceiling without coupling the
    // data-only CLI command to either transport assembly.
    internal const int MaximumSnapshotBytes = 4_194_304;

    public static async Task<int> RunAsync(string[] args, TextWriter output)
    {
        var parsed = new CommandArguments(args, "--type", "--output", "--instance");
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException(
                "Usage: gbar render <snapshot.json> [--output <canonical-snapshot.json>]");

        var source = Path.GetFullPath(parsed.Positionals[0]);
        if (!File.Exists(source)) throw new CliUsageException($"File does not exist: {source}");

        ViewSnapshot snapshot;
        if (Path.GetExtension(source).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            if (parsed.Option("--type") is not null ||
                parsed.Option("--instance") is not null)
                throw new CliUsageException(
                    "--type and --instance are unavailable because render accepts only existing snapshots.");
            snapshot = await ReadSnapshotAsync(source).ConfigureAwait(false);
        }
        else if (Path.GetExtension(source).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliOperationException(
                "Widget assembly rendering is unavailable because gbar never loads author code into the CLI process. " +
                "Use gbar dev for isolated AppContainer execution, or render an existing data-only snapshot.json file.");
        }
        else
        {
            throw new CliUsageException(
                "Render input must be a .json snapshot. Use gbar dev for isolated widget execution.");
        }

        var destination = parsed.Option("--output");
        if (destination is not null)
        {
            var destinationPath = Path.GetFullPath(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllBytesAsync(destinationPath, SnapshotJson.Serialize(snapshot));
            await output.WriteLineAsync($"Snapshot written to {destinationPath}");
        }

        await output.WriteLineAsync(SnapshotPreview.Format(snapshot));
        return 0;
    }

    private static async Task<ViewSnapshot> ReadSnapshotAsync(string source)
    {
        await using var stream = new FileStream(source, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = 64 * 1024,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        });
        var length = stream.Length;
        if (length is <= 0 or > MaximumSnapshotBytes)
            throw new CliOperationException(
                $"Snapshot input must be between 1 and {MaximumSnapshotBytes} bytes.");

        var payload = new byte[(int)length];
        await stream.ReadExactlyAsync(payload).ConfigureAwait(false);
        try
        {
            return SnapshotJson.Deserialize(payload);
        }
        catch (Exception exception) when (exception is JsonException or ProtocolValidationException)
        {
            throw new CliOperationException($"Snapshot is invalid: {exception.Message}", exception);
        }
    }
}
