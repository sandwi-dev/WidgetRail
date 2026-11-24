using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private FavoriteButtonFeedback? desktopFavoriteFeedback;
    private FavoriteButtonFeedback DesktopFeedback => desktopFavoriteFeedback ??= new(FavoriteMenuButton);
    private WidgetMotionOptions FavoriteMotion => WidgetMotionOptions.From(Appearance, systemUi.AnimationsEnabled);
    private MenuFlyout? favoriteFlyout;
    private Action? refreshFavoriteMenu;
    private bool favoriteProgressVisible;

    private async Task RevealFavoriteProgressAsync(TrayDirection direction, FavoriteInvocation invocation)
    {
        try { await Task.Delay(300, lifetime.Token); }
        catch (OperationCanceledException) { return; }
        if (retired || runningFavorites.GetValueOrDefault(direction) != invocation) return;
        invocation.ShowProgress = true;
        RefreshFavoriteProgress();
    }

    private void RefreshFavoriteProgress()
    {
        refreshFavoriteMenu?.Invoke();
        if (!favoriteProgressVisible && !runningFavorites.Values.Any(work => work.ShowProgress)) return;
        var palette = ShellChromePalette.Resolve(ShellPalette, Appearance);
        var motion = FavoriteMotion;
        var any = false;
        foreach (var direction in Directions)
        {
            var busy = runningFavorites.TryGetValue(direction, out var work) && work.ShowProgress &&
                work.Binding == preferences.TrayActions.Get(direction);
            any |= busy;
            radialView?.SetFavoriteProgress(direction, busy && RadialOpen && foreground && !reordering, palette, motion);
        }
        favoriteProgressVisible = any;
        if (any || desktopFavoriteFeedback is not null)
            DesktopFeedback.SetBusy(any && visible && foreground && FavoriteMenuButton.Visibility == Visibility.Visible, palette, motion);
    }

    private void FavoriteMenuClicked(object sender, RoutedEventArgs args)
    {
        if (!FavoriteMenuButton.IsEnabled || switching || !foreground || owner is null || reordering || PinnedInteractionRequested) return;
        if (favoriteFlyout is not null) { favoriteFlyout.Hide(); return; }
        var selection = selectionVersion;
        var session = visibleSince;
        bool Current() => !retired && visible && foreground && selectionVersion == selection && visibleSince == session &&
            !RadialOpen && !HostChoiceActive && !switching && !PinnedInputActive;
        if (!Current()) return;
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.Top };
        var rows = new List<Action>();
        foreach (var direction in Directions)
        {
            var icon = new FontIcon { FontFamily = new("Segoe Fluent Icons") };
            var row = new MenuFlyoutItem
            {
                Icon = icon,
                KeyboardAcceleratorTextOverride = direction switch
                {
                    TrayDirection.Up => "↑", TrayDirection.Right => "→",
                    TrayDirection.Down => "↓", TrayDirection.Left => "←",
                    _ => throw new ArgumentOutOfRangeException(nameof(direction))
                }
            };
            AutomationProperties.SetAutomationId(row, "Favorite.Menu." + direction);
            Favorite? shown = null;
            TrayActionBinding? binding = null;
            void Update()
            {
                binding = preferences.TrayActions.Get(direction);
                shown = ResolveFavorite(direction);
                var title = shown?.Label ?? "Assign action…";
                var provider = binding is null ? null : favoriteProviders.GetValueOrDefault(binding.Provider);
                var providerName = binding is not null && provider is not null && provider.PublisherId == binding.Publisher && provider.PackageId == binding.Package
                    ? provider.Name : binding?.Provider;
                var work = runningFavorites.GetValueOrDefault(direction);
                var inProgress = work is not null && work.Binding == binding;
                if (inProgress) title = work!.Label;
                var reason = inProgress ? "In progress" : shown?.Reason;
                icon.Glyph = (inProgress, reason, binding) switch
                {
                    (true, _, _) => "\uE823",     // Clock
                    (_, not null, _) => "\uE733", // Blocked
                    (_, _, null) => "\uE710",     // Add
                    _ => "\uE945"                 // Quick actions
                };
                row.Text = title;
                var detail = string.Join(", ", new[] { providerName, reason }.Where(value => !string.IsNullOrEmpty(value)));
                NativePopupTheme.SetToolTip(row, detail);
                row.IsEnabled = !inProgress && (binding is null || shown?.Command is not null);
                AutomationProperties.SetName(row, detail.Length == 0 ? $"{title}, {direction}" : $"{title}, {direction}, {detail}");
            }
            rows.Add(Update);
            Update();
            row.Click += async (_, _) =>
            {
                var selected = shown;
                var selectedBinding = binding;
                if (!Current() || selectedBinding != preferences.TrayActions.Get(direction)) return;
                if (selectedBinding is null) { await AssignFavoriteAsync(direction); return; }
                if (interactive)
                {
                    SetInteractive(false);
                    await ReconcileLifecycleAsync(restore: false);
                }
                if (Current() && selectedBinding == preferences.TrayActions.Get(direction))
                    await InvokeFavoriteAsync(direction, selected, ControllerInputOrigin.AccessibilityAutomation);
            };
            menu.Items.Add(row);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        var edit = new MenuFlyoutItem { Text = "Edit assignments…", Icon = new SymbolIcon(Symbol.Edit) };
        AutomationProperties.SetAutomationId(edit, "Favorite.Menu.Edit");
        edit.Click += async (_, _) => { if (Current()) await EditFavoriteSlotsAsync(); };
        menu.Items.Add(edit);
        Input.GamepadKeyBoundary.ObserveFlyout(menu, FavoriteMenuButton);
        var theme = NativePopupTheme.Menu(menu, this);
        favoriteFlyout = menu;
        refreshFavoriteMenu = () => { foreach (var update in rows) update(); };
        menu.Closed += (_, _) =>
        {
            theme?.Dispose();
            if (ReferenceEquals(menu, favoriteFlyout)) { favoriteFlyout = null; refreshFavoriteMenu = null; }
        };
        menu.ShowAt(FavoriteMenuButton);
    }
}
