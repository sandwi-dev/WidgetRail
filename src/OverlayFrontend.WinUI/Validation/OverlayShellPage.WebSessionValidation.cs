using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableHostedSessionValidation(string path)
    {
        Loaded += async (_, _) =>
        {
            var checks = new List<string>(); string? error = null; var step = "startup";
            try
            {
                if (startup is not null) await startup;
                await WebAppUntil(() => activeWidget == "web-session" && interactive && foreground && !switching);
                step = "open hidden";
                await Action("open"); await Status("ready");
                Check(browserOwner!.ResidentCount == 1, "Authenticated worker opens a packaged session without a view");
                await Action("increment"); await Status("count:1");
                Check(true, "Typed request/reply crosses worker, broker, bridge and real WebView");
                if (Environment.GetCommandLineArgs().Contains("--measure-session-latency"))
                {
                    await Action("measure");
                    if (Environment.GetCommandLineArgs().Contains("--session-hidden-probe"))
                    {
                        await Status("music-ready");
                        NativePickerVisibilityForValidation!(false);
                        await Task.Delay(32000);
                        NativePickerVisibilityForValidation(true);
                    }
                    var until = Environment.TickCount64 + 120_000;
                    while (FindControl<TextBlock>(surface!, "Widget.status")?.Text.StartsWith("metrics", StringComparison.Ordinal) != true)
                    { if(Environment.TickCount64>until)throw new TimeoutException("Measurement did not finish.");await Task.Delay(25); }
                    var result = FindControl<TextBlock>(surface!, "Widget.status")!.Text;
                    await File.WriteAllTextAsync(path, result);
                    return;
                }
                step = "attach";
                await Action("view");
                await WebAppUntil(() => FindSlot(surface)?.Surface is { IsReady: true });
                var page = FindSlot(surface)!.Surface!;
                Check(browserOwner.CreatedCount == 1, "View attaches to the existing controller");
                step = "detach and message";
                await Action("view"); await Action("increment"); await Status("count:2");
                await Action("view"); await WebAppUntil(() => ReferenceEquals(FindSlot(surface)?.Surface, page));
                Check(true, "Detaching and reattaching preserves the same live page and its state");
                step = "cancel";
                await Action("cancel"); await Status("canceled");
                await WebAppUntilScript(page, "window.wasCanceled===true");
                await Action("increment"); await Status("count:3");
                Check(true, "Cancellation aborts the page handler without corrupting subsequent requests");
                step = "retire worker";
                await owner!.Session.RestartAsync(owner.Session.GetTarget("web-session"));
                await WebAppUntil(() => !page.IsReady && browserOwner.ResidentCount == 0);
                await SelectAsync("web-session");
                await Status("closed");
                Check(true, "Worker replacement retires its sessions and borrowed view");
                await Action("open"); await Status("ready");
                await Action("increment"); await Status("count:1");
                Check(true, "A replacement worker starts a fresh isolated page");
                await Action("close"); await Status("closed");
                await WebAppUntil(() => browserOwner.ResidentCount == 0);
                Check(true, "Explicit close releases the native controller");
            }
            catch (Exception failure) { error = failure.ToString(); }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = error is null, step, checks, error }));
            void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
            async Task Action(string id)
            {
                await WebAppUntil(() => surface?.CurrentBinding is not null && !switching);
                await InvokeAsync(new(surface!.CurrentBinding!.Frame, new(id, id, ControllerButton.A, InputScopeId: "root")));
            }
            Task Status(string value) => WebAppUntil(() => surface is not null && FindControl<TextBlock>(surface, "Widget.status")?.Text == value);
        };
    }
    private static BrowserSlot? FindSlot(Microsoft.UI.Xaml.DependencyObject? root)
    {
        if (root is null) return null;
        if (root is BrowserSlot slot) return slot;
        for (var i=0; i<Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindSlot(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root,i)) is { } child) return child;
        return null;
    }
}
