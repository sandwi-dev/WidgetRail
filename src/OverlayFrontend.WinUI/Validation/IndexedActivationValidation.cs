using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using PresentationSession = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Real worker refresh barriers exercise activation during retained native row refresh.</summary>
internal static class IndexedActivationValidation
{
    internal sealed record Result(string ResultCode, IReadOnlyList<string> Checks, string? Error);
    internal static async Task<Result> RunAsync(WidgetViewPresenter presenter, PresentationSession session, CancellationToken cancellation)
    {
        var checks = new List<string>();
        Result result;
        try
        {
            await Prepare();
            await Refresh("activation-refresh");
            var before = Calls();
            await Press();
            await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Repeated);
            await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
            await Press(); // Coalesce while the same single intent is waiting.
            Check(Calls() == before, "stale displayed row does not invoke a retired lease");
            await Send("activation-release");
            await Until(() => Status() == "row:301:75:open");
            await Task.Delay(120, cancellation);
            Check(Calls() == before + 2, "refresh delivers one A after replacement row publication despite repeat/release notifications");

            await Cancelled("B cancels even when its stale row shortcut cannot dispatch", async () => { await presenter.HandleControllerButtonAsync(ControllerButton.B); });
            await Cancelled("navigation", () => { presenter.MoveFocus(FocusNavigationDirection.Down); return Task.CompletedTask; });
            await Cancelled("focus leaves collection", () =>
            {
                Descendants(presenter).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "Widget.parent").Focus(FocusState.Keyboard);
                return Task.CompletedTask;
            });
            await Cancelled("query replacement", () => Send("activation-replace"));
            await Cancelled("modal scope", () => Send("activation-modal"));
            await Cancelled("host hide/deactivation", () => { presenter.ResetPressedStyles(); return Task.CompletedTask; });
            await Cancelled("expiry", () => Task.Delay(2200, cancellation));
            await Cancelled("changed action", () => Task.CompletedTask, "activation-changed");
            await Cancelled("disabled replacement", () => Task.CompletedTask, "activation-disabled");
            result = new("passed", checks, null);
        }
        catch (Exception error) { result = new("failed", checks, error.ToString()); }
        finally
        {
            // Leave no blocked provider operation behind when an assertion fails.
            try { await Send("activation-release"); } catch (Exception) { }
        }
        var output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "indexed-activation-result.json"), JsonSerializer.Serialize(result));
        return result;

        async Task Cancelled(string reason, Func<Task> cancel, string refresh = "activation-refresh")
        {
            await Prepare();
            await Refresh(refresh);
            await Press();
            await cancel();
            var calls = Calls();
            await Send("activation-release");
            await Until(() => Row()?.Lease.IsCurrent == true);
            await Task.Delay(120, cancellation);
            Check(Calls() == calls + 1 && Status() == "activation-release", reason + " cancels deferred A without invoking another command");
        }
        async Task Prepare()
        {
            await Send("activation-mode");
            await Until(() => FocusId() == "Widget.items.Item.75" && Row()?.Lease.IsCurrent == true);
        }
        async Task Refresh(string action)
        {
            await Send(action);
            await Until(() => Status() == action && Row()?.Lease.IsCurrent == false);
        }
        async Task Press() => _ = await presenter.HandleControllerButtonAsync(ControllerButton.A);
        async Task Send(string action)
        {
            var frame = Frame();
            await session.SendActionAsync(frame.Authority, new(action, "root", InputScopeId: frame.Authority.ActiveInputScopeId), cancellation);
            await Until(() => Frame().Authority.SnapshotSequence > frame.Authority.SnapshotSequence);
        }
        WidgetPresentationFrame Frame() => session.GetState("indexed-owned")?.LastGood ?? throw new InvalidOperationException("Missing frame");
        WidgetIndexedRow? Row() => Descendants(presenter).OfType<ListViewBase>().FirstOrDefault()?.Items[75] is IndexedItem<WidgetIndexedRow> slot ? slot.Value : null;
        string? Status() => Find(Frame().Snapshot.Root, "status")?.Text;
        int Calls() => int.Parse(Find(Frame().Snapshot.Root, "calls")!.Text![7..], System.Globalization.CultureInfo.InvariantCulture);
        string FocusId() => FocusManager.GetFocusedElement(presenter.XamlRoot) is DependencyObject focused ? AutomationProperties.GetAutomationId(focused) : "";
        async Task Until(Func<bool> condition, [CallerArgumentExpression(nameof(condition))] string? waiting = null)
        {
            var deadline = Environment.TickCount64 + 7000;
            while (!condition())
            {
                if (Environment.TickCount64 > deadline) throw new TimeoutException($"Activation probe waiting {waiting}; status={Status()}, focus={FocusId()}, calls={Calls()}");
                await Task.Delay(20, cancellation);
            }
        }
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            checks.Add(name);
        }
    }
    private static ViewNode? Find(ViewNode node, string id) => node.Id == id ? node : node.Children.Select(child => Find(child, id)).FirstOrDefault(found => found is not null);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); ++index)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
