using System.Diagnostics;
using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.EmbeddedMediaAdapterConformance;

public enum EmbeddedMediaFakePlayerProfile
{
    HtmlMediaElement,
    StateCallbackPlayer,
}

public static class EmbeddedMediaAdapterConformanceGate
{
    private const int MaximumDiagnosticCharacters = 8_192;

    public static async Task VerifyAsync(
        ViewSnapshot snapshot,
        string adapterPath,
        EmbeddedMediaFakePlayerProfile profile,
        IReadOnlyCollection<EmbeddedMediaPlaybackCommandKind> playbackCommands,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterPath);
        ArgumentNullException.ThrowIfNull(playbackCommands);
        if (!File.Exists(adapterPath))
            throw new FileNotFoundException("The embedded-media adapter was not found.", adapterPath);

        using var snapshotDocument = JsonDocument.Parse(SnapshotJson.Serialize(snapshot));
        var embeddedMedia = snapshotDocument.RootElement.GetProperty("embeddedMediaSession");
        var declaredCommands = embeddedMedia.GetProperty("commands")
            .EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .ToArray();
        if (declaredCommands.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException(
                "The embedded-media command declaration contained an empty command name.");

        var request = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new
        {
            declaredCommands,
            playbackCommands = playbackCommands.Select(command => command.ToString()).ToArray(),
        }));
        var runnerPath = Path.Combine(
            Path.GetTempPath(), $"wrail-adapter-conformance-{Guid.NewGuid():N}.mjs");
        try
        {
            await WriteRunnerAsync(runnerPath, cancellationToken).ConfigureAwait(false);
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "node",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            process.StartInfo.ArgumentList.Add(runnerPath);
            process.StartInfo.ArgumentList.Add(Path.GetFullPath(adapterPath));
            process.StartInfo.ArgumentList.Add(profile switch
            {
                EmbeddedMediaFakePlayerProfile.HtmlMediaElement => "html-media",
                EmbeddedMediaFakePlayerProfile.StateCallbackPlayer => "state-callback",
                _ => throw new ArgumentOutOfRangeException(nameof(profile)),
            });
            process.StartInfo.ArgumentList.Add(request);

            if (!process.Start())
                throw new InvalidOperationException(
                    "The embedded-media adapter conformance runner did not start.");
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await stdout.ConfigureAwait(false);
            var error = await stderr.ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"Embedded-media adapter conformance failed (exit {process.ExitCode}). " +
                    Bounded(string.Concat(error, output)));
        }
        finally
        {
            try { File.Delete(runnerPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task WriteRunnerAsync(
        string runnerPath,
        CancellationToken cancellationToken)
    {
        const string suffix = ".adapter-conformance.mjs";
        var assembly = typeof(EmbeddedMediaAdapterConformanceGate).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(suffix, StringComparison.Ordinal));
        await using var resource = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("The adapter conformance runner is unavailable.");
        await using var output = new FileStream(
            runnerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 16 * 1_024, useAsync: true);
        await resource.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static string Bounded(string value)
    {
        var normalized = value.Trim();
        return normalized.Length <= MaximumDiagnosticCharacters
            ? normalized
            : normalized[..MaximumDiagnosticCharacters];
    }
}
