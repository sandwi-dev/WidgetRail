using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetProtocol;
using Windows.System;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class FileBrowserDialog
{
    private TextBox? editField;
    private bool initialized;
    internal IReadOnlyList<ControllerGuideHint> Guide => Keyboard is { } keyboard ? WidgetViewPresenter.CaptureKeyboardGuide(keyboard) :
        request.Mode == FileBrowserMode.Multiple ? (ControllerGuideHint[])[new(ControllerPrompt.A,"Select",ControllerButton.A),
            new(ControllerPrompt.LeftTrigger,"Hold to select"),new(ControllerPrompt.X,request.CommitLabel,ControllerButton.X),
            new(ControllerPrompt.Y,"Search",ControllerButton.Y),new(ControllerPrompt.B,"Back",ControllerButton.B),
            new(ControllerPrompt.Menu,"Cancel",ControllerButton.Menu)] :
        (ControllerGuideHint[])[new(ControllerPrompt.A,"Open",ControllerButton.A),new(ControllerPrompt.X,request.CommitLabel,ControllerButton.X),
            new(ControllerPrompt.Y,"Search",ControllerButton.Y),new(ControllerPrompt.B,"Back",ControllerButton.B),
            new(ControllerPrompt.Menu,"Cancel",ControllerButton.Menu)];
    private void Retire()
    {
        if (closed)
            return;
        closed = true;
        Keyboard?.Hide();
        reading?.Cancel();
        lifetime.Cancel();
        lifetime.Dispose();
    }
    internal void Cancel()
    {
        if (closed)
            return;
        editField = null;
        Result = [];
        UseWindows = false;
        Keyboard?.Hide();
        Hide();
        Retire();
    }
    internal async Task PresentAsync(FrameworkElement origin, CancellationToken token)
    {
        try
        {
            using var cancel = token.Register(() => DispatcherQueue.TryEnqueue(Cancel));
            while (!closed)
            {
                token.ThrowIfCancellationRequested();
                await ShowPickerAsync(origin);
                token.ThrowIfCancellationRequested();
                if (editField is not { } field || closed)
                    break;
                editField = null;
                var accepted = false;
                WidgetTextEntryDialog? keyboard = null;
                keyboard = new(new ViewNode
                {
                    Id = "files.edit",
                    Kind = ViewNodeKind.TextEntry,
                    AccessibilityLabel = field.PlaceholderText,
                    TextEntryValue = field.Text,
                    TextEntryMaximumLength = field == location ? 32760 : field == filename ? 255 : 256
                },
                    commit => { accepted = commit; keyboard!.Hide(); });
                keyboard.BindRoot(origin.XamlRoot);
                keyboard.ThemeLease = NativePopupTheme.Dialog(keyboard, origin);
                Keyboard = keyboard;
                keyboard.GuideChanged = () => GuideChanged?.Invoke();
                GuideChanged?.Invoke();
                try
                {
                    await keyboard.ShowKeyboardAsync(origin);
                    if (accepted && !closed && !token.IsCancellationRequested)
                    {
                        field.Text = keyboard.TakeValue();
                        if (field == location)
                            Go(location.Text);
                    }
                }
                finally { Keyboard = null; keyboard.Erase(); GuideChanged?.Invoke(); }
            }
        }
        finally { Retire(); }
    }
    private void Edit(TextBox field)
    {
        if (closed || committing)
            return;
        editField = field;
        Admission.Reset();
        Hide();
    }
    private void Keys(object sender, KeyRoutedEventArgs args)
    {
        if (Input.GamepadKeyBoundary.Owns(this, args))
            return;
        if (Down(VirtualKey.Control) && args.Key is VirtualKey.F or VirtualKey.L)
        {
            var field = args.Key == VirtualKey.L ? location : search;
            if (field == location)
                BeginLocationEdit();
            if (showingPlaces && field == search)
            {
                showingPlaces = false;
                LayoutPicker();
            }
            if (reviewing && field == search)
                ToggleReview();
            field.Focus(FocusState.Keyboard);
            field.SelectAll();
            args.Handled = true;
            return;
        }
        var focused = FocusManager.GetFocusedElement(XamlRoot);
        if (args.Key == VirtualKey.Enter && Down(VirtualKey.Control) && !enteringLocation)
        {
            args.Handled = true;
            if (IsPrimaryButtonEnabled)
                _ = CommitAsync();
            return;
        }
        if (args.Key == VirtualKey.A && Down(VirtualKey.Control) && request.Mode == FileBrowserMode.Multiple && FocusedRow() is not null)
        {
            args.Handled = true;
            var visibleFiles = rows.Where(row => !row.Entry.Folder && !row.Checked).ToArray();
            if (picked.Count + visibleFiles.Length > request.Limit)
                Message($"Choose up to {request.Limit} files.");
            else
            foreach (var file in visibleFiles)
                Mark(file, true);
            return;
        }
        if (args.Key == VirtualKey.Escape && focused is TextBox)
        {
            args.Handled = true;
            if (ReferenceEquals(focused, location) && page is not null)
                ShowLocation(page.Directory);
            FocusRow(rows.FirstOrDefault());
            return;
        }
        if (args.Key == VirtualKey.Up && Down(VirtualKey.Menu) || args.Key == VirtualKey.Back && focused is not TextBox)
        {
            args.Handled = true;
            GoToParent();
            return;
        }
        if (focused is TextBox || FocusedRow() is not { } row)
            return;
        if (args.Key == VirtualKey.Space && request.Mode == FileBrowserMode.Multiple && !row.Entry.Folder)
        {
            args.Handled = true;
            Mark(row, !row.Checked);
            anchor = row.Entry.Path;
        }
        else if (args.Key == VirtualKey.Enter)
        {
            args.Handled = true;
            Activate(row);
        }
        else if (Down(VirtualKey.Shift) && args.Key is VirtualKey.Up or VirtualKey.Down && request.Mode == FileBrowserMode.Multiple)
        {
            args.Handled = true;
            anchor ??= row.Entry.Path;
            var i = Math.Clamp(Array.IndexOf(rows, row) + (args.Key == VirtualKey.Up ? -1 : 1), 0, rows.Length - 1);
            FocusRow(rows[i]);
            MarkRange(rows[i]);
        }
    }
    internal void Receive(ControllerFrame frame, bool owns)
    {
        if (Admission.Observe(frame, owns) != Input.DialogControllerAdmissionResult.Ready)
        {
            HeldSelect = false;
            scroll.Reset();
            return;
        }
        WidgetControllerPrompts.Set(frame.LastInputFamily);
        var nav = frame.DpadNavigation.Phase != NavigationPhase.None ? frame.DpadNavigation : frame.StickNavigation;
        var direction = nav.Direction switch
        {
            NavigationDirection.Up => FocusNavigationDirection.Up,
            NavigationDirection.Down => FocusNavigationDirection.Down,
            NavigationDirection.Left => FocusNavigationDirection.Left,
            NavigationDirection.Right => FocusNavigationDirection.Right,
            _ => FocusNavigationDirection.None
        };
        if (Keyboard is { } keyboard)
        {
            if (nav.Phase != NavigationPhase.None)
                keyboard.MoveFocus(direction);
            foreach (var (mask, button) in Input.ControllerGestureOwnership.DigitalButtons)
            {
                if ((frame.PressedButtons & mask) != 0)
                    keyboard.Handle(button, ControllerEventPhase.Pressed);
                if ((frame.ReleasedButtons & mask) != 0)
                    keyboard.Handle(button, ControllerEventPhase.Released);
            }
            if (frame.LeftTriggerPressed != 0)
                keyboard.Handle(ControllerButton.LeftTrigger, ControllerEventPhase.Pressed);
            if (frame.RightTriggerPressed != 0)
                keyboard.Handle(ControllerButton.RightTrigger, ControllerEventPhase.Pressed);
            if (frame.LeftTriggerReleased != 0)
                keyboard.Handle(ControllerButton.LeftTrigger, ControllerEventPhase.Released);
            if (frame.RightTriggerReleased != 0)
                keyboard.Handle(ControllerButton.RightTrigger, ControllerEventPhase.Released);
            keyboard.SampleHeldButtons(frame.State.Buttons, frame.PressedButtons, Environment.TickCount64);
            return;
        }
        if (typeFilter.IsDropDownOpen)
        {
            if (nav.Phase != NavigationPhase.None && direction is FocusNavigationDirection.Up or FocusNavigationDirection.Down)
            {
                filterCursor = Math.Clamp(filterCursor + (direction == FocusNavigationDirection.Up ? -1 : 1), 0, typeFilter.Items.Count - 1);
                (typeFilter.ContainerFromIndex(filterCursor) as ComboBoxItem)?.Focus(FocusState.Keyboard);
            }
            if ((frame.PressedButtons & 0x1000) != 0) typeFilter.SelectedIndex = filterCursor;
            if ((frame.PressedButtons & 0x3000) != 0)
            {
                typeFilter.IsDropDownOpen = false;
                typeFilter.Focus(FocusState.Keyboard);
            }
            return;
        }
        if ((frame.PressedButtons & 0x10) != 0)
        {
            Cancel();
            return;
        }
        if ((frame.PressedButtons & 0x100) != 0)
        {
            if (up.IsEnabled && !committing)
                GoToParent();
            return;
        }
        var extending = frame.State.LeftTrigger > 30 && request.Mode == FileBrowserMode.Multiple;
        if (extending && !HeldSelect && FocusedRow() is { } first)
            Mark(first, true);
        HeldSelect = extending;
        if (nav.Phase != NavigationPhase.None && direction != FocusNavigationDirection.None)
        {
            if (FocusedRow() is { } current && direction is FocusNavigationDirection.Up or FocusNavigationDirection.Down)
            {
                var i = Array.IndexOf(rows, current) + (direction == FocusNavigationDirection.Up ? -1 : 1);
                if ((uint)i < rows.Length)
                {
                    FocusRow(rows[i]);
                    if (extending)
                        Mark(rows[i], true);
                }
                else
                    FocusManager.TryMoveFocus(direction, new FindNextElementOptions { SearchRoot = this });
            }
            else
                FocusManager.TryMoveFocus(direction, new FindNextElementOptions { SearchRoot = this });
        }
        if ((frame.PressedButtons & 0x2000) != 0)
        {
            Back();
            return;
        }
        if ((frame.PressedButtons & 0x8000) != 0)
        {
            if (reviewing)
                ToggleReview();
            Edit(search);
            return;
        }
        if ((frame.PressedButtons & 0x4000) != 0 && IsPrimaryButtonEnabled)
        {
            _ = CommitAsync();
            return;
        }
        if ((frame.PressedButtons & 0x1000) != 0)
        {
            if (ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), typeFilter))
                typeFilter.IsDropDownOpen = true;
            else if (ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), editLocation))
            {
                BeginLocationEdit();
                Edit(location);
            }
            else if (FocusManager.GetFocusedElement(XamlRoot) is TextBox field)
                Edit(field);
            else if (FocusedRow() is { } row)
                Activate(row);
            else if (DialogChoiceNavigation.FocusedItem(places)?.Tag is Place place)
                Go(place.Path);
            else if (FocusManager.GetFocusedElement(XamlRoot) is FrameworkElement element)
                (FrameworkElementAutomationPeer.CreatePeerForElement(element)?.GetPattern(PatternInterface.Invoke) as IInvokeProvider)?.Invoke();
        }
        var delta = scroll.Sample(frame.State.RightThumbX, frame.State.RightThumbY, Environment.TickCount64);
        if (delta.Y != 0 && (fileScroll ??= FindViewport(files)) is { } viewport)
            viewport.ChangeView(null, Math.Clamp(viewport.VerticalOffset + delta.Y, 0, viewport.ScrollableHeight), null, true);
        else if (scroll.ShouldSettle(Environment.TickCount64))
        {
            scroll.Reset();
            if (FocusedRow() is { } current && files.ItemsPanelRoot is ItemsStackPanel list && list.FirstVisibleIndex >= 0 &&
                list.LastVisibleIndex >= list.FirstVisibleIndex && list.LastVisibleIndex < rows.Length)
            {
                var i = Array.IndexOf(rows, current);
                if (i < list.FirstVisibleIndex || i > list.LastVisibleIndex)
                    FocusRow(rows[Math.Clamp(i, list.FirstVisibleIndex, list.LastVisibleIndex)]);
            }
        }
    }
    private static ScrollViewer? FindViewport(DependencyObject node)
    {
        if (node is ScrollViewer view)
            return view;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        if (FindViewport(VisualTreeHelper.GetChild(node, i)) is { } found)
            return found;
        return null;
    }
}
