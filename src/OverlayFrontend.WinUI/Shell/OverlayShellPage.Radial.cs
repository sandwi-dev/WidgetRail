using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private readonly RadialChooserPolicy radialInput = new();
    private RadialChooserView? radialView;
    private bool radialRequested, radialShowing;
    private string? radialSelection;
    private int radialPage;
    private bool RadialOpen => radialRequested && Appearance.WidgetSwitcher == WidgetSwitcherLayout.Radial &&
        visible && !retired && !interactive && !IsMediaFullscreen && !PinnedInputActive && catalogItems.Count != 0;

    private void InitializeRadialChooser()
    {
        radialView = new(ResolveTrayIconAsync) { Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 0, 0, 12) };
        ProductionLayout.Children.Add(radialView);
        WidgetHost.GettingFocus += (_, args) =>
        {
            if (RadialOpen && radialView.IsLoaded && radialSelection is { } selected)
                args.TrySetNewFocusedElement(radialView.ContextAnchor(selected));
        };
        radialView.Selected = PreviewRadial;
        radialView.Activated = item => { if (reordering) { FinishTrayReorder(); return; } radialSelection = item.Id; _ = SelectAsync(item.Id); };
        radialView.ContextRequestedFor = item => { radialSelection = item.Id; RefreshRadialChooser(); _ = ShowTrayMenuAsync(item); };
        radialView.PageRequested = BrowseRadialPage;
        radialView.DirectionRequested = StepRadialSelection;
        radialView.BackRequested = async () =>
        {
            await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
            await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
        };
    }
    private void PrepareRadialBackEntry()
    {
        if (Appearance.WidgetSwitcher != WidgetSwitcherLayout.Radial || catalogItems.Count == 0) return;
        radialRequested = true;
        radialSelection = activeWidget ?? requestedWidget ?? catalogItems[0].Id;
        radialPage = Math.Max(0, catalogItems.ToList().FindIndex(item => item.Id == radialSelection)) / 8;
        radialInput.RebasePage();
    }
    private void RefreshRadialChooser()
    {
        if (radialView is null) return;
        var showing = RadialOpen;
        if (interactive || Appearance.WidgetSwitcher != WidgetSwitcherLayout.Radial) radialRequested = false;
        if (showing)
        {
            if (!catalogItems.Any(item => item.Id == radialSelection)) radialSelection =
                catalogItems.FirstOrDefault(item => item.Id == activeWidget)?.Id ?? catalogItems[0].Id;
            radialPage = Math.Clamp(radialPage, 0, RadialChooserPolicy.PageCount(catalogItems.Count) - 1);
            radialView.HorizontalAlignment = WidgetSurface.HorizontalAlignment;
            radialView.Width = radialView.Height = Math.Max(1, Math.Min(400, Math.Min(Math.Max(1, shellViewport.Width - 48 / Appearance.InterfaceScale), Math.Max(1, shellViewport.Height - ProductionShellGeometry.ReservedHeight - 38 / Appearance.InterfaceScale)) - 24));
            radialView.Update(catalogItems, radialSelection, radialPage, ShellPalette, Appearance, systemUi.AnimationsEnabled);
        }
        if (!showing) radialView.Close();
        radialView.Visibility = showing ? Visibility.Visible : Visibility.Collapsed;
        radialView.IsHitTestVisible = !PinnedAdjustmentActive;
        RailControls.Opacity = showing ? 0 : 1;
        RailControls.IsHitTestVisible = !showing && !PinnedAdjustmentActive;
        Tray.IsEnabled = !showing && !IsMediaFullscreen;
        UpdateRailOverflow();
        // The wheel overlays retained content and owns native focus while open.
        WidgetHost.IsHitTestVisible = !showing && !PinnedAdjustmentActive;

        if (showing && !radialShowing)
            DispatcherQueue.TryEnqueue(() => { if (RadialOpen) { radialView.Open(WidgetMotionOptions.From(Appearance, systemUi.AnimationsEnabled)); if (!PinnedAdjustmentActive) radialView.FocusSelected(); } });
        radialShowing = showing;
        PositionOpeningIndicator();
    }
    private BridgeWidgetDescriptor? RadialSelectedWidget() => RadialOpen ? catalogItems.FirstOrDefault(item => item.Id == radialSelection) : null;
    private void PreviewRadial(BridgeWidgetDescriptor item)
    {
        if (!RadialOpen || reordering || item.Id == radialSelection) return;
        radialSelection = item.Id;
        RefreshRadialChooser();
        _ = SelectAsync(item.Id, enterWidget: false);
    }
    private void BrowseRadialPage(int direction)
    {
        if (!RadialOpen || reordering || trayMenu is not null) return;
        radialPage = RadialChooserPolicy.NextPage(catalogItems.Count, radialPage, direction);
        radialInput.RebasePage();
        RefreshRadialChooser();
        radialView?.FocusPaging();
    }
    private void StepRadialSelection(FocusNavigationDirection direction)
    {
        if (!RadialOpen) return;
        if (trayMenu is not null) { NavigateTray(direction); return; }
        var delta = direction is FocusNavigationDirection.Up or FocusNavigationDirection.Left ? -1 : 1;
        if (reordering) { MoveTrayWidget(delta); return; }
        var selected = catalogItems.ToList().FindIndex(item => item.Id == radialSelection);
        var index = RadialChooserPolicy.StepSelection(catalogItems.Count, radialPage, selected, delta);
        if (index < 0) return;
        var item = catalogItems[index]; PreviewRadial(item); radialView?.ButtonFor(item.Id)?.Focus(FocusState.Keyboard);
    }
    private bool ReceiveRadialNavigation(ControllerFrame frame)
    {
        var owns = RadialOpen && trayMenu is null;
        radialInput.UpdateOwner(owns, frame.State.RightThumbX, frame.State.RightThumbY);
        if (!owns) return false;
        if (!reordering && radialInput.SelectSector(frame.State.LeftThumbX, frame.State.LeftThumbY) is { } sector &&
            radialPage * 8 + sector < catalogItems.Count)
        {
            var item = catalogItems[radialPage * 8 + sector];
            if (item.Id != radialSelection) { PreviewRadial(item); radialView?.ButtonFor(item.Id)?.Focus(FocusState.Keyboard); }
        }
        var direction = frame.DpadNavigation.Phase != NavigationPhase.None ? frame.DpadNavigation :
            reordering ? frame.StickNavigation : default;
        if (direction.Phase != NavigationPhase.None)
            StepRadialSelection(direction.Direction is NavigationDirection.Up or NavigationDirection.Left ? FocusNavigationDirection.Left : FocusNavigationDirection.Right);
        if (!reordering && radialInput.PageStep(frame.State.RightThumbX, Environment.TickCount64) is var step && step != 0) BrowseRadialPage(step);
        return true;
    }
    private bool FocusRadialWidget(object item)
    {
        if (!RadialOpen || item is not BridgeWidgetDescriptor descriptor) return false;
        if (reordering) radialPage = Math.Max(0, catalogItems.IndexOf(descriptor)) / 8;
        radialSelection = descriptor.Id;
        RefreshRadialChooser();
        if (!radialView!.FocusSelected()) radialView.FocusPaging();
        return true;
    }
    private IReadOnlyList<ControllerGuideHint> RadialGuideHints()
    {
        var hints = new List<ControllerGuideHint> { new(ControllerPrompt.LeftStickMove, "Choose widget"),
            new(ControllerPrompt.A, "Open widget", ControllerButton.A), new(ControllerPrompt.Y, "Reorder · hold to restart", ControllerButton.Y),
            new(ControllerPrompt.Menu, "Commands", ControllerButton.Menu) };
        if (catalogItems.Count > 8) hints.Insert(1, new(ControllerPrompt.RightStickMove, "Change page"));
        return hints;
    }

    private async Task<bool> ReturnFromRadialAsync()
    {
        if (!RadialOpen || reordering || activeWidget is not { } current) return false;
        // B and A on the displayed widget use the same entry transaction. It
        // reuses the retained presenter and waits for Interactive admission
        // before restoring focus, rather than racing Visible-state row updates.
        await SelectAsync(current);
        return true;
    }
}
