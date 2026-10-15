using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Controller navigation over native menus. Commands and ownership stay with the caller.</summary>
internal sealed class NativeMenuNavigator : IDisposable
{
    private readonly MenuFlyout menu;
    private readonly Func<bool> isCurrent;
    private readonly Dictionary<MenuFlyoutItemBase, MenuFlyoutSubItem?> parents = [];
    private MenuFlyoutItemBase? remembered;
    private bool disposed;

    internal NativeMenuNavigator(MenuFlyout menu, Func<bool> isCurrent)
    {
        this.menu = menu; this.isCurrent = isCurrent;
        Simplify(menu.Items);
        Add(menu.Items, null);
        void Add(IEnumerable<MenuFlyoutItemBase> items, MenuFlyoutSubItem? parent)
        {
            foreach (var item in items)
            {
                if (item is MenuFlyoutSeparator) continue;
                parents.Add(item, parent);
                item.GotFocus += GotFocus;
                item.Loaded += Loaded;
                if (item is MenuFlyoutSubItem sub) Add(sub.Items, sub);
            }
        }
    }
    // Resolve structure before theme tracking and native presentation. Keep the
    // original action object so identity, command, shortcuts and help survive.
    private static void Simplify(IList<MenuFlyoutItemBase> items)
    {
        for (var index = items.Count - 1; index >= 0; index--)
        {
            if (items[index] is not MenuFlyoutSubItem { Visibility: Visibility.Visible } group) continue;
            Simplify(group.Items);
            var choices = group.Items.Where(item => item is not MenuFlyoutSeparator && item.Visibility == Visibility.Visible).ToArray();
            if (choices.Length == 0) { items.RemoveAt(index); continue; }
            if (choices.Length != 1) continue;
            var action = choices[0];
            group.Items.Remove(action);
            if (!group.IsEnabled) action.IsEnabled = false;
            items[index] = action;
        }
    }
    private void GotFocus(object sender, RoutedEventArgs args) => remembered = (MenuFlyoutItemBase)sender;
    private void Loaded(object sender, RoutedEventArgs args)
    {
        Input.GamepadKeyBoundary.ObserveOpenPopups((FrameworkElement)sender);
        if (pendingFocus is { } target && ReferenceEquals(sender, target)) TryFocus(target);
    }
    private MenuFlyoutItemBase? pendingFocus;
    private bool Current => !disposed && isCurrent();
    private MenuFlyoutItemBase? Focused()
    {
        if (!Current) return null;
        var root = parents.Keys.FirstOrDefault(item => item.IsLoaded)?.XamlRoot;
        var focused = root is null ? null : FocusManager.GetFocusedElement(root);
        return focused is MenuFlyoutItemBase item && parents.ContainsKey(item) ? item : remembered;
    }
    internal void FocusFirst() => Focus(menu.Items.FirstOrDefault(Available));
    private static bool Available(MenuFlyoutItemBase item) => item is not MenuFlyoutSeparator && item.IsEnabled && item.Visibility == Visibility.Visible;
    private void Focus(MenuFlyoutItemBase? item)
    {
        if (!Current || item is null) return;
        pendingFocus = item;
        TryFocus(item);
    }
    private void TryFocus(MenuFlyoutItemBase item)
    {
        if (!Current || !item.IsLoaded || !Available(item)) return;
        if (item.Focus(FocusState.Keyboard))
        {
            pendingFocus = null;
            item.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
            // Newly opened native submenus can focus before their first layout.
            // Reconcile only the latest focus after queued layout work completes.
            item.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (Current && item.IsLoaded && ReferenceEquals(Focused(), item))
                    item.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
            });
        }
    }
    internal bool Back()
    {
        if (!Current) return false;
        // Also handle mouse-opened submenus whose parent still has focus.
        var sub = parents.Keys.OfType<MenuFlyoutSubItem>().LastOrDefault(item => Expanded(item));
        if (sub is null) return false;
        pendingFocus = null;
        Expansion(sub)?.Collapse();
        Focus(sub);
        return true;
    }
    internal void Move(FocusNavigationDirection direction)
    {
        if (!Current) return;
        if (direction == FocusNavigationDirection.Left) { Back(); return; }
        if (direction == FocusNavigationDirection.Right) { if (Focused() is MenuFlyoutSubItem sub) Open(sub); return; }
        if (direction is not (FocusNavigationDirection.Up or FocusNavigationDirection.Down)) return;
        var focused = Focused();
        var parent = focused is not null ? parents[focused] : null;
        var siblings = (parent?.Items ?? menu.Items).Where(Available).ToArray();
        if (siblings.Length == 0) return;
        var index = Array.IndexOf(siblings, focused);
        var next = (index + (direction == FocusNavigationDirection.Up ? -1 : 1) + siblings.Length) % siblings.Length;
        if (focused is MenuFlyoutSubItem opened && Expanded(opened)) Expansion(opened)?.Collapse();
        Focus(siblings[next]);
    }
    internal async Task ActivateAsync()
    {
        if (Focused() is not { } item || !Available(item)) return;
        if (item is MenuFlyoutSubItem sub) { Open(sub); return; }
        if (item is MenuFlyoutItem { Command: { } command } action && command.CanExecute(action.CommandParameter))
        {
            if (command is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand asyncCommand) await asyncCommand.ExecuteAsync(action.CommandParameter);
            else command.Execute(action.CommandParameter);
        }
    }
    private void Open(MenuFlyoutSubItem sub)
    {
        if (!Current) return;
        Expansion(sub)?.Expand();
        Focus(sub.Items.FirstOrDefault(Available));
    }
    private static IExpandCollapseProvider? Expansion(MenuFlyoutSubItem sub) =>
        FrameworkElementAutomationPeer.CreatePeerForElement(sub)?.GetPattern(PatternInterface.ExpandCollapse) as IExpandCollapseProvider;
    private static bool Expanded(MenuFlyoutSubItem sub) => Expansion(sub)?.ExpandCollapseState == ExpandCollapseState.Expanded;
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; pendingFocus = null;
        foreach (var item in parents.Keys) { item.GotFocus -= GotFocus; item.Loaded -= Loaded; }
        parents.Clear(); remembered = null;
    }
}
