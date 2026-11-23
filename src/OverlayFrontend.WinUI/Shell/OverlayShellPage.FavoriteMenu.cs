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
            var row = new MenuFlyoutItem();
            var glyph = direction switch { TrayDirection.Up => "\uE74A", TrayDirection.Right => "\uE72A",
                TrayDirection.Down => "\uE74B", _ => "\uE72B" };
            row.Icon = new FontIcon { Glyph = glyph, FontFamily = new("Segoe Fluent Icons") };
            AutomationProperties.SetAutomationId(row, "Favorite.Menu." + direction);
            Favorite? shown = null;
            TrayActionBinding? binding = null;
            void Update()
            {
                binding = preferences.TrayActions.Get(direction);
                shown = ResolveFavorite(direction);
                var title = shown?.Label ?? "Assign action…";
                var detail = binding is null ? direction.ToString() :
                    favoriteProviders.GetValueOrDefault(binding.Provider)?.Name ?? "Widget unavailable";
                var work = runningFavorites.GetValueOrDefault(direction);
                var inProgress = work is not null && work.Binding == binding;
                if (inProgress) { title = work!.Label; detail = "In progress"; }
                else if (shown?.Reason is { } reason)
                    detail = reason;
                row.Text = title;
                row.KeyboardAcceleratorTextOverride = detail;
                ToolTipService.SetToolTip(row, inProgress ? null : shown?.Reason);
                row.IsEnabled = !inProgress && (binding is null || shown?.Command is not null);
                AutomationProperties.SetName(row, $"{title}, {direction}, {detail}");
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
        var edit = new MenuFlyoutItem { Text = "Edit assignments…" };
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
