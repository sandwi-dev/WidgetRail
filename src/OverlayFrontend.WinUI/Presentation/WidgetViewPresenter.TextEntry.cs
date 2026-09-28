using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private sealed record TextEntryPopup(Binding Owner, WidgetPresentationAuthority Authority, ViewNode Declaration, WidgetTextEntryDialog Dialog);
    private TextEntryPopup? textEntryPopup;
    private Task? textEntryLifetime;

    private static string TextEntryLabel(ViewNode node) =>
        (node.TextEntryInputKind == TextEntryInputKind.Sensitive || string.IsNullOrEmpty(node.TextEntryValue)
            ? node.TextEntryPlaceholder : node.TextEntryValue) ?? string.Empty;

    private Button CreateTextEntry(WidgetElementIdentity identity, object token)
    {
        var button = new Button { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        button.Click += (_, _) => OpenTextEntry(identity, token);
        return button;
    }

    private void OpenTextEntry(WidgetElementIdentity identity, object token)
    {
        if (applying || disposed || presentationOnly || frame is null || XamlRoot is null ||
            !bindings.TryGetValue(identity.Id, out var binding) || binding.Identity != identity ||
            !ReferenceEquals(binding.Token, token) || !Eligible(binding) || !binding.Element.IsLoaded) return;
        DismissTransientControl();
        var node = declarations[identity.Id].Node;
        var dialog = new WidgetTextEntryDialog(node, commit => _ = CompleteTextEntryAsync(commit));
        dialog.SetBinding(RequestedThemeProperty, new Microsoft.UI.Xaml.Data.Binding { Source = this, Path = new PropertyPath(nameof(ActualTheme)), Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay });
        dialog.BindRoot(XamlRoot);
        var popup = new TextEntryPopup(binding, frame.Authority, node, dialog);
        textEntryPopup = popup;
        textEntryLifetime = ShowTextEntryAsync(popup, textEntryLifetime);
    }

    private async Task ShowTextEntryAsync(TextEntryPopup popup, Task? previous)
    {
        try
        {
            if (previous is not null) await previous;
            if (ReferenceEquals(textEntryPopup, popup) && TextEntryIsCurrent(popup)) await popup.Dialog.ShowAsync();
        }
        catch (Exception) { ReportFailure(new InvalidOperationException("Text entry could not be opened.")); }
        finally
        {
            if (ReferenceEquals(textEntryPopup, popup)) textEntryPopup = null;
            popup.Dialog.Erase();
            // Revoke before restoring; never restore an obsolete page/runtime.
            if (textEntryPopup is null && TextEntryIsCurrent(popup)) FocusBinding(popup.Owner);
        }
    }

    private bool TextEntryIsCurrent(TextEntryPopup popup) => !disposed && frame is not null &&
        SameOwner(popup.Authority, frame.Authority) && popup.Authority.ActiveInputScopeId == frame.Authority.ActiveInputScopeId &&
        bindings.TryGetValue(popup.Owner.Identity.Id, out var binding) && ReferenceEquals(binding, popup.Owner) && Eligible(binding) &&
        declarations[binding.Identity.Id].Node is { } node && node.ActionId == popup.Declaration.ActionId &&
        node.TextEntryValue == popup.Declaration.TextEntryValue && node.TextEntryMaximumLength == popup.Declaration.TextEntryMaximumLength &&
        node.TextEntryInputKind == popup.Declaration.TextEntryInputKind;

    private bool DismissTextEntry()
    {
        if (textEntryPopup is not { } popup) return false;
        textEntryPopup = null;
        popup.Dialog.Erase();
        popup.Dialog.Hide();
        return true;
    }

    private async Task CompleteTextEntryAsync(bool commit)
    {
        if (textEntryPopup is not { } popup) return;
        if (!commit || !TextEntryIsCurrent(popup) || DispatchActionAsync is null) { DismissTextEntry(); return; }
        var authority = frame!.Authority;
        var action = new WidgetActionEvent(popup.Declaration.ActionId!, popup.Owner.Identity.Id, ControllerButton.A,
            Sequence: ++actionSequence, MonotonicTimestampMicroseconds: Environment.TickCount64 * 1000,
            InputScopeId: authority.ActiveInputScopeId)
        { CommittedText = popup.Dialog.TakeValue(), FocusedElementId = popup.Owner.Identity.Id };
        DismissTextEntry();
        try { await DispatchActionAsync(new(authority, action)); }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception)
        {
            // Widget exceptions may echo the committed secret. Do not forward an
            // exception/message/inner exception from the sensitive boundary.
            ReportFailure(new InvalidOperationException("Text entry commit failed."));
        }
    }

    private bool ActivateTextEntry()
    {
        if (textEntryPopup is { } popup) { popup.Dialog.Activate(); return true; }
        if (FocusedBinding() is { Identity.Kind: ViewNodeKind.TextEntry } binding && Eligible(binding))
        { OpenTextEntry(binding.Identity, binding.Token); return true; }
        return false;
    }
}
