using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed record ShellUtilityEntry(string Id, string Label, object Revision, Func<UIElement> Content, bool Actionable = true);

/// <summary>A modeless shell list. Showing it never transfers focus from its icon.</summary>
internal sealed partial class ShellUtilityPanel : UserControl, IDisposable
{
    private readonly WidgetPreviewFrame material = new() { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
    private Border Frame => material.Surface;
    private readonly TextBlock heading = new() { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button command = new() { MinWidth = 64 };
    private readonly TextBlock count = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly FontIcon emptyIcon = new() { Glyph = "\uE73E", FontFamily = new("Segoe Fluent Icons"), FontSize = 20 };
    private readonly Grid message = new() { ColumnSpacing = 12 };
    private readonly ListView list = new()
    {
        SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        ItemContainerTransitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection(),
    };
    private readonly Dictionary<string, (ShellUtilityEntry Entry, ListViewItem Row)> rows = new(StringComparer.Ordinal);
    private readonly SolidColorBrush ink = new(), muted = new(), hover = new(), pressed = new(), pressedInk = new(),
        clear = new(Microsoft.UI.Colors.Transparent);
    private (ShellChromePalette Palette, PreviewFrameStyle Style, double Scale, double Height)? appearance;
    private ListViewItem? pendingFocus;
    private string? lastFocusedId;
    private bool commandFocused;
    internal bool HasPendingFocus => pendingFocus is not null;
    internal event Action<string>? Activated;
    internal event Action? Command;
    internal event Action? Back;
    internal event Action? FocusChanged;
    internal event Action<string>? EntryShown;
    internal bool Busy { get; set; }
    internal bool FocusedActionable => FocusedId is { } id && rows.TryGetValue(id, out var row) && row.Entry.Actionable;

    internal ShellUtilityPanel()
    {
        IsTabStop = false;
        HorizontalAlignment = HorizontalAlignment.Left; VerticalAlignment = VerticalAlignment.Bottom;
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { heading, count } };
        header.Children.Add(title); header.Children.Add(command); Grid.SetColumn(command, 1);
        message.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        message.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        message.Children.Add(emptyIcon); message.Children.Add(status); Grid.SetColumn(status, 1);
        Frame.Child = new StackPanel { Spacing = 8, Children = { header, message, list } };
        Content = material;
        AutomationProperties.SetAutomationId(this, "Overlay.Utility.Panel");
        AutomationProperties.SetAutomationId(list, "Overlay.Utility.List");
        AutomationProperties.SetAutomationId(command, "Overlay.Utility.Command");
        AutomationProperties.SetAutomationId(status, "Overlay.Utility.Status");
        AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        foreach (var state in new[] { "", "PointerOver", "Pressed", "Disabled" })
        {
            command.Resources["ButtonForeground" + state] = state == "Disabled" ? muted : state == "Pressed" ? pressedInk : ink;
            command.Resources["ButtonBackground" + state] = state == "Pressed" ? pressed : state == "PointerOver" ? hover : clear;
            list.Resources["ListViewItemForeground" + state] = state == "Disabled" ? muted : state == "Pressed" ? pressedInk : ink;
            list.Resources["ListViewItemBackground" + state] = state == "Pressed" ? pressed : state == "PointerOver" ? hover : clear;
        }
        command.Click += (_, _) => { if (!Busy) Command?.Invoke(); };
        list.ItemClick += (_, e) => { if (!Busy && e.ClickedItem is ListViewItem { Tag: string id } && rows[id].Entry.Actionable) Activated?.Invoke(id); };
        GotFocus += (_, _) =>
        {
            lastFocusedId = FocusedId;
            commandFocused = command.FocusState != FocusState.Unfocused;
            FocusChanged?.Invoke();
        };
        list.LayoutUpdated += (_, _) => RestorePendingFocus();
        Unloaded += (_, _) => CancelPendingFocus();
        PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Escape: Back?.Invoke(); e.Handled = true; break;
                case Windows.System.VirtualKey.Up: Move(-1); e.Handled = true; break;
                case Windows.System.VirtualKey.Down: Move(1); e.Handled = true; break;
                case Windows.System.VirtualKey.Enter when FocusedId is not null: Activate(); e.Handled = true; break;
            }
        };
    }

    internal bool ContainsFocus
    {
        get
        {
            for (var node = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
                 node is not null; node = VisualTreeHelper.GetParent(node))
                if (ReferenceEquals(node, this)) return true;
            return false;
        }
    }
    internal string? FocusedId
    {
        get
        {
            for (var node = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
                 node is not null && !ReferenceEquals(node, this); node = VisualTreeHelper.GetParent(node))
                if (node is ListViewItem { Tag: string id }) return id;
            return null;
        }
    }
    internal void Configure(string title, string action, bool iconCommand = false)
    {
        heading.Text = title;
        command.Content = iconCommand ? new FontIcon { Glyph = "\uE72C", FontFamily = new("Segoe Fluent Icons"), FontSize = 18 } : action;
        command.MinWidth = iconCommand ? 36 : 64;
        command.Padding = new(8);
        AutomationProperties.SetName(command, action);
        ToolTipService.SetToolTip(command, iconCommand ? action : null);
    }
    internal void SetCount(int value) { count.Text = $"· {value}"; count.Visibility = value > 0 ? Visibility.Visible : Visibility.Collapsed; }
    internal void SetStatus(string text)
    {
        status.Text = text; message.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        var empty = text == "You're all caught up";
        emptyIcon.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        message.Margin = empty ? new(4, 12, 4, 12) : new(0, 4, 0, 4);
    }
    internal void SetCommandEnabled(bool value)
    {
        if (!value && command.FocusState != FocusState.Unfocused) Back?.Invoke();
        command.IsEnabled = value;
        command.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void Update(IReadOnlyList<ShellUtilityEntry> entries)
    {
        var focused = FocusedId;
        var oldIndex = focused is not null && rows.TryGetValue(focused, out var old) ? list.Items.IndexOf(old.Row) : 0;
        var ids = entries.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in rows.Keys.Where(id => !ids.Contains(id)).ToArray())
        { list.Items.Remove(rows[id].Row); rows.Remove(id); }
        // Preserve existing order while the panel is open, including the focused row.
        foreach (var entry in entries)
        {
            if (rows.TryGetValue(entry.Id, out var existing))
            {
                if (!Equals(existing.Entry.Revision, entry.Revision))
                {
                    if (existing.Row.Content is ShellTaskWindowRow existingTask && entry.Revision is WidgetRail.PlatformBroker.TaskWindowSummary window)
                        existingTask.UpdateWindow(window);
                    else existing.Row.Content = entry.Content();
                    AutomationProperties.SetName(existing.Row, entry.Label);
                    rows[entry.Id] = (entry, existing.Row);
                }
            }
            else
            {
                var row = new ListViewItem { Content = entry.Content(), Tag = entry.Id, MinHeight = 48,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(10, 8, 10, 8), Margin = new(0, 2, 0, 2) };
                AutomationProperties.SetName(row, entry.Label);
                AutomationProperties.SetAutomationId(row, "Overlay.Utility.Item." + entry.Id);
                row.Loaded += (_, _) => EntryShown?.Invoke(entry.Id);
                row.GotFocus += (_, _) => row.Background = hover;
                row.LostFocus += (_, _) => row.Background = clear;
                rows.Add(entry.Id, (entry, row)); list.Items.Add(row);
                if (appearance is { } current)
                { row.Foreground = ink; row.FocusVisualPrimaryBrush = new SolidColorBrush(current.Palette.Focus); }
            }
            if (appearance is { } style && rows[entry.Id].Row.Content is ShellTaskWindowRow task) task.Apply(style.Palette, style.Scale);
        }
        if (focused is not null && !rows.ContainsKey(focused)) FocusIndex(Math.Clamp(oldIndex, 0, Math.Max(0, list.Items.Count - 1)));
    }
    internal void Enter() => FocusIndex(0);
    internal void RestoreEntryFocus()
    {
        if (commandFocused && command.IsEnabled) command.Focus(FocusState.Keyboard);
        else FocusIndex(lastFocusedId is not null && rows.TryGetValue(lastFocusedId, out var row) ? list.Items.IndexOf(row.Row) : 0);
    }
    internal void CancelPendingFocus() => pendingFocus = null;
    private void RestorePendingFocus()
    {
        if (pendingFocus is not { } row) return;
        if (!list.Items.Contains(row)) { pendingFocus = null; return; }
        if (!IsLoaded) return;
        if (row.IsLoaded && row.ActualHeight > 0 && row.Focus(FocusState.Keyboard)) pendingFocus = null;
    }
    private void FocusIndex(int index)
    {
        pendingFocus = null;
        if (list.Items.Count == 0) { if (command.IsEnabled) command.Focus(FocusState.Keyboard); else Back?.Invoke(); return; }
        var row = (ListViewItem)list.Items[Math.Clamp(index, 0, list.Items.Count - 1)];
        pendingFocus = row;
        list.ScrollIntoView(row);
        RestorePendingFocus();
        DispatcherQueue.TryEnqueue(RestorePendingFocus);
    }
    internal void Move(int delta)
    {
        if (FocusedId is { } id && rows.TryGetValue(id, out var entry))
        {
            var index = list.Items.IndexOf(entry.Row);
            if (index == 0 && delta < 0 && command.IsEnabled) command.Focus(FocusState.Keyboard);
            else FocusIndex(index + delta);
        }
        else FocusIndex(0);
    }
    internal void Activate()
    {
        if (Busy) return;
        if (FocusedId is { } id) { if (rows[id].Entry.Actionable) Activated?.Invoke(id); } else Command?.Invoke();
    }
    internal void Scroll(double delta)
    {
        ScrollViewer? Find(DependencyObject root)
        {
            if (root is ScrollViewer viewer) return viewer;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                if (Find(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
            return null;
        }
        if (Find(list) is { } scroll) scroll.ChangeView(null, scroll.VerticalOffset + delta, null, disableAnimation: false);
    }
    internal void Apply(ShellChromePalette palette, PreviewFrameStyle style, double scale, double availableHeight)
    {
        if (appearance == (palette, style, scale, availableHeight)) return;
        appearance = (palette, style, scale, availableHeight);
        material.Apply(style.HighContrast ? style : style with
        {
            Top = style.Top with { A = (byte)(style.Top.A * .65) },
            Side = style.Side with { A = (byte)(style.Side.A * .65) },
            Bottom = style.Bottom with { A = (byte)(style.Bottom.A * .65) },
        });
        Frame.Padding = new(12 * scale);
        list.MaxHeight = Math.Max(48, Math.Min(380 * scale, availableHeight - 120 * scale));
        heading.FontSize = 20 * scale; status.FontSize = count.FontSize = 14 * scale; list.FontSize = 16 * scale; command.FontSize = 14 * scale;
        ink.Color = palette.Text; muted.Color = palette.Muted;
        hover.Color = palette.HighContrast ? palette.Selected : ShellChromePalette.Blend(palette.Surface, palette.Text, .08);
        pressed.Color = palette.Selected; pressedInk.Color = palette.SelectedText;
        if (palette.HighContrast) hover.Color = palette.Surface;
        heading.Foreground = list.Foreground = command.Foreground = ink;
        status.Foreground = count.Foreground = emptyIcon.Foreground = muted;
        command.Background = new SolidColorBrush(palette.Item);
        command.FocusVisualPrimaryBrush = new SolidColorBrush(palette.Focus);
        foreach (var (_, row) in rows.Values)
        {
            row.Foreground = ink; row.FocusVisualPrimaryBrush = new SolidColorBrush(palette.Focus);
            if (row.Content is NotificationCard card) card.ApplyBannerPalette(palette);
            if (row.Content is ShellTaskWindowRow task) task.Apply(palette, scale);
        }
    }
    public void Dispose() { CancelPendingFocus(); material.Dispose(); }
}
