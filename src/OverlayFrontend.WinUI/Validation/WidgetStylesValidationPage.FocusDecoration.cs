using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeFocusDecorationAsync()
    {
        await FilledPrimaryFocusAsync();
        await RetiredFocusLoadedAsync();
        await TargetedFocusDecorationAsync();
        var settings = AppearanceSettings.Default with { Motion = MotionPreference.Full, FocusAnimation = WidgetFocusAnimation.Fade };
        NativeComputedStyleAdapter.SetMotionPolicy(settings, true);
        var button = new Button { Content = "Focus decoration", Width = 220, Height = 60, Margin = new Thickness(12) };
        host.Children.Add(button);
        using var adapter = new NativeComputedStyleAdapter(button);
        var styles = Compute("""
            #outline { color: #ffeedd; background: #123456; corner-radius: 8px; transition-duration: 120ms; }
            #outline:focused { outline-color: #aabbcc; outline-width: 2px; outline-offset: -2px; scale: 1.05; }
            #outline:pressed { scale: 0.95; }
            """, "outline", "button");
        try
        {
            outside.Focus(FocusState.Keyboard);
            adapter.Update(styles);
            await Wait(() => button.IsLoaded && adapter.FocusDecoration is { IsVisible: false });
            var decoration = adapter.FocusDecoration!;
            var path = (Microsoft.UI.Composition.CompositionRoundedRectangleGeometry)
                ((Microsoft.UI.Composition.CompositionSpriteShape)((Microsoft.UI.Composition.ShapeVisual)decoration.DecorationVisual).Shapes[0]).Geometry;
            Check(path.Offset == new Vector2(1) && path.Size == new Vector2(218, 58),
                "negative outline offset matches the native centered stroke geometry");
            Check(!button.UseSystemFocusVisuals && ElementCompositionPreview.GetElementChildVisual(button) is not null,
                "authored focus outline receives one dedicated native child visual");
            button.Focus(FocusState.Keyboard);
            await Wait(() => decoration.Playback is not null);
            Check(await decoration.Playback! == WidgetMotionOutcome.Completed,
                "global Fade animates the authored focus decoration on the compositor");
            await Wait(() => adapter.ScaleMotion is { IsAnimating: false });
            Check(decoration.IsVisible && Math.Abs(button.Scale.X - 1.05f) < .0001 && button.Opacity == 1 &&
                ColorOf(button.Background) == Windows.UI.Color.FromArgb(255, 18, 52, 86),
                "authored scale and focus decoration share the control without tinting its content");
            var playback = decoration.Playback;
            adapter.Update(styles);
            Check(ReferenceEquals(playback, decoration.Playback), "same-state snapshots do not replay focus decoration motion");
            NativeComputedStyleAdapter.SetMotionPolicy(settings with { FocusAnimation = WidgetFocusAnimation.Settle }, true);
            await Task.Delay(20);
            outside.Focus(FocusState.Keyboard);
            await Wait(() => decoration.Playback != playback);
            await decoration.Playback!;
            button.Focus(FocusState.Keyboard);
            await Wait(() => decoration.IsVisible);
            await Wait(() => decoration.Playback?.IsCompleted == true);
            Check(decoration.DecorationVisual.Scale == Vector3.One && button.ActualWidth == 220,
                "Settle transforms only the decoration and preserves native control layout");
            WidgetViewPresenter.SetHighContrastStyleOverride(true);
            await Wait(() => adapter.FocusDecoration is null);
            Check(button.UseSystemFocusVisuals && ElementCompositionPreview.GetElementChildVisual(button) is null,
                "high contrast restores the platform focus visual and removes the authored layer");
            WidgetViewPresenter.SetHighContrastStyleOverride(false);
            await Wait(() => adapter.FocusDecoration is not null);
            adapter.Update(null);
            Check(button.UseSystemFocusVisuals && ElementCompositionPreview.GetElementChildVisual(button) is null,
                "removing outline styles retires compositor resources and restores native focus behavior");
            adapter.Update(Compute("#outer { outline-color: #ffffff; outline-width: 2px; outline-offset: 3px; }", "outer", "button"));
            var expanded = adapter.FocusDecoration!.DecorationVisual;
            Check(expanded.Size == new Vector2(230, 70) && expanded.Parent.Offset == new Vector3(-5, -5, 0),
                "positive outline offset reserves the complete outside stroke without moving control content");
            var depth = Compute("#depth { background: rgba(40,60,80,.8); surface-shading: .1; }", "depth", "button");
            adapter.Update(depth);
            var gradient = (LinearGradientBrush)button.Background;
            Check(gradient.GradientStops.Count == 2 && gradient.GradientStops[0].Color.R > 40 &&
                gradient.GradientStops[1].Color.R < 40 && gradient.GradientStops.All(stop => stop.Color.A == 204),
                "surface depth maps theme color to a native gradient while preserving authored alpha");
            adapter.Update(depth);
            Check(ReferenceEquals(gradient, button.Background), "unchanged surface shading reuses its native gradient brush");
            WidgetViewPresenter.SetHighContrastStyleOverride(true);
            await Wait(() => button.Background is SolidColorBrush);
            Check(button.Background is SolidColorBrush, "high contrast removes decorative surface shading");
        }
        finally
        {
            adapter.Dispose(); host.Children.Remove(button);
            WidgetViewPresenter.SetHighContrastStyleOverride(null);
            NativeComputedStyleAdapter.SetMotionPolicy(AppearanceSettings.Default, true);
        }
    }

    private async Task FilledPrimaryFocusAsync()
    {
        var catalog = new ThemeCatalog(new PlatformSettingsPaths(Path.Combine(Path.GetTempPath(), "wrail-filled-focus-native")));
        NativeComputedStyleAdapter.SetMotionPolicy(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, true);
        try
        {
            foreach (var themeId in new[] { ThemeIdentity.BuiltInMonoInk, ThemeIdentity.BuiltInMineralLight })
            {
                var theme = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package, new([], []), catalog.Load(themeId, "1.0.0").Package).Theme!;
                var styles = Compute(theme, "filled-primary", "button", ["wrail-icon-button", "wrail-icon-button--primary"]);
                var button = new Button { Content = "▶", Width = 48, Height = 48, UseSystemFocusVisuals = true };
                var clipped = new Border { Width = 64, Height = 64, Padding = new(8), Child = button,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Clip = new RectangleGeometry { Rect = new(0, 0, 64, 64) } };
                host.Children.Add(clipped);
                using var adapter = new NativeComputedStyleAdapter(button);
                try
                {
                    adapter.Update(styles);
                    await Wait(() => button.IsLoaded && button.ActualWidth == 48);
                    button.Focus(FocusState.Keyboard);
                    await Wait(() => adapter.FocusDecoration is { IsVisible: true });
                    var visual = (Microsoft.UI.Composition.ShapeVisual)adapter.FocusDecoration!.DecorationVisual;
                    var stroke = (Microsoft.UI.Composition.CompositionSpriteShape)visual.Shapes[0];
                    var geometry = (Microsoft.UI.Composition.CompositionRoundedRectangleGeometry)stroke.Geometry;
                    Check(NativeComputedStyleAdapter.TryColor(styles.Focused["outline-color"].Text, out var expected) &&
                        ((Microsoft.UI.Composition.CompositionColorBrush)stroke.StrokeBrush).Color == expected && stroke.StrokeThickness == 2,
                        themeId + " actual primary uses the themed2DIP focus stroke");
                    Check(visual.Size == new Vector2(56) && visual.Parent.Offset == new Vector3(-4, -4, 0) && geometry.Offset == new Vector2(1),
                        themeId + " actual primary ring is separated2DIP from the fill with full outside stroke reserved");
                    var origin = button.TransformToVisual(clipped).TransformPoint(new());
                    Check(origin.X - 4 >= 0 && origin.Y - 4 >= 0 && origin.X + button.ActualWidth + 4 <= clipped.ActualWidth &&
                        origin.Y + button.ActualHeight + 4 <= clipped.ActualHeight,
                        themeId + " outer focus geometry stays within a clipped ancestor with8DIP padding");
                    WidgetViewPresenter.SetHighContrastStyleOverride(true);
                    await Wait(() => adapter.FocusDecoration is null);
                    Check(button.UseSystemFocusVisuals, themeId + " high contrast restores system primary-button focus");
                }
                finally { WidgetViewPresenter.SetHighContrastStyleOverride(false); host.Children.Remove(clipped); }
            }
        }
        finally
        {
            WidgetViewPresenter.SetHighContrastStyleOverride(null);
            NativeComputedStyleAdapter.SetMotionPolicy(AppearanceSettings.Default, true);
        }
    }

    private async Task TargetedFocusDecorationAsync()
    {
        var bubble=new Border{Width=100,Height=44,CornerRadius=new(10),HorizontalAlignment=HorizontalAlignment.Right,
            Background=new SolidColorBrush(Microsoft.UI.Colors.DimGray),Child=new TextBlock{Text="Bubble",HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
        var row=new Button{Content=bubble,Width=320,Height=90,HorizontalContentAlignment=HorizontalAlignment.Stretch};
        host.Children.Add(row);
        using var adapter=new NativeComputedStyleAdapter(row);
        var style=Compute("#target:focused { outline-color:#ffffff; outline-width:2px; outline-offset:-2px; }","target","button");
        var bubbleStyle=Compute("#bubble { corner-radius:10px; }","bubble","stack");
        var lifecycle=new List<object>();
        void Observe(string phase)
        {
            if(lifecycle.Count>=20)return;
            lifecycle.Add(new{phase,targetLoaded=bubble.IsLoaded,controlLoaded=row.IsLoaded,targetHasRoot=bubble.XamlRoot is not null,
                sameRoot=ReferenceEquals(bubble.XamlRoot,row.XamlRoot),parent=VisualTreeHelper.GetParent(bubble)?.GetType().Name,
                sameContent=ReferenceEquals(row.Content,bubble),controlFocus=row.FocusState.ToString(),
                nativeFocus=ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),row),bubble.ActualWidth,bubble.ActualHeight,
                decoration=adapter.FocusDecoration is not null,drawable=adapter.FocusDecoration?.IsVisible});
        }
        RoutedEventHandler loaded=(_,_)=>Observe("target-loaded"),unloaded=(_,_)=>Observe("target-unloaded");
        bubble.Loaded+=loaded;bubble.Unloaded+=unloaded;
        try
        {
            await Wait(()=>bubble.IsLoaded && bubble.ActualWidth==100);
            Observe("before-adapter");
            adapter.SetFocusVisualTarget(bubble,bubbleStyle);adapter.Update(style);
            Observe("after-adapter");
            row.Focus(FocusState.Keyboard);
            Observe("after-focus");
            try{await Wait(()=>adapter.FocusDecoration is {IsVisible:true});}
            catch
            {
                Observe("readiness-timeout");
                if(Shell.FrontendArguments.Value(Environment.GetCommandLineArgs(),"--focus-decoration-result") is {} failurePath)
                {
                    await File.WriteAllTextAsync(Path.ChangeExtension(failurePath,".lifecycle.json"),System.Text.Json.JsonSerializer.Serialize(lifecycle));
                    await CaptureStatusAsync(host,Path.ChangeExtension(failurePath,".png"));
                }
                throw;
            }
            var decoration=adapter.FocusDecoration!;
            var geometry=(Microsoft.UI.Composition.CompositionRoundedRectangleGeometry)
                ((Microsoft.UI.Composition.CompositionSpriteShape)((Microsoft.UI.Composition.ShapeVisual)decoration.DecorationVisual).Shapes[0]).Geometry;
            var immediateSize=geometry.Size;var immediateRadius=geometry.CornerRadius;
            var immediateTargetWidth=bubble.ActualWidth;var immediateTargetHeight=bubble.ActualHeight;
            if(Shell.FrontendArguments.Value(Environment.GetCommandLineArgs(),"--focus-decoration-result") is {} evidence)
            {
                await CaptureStatusAsync(host,Path.ChangeExtension(evidence,".png"));
                await File.WriteAllTextAsync(Path.ChangeExtension(evidence,".geometry.json"),System.Text.Json.JsonSerializer.Serialize(new
                {targetWidth=immediateTargetWidth,targetHeight=immediateTargetHeight,rowWidth=row.ActualWidth,rowHeight=row.ActualHeight,
                    outlineWidth=immediateSize.X,outlineHeight=immediateSize.Y,radiusX=immediateRadius.X,radiusY=immediateRadius.Y,
                    afterCaptureWidth=geometry.Size.X,afterCaptureHeight=geometry.Size.Y,afterCaptureRadius=geometry.CornerRadius.X,
                    configuredRadius=bubbleStyle.Focused?.GetValueOrDefault("corner-radius"),lifecycle}));
            }
            Check(immediateSize==new Vector2(98,42) && immediateRadius==new Vector2(10),
                $"targeted outline fits the actual bubble and its radius rather than full collection row (target {immediateTargetWidth}x{immediateTargetHeight}, outline {immediateSize}, radius {immediateRadius})");
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),row) && !row.UseSystemFocusVisuals,
                "targeted outline retains original native focus and one input owner");
            bubble.Width=180;
            await Wait(()=>geometry.Size.X==178);
            Check(ReferenceEquals(decoration,adapter.FocusDecoration),"wrapping or resizing updates one target decoration without moving focus");
            WidgetViewPresenter.SetHighContrastStyleOverride(true);
            await Wait(()=>adapter.FocusDecoration is {IsVisible:true});
            Check(!row.UseSystemFocusVisuals,"high contrast retains targeted outline with system focus color");
            adapter.SetFocusVisualTarget(null,null);adapter.Update(style);
            Check(ElementCompositionPreview.GetElementChildVisual(bubble) is null,"recycling clears the previous bubble adornment");
        }
        finally{bubble.Loaded-=loaded;bubble.Unloaded-=unloaded;adapter.Dispose();host.Children.Remove(row);WidgetViewPresenter.SetHighContrastStyleOverride(null);}
    }

    private async Task RetiredFocusLoadedAsync()
    {
        var button = new Button { Content = "Retired focus", Width = 120, Height = 44, UseSystemFocusVisuals = true };
        WidgetFocusDecoration? decoration = null;
        var retiredOnLoad = false;
        // Match passive pin entry: a preceding Loaded callback withdraws the
        // decoration while XAML is delivering that same event to its subscribers.
        button.Loaded += (_, _) => { retiredOnLoad = true; decoration?.Dispose(); };
        decoration = WidgetFocusDecoration.Create(button) ?? throw new InvalidOperationException("Missing focus adornment");
        try
        {
            host.Children.Add(button);
            await Wait(() => retiredOnLoad && button.IsLoaded);
            button.Width = 180;
            await Task.Delay(40);
            Check(!decoration.IsVisible && button.UseSystemFocusVisuals,
                "focus decoration retired during Loaded ignores captured native callbacks and later resize without using closed visuals");
        }
        finally { decoration.Dispose(); host.Children.Remove(button); }
    }
}
