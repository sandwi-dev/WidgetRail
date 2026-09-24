using System.Diagnostics;
using System.Text;
using System.Text.Json;
using WidgetRail.Samples.YtMusicWidget.Standalone;

namespace WidgetRail.YtMusicPlaybackHost;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 4 || args[0] != "--parent-pid" || args[2] != "--profile" || !int.TryParse(args[1], out var parentId) ||
            parentId <= 0 || !Console.IsInputRedirected || !Console.IsOutputRedirected) return 2;
        // WinExe has redirected pipe handles but no console input handle to reconfigure.
        Console.SetIn(new StreamReader(Console.OpenStandardInput(), Encoding.UTF8));
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
        using var lifetime = new CancellationTokenSource();
        ApplicationConfiguration.Initialize();
        var gate = new object();
        void Emit(object value)
        {
            lock (gate) { Console.WriteLine(JsonSerializer.Serialize(value, JsonProcess.Json)); Console.Out.Flush(); }
        }
        using var form = new PlayerForm(Emit, args[3]);
        _ = form.Handle;
        void Stop()
        {
            if (!form.IsDisposed && form.IsHandleCreated) form.BeginInvoke(form.Stop);
        }
        _ = Task.Run(async () =>
        {
            try { using var parent = Process.GetProcessById(parentId); await parent.WaitForExitAsync(lifetime.Token); }
            catch (OperationCanceledException) { return; }
            catch (ArgumentException) { }
            Stop();
        });
        _ = Task.Run(async () =>
        {
            try
            {
                while (await JsonProcess.ReadLineAsync(Console.In, lifetime.Token) is { } line)
                {
                    using var document = JsonDocument.Parse(line);
                    var value = document.RootElement.Clone();
                    if (!form.IsDisposed) form.BeginInvoke(() => form.HandleRequest(value));
                }
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or JsonException) { }
            Stop();
        });
        Application.Run(form);
        lifetime.Cancel();
        return 0;
    }
}
