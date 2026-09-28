using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeIndexedBoxAsync()
    {
        // Resolve the actual package and platform rules used by the YT row.
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Directory.Build.props"))) repository = repository.Parent;
        if (repository is null) throw new InvalidOperationException("Native production-style probe needs its source checkout.");
        var platform = WrssPackageLoader.Load("builtin-default.wrss", new WrssFileSourceProvider(Path.Combine(repository.FullName, "src", "PlatformSettings", "Themes")));
        var package = WrssPackageLoader.Load("default.wrss", new WrssFileSourceProvider(Path.Combine(repository.FullName, "samples", "YtMusicWidget", "styles")));
        var compilation = WrssThemeCompiler.Compile([new WrssThemeLayer(0, platform.Documents), new WrssThemeLayer(200, package.Documents)]);
        var theme = compilation.Theme ?? throw new InvalidOperationException("YT theme fixture did not compile.");
        var node = new WidgetView(UI.Tile("Track title", "Song", "track", "track", subtitle: "Artist")
            .AddClasses("music-track")).CreateSnapshot("indexed.box", 1).Root;
        var styles = new Dictionary<string, BridgeNodeRenderStyles>();
        Resolve(node);
        var frame = CreateFrame(node, styles);
        var container = new ListViewItem { Width = 500, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var fragment = new WidgetViewPresenter(presentationOnly: true);
        fragment.UseIndexedContainerStyles();
        fragment.ApplyFragment(frame, node, frame.Authority.ActiveInputScopeId);
        container.Content = fragment;
        host.Children.Add(container);
        using var adapter = new NativeComputedStyleAdapter(container);
        adapter.Update(styles[node.Id]);
        try
        {
            await Wait(() => container.ActualHeight > 0);
            Check(container.MinHeight == 82 && container.Padding.Top == 9 && ((FrameworkElement)fragment.Content).MinHeight == 0,
                "actual YT track minimum and padding have one native container owner");
            Check(Math.Abs(container.ActualHeight - 82) < 1,
                "YT authored 82-DIP row does not add another 18 DIPs of padding to an inner minimum");
            var loadedHeight = container.ActualHeight;
            WidgetIndexedCollectionView.ConfigureContainerLayout(container, ScrollAxis.Vertical, 0);
            fragment.ApplyFragment(frame, node, frame.Authority.ActiveInputScopeId);
            await Task.Delay(40);
            Check(container.MinHeight == 82 && container.ActualHeight == loadedHeight,
                "repeated slot notifications preserve the active row's box constraints");
        }
        finally { adapter.Dispose(); host.Children.Remove(container); await fragment.DisposeAsync(); }

        var viewportItem = new ListViewItem { Content = "Viewport-relative item" };
        var viewport = new WidgetViewPresenter { Width = 600, Height = 200, Content = viewportItem };
        host.Children.Add(viewport);
        using var viewportStyle = new NativeComputedStyleAdapter(viewportItem);
        try
        {
            await Wait(() => viewport.ActualWidth == 600);
            viewportStyle.Update(Compute("#viewportItem { width: 50vw; min-height: 20vh; }", "viewportItem", "actionSurface"));
            Check(viewportItem.Width == 300 && viewportItem.MinHeight == 40,
                "indexed container viewport units resolve against the widget rather than the row");
            viewport.Width = 400;
            await Wait(() => viewport.ActualWidth == 400);
            viewportStyle.RefreshBoxLayout();
            Check(viewportItem.Width == 200 && viewportItem.MinHeight == 40,
                "responsive container layout refresh recomputes viewport units without a new item snapshot");
        }
        finally { viewportStyle.Dispose(); host.Children.Remove(viewport); await viewport.DisposeAsync(); }

        void Resolve(ViewNode item)
        {
            IReadOnlyDictionary<string, BridgeComputedStyleValue> State(params WrssPseudoState[] states) =>
                theme.Resolve(new(item.Kind == ViewNodeKind.ActionSurface ? "actionSurface" : item.Kind.ToString().ToLowerInvariant(),
                    item.Id, item.StyleClasses.ToHashSet(), states.ToHashSet()))
                    .Properties.ToDictionary(pair => pair.Key, pair => new BridgeComputedStyleValue
                    { Kind = pair.Value.Kind, Text = pair.Value.Text, Number = pair.Value.Number, Unit = pair.Value.Unit });
            styles[item.Id] = new() { Base = State(), Focused = State(WrssPseudoState.Focused), Pressed = State(WrssPseudoState.Focused, WrssPseudoState.Pressed) };
            foreach (var child in item.Children) Resolve(child);
        }
    }
}
