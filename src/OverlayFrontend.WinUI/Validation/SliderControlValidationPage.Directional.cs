using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class SliderControlValidationPage
{
    private readonly List<object> directionalEvidence=[];

    // Each case creates a new native slider. No pointer/UIA value write or
    // direct slider Focus() may prime the control before the directional check.
    private async Task ColdDirectionalEntryAsync()
    {
        foreach(var (shape,width) in new[]{("main-scroll",780d),("pin-minimum",300d),("pin-default",560d)})
        foreach(var endpoint in new[]{0d,50d,100d})
        {
            presenter.ResetPressedStyles();presenter.Width=width;presenter.Height=200;
            var row=new ViewNode {Id="audio.row",Kind=ViewNodeKind.Row,Children=(ViewNode[])[
                new(){Id="audio.mute",Kind=ViewNodeKind.Button,Text="Mute stream",ActionId="mute"},
                new(){Id="audio.volume",Kind=ViewNodeKind.Slider,Value=endpoint,Minimum=0,Maximum=100,Step=5,
                    ValueChangedActionId="volume",AccessibilityLabel="Stream volume",AccessibilityValue=endpoint+"%",
                    SliderInteractionMode=SliderInteractionMode.ActivateToAdjust}]};
            var card=new ViewNode {Id="audio.card",Kind=ViewNodeKind.Stack,Children=(ViewNode[])[
                new(){Id="audio.title",Kind=ViewNodeKind.Text,Text="Stream audio"},row]};
            var root=new ViewNode {Id="audio.root",Kind=shape=="main-scroll"?ViewNodeKind.Scroll:ViewNodeKind.Stack,
                ScrollAxis=shape=="main-scroll"?ScrollAxis.Vertical:null,Children=(ViewNode[])[card]};
            var snapshot=new ViewSnapshot {WidgetInstanceId="slider.directional",Sequence=++sequence,Root=root,
                ActiveInputScopeId=root.Id,InitialFocusId="audio.mute"};
            var runtime=$"cold-{shape}-{endpoint}";
            var descriptor=new BridgeWidgetDescriptor {Id="slider",Name="Cold slider navigation",InstanceId=snapshot.WidgetInstanceId,
                RuntimeGeneration=runtime,PresentationGeneration="presentation",Icon=WidgetGlyph.Connection,PackageContentDigest=""};
            Dictionary<string,BridgeComputedStyleValue> Style(params (string Key,BridgeComputedStyleValue Value)[] values)=>values.ToDictionary(value=>value.Key,value=>value.Value);
            BridgeNodeRenderStyles States(params (string Key,BridgeComputedStyleValue Value)[] values)
            {var style=Style(values);return new(){Base=style,Focused=style,Pressed=style};}
            BridgeComputedStyleValue Length(double amount)=>new(){Kind=WrssValueKind.Length,Number=amount,Unit="px",Text=amount+"px"};
            BridgeComputedStyleValue Number(double amount)=>new(){Kind=WrssValueKind.Number,Number=amount,Text=amount.ToString(System.Globalization.CultureInfo.InvariantCulture)};
            BridgeComputedStyleValue Keyword(string text)=>new(){Kind=WrssValueKind.Keyword,Text=text};
            BridgeComputedStyleValue Padding(string text)=>new(){Kind=WrssValueKind.LengthList,Text=text};
            var styles=new Dictionary<string,BridgeNodeRenderStyles>
            {
                ["audio.root"]=States(("height",Length(200)),("min-width",Length(0)),("min-height",Length(0))),
                ["audio.card"]=States(("padding",Padding("14px")),("gap",Length(12))),
                ["audio.row"]=States(("gap",Length(10)),("align",Keyword("center")),("min-width",Length(0))),
                ["audio.mute"]=States(("min-width",Length(0)),("min-height",Length(40)),("padding",Padding("8px 12px")),("font-size",Length(13)),("flex-grow",Number(0))),
                ["audio.volume"]=States(("min-width",Length(0)),("flex-grow",Number(1))),
            };
            void Publish(ViewSnapshot next)=>presenter.Apply(new(new(descriptor.Id,runtime,descriptor.PresentationGeneration,1,next.WidgetInstanceId,next.Sequence,next.ActiveInputScopeId),
                descriptor,SnapshotJson.Deserialize(SnapshotJson.Serialize(next)),styles));
            Publish(snapshot);
            await Until(()=>Find(presenter,"Widget.audio.mute") is Control {IsLoaded:true,ActualWidth:>0}&&
                Find(presenter,"Widget.audio.volume") is Slider {IsLoaded:true,ActualWidth:>0,ActualHeight:>0});
            await Task.Delay(40);
            Check(presenter.FocusHostTarget("audio.mute"),$"{shape} {endpoint}: first native focus enters the adjacent button");
            var slider=(Slider)Find(presenter,"Widget.audio.volume")!;
            var button=(Control)Find(presenter,"Widget.audio.mute")!;
            var actionCount=actions.Count;var before=slider.Value;
            var handled=presenter.MoveFocus(FocusNavigationDirection.Right);
            var reached=FocusId=="Widget.audio.volume";
            var evidence=new {shape,width,endpoint,handled,reached,focus=FocusId,
                slider.IsTabStop,slider.IsEnabled,slider.IsLoaded,slider.IsFocusEngagementEnabled,slider.IsFocusEngaged,
                sliderBounds=slider.TransformToVisual(presenter).TransformBounds(new(0,0,slider.ActualWidth,slider.ActualHeight)),
                buttonBounds=button.TransformToVisual(presenter).TransformBounds(new(0,0,button.ActualWidth,button.ActualHeight)),
                nativeThumbPresent=Part<Microsoft.UI.Xaml.Controls.Primitives.Thumb>(slider,"HorizontalThumb") is not null};
            directionalEvidence.Add(evidence);
            if(!reached)
            {
                // Diagnostic only after recording the failed cold directional
                // attempt. This is native programmatic focus, not a mouse test.
                var direct=slider.Focus(FocusState.Keyboard);
                directionalEvidence.Add(new {shape,endpoint,directKeyboardFocus=direct,focus=FocusId,inspection=presenter.CaptureDeveloperInspection()});
            }
            Check(reached,$"{shape} {endpoint}: Right reaches a cold slider without pointer priming or authored neighbors");
            Check(slider.Value==before&&actions.Count==actionCount,$"{shape} {endpoint}: entering the slider does not adjust its value");
            presenter.MoveFocus(FocusNavigationDirection.Left);
            Check(FocusId=="Widget.audio.mute",$"{shape} {endpoint}: inactive slider returns left to its button");
            // Earlier Discord UI replaced a saved-boost reset button with the
            // slider while its adjacent Mute button retained focus. Reproduce
            // that generic declaration change with two immediate publications.
            styles["audio.reset"]=styles["audio.mute"];
            var resetRow=row with{Children=(ViewNode[])[row.Children[0],new(){Id="audio.reset",Kind=ViewNodeKind.Button,Text="Use 100% volume",ActionId="reset"}]};
            var resetRoot=root with{Children=(ViewNode[])[card with{Children=(ViewNode[])[card.Children[0],resetRow]}]};
            Publish(snapshot with{Sequence=++sequence,Root=resetRoot});
            await Until(()=>Find(presenter,"Widget.audio.reset") is Control {IsLoaded:true,ActualWidth:>0});
            await Task.Delay(40);
            Publish(snapshot with{Sequence=++sequence});Publish(snapshot with{Sequence=++sequence});
            await Until(()=>Find(presenter,"Widget.audio.volume") is Slider {IsLoaded:true,ActualWidth:>0,ActualHeight:>0});
            await Task.Delay(40);
            var replacement=(Slider)Find(presenter,"Widget.audio.volume")!;
            Check(!ReferenceEquals(slider,replacement)&&ReferenceEquals(button,Find(presenter,"Widget.audio.mute"))&&FocusId=="Widget.audio.mute",
                $"{shape} {endpoint}: reset-to-slider replacement retains its neighboring button and focus");
            var replacementHandled=presenter.MoveFocus(FocusNavigationDirection.Right);
            directionalEvidence.Add(new{shape,endpoint,phase="reset-to-slider",handled=replacementHandled,focus=FocusId,
                replacement.IsTabStop,replacement.IsLoaded,replacement.IsFocusEngagementEnabled,
                bounds=replacement.TransformToVisual(presenter).TransformBounds(new(0,0,replacement.ActualWidth,replacement.ActualHeight))});
            Check(FocusId=="Widget.audio.volume",$"{shape} {endpoint}: Right reaches the newly replaced slider before pointer interaction");
        }
        presenter.Width=double.NaN;presenter.Height=double.NaN;
    }
}
