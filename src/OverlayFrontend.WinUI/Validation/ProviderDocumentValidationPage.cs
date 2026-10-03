using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetStyling;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class ProviderDocumentValidationPage : Page, IAsyncDisposable
{
    private readonly BrowserSurface browser;
    private readonly WidgetViewPresenter presenter = new();
    private readonly NativePopupTheme theme = new();
    internal ProviderDocumentValidationPage(string result)
    {
        theme.Attach(this); theme.Update(null, AppearanceSettings.Default);
        var reference = new ProviderDocumentReference(Guid.NewGuid().ToString("N"), ProviderDocumentReference.RestrictedHtml);
        var document = new WebBrowserDocument("document.fixture", 1, reference.Url, "Restricted description")
        { InteractionMode = BrowserInteractionMode.ActivateToInteract, ProviderDocument = reference };
        var arguments = Environment.GetCommandLineArgs();
        var profile = Shell.FrontendArguments.Value(arguments, "--document-profile") ??
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(result))!, "webview-profile");
        var widgetId = Shell.FrontendArguments.Value(arguments, "--document-widget") ?? "fixture.documents";
        Task<CoreWebView2Environment>? environment = null;
        browser = new(widgetId, document,
            () => environment ??= CoreWebView2Environment.CreateWithOptionsAsync(null, profile, null).AsTask(),
            (string[])["<style>body{margin:8px;color:white;background:#202020;font:16px Segoe UI}.chips{display:flex;flex-wrap:wrap;gap:8px}.chip{width:250px;height:48px;border:1px solid gray}</style><div class='chips'><div class='chip'>Source one</div><div class='chip'>Source two</div></div><script>window.untrustedRan=true</script>"]);
        long sequence = 0;
        void Apply(double width, double? height = null)
        {
            var snapshot = new ViewSnapshot { WidgetInstanceId = "documents.instance", Sequence = ++sequence,
                ActiveInputScopeId = "documents", InitialFocusId = "provider",
                Root = new() { Id = "documents", Kind = ViewNodeKind.Stack,
                    Children = (ViewNode[])[new() { Id = "provider", Kind = ViewNodeKind.WebBrowser, WebBrowser = document, AccessibilityLabel = document.AccessibleName }] } };
            var descriptor = new BridgeWidgetDescriptor { Id = widgetId, Name = "Documents", InstanceId = snapshot.WidgetInstanceId,
                RuntimeGeneration = "runtime-1", PresentationGeneration = "presentation", Icon = WidgetGlyph.Connection, PackageContentDigest = "" };
            var styles = new Dictionary<string, BridgeComputedStyleValue> {
                ["width"] = Length(width), ["min-height"] = Length(48), ["max-height"] = Length(640) };
            if (height is { } fixedHeight) styles["height"] = Length(fixedHeight);
            presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                snapshot.WidgetInstanceId, sequence, snapshot.ActiveInputScopeId), descriptor,
                SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)),
                new Dictionary<string, BridgeNodeRenderStyles> { ["provider"] = new() {
                    Base = styles, Focused = new Dictionary<string, BridgeComputedStyleValue>(), Pressed = new Dictionary<string, BridgeComputedStyleValue>() } }));
        }
        static BridgeComputedStyleValue Length(double number) => new() {
            Kind = WrssValueKind.Length, Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture) + "px", Number = number, Unit = "px" };
        Apply(700);
        var other = new Button { Content = "Another control" };
        Content = new StackPanel { Spacing = 16, Margin = new(24), Children = { new TextBlock { Text = "Document sizing and focus", FontSize = 24 }, presenter, other } };
        Loaded += async (_, _) =>
        {
            var checks = new List<string>(); string? error = null;
            try
            {
                var slot = FindSlot(presenter) ?? throw new InvalidOperationException("Presenter did not create a browser slot.");
                slot.Host.Children.Add(browser); slot.SetSurface(browser); browser.SetPresentation(true, true);
                Check(double.IsNaN(slot.Height), "Presenter preserves auto-height for a document without an authored height");
                Check(!browser.IsToolbarVisible, "Provider toolbar is absent before WebView initialization");
                await Until(() => browser.IsReady && !browser.IsNavigationInProgress && browser.ProviderContentHeight is > 48 and < 120);
                Check(!browser.IsToolbarVisible, "Provider toolbar stays absent after navigation");
                Check(slot.ActualHeight < 130, "Auto-height fits one row of supplied HTML without blank padding");
                var wideHeight = slot.ActualHeight;
                Apply(310);
                await Until(() => slot.ActualHeight > wideHeight + 30);
                Check(slot.ActualHeight < 200, "Width changes remeasure wrapped content");
                Check(await browser.EvaluateFixtureAsync("typeof window.untrustedRan") == "\"undefined\"", "Content sizing does not enable untrusted scripts");
                slot.Focus(FocusState.Keyboard);
                await Until(() => slot.IsFocusOutlineVisible);
                Check(true, "Provider container displays its themed focus outline");
                slot.Enter();
                Check(!slot.IsFocusOutlineVisible && browser.IsInteracting, "Outline hides while interacting with the document");
                browser.HandleButton(ControllerButton.B, ControllerEventPhase.Pressed);
                browser.HandleButton(ControllerButton.B, ControllerEventPhase.Released);
                await Until(() => slot.IsFocusOutlineVisible);
                Check(true, "Leaving interaction restores the focused container outline");
                other.Focus(FocusState.Keyboard);
                await Until(() => !slot.IsFocusOutlineVisible);
                Check(true, "Outline clears when focus leaves the surface");
                Apply(310, 360);
                await Until(() => Math.Abs(slot.ActualHeight - 360) < 2);
                Check(true, "Authored fixed document height remains authoritative");
                slot.IsProviderDocument = false; slot.RefreshFocusPresentation(); slot.Focus(FocusState.Keyboard);
                await Until(() => slot.IsFocusOutlineVisible);
                Check(true, "Ordinary browser containers share the same focus outline");
                slot.IsProviderDocument = true; Apply(700);
                await Until(() => slot.ActualHeight < 130);
                Check(double.IsNaN(slot.Height), "Removing authored height restores content sizing through the presenter");
            }
            catch (Exception failure) { error = failure.ToString(); }
            await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { passed = error is null, checks, error,
                environmentMs = browser.EnvironmentInitializationMilliseconds,
                controllerMs = browser.ControllerInitializationMilliseconds,
                navigationMs = browser.DocumentNavigationMilliseconds }));
            void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        };
    }
    private static async Task Until(Func<bool> ready)
    { var end = Environment.TickCount64 + 20000; while (!ready()) { if (Environment.TickCount64 > end) throw new TimeoutException("Document validation condition timed out."); await Task.Delay(40); } }
    private static BrowserSlot? FindSlot(DependencyObject element)
    {
        if (element is BrowserSlot slot) return slot;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); ++index)
            if (FindSlot(VisualTreeHelper.GetChild(element, index)) is { } found) return found;
        return null;
    }
    public async ValueTask DisposeAsync()
    { await presenter.DisposeAsync(); await browser.DisposeAsync(); }
}
