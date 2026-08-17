using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WrailCli;

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
                "Usage: wrail render <snapshot.json> [--output <canonical-snapshot.json>]");

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
                "Widget assembly rendering is unavailable because wrail never loads author code into the CLI process. " +
                "Use wrail dev for isolated AppContainer execution, or render an existing data-only snapshot.json file.");
        }
        else
        {
            throw new CliUsageException(
                "Render input must be a .json snapshot. Use wrail dev for isolated widget execution.");
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
        return await ReadSnapshotAsync(stream).ConfigureAwait(false);
    }

    internal static async Task<ViewSnapshot> ReadSnapshotAsync(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("Snapshot input stream must be readable.", nameof(stream));
        if (stream.CanSeek && stream.Length is <= 0 or > MaximumSnapshotBytes)
            throw new CliOperationException(
                $"Snapshot input must be between 1 and {MaximumSnapshotBytes} bytes.");

        var initialCapacity = stream.CanSeek
            ? (int)Math.Clamp(stream.Length, 0, MaximumSnapshotBytes)
            : 0;
        using var payload = new MemoryStream(initialCapacity);
        var chunk = new byte[64 * 1024];
        while (payload.Length <= MaximumSnapshotBytes)
        {
            var remainingThroughDetectionByte =
                MaximumSnapshotBytes + 1L - payload.Length;
            var read = await stream.ReadAsync(
                chunk.AsMemory(0, (int)Math.Min(chunk.Length, remainingThroughDetectionByte)))
                .ConfigureAwait(false);
            if (read == 0) break;
            payload.Write(chunk, 0, read);
        }

        if (payload.Length is <= 0 or > MaximumSnapshotBytes)
            throw new CliOperationException(
                $"Snapshot input must be between 1 and {MaximumSnapshotBytes} bytes.");
        try
        {
            if (!payload.TryGetBuffer(out var segment))
                throw new InvalidOperationException("Snapshot buffer was unavailable.");
            return SnapshotJson.Deserialize(
                segment.AsSpan(0, checked((int)payload.Length)));
        }
        catch (Exception exception) when (exception is JsonException or ProtocolValidationException)
        {
            throw new CliOperationException($"Snapshot is invalid: {exception.Message}", exception);
        }
    }
}
