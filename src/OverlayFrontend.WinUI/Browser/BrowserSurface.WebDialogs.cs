using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private bool webDialogPending;

    private void ScriptDialogOpening(CoreWebView2 owner, CoreWebView2ScriptDialogOpeningEventArgs args)
    {
        // A confirmed Stop/profile reset must finish, rather than resurrect a
        // document because its beforeunload handler refuses the host action.
        if (profileChanging && args.Kind == CoreWebView2ScriptDialogKind.Beforeunload) { args.Accept(); return; }
        var origin = BrowserSiteIdentity.Origin(args.Uri, requireHttps: false);
        if (!CanRequestSiteDecision || HasDialog || origin is null || Choose is null) return;
        var token = (siteDecisions ??= CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)).Token;
        var deferral = args.GetDeferral();
        webDialogPending = true;
        CancelPointer();
        RunSiteDecision(async () =>
        {
            try
            {
                var title = args.Kind == CoreWebView2ScriptDialogKind.Beforeunload ? "Leave this page?" : "Website message";
                var message = $"{document.AccessibleName}\n{origin}\n\n{BoundWebsiteText(args.Message, 2000)}";
                if (args.Kind == CoreWebView2ScriptDialogKind.Prompt)
                {
                    var response = await PromptWebsiteAsync($"{document.AccessibleName}\n{origin}", BoundWebsiteText(args.Message, 2000), BoundWebsiteText(args.DefaultText, 2048), token);
                    if (response is not null && CanCompleteSiteDecision && ReferenceEquals(owner, core))
                    { args.ResultText = response; args.Accept(); }
                }
                else
                {
                    var label = args.Kind == CoreWebView2ScriptDialogKind.Beforeunload ? "Leave page" : "OK";
                    var response = await Choose!(this, title, message, (HostDialogChoice[])[new("accept", label)], token);
                    token.ThrowIfCancellationRequested();
                    if (response == "accept" && CanCompleteSiteDecision && ReferenceEquals(owner, core)) args.Accept();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally { webDialogPending = false; deferral.Complete(); InteractionChanged?.Invoke(); }
        });
    }

    private async Task<string?> PromptWebsiteAsync(string label, string message, string value, CancellationToken token)
    {
        var accepted = false;
        WidgetTextEntryDialog? dialog = null;
        dialog = new(new ViewNode { Id = "browser.prompt", Kind = ViewNodeKind.TextEntry,
            TextEntryValue = value, TextEntryMaximumLength = 2048, AccessibilityLabel = label,
            TextEntryInputKind = TextEntryInputKind.Ordinary }, commit => { accepted = commit; dialog!.Hide(); }, commitLabel: "Submit", description: message);
        dialog.BindRoot(XamlRoot);
        dialog.ThemeLease = NativePopupTheme.Dialog(dialog, this);
        dialog.GuideChanged = () => InteractionChanged?.Invoke();
        interacting = browsing = true;
        editDialog = dialog;
        InteractionChanged?.Invoke();
        using var dismiss = token.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
        try
        {
            await dialog.ShowKeyboardAsync(this);
            token.ThrowIfCancellationRequested();
            return accepted ? dialog.TakeValue() : null;
        }
        finally
        {
            dialog.Erase(); dialog.ThemeLease?.Dispose(); dialog.ThemeLease = null;
            if (ReferenceEquals(editDialog, dialog)) editDialog = null;
            if (!retired && input) FocusInteraction?.Invoke();
            DrawCursor();
        }
    }

    private static string BoundWebsiteText(string? value, int maximum) =>
        string.IsNullOrEmpty(value) ? "" : value[..Math.Min(value.Length, maximum)];
}
