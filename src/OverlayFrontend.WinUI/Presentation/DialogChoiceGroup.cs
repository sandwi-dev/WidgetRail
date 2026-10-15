using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>A native list group. Its heading is outside every actionable item.</summary>
public sealed partial class DialogChoiceGroup(string title) : ObservableCollection<ListViewItem>
{
    public string Title { get; } = title;
    public Microsoft.UI.Xaml.Visibility HeaderVisibility => Title.Length == 0
        ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
}
