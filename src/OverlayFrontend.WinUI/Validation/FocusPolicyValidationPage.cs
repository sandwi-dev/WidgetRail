using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using Windows.System;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Native focus-policy integration through the shared presenter, without hardware.</summary>
internal sealed class FocusPolicyValidationPage : Page
{
    private readonly WidgetViewPresenter presenter = new();
    private readonly TextBlock status = new();
    private long sequence;
    private long owner = 1;
    private long request;
    private bool ready;
    private bool neighbors = true;
    private bool disableThird;
    private bool groupNeighbor;
    private bool passive;
    private readonly Button outside = new() { Content = "Host tray" };

    public FocusPolicyValidationPage()
    {
        AutomationProperties.SetAutomationId(status, "FocusPolicy.Status");
        AutomationProperties.SetAutomationId(outside, "FocusPolicy.Outside");
        Content = new StackPanel { Spacing = 12, Children = { status, outside, presenter } };
        KeyDown += (_, args) =>
        {
            switch (args.Key)
            {
                case VirtualKey.F1:
                    passive = !passive; presenter.SetAutomaticFocusEnabled(!passive);
                    if (passive) outside.Focus(FocusState.Keyboard); else presenter.Enter();
                    Apply(); break;
                case VirtualKey.F2: ++request; Apply(); break;
                case VirtualKey.F3: ready = !ready; Apply(); break;
                case VirtualKey.F4: presenter.MoveFocus(FocusNavigationDirection.Down); break;
                case VirtualKey.F5: presenter.MoveFocus(FocusNavigationDirection.Right); break;
                case VirtualKey.F6: neighbors = !neighbors; Apply(); break;
                case VirtualKey.F7: disableThird = !disableThird; Apply(); break;
                case VirtualKey.F8: ++owner; sequence = request = 0; disableThird = false; Apply(); break;
                case VirtualKey.F9: request = 0; Apply(); break;
                case VirtualKey.F10: groupNeighbor = !groupNeighbor; Apply(); break;
                case VirtualKey.F12: Apply(); break;
                default: return;
            }
            args.Handled = true;
        };
        Apply();
    }

    private void Apply()
    {
        var row = new ViewNode { Id = "group", Kind = ViewNodeKind.Row, InitialChildFocusId = ready ? "second" : null,
            Children = ready ? (ViewNode[])[
                new() { Id = "first", Kind = ViewNodeKind.Button, Text = "First", ActionId = "first", Focus = neighbors ? new FocusNeighbors(Right: "third") : null },
                new() { Id = "second", Kind = ViewNodeKind.Button, Text = "Second", ActionId = "second" },
                new() { Id = "third", Kind = ViewNodeKind.Button, Text = "Third", ActionId = "third", IsDisabled = disableThird },
            ] : [] };
        var snapshot = new ViewSnapshot { WidgetInstanceId = "focus.instance", Sequence = ++sequence,
            ActiveInputScopeId = "page", InitialFocusId = "header",
            FocusGroupEntryRequest = request == 0 ? null : new() { RequestId = request, GroupId = "group" },
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
}
