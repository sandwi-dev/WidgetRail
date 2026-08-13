using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GameBarAlternative.AvaloniaPrototype.ViewModels;
using GameBarAlternative.AvaloniaPrototype.Presentation;

namespace GameBarAlternative.AvaloniaPrototype.Views;

public sealed partial class GameLauncherPage : UserControl, IPrototypeFocusPage, IPrototypeSemanticPage
{
    private int pendingFocusIndex = -1;

    public GameLauncherPage() : this(new GameLauncherViewModel(
        new Remote.RemoteWidgetProjection(new Remote.FakeRemoteWidgetEndpoint()),
        new AvaloniaUiScheduler()))
    {
    }

    internal GameLauncherPage(GameLauncherViewModel viewModel)
    {
        AvaloniaXamlLoader.Load(this);
        ViewModel = viewModel;
        DataContext = viewModel;
        ApplicationListControl.ContainerPrepared += ContainerPrepared;
        AttachedToVisualTree += PageAttachedToVisualTree;
        DetachedFromVisualTree += PageDetachedFromVisualTree;
    }

    public GameLauncherViewModel ViewModel { get; }

    public ListBox ApplicationListControl => this.FindControl<ListBox>("ApplicationList")!;

    public ScrollViewer? ApplicationScrollControl =>
        ApplicationListControl.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    public int RealizedContainerCount => ApplicationListControl.GetRealizedContainers().Count();

    public Control InitialFocus => this.FindControl<Button>("PrimaryAction")!;

    public bool TryMoveSemantic(Control focused, NavigationDirection direction)
    {
        if (direction is not (NavigationDirection.Up or NavigationDirection.Down) ||
            FindContainer(focused) is not { } container)
        {
            return false;
        }

        var current = ApplicationListControl.IndexFromContainer(container);
        var next = current + (direction == NavigationDirection.Down ? 1 : -1);
        return next >= 0 && next < ViewModel.State.Items.Count && FocusIndex(next);
    }

    public bool TryActivateSemantic(Control focused)
    {
        if (FindContainer(focused) is not { } container)
        {
            return false;
        }

        var index = ApplicationListControl.IndexFromContainer(container);
        if (index < 0 || index >= ViewModel.State.Items.Count)
        {
            return false;
        }

        ViewModel.SelectedItem = ViewModel.State.Items[index];
        _ = ViewModel.InvokeItemAsync(ViewModel.State.Items[index]);
        return true;
    }

    public bool TryRestoreSemanticFocus(string automationId)
    {
        if (!SemanticAutomationIdentity.TryDecodeLauncherItem(automationId, out var itemId))
        {
            return false;
        }

        return FocusItem(itemId);
    }

    public bool FocusIndex(int index)
    {
        if (index < 0 || index >= ViewModel.State.Items.Count)
        {
            return false;
        }

        pendingFocusIndex = index;
        ApplicationListControl.SelectedIndex = index;
        ApplicationListControl.ScrollIntoView(index);
        ApplicationListControl.UpdateLayout();
        if (TryFocusPrepared(index))
        {
            return true;
        }

        Dispatcher.UIThread.Post(() => TryFocusPrepared(index), DispatcherPriority.Input);
        return true;
    }

    public string? FocusedSemanticId(Control? focused)
    {
        var container = focused is null ? null : FindContainer(focused);
        var index = container is null ? -1 : ApplicationListControl.IndexFromContainer(container);
        return index >= 0 && index < ViewModel.State.Items.Count ? ViewModel.State.Items[index].Id.Value : null;
    }

    public string? FocusedAutomationId(Control? focused) =>
        focused is null || FindContainer(focused) is not { } container
            ? null
            : AutomationProperties.GetAutomationId(container);

    private bool FocusItem(Remote.RemoteWidgetItemId itemId)
    {
        var index = ViewModel.State.Items
            .Select((item, position) => (item, position))
            .FirstOrDefault(entry => entry.item.Id == itemId)
            .position;
        return index >= 0 && index < ViewModel.State.Items.Count &&
            ViewModel.State.Items[index].Id == itemId && FocusIndex(index);
    }

    private void ViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GameLauncherViewModel.State) || ViewModel.SelectedItem is not { } selected)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => FocusItem(selected.Id), DispatcherPriority.Normal);
    }

    private void PageAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) =>
        ViewModel.PropertyChanged += ViewModelPropertyChanged;

    private void PageDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) =>
        ViewModel.PropertyChanged -= ViewModelPropertyChanged;

    private void ContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is not ListBoxItem container || e.Index < 0 || e.Index >= ViewModel.State.Items.Count)
        {
            return;
        }

        var item = ViewModel.State.Items[e.Index];
        AutomationProperties.SetAutomationId(container, item.AutomationId);
        AutomationProperties.SetName(container, item.AutomationName);
        if (e.Index == pendingFocusIndex)
        {
            Dispatcher.UIThread.Post(() => TryFocusPrepared(e.Index), DispatcherPriority.Input);
        }
    }

    private bool TryFocusPrepared(int index)
    {
        if (ApplicationListControl.ContainerFromIndex(index) is not ListBoxItem container)
        {
            return false;
        }

        pendingFocusIndex = -1;
        return container.Focus(NavigationMethod.Directional);
    }

    private ListBoxItem? FindContainer(Control focused) =>
        focused as ListBoxItem ?? focused.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
}
