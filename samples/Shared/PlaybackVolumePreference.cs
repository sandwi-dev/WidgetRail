using System.Globalization;

namespace WidgetRail.Samples.Shared;

/// <summary>A small optional per-widget preference; playback observations never write it.</summary>
internal sealed class PlaybackVolumePreference(string? path) : IAsyncDisposable
{
    private readonly object _gate = new();
    private Task? _writer;
    private double _latest;
    private long _revision;
    private bool _stopping;

    internal double Read(double fallback)
    {
        if (path is null) return fallback;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length is <= 0 or > 64) return fallback;
            using var reader = new StreamReader(stream);
            return double.TryParse(reader.ReadToEnd(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
                double.IsFinite(value) && value is >= 0 and <= 1 ? value : fallback;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return fallback; }
    }

    internal void Set(double value)
    {
        if (path is null || !double.IsFinite(value) || value is < 0 or > 1) return;
        lock (_gate)
        {
            if (_stopping) return;
            _latest = value;
            ++_revision;
            _writer ??= Task.Run(WriteLoopAsync);
        }
    }

    private async Task WriteLoopAsync()
    {
        while (true)
        {
            // Coalesce held-slider updates without delaying playback or blocking presentation.
            await Task.Delay(300).ConfigureAwait(false);
            double value;
            long revision;
            lock (_gate) { value = _latest; revision = _revision; }
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path!))!);
                await File.WriteAllTextAsync(temporary, value.ToString("R", CultureInfo.InvariantCulture)).ConfigureAwait(false);
                File.Move(temporary, path!, overwrite: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Trace.TraceWarning("Playback volume preference could not be saved: {0}", error.GetType().Name);
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
            lock (_gate)
            {
                if (revision != _revision) continue;
                _writer = null;
                return;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? writer;
        lock (_gate) { _stopping = true; writer = _writer; }
        if (writer is not null) await writer.ConfigureAwait(false);
    }
}
