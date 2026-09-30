using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeBusyFocusAsync()
    {
        var setting = new ViewNode { Id = "busy.setting", Kind = ViewNodeKind.Button, Text = "Setting", ActionId = "setting" };
        var refresh = new ViewNode { Id = "busy.refresh", Kind = ViewNodeKind.Button, Text = "Check again", ActionId = "refresh" };
        var slider = new ViewNode { Id = "busy.slider", Kind = ViewNodeKind.Slider, Minimum = 0, Maximum = 10, Step = 1,
            Value = 5, ValueChangedActionId = "change", AccessibilityLabel = "Setting value", AccessibilityValue = "5" };
        void Render(bool busy) => presenter.Apply(CreateFrame(new ViewNode { Id = "busy.root", Kind = ViewNodeKind.Stack,
            Children = (ViewNode[])[setting with { IsBusy = busy }, refresh, slider with { IsBusy = busy }] },
            new Dictionary<string, BridgeNodeRenderStyles>()));
        Render(false);
        await Wait(() => Find<Button>("Widget.busy.setting")?.IsLoaded == true);
        var control = Find<Button>("Widget.busy.setting")!;
        var retry = Find<Button>("Widget.busy.refresh")!;
        var value = Find<Slider>("Widget.busy.slider")!;
        control.Focus(FocusState.Keyboard);
        await Wait(() => control.FocusState != FocusState.Unfocused);
        var before = actions;
        Render(true);
        await Task.Delay(60);
        Check(ReferenceEquals(control, Find<Button>("Widget.busy.setting")) && control.FocusState != FocusState.Unfocused,
            "busy publication retains the exact native setting and focus instead of entering the fallback button");
        control.Command!.Execute(null);
        value.Value = 8;
        await Task.Delay(30);
        Check(actions == before && value.Value == 5, "busy native controls reject action and value changes while retaining focus");
        Render(false);
        await Task.Delay(40);
        Check(control.FocusState != FocusState.Unfocused, "completed setting publication preserves focus");
        control.Command!.Execute(null);
        await Wait(() => actions == before + 1);
        Render(true);
        retry.Focus(FocusState.Keyboard);
        Render(false);
        await Task.Delay(40);
        Check(retry.FocusState != FocusState.Unfocused, "an explicit focus move during busy state survives completion");

        // Presentation focus and action authority have distinct lifetimes.
        // Cover a noninitial item, then a transient declaration lacking it.
        presenter.SetAutomaticFocusEnabled(false);
        presenter.SetPresentationInputEnabled(false);
        await presenter.SetPresentationActiveAsync(false);
        outside.Focus(FocusState.Keyboard);
        Check(presenter.RestoreRetainedFocusPresentation() && retry.FocusState != FocusState.Unfocused,
            "retained noninitial focus presentation restores before interactive admission");
        before = actions;
        retry.Command!.Execute(null);
        await Task.Delay(20);
        Check(actions == before, "restoring retained focus does not grant suspended action authority");
        presenter.Apply(CreateFrame(new ViewNode { Id = "busy.root", Kind = ViewNodeKind.Stack,
            Children = (ViewNode[])[new() { Id = "busy.loading", Kind = ViewNodeKind.Text, Text = "Loading" }] },
            new Dictionary<string, BridgeNodeRenderStyles>()));
        Render(false);
        await presenter.SetPresentationActiveAsync(true);
        presenter.SetPresentationInputEnabled(true);
        presenter.SetAutomaticFocusEnabled(true);
        presenter.Enter(restoreNativeFocus: true);
        await Wait(() => Find<Button>("Widget.busy.refresh") is { FocusState: not FocusState.Unfocused });
        Check(Find<Button>("Widget.busy.refresh") is { FocusState: not FocusState.Unfocused },
            "transient loading tree preserves exact same-scope focus memory through resume");

        presenter.Apply(CreateFrame(new ViewNode { Id = "busy.root", Kind = ViewNodeKind.Stack,
            Children = (ViewNode[])[setting, refresh with { IsDisabled = true }] },
            new Dictionary<string, BridgeNodeRenderStyles>()));
        presenter.Enter(restoreNativeFocus: true);
        await Wait(() => Find<Button>("Widget.busy.setting") is { FocusState: not FocusState.Unfocused });
        Check(true, "remembered target that is no longer focusable falls back without blocking entry");

    }
}
