using System.Text;
using System.Threading.Channels;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

/// <summary>Bounded, nonblocking diagnostics; no disk work runs on the input dispatcher.</summary>
internal sealed class PlatformInputDiagnostics : IDisposable
{
    private const int MaximumMessageLength = 4096;
    private readonly Channel<string> messages = Channel.CreateBounded<string>(new BoundedChannelOptions(128)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait,
        AllowSynchronousContinuations = false,
    });
    private long dropped;
    private int closed;

    public PlatformInputDiagnostics(string path, int maximumBytes = 512 * 1024)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 64 * 1024);
        Completion = Task.Run(() => DrainAsync(path, maximumBytes));
    }

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WidgetRail", "WinUI", "diagnostics", "controller.log");
    public Task Completion { get; }
    public Exception? Failure { get; private set; }

    public void Write(string message)
    {
        if (Volatile.Read(ref closed) != 0) return;
        if (message.Length > MaximumMessageLength) message = message[..MaximumMessageLength];
        // Native diagnostics are copied before arriving here. Keep one event per line.
        message = message.Replace('\r', ' ').Replace('\n', ' ');
        if (!messages.Writer.TryWrite($"{DateTimeOffset.UtcNow:O} ticks={Environment.TickCount64} {message}"))
            Interlocked.Increment(ref dropped);
    }

    private async Task DrainAsync(string path, int maximumBytes)
    {
        FileStream? output = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            output = Open();
            await foreach (var message in messages.Reader.ReadAllAsync())
            {
                var lost = Interlocked.Exchange(ref dropped, 0);
                var line = lost == 0 ? message : $"diagnostics dropped={lost}\n{message}";
                var bytes = Encoding.UTF8.GetBytes(line + "\n");
                if (output.Length + bytes.Length > maximumBytes)
                {
                    await output.DisposeAsync();
                    output = Open();
                }
                await output.WriteAsync(bytes);
                await output.FlushAsync();
            }
        }
        catch (Exception error)
        {
            Failure = error;
            Interlocked.Exchange(ref closed, 1);
            messages.Writer.TryComplete();
            System.Diagnostics.Debug.WriteLine($"Controller diagnostics unavailable: {error.Message}");
        }
        finally
        {
            if (output is not null)
            {
                try { await output.DisposeAsync(); }
                catch (Exception error) { Failure ??= error; }
            }
        }

        FileStream Open()
        {
            // One input owner per process/product. Retain the preceding segment/run,
            // keeping disk use bounded while preserving evidence over a restart.
            if (File.Exists(path)) File.Move(path, path + ".previous", overwrite: true);
            return new(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
                bufferSize: 4096, FileOptions.Asynchronous);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref closed, 1) != 0) return;
        messages.Writer.TryComplete();
    }
}
