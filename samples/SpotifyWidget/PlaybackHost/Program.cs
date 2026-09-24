using WidgetRail.SpotifyPlayback;

namespace WidgetRail.SpotifyPlaybackHost;

internal static class Program
{
    private const string SingletonName = "Local\\WidgetRail.SpotifyPlaybackHost.v1";

    [STAThread]
    private static int Main(string[] args)
    {
        if (!TryArguments(args, out var parentProcessId, out var profilePath) ||
            !Console.IsInputRedirected || !Console.IsOutputRedirected)
            return 2;

        using var singleton = new Mutex(initiallyOwned: true, SingletonName, out var isOwner);
        if (!isOwner) return 3;
        using var lifetime = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            lifetime.Cancel();
        };

        ApplicationConfiguration.Initialize();
        var writeGate = new object();
        void Emit(SpotifyPlaybackEvent value)
        {
            var line = SpotifyPlaybackProtocolCodec.EncodeEvent(value);
            lock (writeGate)
            {
                Console.Out.WriteLine(line);
                Console.Out.Flush();
            }
        }

        using var form = new SpotifyPlaybackHostForm(Emit, profilePath);
        _ = form.Handle;
        void Stop()
        {
            lifetime.Cancel();
            if (!form.IsDisposed && form.IsHandleCreated)
                form.BeginInvoke(form.Stop);
        }

        _ = Task.Run(async () =>
        {
            try { await ParentLifetime.WaitForExitAsync(parentProcessId, lifetime.Token); }
            catch (OperationCanceledException) { return; }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            Stop();
        });
        _ = Task.Run(async () =>
        {
            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    var line = await BoundedTextLineReader.ReadAsync(Console.In,
                        SpotifyPlaybackProtocol.MaximumMessageCharacters, lifetime.Token);
                    if (line is null) break;
                    SpotifyPlaybackRequest request;
                    try { request = SpotifyPlaybackProtocolCodec.DecodeRequest(line); }
                    catch (SpotifyPlaybackProtocolException exception)
                    {
                        Emit(new(SpotifyPlaybackProtocol.Version, "protocol_error", null,
                            new { code = exception.Code, message = exception.Message }));
                        continue;
                    }
                    if (!form.IsDisposed && form.IsHandleCreated)
                        form.BeginInvoke(() => form.HandleRequest(request));
                }
            }
            catch (OperationCanceledException) { return; }
            catch (SpotifyPlaybackProtocolException exception)
            {
                Emit(new(SpotifyPlaybackProtocol.Version, "protocol_error", null,
                    new { code = exception.Code, message = exception.Message }));
            }
            Stop();
        });

        Application.Run(form);
        lifetime.Cancel();
        return 0;
    }

    private static bool TryArguments(string[] args, out int parentProcessId, out string profilePath)
    {
        parentProcessId = 0;
        profilePath = args.Length == 4 ? args[3] : string.Empty;
        return args.Length == 4 && args[2] == "--profile" &&
            Path.IsPathFullyQualified(profilePath) &&
            string.Equals(args[0], "--parent-pid", StringComparison.Ordinal) &&
            int.TryParse(args[1], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out parentProcessId) &&
            parentProcessId > 0;
    }
}
