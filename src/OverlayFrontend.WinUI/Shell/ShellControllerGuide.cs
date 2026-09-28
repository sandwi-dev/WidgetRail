using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>All semantic variants stay measured; changing input context never changes the guide's extent.</summary>
internal sealed class ShellControllerGuide : ContentControl, IDisposable
{
    private readonly Grid stage = new();
    private readonly List<(Grid Panel, bool Reorder, bool PlayStation, string Help)> variants = [];
    private readonly List<FrameworkElement> typography = [];
    private bool reordering, hidden, subscribed, disposed;
    internal event Action? Changed;
    internal string HelpText { get; private set; } = string.Empty;
    internal FrameworkElement BackgroundSurface => stage;
    internal IReadOnlyList<FrameworkElement> Typography => typography;

    internal ShellControllerGuide()
    {
        IsTabStop = false; IsHitTestVisible = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Content = stage;
        foreach (var reorder in new[] { false, true })
        foreach (var playStation in new[] { false, true }) AddVariant(reorder, playStation);
        Loaded += (_, _) => { if (!subscribed && !disposed) { subscribed = true; WidgetControllerPrompts.Changed += Update; } Update(); };
        Unloaded += (_, _) => { if (!IsLoaded && subscribed) { WidgetControllerPrompts.Changed -= Update; subscribed = false; } };
        Update();
    }

    internal void SetState(bool reorder, bool inactive)
    { reordering = reorder; hidden = inactive; Update(); }

    private void AddVariant(bool reorder, bool playStation)
    {
        (ControllerPrompt Prompt, string Label)[] hints = reorder
            ? [(ControllerPrompt.DPadHorizontal, "Move widget"), (ControllerPrompt.A, "Done"), (ControllerPrompt.B, "Done"), (ControllerPrompt.Y, "Done")]
            : [(ControllerPrompt.A, "Open widget"), (ControllerPrompt.Y, "Reorder · hold to restart"), (ControllerPrompt.Menu, "Commands"), (ControllerPrompt.B, "Close")];
        var panel = new Grid { ColumnSpacing = 12 };
        var help = new List<string>();
        for (var index = 0; index < hints.Length; ++index)
        {
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(index == 1 && !reorder ? 2 : 1, GridUnitType.Star) });
            var cell = new Grid { ColumnSpacing = 6 };
            cell.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); cell.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var icon = new FontIcon { MinWidth = 24, MinHeight = 24, VerticalAlignment = VerticalAlignment.Center };
            WidgetGlyphs.Apply(icon, new() { Id = "shell.guide", Kind = ViewNodeKind.ControllerGlyph, ControllerPrompt = hints[index].Prompt }, playStation);
            var text = new TextBlock { Text = hints[index].Label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
            AutomationProperties.SetAccessibilityView(text, AccessibilityView.Raw);
            typography.Add(icon); typography.Add(text);
            cell.Children.Add(icon); cell.Children.Add(text); Grid.SetColumn(text, 1);
            panel.Children.Add(cell); Grid.SetColumn(cell, index);
            help.Add(WidgetGlyphs.AccessibleName(hints[index].Prompt, playStation) + ": " + hints[index].Label);
        }
        var description = string.Join(". ", help) + ".";
        variants.Add((panel, reorder, playStation, description));
        stage.Children.Add(panel);
    }

    private void Update()
    {
        if (disposed) return;
        if (!DispatcherQueue.HasThreadAccess) { DispatcherQueue.TryEnqueue(Update); return; }
        foreach (var variant in variants)
        {
            var active = variant.Reorder == reordering && variant.PlayStation == WidgetControllerPrompts.PlayStation;
            variant.Panel.Opacity = active ? 1 : 0;
            // All glyph/text children are decorative. One family-correct guide name
            // is exposed instead of reading both measured variants or glyph codes.
            AutomationProperties.SetAccessibilityView(variant.Panel, AccessibilityView.Raw);
            if (active) HelpText = variant.Help;
        }
        Opacity = hidden ? 0 : 1;
        AutomationProperties.SetName(this, HelpText);
        AutomationProperties.SetAccessibilityView(this, hidden ? AccessibilityView.Raw : AccessibilityView.Content);
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (subscribed) WidgetControllerPrompts.Changed -= Update;
        subscribed = false;
    }
}
