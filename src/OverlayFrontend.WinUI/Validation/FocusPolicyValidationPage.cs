using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using Windows.System;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Native focus-policy integration through the shared presenter, without hardware.</summary>
internal sealed partial class FocusPolicyValidationPage : Page
{
    private readonly WidgetViewPresenter presenter = new() { Width = 700 };
    private readonly TextBlock status = new();
    private long sequence;
    private long owner = 1;
    private long request;
    private string? exactTarget;
    private FocusProbeWidget focusSource = new();
    private bool ready;
    private bool neighbors = true;
    private bool disableThird;
    private bool hideThird;
    private bool hideThirdParent;
    private bool groupNeighbor;
    private bool passive;
    private readonly Button outside = new() { Content = "Host tray" };

    public FocusPolicyValidationPage()
    {
        AutomationProperties.SetAutomationId(status, "FocusPolicy.Status");
        AutomationProperties.SetAutomationId(outside, "FocusPolicy.Outside");
        var surface = (Grid)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" RequestedTheme=\"Dark\" Background=\"{ThemeResource ApplicationPageBackgroundThemeBrush}\" Padding=\"24\" />");
        surface.Children.Add(new StackPanel { Spacing = 12, Children = { status, outside, presenter } });
        Content = surface;
        KeyDown += (_, args) =>
        {
            switch (args.Key)
            {
                case VirtualKey.F1:
                    passive = !passive; presenter.SetAutomaticFocusEnabled(!passive);
                    if (passive) outside.Focus(FocusState.Keyboard); else presenter.Enter();
                    Apply(); break;
                case VirtualKey.F2: exactTarget = null; request = focusSource.Request().Id; Apply(); break;
                case VirtualKey.F3: ready = !ready; Apply(); break;
                case VirtualKey.F4: presenter.MoveFocus(FocusNavigationDirection.Down); break;
                case VirtualKey.F5: presenter.MoveFocus(FocusNavigationDirection.Right); break;
                case VirtualKey.F6: neighbors = !neighbors; Apply(); break;
                case VirtualKey.F7: disableThird = !disableThird; Apply(); break;
                case VirtualKey.F8: ++owner; focusSource.Dispose(); focusSource = new(); sequence = request = 0; exactTarget = null; disableThird = false; hideThird = hideThirdParent = false; Apply(); break;
                case VirtualKey.F9: request = 0; Apply(); break;
                case VirtualKey.F10: groupNeighbor = !groupNeighbor; Apply(); break;
                case VirtualKey.F11: exactTarget = null; request = focusSource.Navigate(); Apply(); break;
                case VirtualKey.F12: Apply(); break;
                case VirtualKey.X: exactTarget = "first"; request = focusSource.Request().Id; Apply(); break;
                case VirtualKey.Y: exactTarget = "third"; request = focusSource.Request().Id; Apply(); break;
                case VirtualKey.C:
                    exactTarget = "third"; request = focusSource.Request().Id;
                    disableThird = true; Apply();
                    disableThird = false; Apply(); break;
                case VirtualKey.H: hideThird = !hideThird; Apply(); break;
                case VirtualKey.P: hideThirdParent = !hideThirdParent; Apply(); break;
                case VirtualKey.J:
                    exactTarget = "third"; request = focusSource.Request().Id;
                    hideThird = true; Apply();
                    hideThird = false; Apply(); break;
                case VirtualKey.K:
                    exactTarget = "third"; request = focusSource.Request().Id;
                    hideThirdParent = true; Apply();
                    hideThirdParent = false; Apply(); break;
                default: return;
            }
            args.Handled = true;
        };
        Unloaded += (_, _) => focusSource.Dispose();
        Apply();
    }

    private void Apply()
    {
        var row = new ViewNode { Id = "group", Kind = ViewNodeKind.Row, InitialChildFocusId = ready ? "second" : null,
            Children = ready ? (ViewNode[])[
                new() { Id = "first", Kind = ViewNodeKind.Button, Text = "First", ActionId = "first", Focus = neighbors ? new FocusNeighbors(Right: "third") : null },
                new() { Id = "second", Kind = ViewNodeKind.Button, Text = "Second", ActionId = "second" },
                new() { Id = "third-parent", Kind = ViewNodeKind.Stack,
                    VisibleWhen = hideThirdParent ? ResponsiveVisibility.ExpandedOnly : ResponsiveVisibility.Always,
                    Children = (ViewNode[])[new() { Id = "third", Kind = ViewNodeKind.Button, Text = "Third", ActionId = "third",
                        IsDisabled = disableThird, VisibleWhen = hideThird ? ResponsiveVisibility.ExpandedOnly : ResponsiveVisibility.Always }] },
            ] : [] };
        var snapshot = new ViewSnapshot { WidgetInstanceId = "focus.instance", Sequence = ++sequence,
            ActiveInputScopeId = "page", InitialFocusId = "header",
            FocusGroupEntryRequest = request == 0 ? null : new() { RequestId = request, GroupId = "group", TargetId = exactTarget },
            Root = new() { Id = "page", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[
                new() { Id = "header", Kind = ViewNodeKind.Button, Text = "Header", ActionId = "header", Focus = ready && groupNeighbor ? new FocusNeighbors(Right: "group") : null }, row,
                new() { Id = "other", Kind = ViewNodeKind.Stack, InputScopeId = "other", Children = (ViewNode[])[
                    new() { Id = "foreign", Kind = ViewNodeKind.Button, Text = "Inactive scope", ActionId = "foreign" },
                ] },
            ] } };
        var descriptor = new BridgeWidgetDescriptor { Id = "focus", Name = "Focus policy", InstanceId = snapshot.WidgetInstanceId,
            RuntimeGeneration = $"runtime-{owner}", PresentationGeneration = "presentation", Icon = WidgetGlyph.Connection, PackageContentDigest = "" };
        presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, owner,
            snapshot.WidgetInstanceId, sequence, snapshot.ActiveInputScopeId), descriptor,
            SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)), new Dictionary<string, BridgeNodeRenderStyles>()));
        status.Text = $"Owner {owner}; revision {sequence}; request {request}; ready {ready}";
    }
    private sealed class FocusProbeWidget : Widget, IDisposable
    {
        private readonly WidgetNavigator<bool> navigation;
        internal FocusProbeWidget() => navigation = CreateNavigator("probe", false);
        internal WidgetFocusRequest Request() => CreateFocusRequest();
        internal long Navigate()
        {
            navigation.NavigateRoot(!navigation.Value.Route, "group");
            return navigation.Value.FocusGroupEntryRequest!.RequestId;
        }
        public override WidgetView Render() => new(UI.Stack("probe.root"));
        public void Dispose() => navigation.Dispose();
    }

}
