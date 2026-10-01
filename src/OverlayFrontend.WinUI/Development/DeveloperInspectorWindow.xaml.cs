using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using WinUIEx;

namespace WidgetRail.OverlayFrontend.WinUI.Development;

public sealed partial class DeveloperInspectorWindow : Window
{
    private readonly Func<DeveloperInspection?> capture;
    private readonly Func<string> navigation;
    private readonly DispatcherQueueTimer timer;
    private readonly Dictionary<string, TreeViewNode> treeNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<TreeViewNode, string> treeKeys = [];
    private readonly Dictionary<string, Rectangle> mapRectangles = new(StringComparer.Ordinal);
    private readonly RectangleGeometry mapClip = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private DeveloperInspection? frame;
    private string? selected;
    private bool rebuilding;
    private bool retiring;
    private string shape = "";

    internal DeveloperInspectorWindow(Func<DeveloperInspection?> capture, Func<string> navigation)
    {
        this.capture = capture;
        this.navigation = navigation;
        InitializeComponent();
        Map.Clip = mapClip;
        this.SetWindowSize(1120, 760);
        timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(200);
        timer.Tick += (_, _) => Refresh();
        InspectorRoot.Loaded += (_, _) => { Refresh(force: true); ready.TrySetResult(); };
        AppWindow.Closing += (_, args) =>
        {
            if (retiring) return;
            args.Cancel = true; timer.Stop(); AppWindow.Hide();
        };
        Closed += (_, _) => timer.Stop();
    }

    internal async Task OpenAsync(CancellationToken token)
    {
        if (retiring) return;
        Activate();
        timer.Start();
        Refresh(force: true);
        await ready.Task.WaitAsync(token);
        if (frame is null) throw new InvalidOperationException("The widget inspector has no admitted presentation.");
    }

    internal void Retire()
    {
        if (retiring) return;
        retiring = true;
        timer.Stop();
        Close();
    }

    private void Refresh(bool force = false)
    {
        if (retiring || !InspectorRoot.IsLoaded || !force && (!AppWindow.IsVisible || Pause.IsChecked == true)) return;
        try
        {
            var next = capture();
            if (next is null) { Status.Text = "Widget unavailable — retaining the last captured layout."; return; }
            frame = next;
            Status.Text = $"{next.WidgetId} · snapshot {next.Sequence} · focus: {next.FocusId} · {next.Nodes.Count} elements" +
                (next.Omitted > 0 ? $" · {next.Omitted} omitted" : "") + " · realized content only";
            Navigation.Text = navigation();
            UpdateTree();
            UpdateDetails();
            DrawMap();
            // A successful manual retry resumes live inspection after a capture
            // failure. Paused or hidden inspectors must remain idle.
            if (AppWindow.IsVisible && Pause.IsChecked != true && !timer.IsRunning) timer.Start();
        }
        catch (Exception error)
        {
            Status.Text = "Inspector capture unavailable; refresh to retry.";
            Diagnostics.FrontendFailureLog.Current.Write("development-inspector", error);
            timer.Stop();
        }
    }

