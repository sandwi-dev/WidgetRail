using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>
/// An authored brush owns that channel; all unowned native visual-state channels
/// keep their original ThemeResource expressions. No theme-resource cache mutation.
/// </summary>
internal sealed class WidgetNativeButtonStates
{
    private readonly Button button;
    private readonly VisualStateGroup group;
    private readonly ContentPresenter body;
    private readonly BrushTransition? backgroundTransition;
    private readonly Dictionary<VisualState, Timeline[]> timelines;
    private (bool Background, bool Foreground, bool Border)? owned;
    private WidgetNativeButtonStates(Button button, VisualStateGroup group, ContentPresenter body)
    {
        this.button = button; this.group = group;
        this.body = body; backgroundTransition = body.BackgroundTransition;
        timelines = group.States.ToDictionary(state => state, state => state.Storyboard?.Children.ToArray() ?? []);
    }
    internal static WidgetNativeButtonStates? Create(Button button)
    {
        button.ApplyTemplate();
        if (VisualTreeHelper.GetChildrenCount(button) == 0 || VisualTreeHelper.GetChild(button, 0) is not FrameworkElement { Name: "WidgetDepthRoot" } root)
            return null;
        var group = VisualStateManager.GetVisualStateGroups(root).FirstOrDefault(group => group.Name == "CommonStates");
        return group is null || root.FindName("ContentPresenter") is not ContentPresenter body ? null : new(button, group, body);
    }
    internal void Update(bool background, bool foreground, bool border)
    {
        var next = (background, foreground, border);
        if (owned == next) return;
        owned = next;
        // Authored backgrounds must not fade from the native theme's initial
        // fill when a page realizes new buttons. WRSS owns this paint channel;
        // preserve the native transition for controls that do not author it.
        body.BackgroundTransition = background ? null : backgroundTransition;
        var stateName = group.CurrentState?.Name ?? (button.IsEnabled ? "Normal" : "Disabled");
        VisualStateManager.GoToState(button, "Normal", false);
        foreach (var (state, originals) in timelines)
        {
            if (state.Storyboard is not { } storyboard) continue;
            storyboard.Children.Clear();
            foreach (var timeline in originals)
            {
                var property = Storyboard.GetTargetName(timeline) == "ContentPresenter" ? Storyboard.GetTargetProperty(timeline) : null;
                if (property == "Background" && background || property == "Foreground" && foreground || property == "BorderBrush" && border) continue;
                storyboard.Children.Add(timeline);
            }
        }
        VisualStateManager.GoToState(button, stateName, false);
    }
    internal void Restore() => Update(false, false, false);
}
