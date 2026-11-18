using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;
using Windows.System;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class IndexedWidgetValidationPage
{
    private async Task ValidateInlinePreviewActionsAsync(string path)
    {
        var checks = new List<string>(); Exception? failed = null;
        var focusTrace = new List<string>();
        presenter.FocusTrace = line => { if (focusTrace.Count == 256) focusTrace.RemoveAt(0); focusTrace.Add(line); };
        XamlRoot? controlRoot = null;
        var popupTheme = new NativePopupTheme(); popupTheme.Attach(presenter);
        var layout = (Grid)Content; Content = null;
        var layers = new Grid(); layers.Children.Add(layout); Content = layers; PresentationLayers.Attach(layers);
        try
        {
            await Send("inline-preview-mode");
            await Until(() => Find<WidgetReadingSurface>("inline.source") is { IsLoaded: true });
            var source = Find<WidgetReadingSurface>("inline.source")!;
            Check(source.Focus(FocusState.Keyboard), "Real worker inline source accepts native focus");
            ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(source).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => FocusId() == "Widget.inline.type");
            var type = Find<Button>("inline.type")!;
            controlRoot = type.XamlRoot;
            await Until(() => type.IsLoaded && type.ActualWidth > 0 && type.ActualHeight > 0);
            var chrome = presenter.FocusPreviewChrome;
            Check(Text("inline.choice") == "all", "Inline type begins with the worker's All captures value");

            var expand = (IExpandCollapseProvider)FrameworkElementAutomationPeer.CreatePeerForElement(type).GetPattern(PatternInterface.ExpandCollapse);
            expand.Expand();
            await Until(() => Popup("screenshots") is { IsLoaded: true } && FocusId() == "Widget.inline.type.Option.all");
            NativeContextProbeInput.PressKey(VirtualKey.Down);
            await Until(() => FocusId() == "Widget.inline.type.Option.screenshots");
            NativeContextProbeInput.PressKey(VirtualKey.Enter);
            await Until(() => Text("inline.choice") == "screenshots" && FocusId() == "Widget.inline.type" &&
                ((IValueProvider)FrameworkElementAutomationPeer.CreatePeerForElement(type).GetPattern(PatternInterface.Value)).Value == "Screenshots");
            Check(ReferenceEquals(type, Find<Button>("inline.type")) && ReferenceEquals(chrome, presenter.FocusPreviewChrome) &&
                presenter.IsPreviewEnteredForValidation && FocusId() == "Widget.inline.type",
                "Native keyboard select reaches the real worker and updates the same inline preview controls");
            Check(FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root, "inline.type") is null,
                "Selected control is truly in FocusPresentation, not duplicated in the ordinary child tree");

            expand.Expand();
            await Until(() => Popup("clips") is { IsLoaded: true });
            await presenter.HandleControllerButtonAsync(ControllerButton.B);
            await Until(() => presenter.PreviewMenuItemsForValidation.Count == 0 && FocusId() == "Widget.inline.type");
            Check(presenter.IsPreviewEnteredForValidation, "Closing a native select with Back retains the entered preview and exact opener");

            var apply = Find<Button>("inline.apply")!;
            await NativeContextProbeInput.LeftClickAsync(apply, new(apply.ActualWidth / 2, apply.ActualHeight / 2));
            await Until(() => Text("inline.applied") == "1" && Find<TextBlock>("inline.applied")?.Text == "1");
            Check(ReferenceEquals(apply, Find<Button>("inline.apply")), "Native preview button dispatches once through the real session and worker");

            var slider = Find<Slider>("inline.volume")!;
            Check(slider.Focus(FocusState.Keyboard), "Inline numeric control takes focus");
            presenter.ActivateFocused(); presenter.MoveFocus(FocusNavigationDirection.Right);
            await Until(() => Text("inline.volume.status") == "55" && Find<TextBlock>("inline.volume.status")?.Text == "55");
            Check(ReferenceEquals(slider, Find<Slider>("inline.volume")), "Typed preview slider value crosses the real bridge without replacing the control");
            await presenter.HandleControllerButtonAsync(ControllerButton.B);
            await presenter.HandleControllerButtonAsync(ControllerButton.B);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), source), "Back returns to the exact inline source after numeric adjustment");

            await presenter.HandleControllerButtonAsync(ControllerButton.A);
            await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
            type.Focus(FocusState.Keyboard);
            expand.Expand();
            await Until(() => Popup("clips") is { IsLoaded: true });
            var outside = Find<Button>("inline.refresh")!;
            Check(outside.Focus(FocusState.Keyboard), "Outside widget control accepts an explicit native focus choice while the select is open");
            expand.Collapse();
            await Until(() => !presenter.IsPreviewEnteredForValidation && presenter.PreviewMenuItemsForValidation.Count == 0);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(controlRoot!), outside), "Closing a select does not steal a genuine outside focus choice back into the preview");

            source.Focus(FocusState.Keyboard);
            ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(source).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => presenter.IsPreviewEnteredForValidation && Find<Button>("inline.type") is { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 } &&
                presenter.IsGuidePresentationReady(requireFocus: true));
            type = Find<Button>("inline.type")!;
            Check(type.Focus(FocusState.Keyboard), "Reentered preview can focus Type after restoring its remembered control");
            await Until(() => FocusId() == "Widget.inline.type");
            expand = (IExpandCollapseProvider)FrameworkElementAutomationPeer.CreatePeerForElement(type).GetPattern(PatternInterface.ExpandCollapse);
            expand.Expand();
            await Until(() => Popup("clips") is not null);
            var oldOption = Popup("clips")!;
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(oldOption);
            Action oldInvoke = peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke ? invoke.Invoke :
                peer.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle ? toggle.Toggle :
                throw new InvalidOperationException("Native option has no activation provider.");
            await Send("inline.hide");
            await Until(() => !presenter.IsFocusPreviewOpen);
            try { oldInvoke(); }
            catch (System.Runtime.InteropServices.COMException) { /* Native teardown may revoke the peer before invocation. */ }
            await Task.Yield();
            Check(Text("inline.choice") == "screenshots", "Retired native inline option cannot change the worker after its source disappears");
            if (failure is not null) throw new InvalidOperationException(failure);
        }
        catch (Exception error) { failed = error; }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = failed is null, checks,
            error = failed?.ToString(), preview = presenter.FocusPreviewDiagnostics(), focused = FocusId(),
            select = presenter.PreviewSelectReadinessForValidation("inline.type"), focusTrace }));
        presenter.FocusTrace = null;
        if (failed is not null) throw failed;

        string? Text(string id) => FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root, id)?.Text;
        string? FocusId()
        {
            var root = presenter.PreviewMenuItemsForValidation.FirstOrDefault()?.XamlRoot ?? controlRoot ?? XamlRoot;
            return FocusManager.GetFocusedElement(root) is DependencyObject focused ? AutomationProperties.GetAutomationId(focused) : null;
        }
        T? Find<T>(string id) where T : FrameworkElement => Descendants(presenter).OfType<T>().FirstOrDefault(node => AutomationProperties.GetAutomationId(node) == "Widget." + id)
            ?? (presenter.FocusPreviewChrome is { } preview ? Descendants(preview).OfType<T>().FirstOrDefault(node => AutomationProperties.GetAutomationId(node) == "Widget." + id) : null);
        MenuFlyoutItemBase? Popup(string id) => presenter.PreviewMenuItemsForValidation
            .FirstOrDefault(item => AutomationProperties.GetAutomationId(item) == "Widget.inline.type.Option." + id);
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        async Task Send(string action)
        {
            var frame = session!.GetState("indexed-owned")!.LastGood!;
            var before = publication;
            await session.SendActionAsync(frame.Authority, new(action, "root", InputScopeId: frame.Snapshot.ActiveInputScopeId), lifetime.Token);
            await Until(() => publication > before && Text("status") == action);
        }
        async Task Until(Func<bool> predicate)
        {
            var deadline = Environment.TickCount64 + 8000;
            while (!predicate())
            {
                if (failure is not null) throw new InvalidOperationException(failure);
                if (Environment.TickCount64 > deadline) throw new TimeoutException("Real inline preview did not settle: " +
                    JsonSerializer.Serialize(new { preview = presenter.FocusPreviewDiagnostics(), focused = FocusId(), choice = Text("inline.choice"), select = presenter.PreviewSelectReadinessForValidation("inline.type") }));
                await Task.Delay(15, lifetime.Token);
            }
        }
    }
}