    private void UpdateTree()
    {
        if (frame is null) return;
        var candidates = frame.Nodes.Where(node => Search.Text.Length == 0 || node.Id.Contains(Search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
        var nextShape = string.Join('\n', candidates.Select(node => node.Id + "\t" + node.ParentId));
        rebuilding = true;
        try
        {
            if (shape != nextShape)
            {
                var expanded = treeNodes.Where(pair => pair.Value.IsExpanded).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
                Tree.RootNodes.Clear(); treeNodes.Clear(); treeKeys.Clear();
                foreach (var node in candidates)
                {
                    var item = new TreeViewNode { Content = Label(node), IsExpanded = node.ParentId is null || expanded.Contains(node.Id) };
                    if (!treeNodes.TryAdd(node.Id, item)) continue;
                    treeKeys[item] = node.Id;
                }
                foreach (var node in candidates)
                {
                    var item = treeNodes[node.Id];
                    if (node.ParentId is not null && treeNodes.TryGetValue(node.ParentId, out var parent)) parent.Children.Add(item);
                    else Tree.RootNodes.Add(item);
                }
                shape = nextShape;
            }
            else foreach (var node in candidates)
                if (treeNodes.TryGetValue(node.Id, out var item) && !Equals(item.Content, Label(node))) item.Content = Label(node);
            if (FollowFocus.IsChecked == true && candidates.FirstOrDefault(node => node.Focused) is { } focused) selected = focused.Id;
            if (selected is null || !treeNodes.ContainsKey(selected)) selected = candidates.FirstOrDefault()?.Id;
            if (selected is not null && treeNodes.TryGetValue(selected, out var selectedNode))
            {
                for (var parent = selectedNode.Parent; parent is not null; parent = parent.Parent) parent.IsExpanded = true;
                Tree.SelectedNode = selectedNode;
            }
        }
        finally { rebuilding = false; }
        static string Label(DeveloperInspectionNode node) => (node.Focused ? "→ " : "") + node.Id + " [" + node.Kind + "]" + (node.Visible ? "" : " (hidden)");
    }

    private void UpdateDetails()
    {
        var text = frame?.Nodes.FirstOrDefault(node => node.Id == selected)?.Details ?? "Select an element to inspect its native geometry, focus policy and resolved styles.";
        if (Details.Text != text) Details.Text = text;
    }

    private void DrawMap()
    {
        if (frame is null || frame.Width <= 0 || frame.Height <= 0 || Map.ActualWidth <= 0 || Map.ActualHeight <= 0) return;
        mapClip.Rect = new Rect(0, 0, Map.ActualWidth, Map.ActualHeight);
        var scale = Math.Min(Map.ActualWidth / frame.Width, Map.ActualHeight / frame.Height);
        var visible = frame.Nodes.Where(node => node.Visible && node.Width > 0 && node.Height > 0).ToArray();
        var ids = visible.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var key in mapRectangles.Keys.Where(key => !ids.Contains(key)).ToArray())
        { Map.Children.Remove(mapRectangles[key]); mapRectangles.Remove(key); }
        foreach (var node in visible)
        {
            if (!mapRectangles.TryGetValue(node.Id, out var rectangle))
            {
                rectangle = new() { IsHitTestVisible = false };
                mapRectangles.Add(node.Id, rectangle); Map.Children.Add(rectangle);
            }
            rectangle.Width = node.Width * scale; rectangle.Height = node.Height * scale;
            rectangle.Style = (Style)InspectorRoot.Resources[node.Id == selected || node.Focused ? "SelectedRectangle" : "MapRectangle"];
            Canvas.SetLeft(rectangle, node.X * scale); Canvas.SetTop(rectangle, node.Y * scale);
        }
    }

    private void SearchChanged(object sender, TextChangedEventArgs args) { UpdateTree(); UpdateDetails(); DrawMap(); }
    private void SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (rebuilding || Tree.SelectedNode is not { } item || !treeKeys.TryGetValue(item, out var key)) return;
        selected = key; FollowFocus.IsChecked = false; UpdateDetails(); DrawMap();
    }
    private void FollowClicked(object sender, RoutedEventArgs args) { UpdateTree(); UpdateDetails(); DrawMap(); }
    private void PauseClicked(object sender, RoutedEventArgs args)
    { if (Pause.IsChecked != true) Refresh(force: true); else Status.Text = "PAUSED · " + Status.Text; }
    private void RefreshClicked(object sender, RoutedEventArgs args) => Refresh(force: true);
    private void MapSizeChanged(object sender, SizeChangedEventArgs args) => DrawMap();
    private void MapPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        if (frame is null || frame.Width <= 0 || frame.Height <= 0) return;
        var scale = Math.Min(Map.ActualWidth / frame.Width, Map.ActualHeight / frame.Height);
        if (scale <= 0) return;
        var point = args.GetCurrentPoint(Map).Position;
        var hit = frame.Nodes.Where(node => node.Visible && node.Width > 0 && node.Height > 0 &&
            new Rect(node.X, node.Y, node.Width, node.Height).Contains(new(point.X / scale, point.Y / scale)))
            .OrderBy(node => node.Width * node.Height).FirstOrDefault();
        if (hit is null) return;
        FollowFocus.IsChecked = false; Search.Text = ""; selected = hit.Id;
        UpdateTree(); UpdateDetails(); DrawMap(); args.Handled = true;
    }
}
