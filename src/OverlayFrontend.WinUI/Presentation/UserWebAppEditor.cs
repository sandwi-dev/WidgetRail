using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using Windows.ApplicationModel.DataTransfer;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>A single draft form; buffered text entry uses the shared controller keyboard.</summary>
internal sealed class UserWebAppEditor : IDisposable
{
    private readonly UserWebAppStore store;
    private readonly UserWebApp? existing;
    private readonly FrameworkElement origin;
    private readonly XamlRoot root;
    private readonly Popup popup;
    private readonly Border panel;
    private readonly Grid cover = new() { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly UserControl form;
    private readonly ScrollViewer viewport;
    private readonly Shell.RightStickScroll scrollInput = new();
    private double scale = 1;
    private readonly StackPanel body = new() { Spacing = 12 };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Dictionary<Button, Func<Task>> actions = [];
    private readonly TaskCompletionSource<UserWebApp?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource lifetime = new();
    private readonly IDisposable? theme;
    private readonly Button addressButton, nameButton;
    private readonly CheckBox? enabledChoice;
    private string address, name;
    private bool busy, closed, confirmRemove;
    internal UIElement InputRoot => cover;
    internal WidgetTextEntryDialog? Keyboard { get; private set; }
    internal Input.DialogControllerAdmission ControllerAdmission { get; } = new();
    internal event Action? Changed;

    internal UserWebAppEditor(UserWebAppStore store, FrameworkElement origin, UserWebApp? existing, string? url, string? title)
    {
        this.store = store; this.origin = origin; this.existing = existing; root = origin.XamlRoot;
        address = existing?.Url ?? url ?? ""; name = existing?.Name ?? title ?? "";
        if (name.Length > 80)
        { name = name[..80]; if (char.IsHighSurrogate(name[^1])) name = name[..^1]; }
        var heading = new TextBlock { Text = existing is null ? "Add webapp" : "Manage webapp", FontSize = 24, Tag = "form-title", TextWrapping = TextWrapping.Wrap };
        body.Children.Add(heading);
        body.Children.Add(new TextBlock { Text = "Keep a website in your widget rail. Sign-in and browser preferences are remembered.", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "Website address" });
        addressButton = Add("", "Address", () => EditAsync(true));
        Add("Paste address", "Paste", PasteAsync);
        body.Children.Add(new TextBlock { Text = "Name" });
        nameButton = Add("", "Name", () => EditAsync(false));
        if (existing is not null)
        {
            enabledChoice = new CheckBox { Content = "Enabled", IsChecked = existing.Enabled, MinHeight = 44 };
            AutomationProperties.SetAutomationId(enabledChoice,"WebApp.Editor.Enabled");
            body.Children.Add(enabledChoice);
        }
        message.Visibility = Visibility.Collapsed;
        body.Children.Add(message);
        Add(existing is null ? "Add and open" : "Save", "Save", SaveAsync);
        if (existing is not null) Add("Remove webapp", "Remove", RemoveAsync);
        Add("Cancel", "Cancel", () => { Close(); return Task.CompletedTask; });
        viewport = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel = new Border { Child = viewport,
            Padding = new Thickness(24), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12) };
        AutomationProperties.SetAutomationId(panel, "WebApp.Editor");
        form = new UserControl { Content = panel, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsTabStop = false };
        AutomationProperties.SetName(form, "Webapp editor"); AutomationProperties.SetAutomationId(form, "WebApp.Editor");
        cover.Children.Add(form);
        popup = new Popup { XamlRoot = root, Child = cover, IsLightDismissEnabled = false };
        panel.Loaded += (_, _) => panel.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => { if (!closed && Keyboard is null) addressButton.Focus(FocusState.Keyboard); });
        theme = NativePopupTheme.Form(panel, origin, zoom => { scale = zoom; body.Spacing = 12 * zoom; Arrange(); });
        panel.KeyDown += (_, args) => { if (args.Key == Windows.System.VirtualKey.Escape) { Close(); args.Handled = true; } };
        panel.GotFocus += (_, args) => { if (args.OriginalSource is FrameworkElement element) element.StartBringIntoView(new() { AnimationDesired = false }); };
        root.Changed += RootChanged;
        Refresh(); Arrange();
    }

    private Button Add(string label, string id, Func<Task> action)
    {
        Button button = id is "Address" or "Name" ? new WidgetValueButton() : new Button { Content = label };
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = id is "Address" or "Name" ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        button.MinHeight = 44;
        AutomationProperties.SetAutomationId(button, "WebApp.Editor." + id);
        actions.Add(button, action); body.Children.Add(button);
        button.Click += async (_, _) => await RunAsync(action);
        return button;
    }
    internal Task<UserWebApp?> ShowAsync()
    {
        popup.IsOpen = true;
        Input.GamepadKeyBoundary.ObserveOpenPopups(origin);
        return completion.Task;
    }
    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Arrange();
    private void Arrange()
    {
        cover.Width = root.Size.Width; cover.Height = root.Size.Height;
        panel.Width = Math.Max(1, Math.Min(560 * scale, root.Size.Width - 32));
        panel.MaxHeight = Math.Max(1, root.Size.Height - 32);
        panel.Measure(new(root.Size.Width, root.Size.Height));
        var rect = origin.IsLoaded ? origin.TransformToVisual(root.Content).TransformBounds(new(0, 0, origin.ActualWidth, origin.ActualHeight)) : new Windows.Foundation.Rect(0,0,root.Size.Width,root.Size.Height);
        var x = Math.Clamp(rect.X + (rect.Width - panel.Width) / 2, 16, Math.Max(16, root.Size.Width - panel.Width - 16));
        var y = Math.Clamp(rect.Y + (rect.Height - panel.DesiredSize.Height) / 2, 16, Math.Max(16, root.Size.Height - panel.DesiredSize.Height - 16));
        form.Margin = new Thickness(x,y,0,0);
    }
    private void Refresh()
    {
        ValueLabel(addressButton,string.IsNullOrWhiteSpace(address) ? "Enter website address…" : address);
        ValueLabel(nameButton,string.IsNullOrWhiteSpace(name) ? "Name from website address" : name);
        message.Visibility = message.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var button in actions.Keys) button.IsEnabled = !busy;
        if (enabledChoice is not null) enabledChoice.IsEnabled = !busy;
        Arrange();
    }
    private static void ValueLabel(Button button,string label)
    {
        if (button is WidgetValueButton value)
        {
            value.SetLabel(label); value.SetAccessibleValue(label, false);
            AutomationProperties.SetName(button,label); return;
        }
        if (button.Content is not TextBlock text)
        {
            text = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = button.Foreground, FontSize = button.FontSize, FontWeight = button.FontWeight, FontFamily = button.FontFamily };
            button.Content = text;
        }
        text.Text = label; AutomationProperties.SetName(button,label);
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (busy || closed) return;
        var restore = FocusManager.GetFocusedElement(root) as Button;
        busy = true; message.Text = ""; Refresh();
        try { await action(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            Diagnostics.FrontendFailureLog.Current.Write("webapp-editor", error);
            message.Text = error is PlatformSettingsException ? error.Message : "Could not complete this action. Please try again.";
        }
        finally { busy = false; if (!closed) { Refresh(); if (restore is { IsLoaded: true, IsEnabled: true }) restore.Focus(FocusState.Keyboard); } }
    }
    private async Task EditAsync(bool editAddress)
    {
        var accepted = false;
        WidgetTextEntryDialog? keyboard = null;
        keyboard = new(new ViewNode { Id = "webapp.edit", Kind = ViewNodeKind.TextEntry,
            AccessibilityLabel = editAddress ? "Website address" : "Webapp name", TextEntryValue = editAddress ? address : name,
            TextEntryMaximumLength = editAddress ? 2048 : 80,
            TextEntryInputKind = editAddress ? TextEntryInputKind.Url : null }, commit => { accepted = commit; keyboard!.Hide(); });
        keyboard.BindRoot(root); keyboard.ThemeLease = NativePopupTheme.Dialog(keyboard, origin);
        keyboard.GuideChanged = () => Changed?.Invoke(); Keyboard = keyboard; Changed?.Invoke();
        try
        {
            await keyboard.ShowKeyboardAsync(panel);
            if (accepted && !closed)
            {
                if (editAddress) address = keyboard.TakeValue(); else name = keyboard.TakeValue();
            }
        }
        finally
        {
            Keyboard = null; keyboard.Erase(); Changed?.Invoke();
            if (!closed) panel.DispatcherQueue.TryEnqueue(() => (editAddress ? addressButton : nameButton).Focus(FocusState.Keyboard));
        }
    }
    private async Task PasteAsync()
    {
        var clipboard = Clipboard.GetContent();
        if (!clipboard.Contains(StandardDataFormats.Text)) { message.Text = "Copy a website address first."; return; }
        var value = await clipboard.GetTextAsync();
        if (!closed) address = UserWebAppStore.NormalizeUrl(value);
    }
    private async Task SaveAsync()
    {
        var url = UserWebAppStore.NormalizeUrl(address);
        var title = string.IsNullOrWhiteSpace(name) ? UserWebAppStore.SuggestedName(url) : name;
        var app = existing is null ? await store.CreateAsync(title, url, lifetime.Token) :
            await store.UpdateAsync(existing.Id, existing.Revision, title, url, enabledChoice?.IsChecked == true, lifetime.Token);
        Close(app);
    }
    private async Task RemoveAsync()
    {
        if (!confirmRemove) { confirmRemove = true; message.Text = "Select Remove webapp again to remove it from your rail. Browser data is retained."; return; }
        await store.RemoveAsync(existing!.Id, existing.Revision, lifetime.Token); Close();
    }
    internal void MoveFocus(FocusNavigationDirection direction)
    {
        if (Keyboard is { } keyboard) keyboard.MoveFocus(direction);
        else FocusManager.TryMoveFocus(direction, new FindNextElementOptions { SearchRoot = panel });
    }
    internal void SampleScroll(short x, short y, long timestamp)
    {
        if (Keyboard is not null) { scrollInput.Reset(); return; }
        var delta = scrollInput.Sample(x,y,timestamp);
        if (delta.Y != 0) viewport.ChangeView(null, Math.Clamp(viewport.VerticalOffset + delta.Y,0,viewport.ScrollableHeight),null,disableAnimation:true);
    }
    internal void Handle(ControllerButton button, ControllerEventPhase phase)
    {
        if (Keyboard is { } keyboard) { keyboard.Handle(button, phase); return; }
        if (phase != ControllerEventPhase.Pressed) return;
        if (button == ControllerButton.B) Close();
        else if (button == ControllerButton.A && !busy && enabledChoice is not null && ReferenceEquals(FocusManager.GetFocusedElement(root),enabledChoice))
            enabledChoice.IsChecked = enabledChoice.IsChecked != true;
        else if (button == ControllerButton.A && FocusManager.GetFocusedElement(root) is Button selected && actions.TryGetValue(selected, out var action)) _ = RunAsync(action);
    }
    internal void Close(UserWebApp? result = null)
    {
        if (closed) return;
        closed = true; lifetime.Cancel(); Keyboard?.Hide(); popup.IsOpen = false; completion.TrySetResult(result);
    }
    public void Dispose()
    {
        Close(); root.Changed -= RootChanged; theme?.Dispose(); popup.Child = null; lifetime.Dispose();
    }
}
