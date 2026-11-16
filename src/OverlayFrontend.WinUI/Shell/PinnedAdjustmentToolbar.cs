using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>One host-owned tool palette; never a selectable pin or a media owner.</summary>
internal sealed class PinnedAdjustmentToolbar:IDisposable
{
    internal PinnedWidgetWindow Window{get;}
    internal Slider Opacity{get;}=new(){Minimum=30,Maximum=100,StepFrequency=5,SmallChange=5,LargeChange=10,MinWidth=120};
    internal Button Save{get;}=new(){Content="Save",MinWidth=72,Height=36,CornerRadius=new(8)};
    internal Button Cancel{get;}=new(){Content="Cancel",MinWidth=72,Height=36,CornerRadius=new(8)};
    internal Button Close{get;}=new(){Content=new FontIcon{Glyph="\uE8BB",FontFamily=new("Segoe Fluent Icons"),FontSize=14},Width=36,Height=36,Padding=new(0),CornerRadius=new(8)};
    private readonly TextBlock label=new(){Text="Opacity",VerticalAlignment=VerticalAlignment.Center};
    private readonly TextBlock value=new(){Width=44,TextAlignment=TextAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
    private readonly TextBlock help=new(){Text="Drag pin · resize edges",VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};
    private readonly Grid root=new(){Padding=new(12),RowSpacing=8,ColumnSpacing=8};
    private readonly Border panel=new(){CornerRadius=new(12),BorderThickness=new(1)};
    private readonly ShellControllerGuide controllerGuide=new();
    private readonly ShellChromeStyles chrome=new();
#if ENABLE_WIDGET_VALIDATION
    internal IReadOnlyList<ControllerGuideHint> DisplayedHintsForValidation=>controllerGuide.DisplayedHints;
    internal Action CaptureCloseForValidation()=>CloseRequested??(()=>{});
    internal Action CaptureSaveForValidation()=>SaveRequested??(()=>{});
    internal Action<int> CaptureOpacityForValidation()=>OpacityChanged??(_=>{});
#endif
    private bool updating;
    private bool layoutQueued,disposed;
    internal bool LayoutPending=>layoutQueued;
    private ShellChromePalette? lastPalette;
    private AppearanceSettings? lastAppearance;
    internal event Action<int>? OpacityChanged;
    internal event Action? SaveRequested;
    internal event Action? CancelRequested;
    internal event Action? CloseRequested;
    internal event KeyEventHandler? KeyPressed;
    internal event Action? LayoutInvalidated;

    internal PinnedAdjustmentToolbar(nint main)
    {
        Window=new("Adjust pin",main,PeerWindowLayer.Toolbar);
        root.TabFocusNavigation=Microsoft.UI.Xaml.Input.KeyboardNavigationMode.Cycle;
        Window.SetHostIdentity("Adjust pin","Overlay.PinAdjustment.Toolbar");
        AutomationProperties.SetHelpText(Window.AutomationRoot,"Drag the pin to move; drag its edges to resize. Arrow keys move; Shift plus arrows resize. Enter saves; Escape cancels.");
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        root.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});root.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var opacityRow=new Grid{ColumnSpacing=8};
        opacityRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});opacityRow.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});opacityRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        opacityRow.Children.Add(label);opacityRow.Children.Add(Opacity);Grid.SetColumn(Opacity,1);opacityRow.Children.Add(value);Grid.SetColumn(value,2);
        root.Children.Add(opacityRow);root.Children.Add(Close);Grid.SetColumn(Close,1);
        var footer=new Grid{ColumnSpacing=8};
        footer.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        footer.Children.Add(help);footer.Children.Add(Save);Grid.SetColumn(Save,1);footer.Children.Add(Cancel);Grid.SetColumn(Cancel,2);
        root.Children.Add(footer);Grid.SetRow(footer,1);Grid.SetColumnSpan(footer,2);
        root.Children.Add(controllerGuide);Grid.SetRow(controllerGuide,2);Grid.SetColumnSpan(controllerGuide,2);
        // Inherit typography without importing a widget body's box geometry or
        // background onto host-owned controls. The native style adapter already
        // provides this boundary for composed text elsewhere in the renderer.
        foreach(var text in new FrameworkElement[]{label,value,help,Save,Cancel})chrome.Register(text,"body",typographyOnly:true);
        foreach(var item in controllerGuide.Typography)chrome.Register(item,item is FontIcon?"controller-glyph":"hint",typographyOnly:true);
        controllerGuide.Present(this,true,[],(ControllerGuideHint[])[
            new(ControllerPrompt.LeftBumper,"Opacity −",ControllerButton.LeftBumper,Required:true),
            new(ControllerPrompt.RightBumper,"+",ControllerButton.RightBumper,Required:true),
            new(ControllerPrompt.X,"Close",ControllerButton.X,Required:true)]);
        controllerGuide.Invoked+=hint=>
        {
            if(hint.Button==ControllerButton.LeftBumper)Opacity.Value=Math.Max(30,Opacity.Value-5);
            else if(hint.Button==ControllerButton.RightBumper)Opacity.Value=Math.Min(100,Opacity.Value+5);
            else if(hint.Button==ControllerButton.X)CloseRequested?.Invoke();
        };
        panel.Child=root;Window.SetContent(panel);
        foreach(var (control,id,name) in new (FrameworkElement,string,string)[]{(Opacity,"Opacity","Pin opacity"),(Save,"Save","Save pin adjustments"),(Cancel,"Cancel","Cancel pin adjustments"),(Close,"Close","Close pin")})
        {AutomationProperties.SetAutomationId(control,"Overlay.PinAdjustment."+id);AutomationProperties.SetName(control,name);ToolTipService.SetToolTip(control,name);}
        Opacity.ValueChanged+=(_,args)=>{var percent=(int)Math.Round(args.NewValue);value.Text=$"{percent}%";if(!updating)OpacityChanged?.Invoke(percent);};
        Save.Click+=(_,_)=>SaveRequested?.Invoke();Cancel.Click+=(_,_)=>CancelRequested?.Invoke();Close.Click+=(_,_)=>CloseRequested?.Invoke();
        Window.CloseRequested+=()=>CancelRequested?.Invoke();
        Window.AutomationRoot.PreviewKeyDown+=(sender,args)=>KeyPressed?.Invoke(sender,args);
        Window.AutomationRoot.Loaded+=(_,_)=>QueueLayout();
        Window.PlacementEnvironmentChanged+=QueueLayout;
    }

    internal void Update(PinnedBounds pin,PinnedMonitor monitor,int opacity,AppearanceSettings appearance,ShellChromePalette palette,
        IReadOnlyDictionary<string,BridgeNodeRenderStyles>? styles)
    {
        updating=true;
        try{Opacity.Value=opacity;value.Text=$"{opacity}%";}
        finally{updating=false;}
        var styleChanged=lastPalette!=palette||lastAppearance!=appearance;
        lastPalette=palette;lastAppearance=appearance;
        Window.InterfaceScale=appearance.InterfaceScale;
        chrome.Update(styles,appearance,false);
        controllerGuide.ApplyAppearance(styles,appearance,false);
        if(styleChanged)
        {
        Window.AutomationRoot.RequestedTheme=(palette.Surface.R*.299+palette.Surface.G*.587+palette.Surface.B*.114)>128?ElementTheme.Light:ElementTheme.Dark;
        panel.Background=new SolidColorBrush(palette.Surface);
        panel.BorderBrush=new SolidColorBrush(palette.HighContrast?palette.Text:ShellChromePalette.Blend(palette.Surface,palette.Text,.18));
        foreach(var text in new[]{label,value,help})
        {text.Foreground=new SolidColorBrush(text==help?palette.Muted:palette.Text);text.FontSize=(text==help?12:14)*appearance.TextScale;}
        value.Width=44*appearance.TextScale;
        foreach(var button in new[]{Save,Cancel,Close})
        {
            button.FontSize=14*appearance.TextScale;button.Height=Math.Max(36,28*appearance.TextScale+8);
            palette.PaintButton(button,.07);
        }
        Close.Width=Close.Height;
        if(Close.Content is FontIcon closeIcon)closeIcon.FontSize=14*appearance.TextScale;
        Opacity.Foreground=new SolidColorBrush(palette.Focus);
        }
        var scale=monitor.Scale*appearance.InterfaceScale;
        panel.Width=Math.Min(380*Math.Max(1,appearance.TextScale),Math.Max(1,monitor.WorkArea.Width/scale-24));
        panel.Measure(new(panel.Width,double.PositiveInfinity));
        Window.Place(PinnedAdjustmentToolbarPlacement.Choose(pin,monitor.WorkArea,(int)Math.Ceiling(panel.Width*scale),
            (int)Math.Ceiling(panel.DesiredSize.Height*scale),(int)Math.Ceiling(8*scale)));
        if(!Window.Interactive)Window.SetInteraction(true);
        if(!Window.IsVisible)Window.Show();
        if(styleChanged)QueueLayout();
    }
    private void QueueLayout()
    {
        if(disposed||layoutQueued)return;
        layoutQueued=true;
        Window.AutomationRoot.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,()=>
        {
            layoutQueued=false;
            if(!disposed)LayoutInvalidated?.Invoke();
        });
    }
    internal void Focus(){Window.Activate();Save.Focus(FocusState.Keyboard);}
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        OpacityChanged=null;SaveRequested=null;CancelRequested=null;CloseRequested=null;KeyPressed=null;LayoutInvalidated=null;
        controllerGuide.Dispose();chrome.Dispose();Window.Dispose();
    }
}
