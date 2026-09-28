using System.Text.Json;
using System.Runtime.CompilerServices;
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

internal static class IndexedContextMenuValidation
{
    internal sealed record Result(string ResultCode, IReadOnlyList<string> Checks, string? Error);
    internal static async Task<Result> RunAsync(WidgetViewPresenter presenter, PresentationSession session, CancellationToken cancellation)
    {
        List<string> checks = [];
        Result result;
        try
        {
            await Prepare();
            var view = Descendants(presenter).OfType<ListViewBase>().Single();
            var container = view.ContainerFromIndex(75);
            var scroll = Descendants(view).OfType<ScrollViewer>().First();
            var offset = scroll.VerticalOffset;
            Check(!await presenter.HandleControllerButtonAsync(ControllerButton.View), "unbound indexed input returns false after exact worker admission");
            var before = Calls();
            await Open();
            Check(FocusId().EndsWith(".Context.context-play", StringComparison.Ordinal), "deep row menu opens using authored X instead of collection shortcut");
            presenter.MoveFocus(FocusNavigationDirection.Down);
            Check(FocusId().EndsWith(".Context.context-remove", StringComparison.Ordinal), "indexed menu skips disabled option");
            await presenter.HandleControllerButtonAsync(ControllerButton.Y);
            Check(Calls() == before, "indexed popup consumes underlying row shortcut");
            await presenter.HandleControllerButtonAsync(ControllerButton.A);
            await Until(() => Status() == "row:400:75:context-remove");
            await Until(() => FocusId() == "Widget.items.Item.75");
            Check(Calls() == before + 1, "worker admits exact indexed owner key lease and action once");
            Check(ReferenceEquals(container, view.ContainerFromIndex(75)) && Math.Abs(offset - scroll.VerticalOffset) < 1,
                "native menu preserves deep container and scroll anchor");
            await Open();
            var old = Row()!;
            await Send("context-refresh");
            await Until(() => !presenter.HasTransientControl && Row()?.Lease.IsCurrent == true && !ReferenceEquals(Row(), old));
            Check(!old.Lease.IsCurrent, "content replacement revokes popup and old lease");
            await Open();
            await Send("context-replace");
            await Until(() => !presenter.HasTransientControl);
            Check(true, "query replacement revokes popup without replay");
            await Prepare();
            await Open();
            await Send("activation-modal");
            await Until(() => !presenter.HasTransientControl && FocusId() == "Widget.modal-play");
            Check(true, "modal scope closes parent indexed popup and focuses modal");
            await presenter.HandleControllerButtonAsync(ControllerButton.B);
            await Until(() => Frame().Authority.ActiveInputScopeId == "root" && Row()?.Lease.IsCurrent == true);
            await Prepare(); await Open();
            before = Calls();
            presenter.DismissTransientControl();
            await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
            Check(Calls() == before && !presenter.HasTransientControl, "host dismissal never invokes retained indexed action");
            result = new("passed", checks, null);
        }
        catch (Exception error) { result = new("failed", checks, error.ToString()); }
        var output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
        Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "indexed-context-menu-result.json"), JsonSerializer.Serialize(result));
        return result;
        async Task Prepare()
        { await Send("context-mode"); await Until(() => FocusId() == "Widget.items.Item.75" && Row()?.Lease.IsCurrent == true); }
        async Task Open()
        {
            await Task.Delay(175, cancellation);
            Check(await presenter.HandleControllerButtonAsync(ControllerButton.X), "indexed X consumed");
            await Until(() => presenter.HasTransientControl && FocusId().Contains(".Context.", StringComparison.Ordinal));
        }
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
            while (!condition()) { if (Environment.TickCount64 > deadline) throw new TimeoutException($"Context probe waiting {waiting}; status={Status()}, focus={FocusId()}, calls={Calls()}"); await Task.Delay(20, cancellation); }
        }
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
    }
    private static ViewNode? Find(ViewNode node, string id) => node.Id == id ? node : node.Children.Select(child => Find(child, id)).FirstOrDefault(found => found is not null);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root; for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); ++index) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child; }
}
