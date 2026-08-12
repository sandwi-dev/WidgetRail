using System.Diagnostics;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.Tests.FullApplicationWidgetFixture;

public sealed class FullApplicationWidget : Widget
{
    private Process? _helper;
    private int _privateBytes;

    protected override async ValueTask OnCreatedAsync(CancellationToken cancellationToken)
    {
        var privateRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameBarAlternative.FullApplicationFixture");
        Directory.CreateDirectory(privateRoot);
        var privateDatabase = Path.Combine(privateRoot, "catalog.bin");
        var bytes = new byte[128 * 1024];
        bytes[0] = 0x47;
        bytes[^1] = 0x41;
        await File.WriteAllBytesAsync(privateDatabase, bytes, cancellationToken);
        _privateBytes = checked((int)new FileInfo(privateDatabase).Length);

        var helperPath = Path.Combine(
            Path.GetDirectoryName(typeof(FullApplicationWidget).Assembly.Location)
                ?? throw new InvalidOperationException("Package payload directory is unavailable."),
            "FullApplicationWidgetFixture.exe");
        var startInfo = new ProcessStartInfo(helperPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--owned-helper");
        _helper = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Owned helper process did not start.");
    }

    protected override ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
    {
        _helper?.Dispose();
        _helper = null;
        return ValueTask.CompletedTask;
    }

    public override WidgetView Render() => new(
        UI.Stack("full-app.root",
            UI.Text($"helper-ready:{_helper?.Id ?? 0}", "full-app.helper"),
            UI.Text($"private-bytes:{_privateBytes}", "full-app.private")));
}
