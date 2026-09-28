using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using Windows.System;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Exercises the actual shared presenter; no backend or hardware.</summary>
internal sealed class WidgetControlsValidationPage : Page
{
    private readonly WidgetViewPresenter presenter = new();
    private readonly TextBlock status = new();
    private readonly Button outside = new() { Content = "Outside widget" };
    private long sequence;
    private long owner = 1;
    private int actions;
    private bool inserted;
    private bool wrapped;
    private bool disableSecond;
    private bool alternateScope;
    private ICommand? retiredCommand;
    private TaskCompletionSource? admissionHold;

    public WidgetControlsValidationPage()
    {
        AutomationProperties.SetAutomationId(status, "Controls.Status");
        AutomationProperties.SetAutomationId(outside, "Controls.Outside");
        Content = new StackPanel { Spacing = 12, Children = { outside,
            new TextBlock { Text = "F5 scope; F7 insert; F8 reparent; F9 disable focused; F10 new owner; F11 invoke retired command; F12 update", TextWrapping = TextWrapping.Wrap }, status, presenter } };
        presenter.DispatchActionAsync = request =>
        {
            status.Text = $"Actions: {++actions}; source: {request.Action.SourceElementId}; owner: {request.Authority.SessionGeneration}";
            return admissionHold?.Task ?? Task.CompletedTask;
        };
        Unloaded += (_, _) => admissionHold?.TrySetResult();
        KeyDown += (_, args) =>
        {
            switch (args.Key)
            {
                case VirtualKey.F2: admissionHold?.TrySetResult(); admissionHold = null; break;
                case VirtualKey.F3: admissionHold ??= new(TaskCreationOptions.RunContinuationsAsynchronously); break;
                case VirtualKey.F5: alternateScope = !alternateScope; Apply(); break;
                case VirtualKey.F7: inserted = !inserted; Apply(); break;
                case VirtualKey.F8: wrapped = !wrapped; Apply(); break;
                case VirtualKey.F9: disableSecond = !disableSecond; Apply(); break;
                case VirtualKey.F10:
                    retiredCommand = FindButton(presenter, "Widget.second")?.Command;
                    ++owner; sequence = 0; disableSecond = false; Apply(); break;
                case VirtualKey.F11:
                    retiredCommand?.Execute(null);
                    status.Text = $"Retired command checked; captured: {retiredCommand is not null}; actions: {actions}";
                    break;
                case VirtualKey.F12: Apply(); break;
                default: return;
            }
            args.Handled = true;
        };
        Apply();
    }

    private void Apply()
    {
        var first = new ViewNode { Id = "first", Kind = ViewNodeKind.Button, Text = "First", ActionId = "first" };
        var second = new ViewNode { Id = "second", Kind = ViewNodeKind.Button, Text = "Second", ActionId = "second", IsDisabled = disableSecond };
        var row = new ViewNode { Id = "buttons", Kind = ViewNodeKind.Row, Children = [first, second] };
        ViewNode body = wrapped ? new() { Id = "wrapper", Kind = ViewNodeKind.Stack, Children = [row] } : row;
        var children = new List<ViewNode>();
        if (inserted) children.Add(new() { Id = "inserted", Kind = ViewNodeKind.Text, Text = "New information" });
        children.Add(body);
        children.Add(new() { Id = "alternate", Kind = ViewNodeKind.Stack, InputScopeId = "alternate",
            Children = [new() { Id = "third", Kind = ViewNodeKind.Button, Text = "Other scope", ActionId = "third" }] });
        children.Add(new() { Id = "progress", Kind = ViewNodeKind.Progress, Value = 25, Maximum = 100, AccessibilityLabel = "Download progress" });
        children.Add(new() { Id = "loading", Kind = ViewNodeKind.LoadingIndicator, AccessibilityLabel = "Loading", IndicatorSize = LoadingIndicatorSize.Compact });
        var snapshot = new ViewSnapshot { WidgetInstanceId = "controls.instance", Sequence = ++sequence,
            ActiveInputScopeId = alternateScope ? "alternate" : "controls", InitialFocusId = alternateScope ? "third" : disableSecond ? "first" : "second",
            Root = new() { Id = "controls", Kind = ViewNodeKind.Stack, Children = children.ToArray() } };
        var validated = SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot));
        var descriptor = new BridgeWidgetDescriptor { Id = "controls", Name = "Controls", InstanceId = snapshot.WidgetInstanceId,
            RuntimeGeneration = $"runtime-{owner}", PresentationGeneration = "presentation", Icon = WidgetGlyph.Connection, PackageContentDigest = "" };
        presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, owner,
            snapshot.WidgetInstanceId, sequence, snapshot.ActiveInputScopeId), descriptor, validated, new Dictionary<string, BridgeNodeRenderStyles>()));
        status.Text = $"Revision: {sequence}; owner: {owner}; actions: {actions}";
    }

    private static Button? FindButton(DependencyObject element, string id)
    {
        if (element is Button button && AutomationProperties.GetAutomationId(button) == id) return button;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); ++index)
            if (FindButton(VisualTreeHelper.GetChild(element, index), id) is { } found) return found;
        return null;
    }
}
