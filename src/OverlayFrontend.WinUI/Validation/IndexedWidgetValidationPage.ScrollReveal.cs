using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class IndexedWidgetValidationPage
{
    private async Task ValidateScrollRevealAsync(string path)
    {
        var checks = new List<string>();
        try
        {
            await Wait(() => View() is { IsLoaded: true } && Parent() is { IsLoaded: true });
            Parent().Focus(FocusState.Keyboard);
            await Send("reveal-exact");
            await Wait(() => Visible(75));
            Check(FocusedParent(), "Deep occurrence reveal retains toolbar focus");
            var scroll = Scroll();
            Check(scroll.VerticalOffset > 0 && View()!.SelectedIndex == -1, "Reveal scrolls without selecting an item");

            scroll.ChangeView(null, 0, null, true);
            await Wait(() => scroll.VerticalOffset < 1);
            await Send("reveal-republish");
            await Task.Delay(350, lifetime.Token);
            Check(scroll.VerticalOffset < 1 && FocusedParent(), "Content updates do not replay a consumed reveal");

            await Send("reveal-wrong");
            await Task.Delay(450, lifetime.Token);
            Check(Scroll().VerticalOffset < 1 && FocusedParent(), "Wrong occurrence key cannot scroll to an unrelated item");

            await Send("reveal-delayed");
            await Task.Delay(100, lifetime.Token);
            Check(Scroll().VerticalOffset < 1 && FocusedParent(), "Missing payload does not scroll to an unverified placeholder");
            presenter.ScrollBy(0, 32);
            await Send("activation-release");
            await Task.Delay(450, lifetime.Token);
            Check(Scroll().VerticalOffset < 100 && FocusedParent(), "User scrolling cancels late scroll-only delivery");

            await Send("reveal-clear");
            Scroll().ChangeView(null, 0, null, true);
            await Send("reveal-delayed");
            await Send("reveal-replace");
            await Send("activation-release");
            await Task.Delay(450, lifetime.Token);
            Check(Scroll().VerticalOffset < 1 && FocusedParent(), "Query replacement retires a pending occurrence reveal");

            await Send("reveal-delayed");
            await presenter.SetPresentationActiveAsync(false);
            await Send("activation-release");
            await presenter.SetPresentationActiveAsync(true);
            await Task.Delay(450, lifetime.Token);
            Check(Scroll().VerticalOffset < 1, "Hide and reopen do not replay a cancelled queue-entry reveal");

            await Send("reveal-exact");
            await Wait(() => Visible(75));
            Check(FocusedParent(), "A fresh navigation request can reveal after cancellation");
            Check(failure is null, "Native presenter reported no errors");
            Write(true, null);
        }
        catch (Exception error) { Write(false, error.ToString()); }

        Button Parent() => Descendants(presenter).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "Widget.parent");
        bool FocusedParent() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Parent());
        ScrollViewer Scroll() => Descendants(View()!).OfType<ScrollViewer>().First();
        bool Visible(int index)
        {
            if (View()?.ContainerFromIndex(index) is not FrameworkElement { IsLoaded: true } row ||
                View()!.Items[index] is not IndexedItem<WidgetIndexedRow> { Value.Lease.IsCurrent: true }) return false;
            var scroll = Scroll();
            var bounds = row.TransformToVisual(scroll).TransformBounds(new(0, 0, row.ActualWidth, row.ActualHeight));
            return bounds.Y >= -1 && bounds.Bottom <= scroll.ViewportHeight + 1;
        }
        async Task Send(string action)
        {
            var frame = session!.GetState("indexed-owned")!.LastGood!;
            await session.SendActionAsync(frame.Authority, new(action, "root", InputScopeId: "root"), lifetime.Token);
            await Wait(() => Descendants(presenter).OfType<TextBlock>().Any(t => AutomationProperties.GetAutomationId(t) == "Widget.status" && t.Text == action));
        }
        async Task Wait(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 7000;
            while (!condition())
            {
                if (Environment.TickCount64 > deadline) throw new TimeoutException("Indexed reveal did not settle: " + presenter.FocusDiagnostics());
                await Task.Delay(20, lifetime.Token);
            }
        }
        void Check(bool passed, string name)
        { if (!passed) throw new InvalidOperationException(name); checks.Add(name); }
        void Write(bool passed, string? error)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { passed, checks, error }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
