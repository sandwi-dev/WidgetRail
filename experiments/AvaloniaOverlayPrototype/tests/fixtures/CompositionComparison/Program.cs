using System.Diagnostics;
using System.Text.Json;
#if DIRECT_DI
using Microsoft.Extensions.DependencyInjection;
#endif

var compositionTimer = Stopwatch.StartNew();
#if DIRECT_DI
using var provider = new ServiceCollection()
    .AddSingleton<IRemoteSource, RemoteSource>()
    .AddSingleton<LauncherState>()
    .AddTransient<PageFactory>()
    .BuildServiceProvider();
var page = provider.GetRequiredService<PageFactory>().Create();
const string strategy = "direct-microsoft-di";
#else
var source = new RemoteSource();
var state = new LauncherState(source);
var page = new PageFactory(state).Create();
const string strategy = "manual-composition";
#endif
compositionTimer.Stop();
GC.KeepAlive(page);
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
using var process = Process.GetCurrentProcess();
process.Refresh();
Console.Write(JsonSerializer.Serialize(new
{
    strategy,
    compositionMilliseconds = compositionTimer.Elapsed.TotalMilliseconds,
    privateMemoryMiB = process.PrivateMemorySize64 / 1024d / 1024d,
}));

internal interface IRemoteSource;
internal sealed class RemoteSource : IRemoteSource;
internal sealed class LauncherState(IRemoteSource source)
{
    public IRemoteSource Source { get; } = source;
}
internal sealed class PageFactory(LauncherState state)
{
    public object Create() => new { state.Source };
}
