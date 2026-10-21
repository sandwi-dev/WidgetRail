using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class PackageIconValidationPage
{
    private async Task PresentationContinuityAsync()
    {
        var host = (StackPanel)Content;
        var view = new WidgetViewPresenter();
        host.Children.Add(view);
        var loads = 0;
        var actions = new List<string>();
        view.ResolvePackageIconAsync = async (asset, _) => { loads++; await Task.Yield(); return Bytes(asset.AssetId); };
        view.DispatchActionAsync = request => { actions.Add(request.Action.ActionId); return Task.CompletedTask; };
        long revision = 0;
        var digest = "continuity-v1";
        var assetId = "red";
        var mode = WidgetPackageIconColorMode.ThemeTint;
        var instance = "continuity.instance";
        try
        {
            ApplyScope("home");
            await Until(() => Icon("original").Content is Image && Icon("tint").Content is ImageIcon && ButtonIcon().Content is ImageIcon);
            var originalSource = ((Image)Icon("original").Content).Source;
            var initialLoads = loads;
            var cache = NativePackageIconTintCache.For(XamlRoot);
            var preparations = cache.PreparationCount;
            var oldButton = Button();
            var oldIcon = ButtonIcon();
            for (var index = 0; index < 12; ++index)
            {
                ApplyScope(index % 2 == 0 ? "library" : "home");
                Check(Icon("original").Content is Image { Source: SvgImageSource } &&
                    Icon("tint").Content is ImageIcon && ButtonIcon().Content is ImageIcon,
                    $"scope switch {index + 1} publishes prepared package icons without a fallback frame");
            }
            Check(!ReferenceEquals(oldButton, Button()) && !ReferenceEquals(oldIcon, ButtonIcon()),
                "scope replacement creates fresh controls and icon visuals");
            Check(ReferenceEquals(originalSource, ((Image)Icon("original").Content).Source) &&
                loads == initialLoads && cache.PreparationCount == preparations,
                "scope replacement reuses admitted images without refetching or rasterizing");
            Invoke(oldButton);
            await Task.Delay(30);
            Check(actions.Count == 0, "retired button cannot dispatch after visual inheritance");
            Invoke(Button());
            await Until(() => actions.Count == 1);
            Check(actions[0] == "action.home", "replacement button dispatches its current scope action");
            ButtonIcon().Foreground = new SolidColorBrush(Microsoft.UI.Colors.Coral);
            await Until(() => NativePackageIconTint.For((ImageIcon)ButtonIcon().Content)?.CurrentColor == Microsoft.UI.Colors.Coral);
            Check(true, "inherited tinted icon follows its new control foreground");

            assetId = "blue"; ApplyScope("changed-asset");
            Check(ButtonIcon().Content is FontIcon, "changed asset cannot inherit the previous icon");
            await Until(() => ButtonIcon().Content is ImageIcon);
            mode = WidgetPackageIconColorMode.OriginalColor; ApplyScope("changed-mode");
            Check(ButtonIcon().Content is FontIcon, "changed color mode cannot inherit an incompatible visual");
            await Until(() => ButtonIcon().Content is Image);
            digest = "continuity-v2"; ApplyScope("changed-package");
            Check(Icon("original").Content is FontIcon && ButtonIcon().Content is FontIcon,
                "package replacement does not inherit old package imagery");
            await Until(() => ButtonIcon().Content is Image);
            instance = "continuity.replacement"; ApplyScope("changed-owner");
            Check(ButtonIcon().Content is FontIcon, "new widget instance cannot inherit previous owner imagery");
            await Until(() => ButtonIcon().Content is Image);
        }
        finally { await view.DisposeAsync(); host.Children.Remove(view); }

        WidgetPackageIconView Icon(string id) => Descendants(view).OfType<WidgetPackageIconView>()
            .Single(element => AutomationProperties.GetAutomationId(element) == "Widget.continuity." + id);
        Button Button() => Descendants(view).OfType<Button>()
            .Single(element => AutomationProperties.GetAutomationId(element) == "Widget.continuity.button");
        WidgetPackageIconView ButtonIcon() => Descendants(Button()).OfType<WidgetPackageIconView>().Single();
        static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        void ApplyScope(string scope)
        {
            var snapshot = new ViewSnapshot { WidgetInstanceId = instance, Sequence = ++revision,
                ActiveInputScopeId = scope, InitialFocusId = "continuity.button", Root = new()
                {
                    Id = "continuity.page", Kind = ViewNodeKind.Stack, InputScopeId = scope,
                    Children = (ViewNode[])[
                        new() { Id = "continuity.original", Kind = ViewNodeKind.Icon, Glyph = WidgetGlyph.Music,
                            PackageIcon = new("red", WidgetPackageIconColorMode.OriginalColor) },
                        new() { Id = "continuity.tint", Kind = ViewNodeKind.Icon, Glyph = WidgetGlyph.Music,
                            PackageIcon = new("red", WidgetPackageIconColorMode.ThemeTint) },
                        new() { Id = "continuity.button", Kind = ViewNodeKind.Button, Glyph = WidgetGlyph.Play,
                            Text = "Open", ActionId = "action." + scope, PackageIcon = new(assetId, mode) }],
                } };
            var descriptor = new BridgeWidgetDescriptor { Id = "continuity", Name = "Continuity", InstanceId = instance,
                RuntimeGeneration = "runtime", PresentationGeneration = "presentation", PackageContentDigest = digest, Icon = WidgetGlyph.Music };
            view.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                instance, revision, scope), descriptor, snapshot, new Dictionary<string, BridgeNodeRenderStyles>()));
            view.UpdateLayout();
        }
    }
}
