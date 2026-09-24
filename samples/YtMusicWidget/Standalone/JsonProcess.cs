using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace WidgetRail.Samples.YtMusicWidget.Standalone;

/// <summary>Private inherited stdio IPC; no discoverable command server or credentials in argv.</summary>
internal sealed class JsonProcess : IAsyncDisposable
{
    internal const int MaximumLine = 2 * 1024 * 1024;
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Process _process;
    private readonly SemaphoreSlim _writer = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _reader;
    private readonly Task _errors;
    private long _sequence;
    internal event Action<JsonElement>? Event;

    internal JsonProcess(string executable, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false), StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        _process = Process.Start(info) ?? throw new IOException("The music helper could not start.");
        _reader = ReadAsync();
        // Third-party stderr can contain URLs or session data. Drain without persisting it.
        _errors = Task.Run(async () =>
        {
            var buffer = new char[4096];
            try { while (await _process.StandardError.ReadAsync(buffer, _lifetime.Token) != 0) { } }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        });
    }

    internal async Task<JsonElement> CallAsync(string method, object? args, CancellationToken token)
    {
        if (_reader.IsCompleted || _lifetime.IsCancellationRequested) throw new IOException("The music helper is no longer running.");
        if (_pending.Count >= 16) throw new IOException("The music service is busy. Try again.");
        var id = Interlocked.Increment(ref _sequence);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(method == "signin" ? 310 : 45));
        try
        {
            var line = JsonSerializer.Serialize(new { id, method, args = args ?? new { } }, Json);
            if (line.Length > MaximumLine) throw new IOException("Music request exceeds the allowed size.");
            await _writer.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                await _process.StandardInput.WriteLineAsync(line.AsMemory(), timeout.Token).ConfigureAwait(false);
                await _process.StandardInput.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            finally { _writer.Release(); }
            return await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        finally { _pending.TryRemove(id, out _); }
    }

    private async Task ReadAsync()
    {
        try
        {
            while (await ReadLineAsync(_process.StandardOutput, _lifetime.Token).ConfigureAwait(false) is { } line)
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var id) && _pending.TryGetValue(id.GetInt64(), out var pending))
                {
                    if (root.TryGetProperty("error", out _))
                        pending.TrySetException(new IOException("YouTube Music could not complete that request. Try again, or reconnect in Setup."));
                    else pending.TrySetResult(root.GetProperty("result").Clone());
                }
                else if (root.TryGetProperty("event", out _)) Event?.Invoke(root.Clone());
            }
        }
        catch (Exception error) when (error is IOException or JsonException or OperationCanceledException or InvalidOperationException) { }
        finally
        {
            foreach (var pending in _pending.Values)
                pending.TrySetException(new IOException("The music helper stopped. Reopen the widget to retry."));
            if (!_lifetime.IsCancellationRequested)
                Event?.Invoke(JsonSerializer.SerializeToElement(new { @event = "failed" }, Json));
        }
    }

    internal static async Task<string?> ReadLineAsync(TextReader reader, CancellationToken token)
    {
        var value = new StringBuilder();
        var character = new char[1];
        while (await reader.ReadAsync(character, token).ConfigureAwait(false) != 0)
        {
            if (character[0] == '\n') return value.ToString();
            if (value.Length >= MaximumLine) throw new IOException("Music response exceeds the allowed size.");
            if (character[0] != '\r') value.Append(character[0]);
        }
        return value.Length == 0 ? null : throw new IOException("Incomplete music response.");
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _process.StandardInput.Close();
        try { await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            try { _process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await _process.WaitForExitAsync().ConfigureAwait(false);
        }
        await Task.WhenAll(_reader, _errors).ConfigureAwait(false);
        _process.Dispose();
        _lifetime.Dispose();
    }
}
