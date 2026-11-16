using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;
internal sealed partial class PackageIconValidationPage
{
    private readonly List<WidgetViewPresenter> labelPresenters = [];
    private async Task IconLabelsAsync()
    {
        try
        {
            var root = Environment.GetCommandLineArgs().First(arg => arg.StartsWith("--icon-assets-root="))[19..];
            var host = (StackPanel)Content;
            host.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 23, 20, 31));
            presenter.Visibility = Visibility.Collapsed;
            var paths = new[] { "samples/PlayniteLibraryWidget/assets/icons/trophy.svg", "samples/DiscordWebViewWidget/assets/icons/voice.svg", "samples/DiscordWebViewWidget/assets/icons/muted.svg" };
            var payloads = paths.Select((path, i) =>
            {
                var bytes = Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(root, path)).Replace("#000000", "#ffffff"));
                return new WidgetPresentationPackageIcon(i.ToString(), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), bytes);
            }).ToArray();
            foreach (var scale in new[] { 1d, .9, .5 })
            {
                var view = new WidgetViewPresenter { Width = 720, HorizontalAlignment = HorizontalAlignment.Left };
                var zoom = new Shell.OverlayScaleRoot { InterfaceScale=scale, HorizontalAlignment=HorizontalAlignment.Left };
                zoom.Children.Add(view); host.Children.Add(zoom); labelPresenters.Add(view);
                view.ResolvePackageIconAsync = (asset, _) => Task.FromResult(payloads[int.Parse(asset.AssetId)]);
                var styles = new Dictionary<string, BridgeNodeRenderStyles>();
                void Style(string id, params (string Key, BridgeComputedStyleValue Value)[] values)
                {
                    var dict = values.ToDictionary(v => v.Key, v => v.Value);
                    var pressed = new Dictionary<string, BridgeComputedStyleValue>(dict);
                    if (id.StartsWith("button"))
                    {
                        dict["scale"] = new() { Kind=WrssValueKind.Number, Text="1", Number=1 };
                        pressed["scale"] = new() { Kind=WrssValueKind.Number, Text="0.96", Number=.96 };
                    }
                    styles[id] = new() { Base = dict, Focused = dict, Pressed = pressed };
                }
                BridgeComputedStyleValue Px(double n) => new() { Kind = WrssValueKind.Length, Text = n + "px", Number = n, Unit = "px" };
                BridgeComputedStyleValue Color(string color) => new() { Kind = WrssValueKind.Color, Text = color };
                Style("root", ("color", Color("#f6f0fa")), ("gap", Px(12)), ("padding", Px(8)));
                var rows = new List<ViewNode>();
                foreach (var button in new[] { false, true })
                {
                    var cells = new List<ViewNode>();
                    for (var i=0; i<3; i++)
                    {
                        var id = $"{(button ? "button" : "row")}{i}";
                        var label = new[] { "MRP Aplenty", "Deafen", "Unmute" }[i];
                        if (button)
                        {
                            cells.Add(new() { Id=id, Kind=ViewNodeKind.Button, Text=label, ActionId=id, Glyph=WidgetGlyph.Music,
                                PackageIcon=new(i.ToString(), WidgetPackageIconColorMode.ThemeTint) });
                            Style(id, ("width",Px(180)), ("height",Px(40)), ("font-size",Px(13)), ("color",Color("#f6f0fa")),
                                ("background",Color("#493f59")), ("border-color",Color("#9a89ab")), ("border-width",Px(1)), ("corner-radius",Px(8)),
                                ("transition-duration",new() {Kind=WrssValueKind.Duration,Text="90ms",Number=90,Unit="ms"}));
                        }
                        else
                        {
                            cells.Add(new() { Id=id, Kind=ViewNodeKind.Row, Children=(ViewNode[])[
                                new() {Id=id+"icon",Kind=ViewNodeKind.Icon,Glyph=WidgetGlyph.Music,PackageIcon=new(i.ToString(),WidgetPackageIconColorMode.ThemeTint)},
                                new() {Id=id+"text",Kind=ViewNodeKind.Text,Text=label}] });
                            Style(id, ("width",Px(180)), ("gap",Px(8)));
                            Style(id+"icon", ("font-size",Px(20)), ("color",Color("#f6f0fa")));
                            Style(id+"text", ("font-size",Px(20)), ("color",Color("#f6f0fa")));
                        }
                    }
                    rows.Add(new() { Id=button?"buttons":"rows",Kind=ViewNodeKind.Row,Children=cells });
                }
                var snapshot = new ViewSnapshot { WidgetInstanceId="labels",Sequence=1,ActiveInputScopeId="root",InitialFocusId="button0",
                    Root=new() { Id="root",Kind=ViewNodeKind.Stack,Children=rows } };
                var descriptor = new BridgeWidgetDescriptor { Id="labels",Name="Labels",InstanceId="labels",RuntimeGeneration="runtime",PresentationGeneration="presentation",PackageContentDigest="fixture",Icon=WidgetGlyph.Music };
                void Publish() => view.Apply(new(new(descriptor.Id,descriptor.RuntimeGeneration,descriptor.PresentationGeneration,1,snapshot.WidgetInstanceId,snapshot.Sequence,"root"),descriptor,snapshot,styles));
                var toggled=false;
                view.DispatchActionAsync = _ =>
                {
                    toggled=!toggled;
                    var updated = rows.Select(row => row with { Children = row.Children.Select(node => node.Id=="button1" ? node with { Text=toggled?"Undeafen":"Deafen" } : node).ToArray() }).ToArray();
                    snapshot = snapshot with { Sequence=snapshot.Sequence+1,Root=snapshot.Root with {Children=updated} };
                    Publish();
                    status.Text=$"Toggle {snapshot.Sequence}: {scale:0.0}x, wait for scaling animation to settle";
                    return Task.CompletedTask;
                };
                Publish();
            }
            var trigger = new Button { Content="Run controller A toggle", HorizontalAlignment=HorizontalAlignment.Left };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(trigger,"IconLabels.ControllerA");
            trigger.Click += async (_, _) =>
            {
                var target=labelPresenters[1];
                if(!target.FocusHostTarget("button1")) {status.Text="Could not focus button";return;}
                await target.HandleControllerButtonAsync(ControllerButton.A,ControllerEventPhase.Pressed);
                await Task.Delay(110);
                await target.HandleControllerButtonAsync(ControllerButton.A,ControllerEventPhase.Released);
                await Task.Delay(250);
                status.Text="Controller A toggle complete; release animation settled";
            };
            host.Children.Insert(1,trigger);
            await Task.Delay(500);
            status.Text = "Icon + right-hand label / animated icon-label buttons at 100%, 90%, 50%";
        }
        catch(Exception error) { status.Text = error.ToString(); }
    }
}
