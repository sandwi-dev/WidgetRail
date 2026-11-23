using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeScopedSectionCacheAsync()
    {
        var page = new WidgetViewPresenter { Width = 500, Height = 240 };
        host.Children.Add(page);
        var calls = 0;
        page.DispatchActionAsync = _ => { ++calls; return Task.CompletedTask; };
        try
        {
            Render("one");
            await Wait(() => Find<Button>("Widget.action.one", page) is { IsLoaded: true, ActualWidth: > 0 });
            var original = Find<Button>("Widget.action.one", page)!;
            var foreground = Find<FrameworkElement>("Widget.scope.one", page)!;
            var surface = Find<WidgetPresentationSurface>("Widget.stable.background", page)!;
            var unloads = 0;
            foreground.Unloaded += (_, _) => ++unloads;
            Render("two");
            await Wait(() => Find<Button>("Widget.action.two", page) is { IsLoaded: true, ActualWidth: > 0 });
            Check(original.IsLoaded && !original.IsHitTestVisible && unloads == 0,
                "Switching scoped tabs parks the complete foreground without unloading its controls");
            original.Command!.Execute(null);
            Check(calls == 0, "The old tab cannot activate an action while its artwork is retained");
            Render("one", disabled: true);
            await Wait(() => !original.IsEnabled && original.IsHitTestVisible);
            Check(page.PageCacheHits == 1 && ReferenceEquals(surface, Find<WidgetPresentationSurface>("Widget.stable.background", page))
                && ReferenceEquals(original, Find<Button>("Widget.action.one", page)) && unloads == 0,
                "Rapid return reuses the foreground under its original background and applies changed action availability");
            Render("one");
            original.Command.Execute(null);
            await Wait(() => calls == 1);
            Render("two"); Render("one");
            await Wait(() => original.IsEnabled && original.IsHitTestVisible && page.OutgoingTransitionCount == 0);
            Check(calls == 1 && unloads == 0, "Interrupted section animations release their composition paint without replaying input");
        }
        finally { host.Children.Remove(page); await page.DisposeAsync(); }

        void Render(string key, bool disabled = false)
        {
            var scope = "scope." + key;
            var button = new ViewNode { Id = "action." + key, Kind = ViewNodeKind.Button, Text = key, ActionId = "open", IsDisabled = disabled };
            var section = new ViewNode { Id = "section." + key, Kind = ViewNodeKind.Stack,
                Transition = new("scoped.tabs", key, key == "one" ? 0 : 1), Children = new[] { button } };
            var branch = new ViewNode { Id = scope, InputScopeId = scope, Kind = ViewNodeKind.Stack, Children = new[] { section } };
            var root = new ViewNode { Id = "stable.background", Kind = ViewNodeKind.BackgroundSurface, Children = new[] { branch } };
            page.Apply(CreateFrame(root, new Dictionary<string, BridgeNodeRenderStyles>(), scope));
        }
    }

    private async Task NativeCachedDragAsync()
    {
        var page = new WidgetViewPresenter { Width = 500, Height = 120 };
        host.Children.Add(page);
        page.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
        var writes = 0; var resumedWrites = 0;
        page.DispatchActionAsync = _ => { ++writes; return Task.CompletedTask; };
        try
        {
            Render("a");
            await Wait(() => Find<Slider>("Widget.cache.drag", page) is { IsLoaded: true, ActualWidth: > 0 });
            var slider = Find<Slider>("Widget.cache.drag", page)!;
            await NativeContextProbeInput.HoldSliderDragAsync(slider, async () =>
            {
                await Task.Delay(80);
                Render("b");
                Check(!HasCapture(slider), "Caching a page cancels native slider/thumb pointer capture");
                Render("a"); await Task.Delay(100);
                Check(ReferenceEquals(slider, Find<Slider>("Widget.cache.drag", page)), "Cancelled drag returns to the same cached slider control");
                resumedWrites = writes;
            }, async () =>
            {
                await Task.Delay(100);
                Check(writes == resumedWrites, "Unpressed pointer movement cannot resume the retired cached-page drag");
            });
        }
        finally { host.Children.Remove(page); await page.DisposeAsync(); }
        void Render(string key)
        {
            var child = key == "a" ? new ViewNode { Id = "cache.drag", Kind = ViewNodeKind.Slider, Minimum = 0, Maximum = 100,
                Step = 1, Value = 50, ValueChangedActionId = "volume", AccessibilityLabel = "Volume", AccessibilityValue = "50%" }
                : new ViewNode { Id = "cache.drag.other", Kind = ViewNodeKind.Button, Text = "Other page", ActionId = "other" };
            var root = new ViewNode { Id = "cache.drag.root", Kind = ViewNodeKind.Stack, Transition = new("drag", key, 0, WidgetTransitionKind.Page, WidgetNavigationOperation.Forward),
                Children = (ViewNode[])[child] };
            page.Apply(CreateFrame(root, new Dictionary<string, BridgeNodeRenderStyles>()));
        }
        static bool HasCapture(DependencyObject root)
        {
            if (root is UIElement { PointerCaptures.Count: > 0 }) return true;
            for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); ++i)
                if (HasCapture(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i))) return true;
            return false;
        }
    }

    private async Task NativeNestedSectionReturnAsync()
    {
        var presenter = new WidgetViewPresenter { Width = 500, Height = 220 };
        host.Children.Add(presenter);
        presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
        try
        {
            await Render("games", "list");
            var original = Find<Button>("Widget.nested.list", presenter);
            await Render("details", "overview");
            await Render("details", "information");
            await Render("details", "achievements");
            await Render("details", "captures");
            Check(presenter.CachedPageCount == 2, "The parent and the previous tab fit the two-view budget");
            await Render("games", "list");
            Check(original is not null && ReferenceEquals(original, Find<Button>("Widget.nested.list", presenter)),
                "Returning after several detail tabs restores the original parent page");
        }
        finally { host.Children.Remove(presenter); await presenter.DisposeAsync(); }

        async Task Render(string page, string section)
        {
            var button = new ViewNode { Id = "nested." + section, Kind = ViewNodeKind.Button, Text = section, ActionId = "select" };
            var content = new ViewNode { Id = "nested.section", Kind = ViewNodeKind.Stack, Children = new[] { button },
                Transition = page == "details" ? new("nested.tabs", section, 0) : null };
            var root = new ViewNode { Id = "nested." + page, Kind = ViewNodeKind.Stack, Children = new[] { content },
                Transition = new("nested.pages", page, 0, WidgetTransitionKind.Page,
                    page == "details" ? WidgetNavigationOperation.Forward : WidgetNavigationOperation.Back) };
            presenter.Apply(CreateFrame(root, new Dictionary<string, BridgeNodeRenderStyles>()));
            await Wait(() => Find<Button>("Widget." + button.Id, presenter) is { IsLoaded: true, ActualWidth: > 0 });
        }
    }

    private async Task NativeSectionCacheAsync()
    {
        var page = new WidgetViewPresenter { Width = 500, Height = 220 };
        host.Children.Add(page);
        page.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
        try
        {
            Render("home", "Original");
            await Wait(() => Find<Button>("Widget.section.home.action", page)?.IsLoaded == true);
            var header = Find<Button>("Widget.section.header", page)!;
            var original = Find<Button>("Widget.section.home.action", page)!;
            Render("library", "Library");
            await Wait(() => Find<Button>("Widget.section.library.action", page)?.IsLoaded == true);
            Check(ReferenceEquals(header, Find<Button>("Widget.section.header", page)) && page.CachedPageCount == 1,
                "Section navigation keeps shared header controls active while parking only the replaced subtree");
            header.Focus(FocusState.Keyboard);
            Render("home", "Updated");
            await Task.Delay(80);
            Check(page.PageCacheHits == 1 && ReferenceEquals(original, Find<Button>("Widget.section.home.action", page)),
                "Keyed section return reuses the original destination controls");
            Check(original.Content is TextBlock { Text: "Updated" } && header.FocusState != FocusState.Unfocused,
                "Section cache applies current content without stealing explicitly retained header focus");
        }
        finally { host.Children.Remove(page); await page.DisposeAsync(); }

        void Render(string key, string text)
        {
            var section = new ViewNode { Id = "section." + key, Kind = ViewNodeKind.Stack,
                Transition = new("destinations", key, key == "home" ? 0 : 1),
                Children = (ViewNode[])[new() { Id = "section." + key + ".action", Kind = ViewNodeKind.Button, ActionId = "action", Text = text }] };
            var root = new ViewNode { Id = "section.root", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[
                new() { Id = "section.header", Kind = ViewNodeKind.Button, ActionId = "header", Text = "Navigation" }, section] };
            page.Apply(CreateFrame(root, new Dictionary<string, BridgeNodeRenderStyles>()));
        }
    }

    private async Task NativePageCacheBudgetAsync()
    {
        var pages = new List<WidgetViewPresenter>();
        try
        {
            for (var i = 0; i < 4; ++i)
            {
                var page = new WidgetViewPresenter { Width = 220, Height = 90 };
                pages.Add(page); host.Children.Add(page);
                page.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
                Render(page, "a"); await Wait(() => page.IsLoaded && page.ActualHeight > 0);
                Render(page, "b"); await Task.Delay(30);
            }
            Check(pages.Sum(page => page.CachedPageCount) == 3 && pages[0].CachedPageCount == 0,
                "Host-wide cache budget evicts the least recently retained destination across presenters");
        }
        finally { foreach (var page in pages) { host.Children.Remove(page); await page.DisposeAsync(); } }
        Check(pages.All(page => page.CachedPageCount == 0), "Global cache membership is released when owners retire");

        void Render(WidgetViewPresenter page, string key)
        {
            var root = new ViewNode { Id = "budget.root", Kind = ViewNodeKind.Stack, Transition = new("budget", key, 0, WidgetTransitionKind.Page, WidgetNavigationOperation.Forward),
                Children = (ViewNode[])[new() { Id = "budget." + key, Kind = ViewNodeKind.Button, Text = key, ActionId = "action" }] };
            page.Apply(CreateFrame(root, new Dictionary<string, BridgeNodeRenderStyles>()));
        }
    }

    private async Task NativePageCacheAsync()
    {
        var page = new WidgetViewPresenter { Width = 500, Height = 300 };
        host.Children.Add(page);
        var invoked = 0;
        Exception? failed = null;
        page.DispatchActionAsync = _ => { ++invoked; return Task.CompletedTask; };
        page.Failed = error => failed = error;
        page.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
        try
        {
            Render("a");
            await Wait(() => page.IsLoaded && Find<Button>("Widget.cache.a.12", page)?.IsLoaded == true);
            var original = Find<Button>("Widget.cache.a.12", page)!;
            var scroll = Find<ScrollViewer>("Widget.cache.a.scroll", page)!;
            await Wait(() => scroll.ViewportHeight > 0 && Find<Button>("Widget.cache.a.0", page) is { FocusState: not FocusState.Unfocused });
            original.Focus(FocusState.Keyboard);
            await Wait(() => scroll.VerticalOffset > 300 && original.FocusState != FocusState.Unfocused);
            await Task.Delay(300);
            var offset = scroll.VerticalOffset;
            Render("b");
            await Wait(() => Find<Button>("Widget.cache.b.0", page)?.IsLoaded == true);
            Check(page.CachedPageCount == 1 && original.IsLoaded && !original.IsHitTestVisible,
                "Hidden page keeps its native tree mounted while removing input");
            original.Command!.Execute(null); await Task.Delay(50);
            Check(invoked == 0, "Cached commands cannot dispatch through the current page");
            Render("a", WidgetNavigationOperation.Back);
            await Wait(() => original.IsHitTestVisible && original.FocusState != FocusState.Unfocused);
            Check(ReferenceEquals(original, Find<Button>("Widget.cache.a.12", page)) && page.PageCacheHits == 1,
                "Back restores the original native page and remembered keyboard focus");
            try { await Wait(() => Math.Abs(scroll.VerticalOffset - offset) < 1); }
            catch (Exception error) { throw new InvalidOperationException($"Scroll before={offset} after={scroll.VerticalOffset}; " + System.Text.Json.JsonSerializer.Serialize(page.NavigationMemoryForValidation()), error); }
            Check(Math.Abs(scroll.VerticalOffset - offset) < 1, $"Cache hit preserves scroll position without resetting the viewport (before={offset}, after={scroll.VerticalOffset})");
            original.Command.Execute(null); await Wait(() => invoked == 1);
            Check(invoked == 1, "Restored page uses current action authority exactly once");
            Render("b"); await Wait(() => Find<Button>("Widget.cache.b.0", page)?.IsLoaded == true);
            Render("c"); await Wait(() => Find<Button>("Widget.cache.c.0", page)?.IsLoaded == true);
            Check(page.CachedPageCount == 2, "Widget retains two previous destinations within the host budget");
            Render("d"); await Wait(() => Find<Button>("Widget.cache.d.0", page)?.IsLoaded == true);
            Render("a", WidgetNavigationOperation.Back);
            await Wait(() => Find<Button>("Widget.cache.a.12", page) is { IsLoaded: true, FocusState: not FocusState.Unfocused });
            Check(!ReferenceEquals(original, Find<Button>("Widget.cache.a.12", page)), "Two-view budget evicts the oldest native tree");
            await Wait(() => Find<ScrollViewer>("Widget.cache.a.scroll", page)!.VerticalOffset > 0);
            Check(Math.Abs(Find<ScrollViewer>("Widget.cache.a.scroll", page)!.VerticalOffset - offset) < 1,
                "Eviction retains existing focus and scroll memory fallback");
            var replacement = Find<Button>("Widget.cache.a.12", page);
            Render("b"); Render("a", WidgetNavigationOperation.Back, disabled: true);
            await Wait(() => Find<Button>("Widget.cache.a.12", page) is { IsEnabled: false });
            Check(ReferenceEquals(replacement, Find<Button>("Widget.cache.a.12", page)) && replacement!.FocusState == FocusState.Unfocused,
                "Cache reconciliation applies latest disabled state and rejects invalid remembered focus");
            Render("b"); Render("a", WidgetNavigationOperation.Back, differentScope: true);
            Check(!ReferenceEquals(replacement, Find<Button>("Widget.cache.a.12", page)), "A different input scope cannot reuse cached authority");
            Render("b");
            Render("a", newOwner: true);
            Check(page.CachedPageCount == 0, "Runtime ownership changes clear all cached native destinations");
            await page.DisposeAsync();
            Check(page.CachedPageCount == 0, "Presenter retirement releases its cached page");
            if (failed is not null) throw failed;
        }
        finally { host.Children.Remove(page); await page.DisposeAsync(); }

        void Render(string key, WidgetNavigationOperation operation = WidgetNavigationOperation.Forward, bool disabled = false, bool differentScope = false, bool newOwner = false)
        {
            var root = new ViewNode { Id = "cache.root", Kind = ViewNodeKind.Stack,
                InputScopeId = key + (differentScope ? "-new" : ""), Transition = new("cache.pages", key, 0, WidgetTransitionKind.Page, operation),
                Children = (ViewNode[])[new() { Id = "cache." + key + ".scroll", Kind = ViewNodeKind.Scroll, ScrollAxis = ScrollAxis.Vertical, Children = Enumerable.Range(0, 20).Select(index =>
                    new ViewNode { Id = "cache." + key + "." + index, Kind = ViewNodeKind.Button, Text = "Item " + index, ActionId = "activate",
                        IsDisabled = disabled && index == 12 ? true : null }).ToArray() }] };
            var styles = new Dictionary<string, BridgeNodeRenderStyles> { [root.Id] = Compute("stack { height: 280px; }", root.Id, "stack"),
                [root.Children[0].Id] = Compute("scroll { height: 270px; }", root.Children[0].Id, "scroll") };
            var frame = CreateFrame(root, styles, root.InputScopeId);
            if (newOwner) frame = new(frame.Authority with { RuntimeGeneration = "replacement" },
                frame.Descriptor with { RuntimeGeneration = "replacement" }, frame.Snapshot, frame.RenderStyles);
            page.Apply(frame);
            page.Enter();
        }
    }
}
